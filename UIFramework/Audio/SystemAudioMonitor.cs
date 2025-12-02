using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using UnityEngine;
using UnityEngine.Audio;
using BepInEx.Logging;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 系统音频监控器 - 监听其他应用的音频播放，自动调整游戏音量
    /// 使用 Windows WASAPI (Windows Audio Session API) 检测活跃的音频会话
    /// </summary>
    public class SystemAudioMonitor : IDisposable
    {
        private static SystemAudioMonitor _instance;
        public static SystemAudioMonitor Instance => _instance ??= new SystemAudioMonitor();

        private readonly ManualLogSource _log;
        private CancellationTokenSource _cts;
        private Task _monitorTask;
        private bool _isRunning;
        private bool _disposed;

        // 当前状态
        private bool _otherAudioPlaying;
        private float _currentVolumeMultiplier = 1f;

        // 配置缓存
        private float _targetVolume;
        private float _detectionInterval;
        private float _fadeInDuration;
        private float _fadeOutDuration;

        // AudioMixer 引用
        private AudioMixer _musicMixer;
        private float _originalMusicVolume;

        // 进程ID（用于排除自己）
        private readonly int _currentProcessId;

        private SystemAudioMonitor()
        {
            _log = BepInEx.Logging.Logger.CreateLogSource("SystemAudioMonitor");
            _currentProcessId = Process.GetCurrentProcess().Id;
        }

        /// <summary>
        /// 初始化并启动监控
        /// </summary>
        /// <param name="musicMixer">游戏的音乐 AudioMixer</param>
        public void Initialize(AudioMixer musicMixer)
        {
            if (!PluginConfig.EnableAutoMuteOnOtherAudio.Value)
            {
                _log.LogInfo("自动静音功能已禁用");
                return;
            }

            _musicMixer = musicMixer;

            // 读取配置
            _targetVolume = PluginConfig.AutoMuteVolumeLevel.Value;
            _detectionInterval = PluginConfig.AudioDetectionInterval.Value;
            _fadeInDuration = PluginConfig.AudioResumeFadeInDuration.Value;
            _fadeOutDuration = PluginConfig.AudioMuteFadeOutDuration.Value;

            // 获取初始音量
            if (_musicMixer != null && _musicMixer.GetFloat("MusicVolume", out float vol))
            {
                _originalMusicVolume = vol;
            }

            Start();
        }

        /// <summary>
        /// 简单初始化（不需要 AudioMixer，使用回调方式）
        /// </summary>
        public void Initialize()
        {
            if (!PluginConfig.EnableAutoMuteOnOtherAudio.Value)
            {
                _log.LogInfo("自动静音功能已禁用");
                return;
            }

            // 读取配置
            _targetVolume = PluginConfig.AutoMuteVolumeLevel.Value;
            _detectionInterval = PluginConfig.AudioDetectionInterval.Value;
            _fadeInDuration = PluginConfig.AudioResumeFadeInDuration.Value;
            _fadeOutDuration = PluginConfig.AudioMuteFadeOutDuration.Value;

            Start();
        }

        /// <summary>
        /// 启动监控
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
            
            _log.LogInfo($"系统音频监控已启动 (检测间隔: {_detectionInterval}秒, 目标音量: {_targetVolume})");
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            
            _cts?.Cancel();
            _isRunning = false;
            
            // 恢复音量
            _currentVolumeMultiplier = 1f;
            
            _log.LogInfo("系统音频监控已停止");
        }

        /// <summary>
        /// 监控循环（在后台线程运行）
        /// </summary>
        private async Task MonitorLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    bool hasOtherAudio = CheckOtherAudioPlaying();
                    
                    if (hasOtherAudio != _otherAudioPlaying)
                    {
                        _otherAudioPlaying = hasOtherAudio;
                        OnOtherAudioStateChanged(hasOtherAudio);
                    }

                    await Task.Delay((int)(_detectionInterval * 1000), token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                _log.LogError($"监控循环异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检测是否有其他应用在播放音频
        /// </summary>
        private bool CheckOtherAudioPlaying()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;
                var sessions = sessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        // 跳过自己的进程
                        uint processId = session.GetProcessID;
                        if (processId == _currentProcessId || processId == 0)
                            continue;

                        // 检查会话状态
                        if (session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                        {
                            // 检查是否真的有声音（峰值电平 > 0）
                            float peakValue = session.AudioMeterInformation.MasterPeakValue;
                            if (peakValue > 0.001f) // 有实际音频输出
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略单个会话的错误
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning($"检测音频会话失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 当其他音频状态改变时的处理
        /// </summary>
        private void OnOtherAudioStateChanged(bool hasOtherAudio)
        {
            if (hasOtherAudio)
            {
                StartVolumeTransition(_targetVolume, _fadeOutDuration);
            }
            else
            {
                StartVolumeTransition(1f, _fadeInDuration);
            }
        }

        /// <summary>
        /// 开始音量过渡（在主线程执行）
        /// </summary>
        private void StartVolumeTransition(float targetMultiplier, float duration)
        {
            // 在主线程启动协程
            MainThreadDispatcher.Instance?.Enqueue(() =>
            {
                // 使用 Unity 协程实现平滑过渡
                MainThreadDispatcher.Instance?.StartCoroutine(
                    VolumeTransitionCoroutine(targetMultiplier, duration));
            });
        }

        /// <summary>
        /// 音量过渡协程
        /// </summary>
        private System.Collections.IEnumerator VolumeTransitionCoroutine(float targetMultiplier, float duration)
        {
            float startMultiplier = _currentVolumeMultiplier;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 使用平滑曲线
                t = t * t * (3f - 2f * t); // SmoothStep

                _currentVolumeMultiplier = Mathf.Lerp(startMultiplier, targetMultiplier, t);
                ApplyVolumeMultiplier(_currentVolumeMultiplier);

                yield return null;
            }

            _currentVolumeMultiplier = targetMultiplier;
            ApplyVolumeMultiplier(targetMultiplier);
        }

        /// <summary>
        /// 应用音量乘数
        /// </summary>
        private void ApplyVolumeMultiplier(float multiplier)
        {
            try
            {
                // 使用补丁类来设置音量乘数
                ChillPatcher.Patches.AudioVolumeMultiplier_Patch.SetVolumeMultiplier(multiplier);
            }
            catch (Exception ex)
            {
                _log.LogWarning($"应用音量乘数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前音量乘数（供外部使用）
        /// </summary>
        public float GetVolumeMultiplier() => _currentVolumeMultiplier;

        /// <summary>
        /// 当前是否有其他音频在播放
        /// </summary>
        public bool IsOtherAudioPlaying => _otherAudioPlaying;

        /// <summary>
        /// 当前音量乘数
        /// </summary>
        public float CurrentVolumeMultiplier => _currentVolumeMultiplier;

        /// <summary>
        /// 监控是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            _cts?.Dispose();
            _log.Dispose();
        }
    }

    /// <summary>
    /// 主线程调度器 - 用于在主线程执行操作
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        public static MainThreadDispatcher Instance => _instance;

        private readonly System.Collections.Generic.Queue<Action> _executionQueue = 
            new System.Collections.Generic.Queue<Action>();

        private static readonly object _lock = new object();

        public static void Initialize()
        {
            if (_instance != null) return;

            var go = new GameObject("MainThreadDispatcher");
            _instance = go.AddComponent<MainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }

        public void Enqueue(Action action)
        {
            lock (_lock)
            {
                _executionQueue.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_lock)
            {
                while (_executionQueue.Count > 0)
                {
                    try
                    {
                        _executionQueue.Dequeue()?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"MainThreadDispatcher error: {ex.Message}");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            _instance = null;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.UIFramework.Audio;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ChillPatcher.ModuleSystem.Services
{
    /// <summary>
    /// 音频加载器实现
    /// </summary>
    public class CoreAudioLoader : IAudioLoader
    {
        private static CoreAudioLoader _instance;
        public static CoreAudioLoader Instance => _instance;

        private static readonly string[] SUPPORTED_FORMATS = { ".mp3", ".wav", ".ogg", ".egg", ".flac", ".aiff", ".aif" };

        public string[] SupportedFormats => SUPPORTED_FORMATS;

        public static void Initialize()
        {
            if (_instance != null)
                return;

            _instance = new CoreAudioLoader();
            Plugin.Logger.LogInfo("CoreAudioLoader 初始化完成");
        }

        private CoreAudioLoader()
        {
        }

        public bool IsSupportedFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLower();
            return SUPPORTED_FORMATS.Contains(extension);
        }

        /// <summary>
        /// 判断 URL 是否是 FLAC 格式（需要特殊处理）
        /// </summary>
        public bool IsFlacUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            // 检查 URL 路径部分是否以 .flac 结尾
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.ToLowerInvariant();
                return path.EndsWith(".flac");
            }
            catch
            {
                return url.ToLowerInvariant().Contains(".flac");
            }
        }

        public async Task<AudioClip> LoadFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Plugin.Logger.LogWarning($"文件不存在: {filePath}");
                return null;
            }

            if (!IsSupportedFormat(filePath))
            {
                Plugin.Logger.LogWarning($"不支持的格式: {filePath}");
                return null;
            }

            try
            {
                var result = await GameAudioInfo.DownloadAudioFile(filePath, CancellationToken.None);
                return result.Item1;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"加载音频失败 '{filePath}': {ex.Message}");
                return null;
            }
        }

        public async Task<AudioClip> LoadFromUrlAsync(string url)
        {
            return await LoadFromUrlAsync(url, CancellationToken.None);
        }

        /// <summary>
        /// 从 URL 加载 AudioClip（支持取消）
        /// </summary>
        /// <param name="url">音频 URL</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载的 AudioClip</returns>
        public async Task<AudioClip> LoadFromUrlAsync(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(url))
            {
                Plugin.Logger.LogWarning("URL 为空");
                return null;
            }

            try
            {
                var audioType = GetAudioTypeFromUrl(url);
                Plugin.Logger.LogDebug($"[CoreAudioLoader] Loading from URL: {url}, AudioType: {audioType}");

                using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
                {
                    // 启用流式加载
                    ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;

                    await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Plugin.Logger.LogError($"[CoreAudioLoader] URL 加载失败: {request.error}");
                        return null;
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        Plugin.Logger.LogInfo($"[CoreAudioLoader] ✅ URL 加载成功: {clip.name} ({clip.length:F1}s, {clip.frequency}Hz)");
                    }
                    return clip;
                }
            }
            catch (OperationCanceledException)
            {
                Plugin.Logger.LogDebug($"[CoreAudioLoader] URL 加载被取消: {url}");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[CoreAudioLoader] URL 加载异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 URL 加载 FLAC 音频（边下边播）
        /// 返回包含 AudioClip 和 UrlFlacLoader 的元组
        /// UrlFlacLoader 需要在播放完成后清理
        /// </summary>
        /// <param name="url">FLAC 文件 URL</param>
        /// <param name="uuid">歌曲 UUID（用于资源管理）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>AudioClip 和 UrlFlacLoader，失败返回 null</returns>
        public async Task<(AudioClip clip, UrlFlacLoader loader)> LoadFromUrlFlacAsync(
            string url, 
            string uuid,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url))
            {
                Plugin.Logger.LogWarning("[CoreAudioLoader] URL 为空");
                return (null, null);
            }

            UrlFlacLoader loader = null;
            
            try
            {
                Plugin.Logger.LogInfo($"[CoreAudioLoader] Loading FLAC from URL: {url}");
                
                loader = new UrlFlacLoader(url);
                
                // 开始下载并等待缓冲
                if (!await loader.StartLoadingAsync(cancellationToken))
                {
                    loader.Dispose();
                    return (null, null);
                }

                var reader = loader.FlacReader;
                if (reader == null)
                {
                    loader.Dispose();
                    return (null, null);
                }

                // 创建流式 AudioClip
                var clip = AudioClip.Create(
                    $"url_flac_{uuid ?? Guid.NewGuid().ToString("N")}",
                    (int)reader.TotalPcmFrames,
                    reader.Channels,
                    reader.SampleRate,
                    true, // streaming
                    (data) => OnFlacPcmReaderCallback(loader, data),
                    (position) => OnFlacSetPositionCallback(loader, position)
                );

                if (clip == null)
                {
                    Plugin.Logger.LogError("[CoreAudioLoader] Failed to create AudioClip");
                    loader.Dispose();
                    return (null, null);
                }

                Plugin.Logger.LogInfo($"[CoreAudioLoader] ✅ URL FLAC ready: {clip.name} ({clip.length:F1}s)");
                
                return (clip, loader);
            }
            catch (OperationCanceledException)
            {
                Plugin.Logger.LogDebug("[CoreAudioLoader] URL FLAC load cancelled");
                loader?.Dispose();
                return (null, null);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[CoreAudioLoader] URL FLAC load failed: {ex.Message}");
                loader?.Dispose();
                return (null, null);
            }
        }

        /// <summary>
        /// FLAC PCM 数据读取回调
        /// </summary>
        private static void OnFlacPcmReaderCallback(UrlFlacLoader loader, float[] data)
        {
            if (loader == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            try
            {
                int framesToRead = data.Length / (loader.FlacReader?.Channels ?? 2);
                loader.ReadFramesWithBuffering(data, framesToRead);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CoreAudioLoader] FLAC read error: {ex.Message}");
                Array.Clear(data, 0, data.Length);
            }
        }

        /// <summary>
        /// FLAC 位置设置回调
        /// </summary>
        private static void OnFlacSetPositionCallback(UrlFlacLoader loader, int position)
        {
            try
            {
                loader?.Seek((ulong)position);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CoreAudioLoader] FLAC seek error: {ex.Message}");
            }
        }

        /// <summary>
        /// 从可播放源加载音频
        /// </summary>
        /// <param name="source">可播放源</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载的 AudioClip</returns>
        public async Task<AudioClip> LoadFromPlayableSourceAsync(
            PlayableSource source, 
            CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                Plugin.Logger.LogWarning("[CoreAudioLoader] PlayableSource 无效");
                return null;
            }

            var path = source.GetPath();
            if (string.IsNullOrEmpty(path))
            {
                Plugin.Logger.LogWarning("[CoreAudioLoader] PlayableSource 路径为空");
                return null;
            }

            if (source.IsRemote)
            {
                if (source.IsExpired)
                {
                    Plugin.Logger.LogWarning($"[CoreAudioLoader] 流媒体 URL 已过期: {source.UUID}");
                    return null;
                }
                return await LoadFromUrlAsync(path, cancellationToken);
            }
            else
            {
                return await LoadFromFileAsync(path);
            }
        }

        public async Task<(AudioClip clip, string title, string artist)> LoadWithMetadataAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return (null, null, null);
            }

            if (!IsSupportedFormat(filePath))
            {
                return (null, null, null);
            }

            try
            {
                var result = await GameAudioInfo.DownloadAudioFile(filePath, CancellationToken.None);
                
                var clip = result.Item1;
                var title = result.Item2;
                var artist = result.Item3;

                // 如果没有标题，使用文件名
                if (string.IsNullOrEmpty(title))
                {
                    title = Path.GetFileNameWithoutExtension(filePath);
                }

                return (clip, title, artist);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"加载音频元数据失败 '{filePath}': {ex.Message}");
                return (null, null, null);
            }
        }

        public void UnloadClip(AudioClip clip)
        {
            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }

        /// <summary>
        /// 根据 URL 推断音频类型
        /// </summary>
        private AudioType GetAudioTypeFromUrl(string url)
        {
            // 移除查询参数
            var urlWithoutQuery = url.Contains("?") ? url.Substring(0, url.IndexOf("?")) : url;
            var extension = Path.GetExtension(urlWithoutQuery)?.ToLower();

            switch (extension)
            {
                case ".mp3":
                    return AudioType.MPEG;
                case ".wav":
                    return AudioType.WAV;
                case ".ogg":
                    return AudioType.OGGVORBIS;
                case ".flac":
                    // Unity 默认不支持 FLAC，但在某些平台可能通过 FMOD 支持
                    // 返回 UNKNOWN 让 Unity 尝试自动检测
                    return AudioType.UNKNOWN;
                case ".aiff":
                case ".aif":
                    return AudioType.AIFF;
                default:
                    // 默认尝试 MP3（大多数流媒体服务返回 MP3）
                    return AudioType.MPEG;
            }
        }
    }
}

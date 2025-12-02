using HarmonyLib;
using Bulbul;
using NestopiSystem.DIContainers;
using System;
using UnityEngine;
using ChillPatcher.UIFramework.Audio;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// 系统音频监控补丁 - 自动检测其他音频并调整游戏音量
    /// </summary>
    [HarmonyPatch]
    public class SystemAudioMonitor_Patches
    {
        /// <summary>
        /// 在 AudioMixerGroupContainer.GetDecibel 中应用音量乘数
        /// </summary>
        [HarmonyPatch(typeof(AudioMixerGroupContainer), "GetDecibel")]
        [HarmonyPrefix]
        static void GetDecibel_Prefix(AudioMixerType audioMixerType, ref float __state)
        {
            // 只对音乐音量应用乘数
            if (audioMixerType != AudioMixerType.Music)
            {
                __state = -1f;
                return;
            }

            // 保存当前音乐音量值
            __state = SaveDataManager.Instance?.SettingData?.MusicVolumeInfo?.Value ?? 1f;

            // 如果监控正在运行，应用音量乘数
            if (SystemAudioMonitor.Instance?.IsRunning == true)
            {
                float multiplier = SystemAudioMonitor.Instance.CurrentVolumeMultiplier;
                if (multiplier < 1f && __state > 0)
                {
                    // 临时修改音量值（会在 Postfix 中恢复）
                    // 注意：这里不能直接修改 .Value，因为那会触发事件
                    // 我们使用 Harmony Transpiler 或者另一种方式
                }
            }
        }

        /// <summary>
        /// 在 FacilityMusic.Setup 之后初始化音频监控器
        /// </summary>
        [HarmonyPatch(typeof(FacilityMusic), "Setup")]
        [HarmonyPostfix]
        static void FacilityMusic_Setup_Postfix()
        {
            try
            {
                // 初始化主线程调度器
                MainThreadDispatcher.Initialize();

                // 初始化音频监控器
                if (PluginConfig.EnableAutoMuteOnOtherAudio.Value)
                {
                    SystemAudioMonitor.Instance.Initialize();
                    Plugin.Log.LogInfo("[SystemAudioMonitor] 音频监控已启动");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SystemAudioMonitor] 初始化失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 更精确的音量控制补丁 - 通过补丁 GetDecibel 方法来应用音量乘数
    /// </summary>
    [HarmonyPatch]
    public class AudioVolumeMultiplier_Patch
    {
        // 静态存储音量乘数
        private static float _volumeMultiplier = 1f;

        /// <summary>
        /// 设置音量乘数
        /// </summary>
        public static void SetVolumeMultiplier(float multiplier)
        {
            _volumeMultiplier = Mathf.Clamp(multiplier, 0f, 1f);
            
            // 触发音量更新
            try
            {
                var container = ProjectLifetimeScope.Resolve<AudioMixerGroupContainer>();
                container?.ManualUpdateVolume(AudioMixerType.Music);
            }
            catch { }
        }

        /// <summary>
        /// 获取音量乘数
        /// </summary>
        public static float GetVolumeMultiplier() => _volumeMultiplier;

        /// <summary>
        /// 修改 GetDecibel 的返回值，应用音量乘数
        /// ✅ 使用 Publicizer 直接访问 private 字段
        /// </summary>
        [HarmonyPatch(typeof(AudioMixerGroupContainer), "GetDecibel")]
        [HarmonyPostfix]
        static void GetDecibel_Postfix(AudioMixerType audioMixerType, ref float __result)
        {
            // 只对音乐音量应用乘数
            if (audioMixerType != AudioMixerType.Music)
                return;

            // 如果乘数是 1，不需要修改
            if (_volumeMultiplier >= 0.999f)
                return;

            // 将分贝值转换为线性值，应用乘数，再转换回分贝
            // decibel = 20 * log10(volume)
            // volume = 10^(decibel/20)
            float linearVolume = (float)Math.Pow(10, __result / 20.0);
            linearVolume *= _volumeMultiplier;
            
            // 避免 log(0)
            if (linearVolume < 0.0001f)
            {
                __result = -80f; // 静音
            }
            else
            {
                __result = 20f * (float)Math.Log10(linearVolume);
            }
        }
    }
}

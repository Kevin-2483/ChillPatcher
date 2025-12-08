using HarmonyLib;
using Bulbul;
using KanKikuchi.AudioManager;
using UnityEngine;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 拦截 MusicService.SetMusicProgress，支持流媒体的延迟 Seek
    /// 
    /// 当用户拖动进度条时：
    /// 1. 如果是流媒体歌曲且缓存未下载完成，设置延迟 Seek
    /// 2. 进度条停在用户选择的位置
    /// 3. 缓存下载完成后，自动 Seek 到该位置
    /// </summary>
    [HarmonyPatch]
    public static class MusicService_SetProgress_Patch
    {
        /// <summary>
        /// 当前活跃的 PCM 流读取器（用于检测 Seek 状态）
        /// </summary>
        public static ChillPatcher.SDK.Interfaces.IPcmStreamReader ActivePcmReader { get; set; }

        /// <summary>
        /// 是否正在从 SetProgress 进行 Seek（防止 PCMSetPositionCallback 重复 Seek）
        /// </summary>
        public static bool IsSeekingFromSetProgress { get; set; }

        /// <summary>
        /// 上次 Seek 的目标帧（用于防抖）
        /// </summary>
        private static ulong _lastSeekFrame;
        private static System.DateTime _lastSeekTime;

        /// <summary>
        /// 设置活跃的 PCM 读取器
        /// </summary>
        public static void SetActivePcmReader(ChillPatcher.SDK.Interfaces.IPcmStreamReader reader)
        {
            // 如果有旧的待定 Seek，取消它
            if (ActivePcmReader != null && ActivePcmReader != reader)
            {
                FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = false;
                FacilityMusic_UpdateFacility_Patch.PendingSeekProgress = 0f;
            }
            ActivePcmReader = reader;
        }

        /// <summary>
        /// 清除活跃的 PCM 读取器
        /// </summary>
        public static void ClearActivePcmReader()
        {
            ActivePcmReader = null;
            FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = false;
            FacilityMusic_UpdateFacility_Patch.PendingSeekProgress = 0f;
            
            // 重置进度跟踪
            MusicService_GetProgress_Patch.ResetProgress();
        }

        [HarmonyPatch(typeof(MusicService), nameof(MusicService.SetMusicProgress))]
        [HarmonyPrefix]
        public static bool SetMusicProgress_Prefix(MusicService __instance, float progress)
        {
            var playingMusic = __instance.PlayingMusic;
            if (playingMusic == null || string.IsNullOrEmpty(playingMusic.AudioClipName))
            {
                return true; // 使用原始逻辑
            }

            // 检查是否是流媒体歌曲
            var music = MusicRegistry.Instance?.GetMusic(playingMusic.UUID);
            if (music == null || music.SourceType != MusicSourceType.Stream)
            {
                return true; // 不是流媒体，使用原始逻辑
            }

            // 获取 AudioPlayer
            var musicManager = SingletonMonoBehaviour<MusicManager>.Instance;
            var player = musicManager.GetPlayer(playingMusic.AudioClip);
            
            if (player == null || player.AudioSource == null || player.AudioSource.clip == null)
            {
                Plugin.Log.LogWarning("[SetProgress_Patch] No audio player found");
                return false; // 跳过原始逻辑
            }

            var clip = player.AudioSource.clip;
            var targetFrame = (ulong)(clip.samples * progress);

            // 检查是否有活跃的 PCM 读取器
            if (ActivePcmReader != null)
            {
                // 防抖：如果刚刚 Seek 到相同位置（500ms 内），跳过
                var now = System.DateTime.Now;
                if (targetFrame == _lastSeekFrame && (now - _lastSeekTime).TotalMilliseconds < 500)
                {
                    return false; // 跳过重复 Seek
                }

                // 尝试 Seek
                bool success = ActivePcmReader.Seek(targetFrame);
                
                if (success)
                {
                    // 记录 Seek 位置和时间（用于防抖）
                    _lastSeekFrame = targetFrame;
                    _lastSeekTime = now;

                    // Seek 成功或已设置延迟 Seek
                    // 使用接口属性检查是否是延迟 Seek
                    if (ActivePcmReader.HasPendingSeek)
                    {
                        // 延迟 Seek - 设置等待状态
                        FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = true;
                        FacilityMusic_UpdateFacility_Patch.PendingSeekProgress = progress;
                        Plugin.Log.LogInfo($"[SetProgress_Patch] Pending seek set to {progress:P1}, waiting for cache (progress: {ActivePcmReader.CacheProgress:F1}%)");
                        return false; // 跳过原始逻辑
                    }
                    
                    // 立即 Seek 成功
                    FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = false;
                    FacilityMusic_UpdateFacility_Patch.PendingSeekProgress = 0f;
                    
                    // 同步更新 AudioSource.time（虽然 PCM 流控制实际位置，但需要同步 UI）
                    // 设置标志防止 PCMSetPositionCallback 重复 Seek
                    IsSeekingFromSetProgress = true;
                    try
                    {
                        float length = clip.length;
                        player.AudioSource.time = Mathf.Clamp(length * progress, 0f, length);
                    }
                    finally
                    {
                        IsSeekingFromSetProgress = false;
                    }
                    
                    Plugin.Log.LogInfo($"[SetProgress_Patch] Seek succeeded to {progress:P1}");
                    return false; // 跳过原始逻辑
                }
                else
                {
                    // Seek 失败
                    Plugin.Log.LogWarning($"[SetProgress_Patch] Seek failed");
                    return false; // 跳过原始逻辑
                }
            }

            // 没有 PCM 读取器，使用原始逻辑
            return true;
        }

        /// <summary>
        /// 当延迟 Seek 完成时调用（由 Go 端触发）
        /// </summary>
        public static void OnPendingSeekCompleted()
        {
            if (FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek)
            {
                FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = false;
                Plugin.Log.LogInfo("[SetProgress_Patch] Pending seek completed, resuming progress updates");
            }
        }
    }
}

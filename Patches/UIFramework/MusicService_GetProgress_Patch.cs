using HarmonyLib;
using Bulbul;
using KanKikuchi.AudioManager;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 拦截 MusicService.GetCurrentMusicProgress，支持流媒体的正确进度显示
    /// 
    /// 问题：
    /// 1. 原始实现使用 AudioSource.time / clip.length 计算进度
    /// 2. 对于流式 PCM 播放，AudioSource.time 可能不准确（特别是 Seek 后）
    /// 3. 流暂停时（网络波动、等待缓存），进度条应该也暂停
    /// 4. Go 端完成待定 Seek 后，C# 端不会收到通知
    /// 
    /// 解决方案：
    /// 1. 对于流媒体歌曲，使用 PCM reader 的 CurrentFrame / TotalFrames 计算进度
    /// 2. 直接从 PCM reader 检查 HasPendingSeek，不依赖 C# 端的标志
    /// 3. 当 HasPendingSeek 时，返回待定位置的进度
    /// 4. Seek 完成后自动恢复正常进度更新
    /// </summary>
    [HarmonyPatch]
    public static class MusicService_GetProgress_Patch
    {
        /// <summary>
        /// 上次有效的进度值（用于流暂停时保持进度）
        /// </summary>
        private static float _lastValidProgress = 0f;

        [HarmonyPatch(typeof(MusicService), nameof(MusicService.GetCurrentMusicProgress))]
        [HarmonyPrefix]
        public static bool GetCurrentMusicProgress_Prefix(MusicService __instance, ref float __result)
        {
            var playingMusic = __instance.PlayingMusic;
            if (playingMusic == null || string.IsNullOrEmpty(playingMusic.AudioClipName))
            {
                __result = 0f;
                return false; // 跳过原始逻辑
            }

            // 检查是否是流媒体歌曲
            var music = MusicRegistry.Instance?.GetMusic(playingMusic.UUID);
            if (music == null || music.SourceType != MusicSourceType.Stream)
            {
                // 不是流媒体，使用原始逻辑
                return true;
            }

            // 获取活跃的 PCM 读取器
            var reader = MusicService_SetProgress_Patch.ActivePcmReader;
            
            // 直接从 PCM reader 检查是否有待定 Seek
            // 不再依赖 IsWaitingForSeek 标志（Go 端完成 Seek 后不会通知 C#）
            if (reader != null && reader.HasPendingSeek)
            {
                // 有待定 Seek，返回待定位置的进度
                if (reader.Info.TotalFrames > 0 && reader.PendingSeekFrame >= 0)
                {
                    __result = (float)reader.PendingSeekFrame / (float)reader.Info.TotalFrames;
                }
                else
                {
                    __result = FacilityMusic_UpdateFacility_Patch.PendingSeekProgress;
                }
                return false;
            }
            
            // Seek 已完成，清除等待标志
            if (FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek)
            {
                FacilityMusic_UpdateFacility_Patch.IsWaitingForSeek = false;
                FacilityMusic_UpdateFacility_Patch.PendingSeekProgress = 0f;
            }

            if (reader != null && reader.Info.TotalFrames > 0)
            {
                // 使用 PCM reader 的真实位置计算进度
                float progress = (float)reader.CurrentFrame / (float)reader.Info.TotalFrames;
                
                // 限制在 0-1 范围内
                progress = UnityEngine.Mathf.Clamp01(progress);
                
                _lastValidProgress = progress;
                __result = progress;
                
                return false; // 跳过原始逻辑
            }

            // 没有有效的 PCM reader，尝试使用原始逻辑
            // 但先检查是否有 AudioPlayer
            var musicManager = SingletonMonoBehaviour<MusicManager>.Instance;
            var player = playingMusic.AudioClip != null 
                ? musicManager.GetPlayer(playingMusic.AudioClip) 
                : null;

            if (player != null && player.AudioSource != null && player.AudioSource.clip != null)
            {
                __result = player.AudioSource.time / player.AudioSource.clip.length;
                return false;
            }

            // 无法获取进度，返回上次有效值或 0
            __result = _lastValidProgress;
            return false;
        }

        /// <summary>
        /// 重置进度（切换歌曲时调用）
        /// </summary>
        public static void ResetProgress()
        {
            _lastValidProgress = 0f;
        }
    }
}

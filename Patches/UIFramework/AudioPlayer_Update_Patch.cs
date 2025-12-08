using HarmonyLib;
using KanKikuchi.AudioManager;
using UnityEngine;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Models;
using Bulbul;
using NestopiSystem.DIContainers;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 修复 AudioPlayer.Update 对流媒体播放结束的错误判断
    /// 
    /// 问题：
    /// 1. AudioPlayer.Update() 通过检测 !isPlaying && time == 0 来判断歌曲结束
    /// 2. 对于流式 PCM 播放，当等待数据时（网络加载、Seek 等待缓存），
    ///    Unity 可能报告 isPlaying = false，但歌曲实际上还没播放完
    /// 3. 这会导致歌曲提前跳到下一首
    /// 
    /// 解决方案：
    /// 1. 拦截 AudioPlayer.Update()
    /// 2. 通过检查 clip.name 是否以 "pcm_stream_" 开头来快速判断是否是流媒体
    ///    这样可以避免影响语音、音效等其他 AudioPlayer
    /// 3. 对于流媒体歌曲，使用 PCM reader 的 IsEndOfStream 来判断是否真正结束
    /// 4. 如果 PCM reader 还没结束，不触发 Finish()
    /// </summary>
    [HarmonyPatch]
    public static class AudioPlayer_Update_Patch
    {
        [HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.Update))]
        [HarmonyPrefix]
        public static bool Update_Prefix(AudioPlayer __instance)
        {
            // 只在 Playing 状态时进行特殊处理
            if (__instance.CurrentState != AudioPlayer.State.Playing)
            {
                return true; // 使用原始逻辑
            }

            var audioSource = __instance.AudioSource;
            if (audioSource == null || audioSource.clip == null)
            {
                return true; // 使用原始逻辑
            }

            // 快速检查：是否是流媒体相关的 AudioClip
            // 流媒体 clip 的名称以 "pcm_stream_" 开头
            var clipName = audioSource.clip.name;
            if (string.IsNullOrEmpty(clipName) || !clipName.StartsWith("pcm_stream_"))
            {
                // 不是流媒体音频，使用原始逻辑
                // 这样就不会影响语音、音效等其他 AudioPlayer
                return true;
            }

            // 是流媒体音乐，进行特殊处理
            var musicService = ProjectLifetimeScope.Resolve<MusicService>();
            if (musicService == null)
            {
                return true;
            }

            var playingMusic = musicService.PlayingMusic;
            if (playingMusic == null || string.IsNullOrEmpty(playingMusic.UUID))
            {
                return true;
            }

            var music = MusicRegistry.Instance?.GetMusic(playingMusic.UUID);
            if (music == null || music.SourceType != MusicSourceType.Stream)
            {
                // 不是流媒体，使用原始逻辑
                return true;
            }

            // ===== 流媒体特殊处理 =====
            
            // 获取 PCM 读取器
            var reader = MusicService_SetProgress_Patch.ActivePcmReader;
            
            // 原始的结束判断条件
            bool originalEndCondition = !audioSource.isPlaying && Mathf.Approximately(audioSource.time, 0f);

            if (originalEndCondition)
            {
                // 游戏认为已结束，但需要检查 PCM reader 的真实状态
                
                if (reader != null)
                {
                    // 检查播放进度
                    float progress = 0f;
                    if (reader.Info.TotalFrames > 0)
                    {
                        progress = (float)reader.CurrentFrame / (float)reader.Info.TotalFrames;
                    }

                    // 如果有待定的 Seek，不应该结束
                    if (reader.HasPendingSeek)
                    {
                        Plugin.Log.LogDebug("[AudioPlayer_Patch] Has pending seek, skipping finish");
                        return false;
                    }

                    // 检查是否真正到达末尾
                    // 满足以下任一条件即认为歌曲结束：
                    // 1. IsEndOfStream = true
                    // 2. 进度 >= 98%
                    bool isNearEnd = progress >= 0.98f;
                    bool isTrulyEnded = reader.IsEndOfStream || isNearEnd;
                    
                    if (!isTrulyEnded)
                    {
                        // PCM 流还没结束，可能是：
                        // 1. 等待网络数据
                        // 2. 等待 Seek 缓存下载
                        // 3. 其他暂时性暂停
                        
                        // 不触发 Finish，跳过原始逻辑
                        Plugin.Log.LogDebug($"[AudioPlayer_Patch] Stream not ended yet (progress={progress:P1}), skipping finish");
                        return false;
                    }
                    
                    // 确认是真正的结束
                    Plugin.Log.LogInfo($"[AudioPlayer_Patch] Stream truly ended (progress={progress:P1}, isEOF={reader.IsEndOfStream}), allowing finish");
                }
                else
                {
                    // 没有 PCM reader 但是是流媒体歌曲
                    // 可能是加载中，不应该结束
                    if (FacilityMusic_UpdateFacility_Patch.IsLoadingMusic)
                    {
                        Plugin.Log.LogDebug("[AudioPlayer_Patch] Music is loading, skipping finish");
                        return false;
                    }
                }
            }

            // 使用原始逻辑
            return true;
        }
    }
}

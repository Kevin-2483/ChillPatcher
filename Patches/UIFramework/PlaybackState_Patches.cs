using HarmonyLib;
using Bulbul;
using System;
using ChillPatcher.UIFramework.Music;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 播放状态恢复补丁 - 在游戏启动时恢复上次的播放状态
    /// </summary>
    [HarmonyPatch]
    public class PlaybackState_Patches
    {
        /// <summary>
        /// 在 FacilityMusic.Setup 之前初始化状态管理器并恢复Tag选择
        /// </summary>
        [HarmonyPatch(typeof(FacilityMusic), "Setup")]
        [HarmonyPrefix]
        static void Setup_Prefix(FacilityMusic __instance)
        {
            try
            {
                // 初始化状态管理器
                PlaybackStateManager.Instance.Initialize();

                // 在 MusicService.Setup() 调用之前恢复Tag选择
                // 这样当 MusicService.Setup() 订阅 CurrentAudioTag 时，就会使用恢复的值
                if (PlaybackStateManager.Instance.ApplySavedAudioTag())
                {
                    Plugin.Log.LogInfo("[PlaybackState] Applied saved AudioTag before MusicService.Setup");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaybackState] Error in Setup_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// 在 FacilityMusic.Setup 之后恢复播放位置并订阅事件
        /// </summary>
        [HarmonyPatch(typeof(FacilityMusic), "Setup")]
        [HarmonyPostfix]
        static void Setup_Postfix(FacilityMusic __instance)
        {
            try
            {
                var musicService = __instance.MusicService;
                if (musicService == null)
                {
                    Plugin.Log.LogWarning("[PlaybackState] MusicService is null in Postfix");
                    return;
                }

                // 订阅事件以监听后续的状态变化
                PlaybackStateManager.Instance.SubscribeToEvents(musicService);

                // 尝试恢复到上次播放的歌曲
                // 需要延迟执行，因为 Setup 结束后游戏会自动播放第一首歌
                // 使用协程延迟一帧执行
                DelayedPlaybackRestore(__instance, musicService);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaybackState] Error in Setup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// 延迟恢复播放位置
        /// </summary>
        private static async void DelayedPlaybackRestore(FacilityMusic facility, MusicService musicService)
        {
            try
            {
                // 等待一帧，让游戏完成初始播放
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(2);

                var savedUUID = PlaybackStateManager.Instance.GetSavedSongUUID();
                if (!string.IsNullOrEmpty(savedUUID))
                {
                    if (PlaybackStateManager.Instance.TryPlaySavedSong(musicService))
                    {
                        Plugin.Log.LogInfo($"[PlaybackState] Restored playback to saved song: {savedUUID}");
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"[PlaybackState] Could not restore saved song, playing from beginning");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaybackState] Error in DelayedPlaybackRestore: {ex.Message}");
            }
        }
    }
}

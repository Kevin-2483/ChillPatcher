using HarmonyLib;
using Bulbul;
using System;
using ChillPatcher.UIFramework.Music;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 播放状态恢复补丁 - 在游戏启动时恢复上次的播放状态
    /// ✅ 使用 Publicizer 直接访问 private 字段（消除反射开销）
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
                // 等待足够的帧数，让游戏完成初始播放和UI初始化
                await Cysharp.Threading.Tasks.UniTask.DelayFrame(5);

                // 首先恢复队列和历史
                var allMusic = musicService.AllMusicList;
                if (PlaybackStateManager.Instance.TryRestoreQueueAndHistory(allMusic))
                {
                    Plugin.Log.LogInfo("[PlaybackState] Restored queue and history");
                }

                var savedUUID = PlaybackStateManager.Instance.GetSavedSongUUID();
                if (!string.IsNullOrEmpty(savedUUID))
                {
                    if (PlaybackStateManager.Instance.TryPlaySavedSong(musicService))
                    {
                        Plugin.Log.LogInfo($"[PlaybackState] Restored playback to saved song: {savedUUID}");
                        
                        // 等待几帧后刷新所有按钮的播放状态
                        await Cysharp.Threading.Tasks.UniTask.DelayFrame(3);
                        
                        // 更新 FacilityMusic 的播放状态和 UI
                        UpdateFacilityMusicPlayState(facility);
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

        /// <summary>
        /// 更新 FacilityMusic 的播放状态，包括 _mainState 和 MusicUI 的显示
        /// ✅ 使用 Publicizer 直接访问 private 字段（消除反射开销）
        /// </summary>
        private static void UpdateFacilityMusicPlayState(FacilityMusic facility)
        {
            try
            {
                if (facility == null)
                {
                    Plugin.Log.LogWarning("[PlaybackState] FacilityMusic is null, cannot update play state");
                    return;
                }

                // ✅ 直接访问 _mainState 字段（Publicizer 消除反射）
                facility._mainState = FacilityMusic.MainState.Playing;
                Plugin.Log.LogInfo("[PlaybackState] Set FacilityMusic._mainState to Playing");

                // ✅ 直接访问 _musicUI 字段并调用 OnPlayMusic()（Publicizer 消除反射）
                if (facility._musicUI != null)
                {
                    facility._musicUI.OnPlayMusic();
                    Plugin.Log.LogInfo("[PlaybackState] Called MusicUI.OnPlayMusic() to update UI");
                }
                else
                {
                    Plugin.Log.LogWarning("[PlaybackState] MusicUI is null");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaybackState] Error updating play state: {ex.Message}");
            }
        }
    }
}

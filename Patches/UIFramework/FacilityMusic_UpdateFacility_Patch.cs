using HarmonyLib;
using Bulbul;
using KanKikuchi.AudioManager;
using NestopiSystem;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 修复 FacilityMusic.UpdateFacility 在流媒体播放时错误判断暂停状态的问题
    /// 
    /// 问题原因：
    /// 1. 原始代码通过 MusicManager.GetPlayer(PlayingMusic.AudioClip) 检查是否有播放器
    ///    如果返回 null 且 _mainState 是 Playing，就会调用 PauseMusic()
    /// 
    /// 2. 对于流媒体/模块导入的歌曲，AudioClip 是动态创建的，和 MusicManager 中的播放器不匹配
    ///    导致即使音乐正在播放，也会被错误地判断为需要暂停
    /// 
    /// 3. 在异步加载歌曲期间，旧歌曲已停止但新歌曲还没开始，IsPlaying() 也返回 false
    ///    这时也会错误地调用 PauseMusic()
    /// 
    /// 修复方案：
    /// 1. 额外检查 MusicManager.IsPlaying()，如果有任何音乐正在播放，则不执行暂停
    /// 2. 添加 IsLoadingMusic 标志，在异步加载期间阻止暂停
    /// 3. 添加 IsWaitingForSeek 标志，在等待缓存下载完成时阻止进度条更新
    /// </summary>
    [HarmonyPatch]
    public static class FacilityMusic_UpdateFacility_Patch
    {
        /// <summary>
        /// 是否正在加载音乐（异步加载期间设为 true）
        /// </summary>
        public static bool IsLoadingMusic { get; set; } = false;
        
        /// <summary>
        /// 是否正在等待 Seek（缓存下载中设为 true）
        /// 当此标志为 true 时，进度条不会更新
        /// </summary>
        public static bool IsWaitingForSeek { get; set; } = false;

        /// <summary>
        /// 等待 Seek 的目标进度（0-1）
        /// </summary>
        public static float PendingSeekProgress { get; set; } = 0f;
        
        /// <summary>
        /// 拦截 UpdateFacility，修复流媒体播放时的状态判断
        /// </summary>
        [HarmonyPatch(typeof(FacilityMusic), nameof(FacilityMusic.UpdateFacility))]
        [HarmonyPrefix]
        public static bool UpdateFacility_Prefix(FacilityMusic __instance)
        {
            // 获取当前状态
            var mainState = __instance._mainState;
            
            // 只在 Playing 状态时进行特殊处理
            if (mainState != FacilityMusic.MainState.Playing)
            {
                // Idle 或其他状态，使用原始逻辑
                return true;
            }
            
            var playingMusic = __instance.PlayingMusic;
            if (playingMusic == null)
            {
                // 没有正在播放的音乐，使用原始逻辑
                return true;
            }
            
            var musicManager = SingletonMonoBehaviour<MusicManager>.Instance;
            var musicUI = __instance._musicUI;
            var musicService = __instance.MusicService;
            
            // 检查是否有播放器（原始检查方式）
            var player = playingMusic.AudioClip != null 
                ? musicManager.GetPlayer(playingMusic.AudioClip) 
                : null;
            
            if (player != null)
            {
                // 原始逻辑：有播放器，更新进度条
                // MusicService_GetProgress_Patch 会处理流媒体的特殊情况（等待 Seek 等）
                musicUI.UpdateProgressBar(musicService.GetCurrentMusicProgress());
                return false; // 跳过原始方法
            }
            
            // ====== 修复核心逻辑 ======
            
            // 检查1：是否正在异步加载音乐
            if (IsLoadingMusic)
            {
                // 正在加载中，不暂停
                Plugin.Log.LogDebug("[FacilityMusic_Patch] Music is loading, skipping pause");
                return false; // 跳过原始方法
            }
            
            // 检查2：MusicManager 是否有任何音乐正在播放
            // 这会检测流媒体/模块导入歌曲的播放状态
            if (musicManager.IsPlaying())
            {
                // 有音乐正在播放（可能是流媒体），更新进度条
                // 但不调用 PauseMusic()
                // MusicService_GetProgress_Patch 会处理流媒体的特殊情况（等待 Seek 等）
                musicUI.UpdateProgressBar(musicService.GetCurrentMusicProgress());
                Plugin.Log.LogDebug("[FacilityMusic_Patch] Music is playing via streaming, skipping pause");
                return false; // 跳过原始方法
            }
            
            // 确实没有音乐在播放且不在加载中，调用暂停
            __instance.PauseMusic();
            Plugin.Log.LogDebug("[FacilityMusic_Patch] No music playing, calling PauseMusic");
            return false; // 跳过原始方法
        }
    }
}

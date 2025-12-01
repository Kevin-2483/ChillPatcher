using Bulbul;
using HarmonyLib;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 拦截MusicService的排除列表操作，对自定义Tag使用独立数据库
    /// </summary>
    [HarmonyPatch(typeof(MusicService))]
    public class MusicService_Excluded_Patch
    {
        /// <summary>
        /// 歌曲排除状态变化事件
        /// </summary>
        public static event System.Action<string, bool> OnSongExcludedChanged;  // (songUUID, isExcluded)

        /// <summary>
        /// Patch ExcludeFromPlaylist - 排除歌曲
        /// </summary>
        [HarmonyPatch("ExcludeFromPlaylist")]
        [HarmonyPrefix]
        static bool ExcludeFromPlaylist_Prefix(MusicService __instance, GameAudioInfo gameAudioInfo, ref bool __result)
        {
            try
            {
                // 检查是否是自定义Tag
                if (ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.IsCustomTag(gameAudioInfo.Tag))
                {
                    // 检查是否已经在排除列表中
                    if (__instance.IsContainsExcludedFromPlaylist(gameAudioInfo))
                    {
                        __result = false;
                        return false; // 跳过原方法
                    }

                    var tagId = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.GetTagIdFromAudio(gameAudioInfo);
                    if (!string.IsNullOrEmpty(tagId))
                    {
                        var manager = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.Instance;
                        if (manager != null)
                        {
                            // 使用独立数据库保存
                            __result = manager.AddExcluded(tagId, gameAudioInfo.UUID);
                            
                            // 触发状态变化事件
                            if (__result)
                            {
                                OnSongExcludedChanged?.Invoke(gameAudioInfo.UUID, true);
                            }
                            
                            return false; // 跳过原方法
                        }
                    }
                }

                // 游戏原生Tag使用原方法
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[ExcludedPatch] 排除歌曲失败: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Patch IncludeInPlaylist - 重新包含歌曲
        /// </summary>
        [HarmonyPatch("IncludeInPlaylist")]
        [HarmonyPrefix]
        static bool IncludeInPlaylist_Prefix(MusicService __instance, GameAudioInfo gameAudioInfo, ref bool __result)
        {
            try
            {
                // 检查是否是自定义Tag
                if (ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.IsCustomTag(gameAudioInfo.Tag))
                {
                    // 检查是否不在排除列表中
                    if (!__instance.IsContainsExcludedFromPlaylist(gameAudioInfo))
                    {
                        __result = false;
                        return false; // 跳过原方法
                    }

                    var tagId = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.GetTagIdFromAudio(gameAudioInfo);
                    if (!string.IsNullOrEmpty(tagId))
                    {
                        var manager = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.Instance;
                        if (manager != null)
                        {
                            // 使用独立数据库移除
                            __result = manager.RemoveExcluded(tagId, gameAudioInfo.UUID);
                            
                            // 触发状态变化事件
                            if (__result)
                            {
                                OnSongExcludedChanged?.Invoke(gameAudioInfo.UUID, false);
                            }
                            
                            return false; // 跳过原方法
                        }
                    }
                }

                // 游戏原生Tag使用原方法
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[ExcludedPatch] 包含歌曲失败: {ex}");
                return true;
            }
        }

        /// <summary>
        /// Patch IsContainsExcludedFromPlaylist - 检查是否在排除列表
        /// </summary>
        [HarmonyPatch("IsContainsExcludedFromPlaylist")]
        [HarmonyPrefix]
        static bool IsContainsExcludedFromPlaylist_Prefix(GameAudioInfo gameAudioInfo, ref bool __result)
        {
            try
            {
                // 检查是否是自定义Tag
                if (ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.IsCustomTag(gameAudioInfo.Tag))
                {
                    var tagId = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.GetTagIdFromAudio(gameAudioInfo);
                    if (!string.IsNullOrEmpty(tagId))
                    {
                        var manager = ChillPatcher.UIFramework.Data.CustomPlaylistDataManager.Instance;
                        if (manager != null)
                        {
                            // 从独立数据库检查
                            __result = manager.IsExcluded(tagId, gameAudioInfo.UUID);
                            return false; // 跳过原方法
                        }
                    }
                }

                // 游戏原生Tag使用原方法
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[ExcludedPatch] 检查排除状态失败: {ex}");
                return true;
            }
        }
    }
}

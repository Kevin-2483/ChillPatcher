using System;
using System.Collections.Generic;
using System.Linq;
using Bulbul;
using ChillPatcher.ModuleSystem;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using HarmonyLib;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 拦截MusicService的播放顺序操作，通过事件通知模块
    /// </summary>
    [HarmonyPatch(typeof(MusicService))]
    public class MusicService_PlaylistOrder_Patch
    {
        /// <summary>
        /// 拦截添加音乐到播放列表
        /// </summary>
        [HarmonyPatch("AddMusicItem")]
        [HarmonyPostfix]
        static void AddMusicItem_Postfix(bool __result, GameAudioInfo music)
        {
            try
            {
                if (!__result || music == null)
                    return;

                var musicInfo = MusicRegistry.Instance?.GetMusic(music.UUID);
                if (musicInfo != null)
                {
                    EventBus.Instance?.Publish(new PlaylistOrderChangedEvent
                    {
                        UpdateType = PlaylistUpdateType.SongAdded,
                        AffectedSongUUIDs = new string[] { music.UUID },
                        ModuleId = musicInfo.ModuleId
                    });
                    Plugin.Log.LogInfo($"[PlaylistOrder] Music added: {music.UUID}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistOrder] Add failed: {ex}");
            }
        }

        /// <summary>
        /// 拦截添加本地音乐
        /// </summary>
        [HarmonyPatch("AddLocalMusicItem")]
        [HarmonyPostfix]
        static void AddLocalMusicItem_Postfix(bool __result, GameAudioInfo music)
        {
            try
            {
                if (!__result || music == null)
                    return;

                var musicInfo = MusicRegistry.Instance?.GetMusic(music.UUID);
                if (musicInfo != null)
                {
                    EventBus.Instance?.Publish(new PlaylistOrderChangedEvent
                    {
                        UpdateType = PlaylistUpdateType.SongAdded,
                        AffectedSongUUIDs = new string[] { music.UUID },
                        ModuleId = musicInfo.ModuleId
                    });
                    Plugin.Log.LogInfo($"[PlaylistOrder] Local music added: {music.UUID}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistOrder] Add local failed: {ex}");
            }
        }

        /// <summary>
        /// 拦截移除本地音乐 - Prefix 处理模块歌曲
        /// 如果是模块歌曲，调用模块的 IDeleteHandler.Delete 方法
        /// </summary>
        [HarmonyPatch("RemoveLocalMusicItem")]
        [HarmonyPrefix]
        static bool RemoveLocalMusicItem_Prefix(MusicService __instance, GameAudioInfo music)
        {
            try
            {
                if (music == null)
                    return true;  // 执行原方法

                // 检查是否是模块注册的歌曲
                var musicInfo = MusicRegistry.Instance?.GetMusic(music.UUID);
                if (musicInfo == null)
                    return true;  // 不是模块歌曲，执行原方法

                // 获取模块
                var module = ModuleLoader.Instance?.GetModule(musicInfo.ModuleId);
                if (module == null)
                    return true;  // 模块不存在，执行原方法

                // 检查模块是否实现了 IDeleteHandler
                var deleteHandler = module as IDeleteHandler;
                if (deleteHandler == null || !deleteHandler.CanDelete)
                {
                    Plugin.Log.LogWarning($"[Delete] Module {musicInfo.ModuleId} does not support delete");
                    return false;  // 不执行原方法（模块不支持删除）
                }

                // 检查歌曲是否可删除
                if (musicInfo.IsDeletable.HasValue && !musicInfo.IsDeletable.Value)
                {
                    Plugin.Log.LogWarning($"[Delete] Song {music.UUID} is not deletable");
                    return false;  // 不执行原方法
                }

                // 调用模块的删除方法
                var success = deleteHandler.Delete(music.UUID);
                if (success)
                {
                    // 从游戏列表移除（不调用原方法的文件删除逻辑）
                    var allMusicList = Traverse.Create(__instance)
                        .Field("_allMusicList")
                        .GetValue<List<GameAudioInfo>>();
                    allMusicList?.Remove(music);

                    var shuffleList = Traverse.Create(__instance)
                        .Field("shuffleList")
                        .GetValue<List<GameAudioInfo>>();
                    shuffleList?.Remove(music);

                    __instance.CurrentPlayList?.Remove(music);

                    Plugin.Log.LogInfo($"[Delete] Module song deleted: {music.UUID}");
                }

                return false;  // 不执行原方法（已自行处理）
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Delete] Error: {ex}");
                return true;  // 出错时执行原方法
            }
        }

        /// <summary>
        /// 拦截移除本地音乐 - Postfix 发布事件
        /// </summary>
        [HarmonyPatch("RemoveLocalMusicItem")]
        [HarmonyPostfix]
        static void RemoveLocalMusicItem_Postfix(GameAudioInfo music)
        {
            try
            {
                if (music == null)
                    return;

                var musicInfo = MusicRegistry.Instance?.GetMusic(music.UUID);
                if (musicInfo != null)
                {
                    EventBus.Instance?.Publish(new PlaylistOrderChangedEvent
                    {
                        UpdateType = PlaylistUpdateType.SongRemoved,
                        AffectedSongUUIDs = new string[] { music.UUID },
                        ModuleId = musicInfo.ModuleId
                    });
                    Plugin.Log.LogInfo($"[PlaylistOrder] Music removed: {music.UUID}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistOrder] Remove failed: {ex}");
            }
        }

        /// <summary>
        /// 拦截交换顺序
        /// </summary>
        [HarmonyPatch("SwapAfter")]
        [HarmonyPostfix]
        static void SwapAfter_Postfix(MusicService __instance, GameAudioInfo target, GameAudioInfo origin)
        {
            try
            {
                if (target == null)
                    return;

                var isShuffle = Traverse.Create(__instance).Property("IsShuffle").GetValue<bool>();
                if (isShuffle)
                    return;

                var musicInfo = MusicRegistry.Instance?.GetMusic(target.UUID);
                if (musicInfo != null)
                {
                    var currentAudioTag = SaveDataManager.Instance.MusicSetting.CurrentAudioTag.CurrentValue;
                    if (currentAudioTag == AudioTag.Favorite)
                        return;

                    // 获取当前所有音乐的UUID顺序
                    var allMusicList = Traverse.Create(__instance)
                        .Field("_allMusicList")
                        .GetValue<List<GameAudioInfo>>();

                    if (allMusicList != null)
                    {
                        var sameTagUuids = allMusicList
                            .Where(m => MusicRegistry.Instance?.GetMusic(m.UUID)?.TagId == musicInfo.TagId)
                            .Select(m => m.UUID)
                            .ToArray();

                        EventBus.Instance?.Publish(new PlaylistOrderChangedEvent
                        {
                            UpdateType = PlaylistUpdateType.Reordered,
                            AffectedSongUUIDs = sameTagUuids,
                            ModuleId = musicInfo.ModuleId
                        });
                        
                        Plugin.Log.LogInfo($"[PlaylistOrder] Order updated: {sameTagUuids.Length} songs");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PlaylistOrder] Swap failed: {ex}");
            }
        }
    }
}

using System;
using Bulbul;
using ChillPatcher.ModuleSystem;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using HarmonyLib;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 拦截MusicService的收藏操作，通过事件通知模块处理
    /// </summary>
    [HarmonyPatch(typeof(MusicService))]
    public class MusicService_Favorite_Patch
    {
        /// <summary>
        /// 收藏状态变化事件 (songUUID, isFavorite)
        /// 用于通知 UI 刷新
        /// </summary>
        public static event Action<string, bool> OnSongFavoriteChanged;

        /// <summary>
        /// 拦截添加收藏
        /// </summary>
        [HarmonyPatch("RegisterFavoriteMusic")]
        [HarmonyPrefix]
        static bool RegisterFavoriteMusic_Prefix(GameAudioInfo gameAudioInfo)
        {
            try
            {
                if (gameAudioInfo == null)
                    return true;

                if (gameAudioInfo.Tag.HasFlagFast(AudioTag.Favorite))
                    return false;

                // 检查是否是模块注册的歌曲
                var musicInfo = MusicRegistry.Instance?.GetMusic(gameAudioInfo.UUID);
                if (musicInfo != null)
                {
                    // 模块歌曲 - 添加收藏标记并通过事件通知
                    gameAudioInfo.Tag = gameAudioInfo.Tag | AudioTag.Favorite;
                    
                    // 发布收藏事件，让模块处理持久化
                    EventBus.Instance?.Publish(new FavoriteChangedEvent
                    {
                        Music = musicInfo,
                        IsFavorite = true,
                        ModuleId = musicInfo.ModuleId
                    });
                    
                    // 触发 UI 刷新事件
                    OnSongFavoriteChanged?.Invoke(gameAudioInfo.UUID, true);
                    
                    Plugin.Log.LogInfo($"[Favorite] Module song favorited: {gameAudioInfo.UUID}");
                    return false; // 不执行原逻辑
                }
                
                // 非模块歌曲 - 执行原逻辑
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Favorite] Add failed: {ex}");
                return true;
            }
        }

        /// <summary>
        /// 拦截移除收藏
        /// </summary>
        [HarmonyPatch("UnregisterFavoriteMusic")]
        [HarmonyPrefix]
        static bool UnregisterFavoriteMusic_Prefix(MusicService __instance, GameAudioInfo gameAudioInfo)
        {
            try
            {
                if (gameAudioInfo == null)
                    return true;

                if (!gameAudioInfo.Tag.HasFlagFast(AudioTag.Favorite))
                    return false;

                // 检查是否是模块注册的歌曲
                var musicInfo = MusicRegistry.Instance?.GetMusic(gameAudioInfo.UUID);
                if (musicInfo != null)
                {
                    // 模块歌曲 - 移除收藏标记并通过事件通知
                    gameAudioInfo.Tag = gameAudioInfo.Tag & ~AudioTag.Favorite;
                    
                    // 更新播放列表
                    var currentAudioTag = SaveDataManager.Instance.MusicSetting.CurrentAudioTag;
                    var currentValue = currentAudioTag.CurrentValue;
                    
                    if (!currentValue.HasFlagFast(gameAudioInfo.Tag))
                    {
                        var currentPlayList = Traverse.Create(__instance)
                            .Field("CurrentPlayList")
                            .GetValue<System.Collections.Generic.List<GameAudioInfo>>();
                        var shuffleList = Traverse.Create(__instance)
                            .Field("shuffleList")
                            .GetValue<System.Collections.Generic.List<GameAudioInfo>>();
                        
                        currentPlayList?.Remove(gameAudioInfo);
                        shuffleList?.Remove(gameAudioInfo);
                    }
                    
                    // 发布收藏事件，让模块处理持久化
                    EventBus.Instance?.Publish(new FavoriteChangedEvent
                    {
                        Music = musicInfo,
                        IsFavorite = false,
                        ModuleId = musicInfo.ModuleId
                    });
                    
                    // 触发 UI 刷新事件
                    OnSongFavoriteChanged?.Invoke(gameAudioInfo.UUID, false);
                    
                    Plugin.Log.LogInfo($"[Favorite] Module song unfavorited: {gameAudioInfo.UUID}");
                    return false; // 不执行原逻辑
                }
                
                // 非模块歌曲 - 执行原逻辑
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Favorite] Remove failed: {ex}");
                return true;
            }
        }
    }
}

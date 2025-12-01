using Bulbul;
using ChillPatcher.UIFramework;
using ChillPatcher.UIFramework.Music;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// MusicUI Patches - 集成虚拟滚动（可配置）
    /// 默认开启，不影响存档，仅优化性能
    /// </summary>
    [HarmonyPatch]
    public class MusicUI_VirtualScroll_Patch
    {
        private static bool _componentsInitialized = false;
        private static bool _mixedComponentsInitialized = false;

        /// <summary>
        /// Patch MusicUI.Setup - 初始化虚拟滚动组件
        /// 使用Prefix确保在Setup内部的ViewPlayList调用之前完成初始化
        /// </summary>
        [HarmonyPatch(typeof(MusicUI), "Setup")]
        [HarmonyPrefix]
        static void Setup_Prefix(MusicUI __instance)
        {
            if (!UIFrameworkConfig.EnableVirtualScroll.Value)
                return;

            try
            {
                // 获取UI组件
                var scrollRect = Traverse.Create(__instance)
                    .Field("scrollRect")
                    .GetValue<UnityEngine.UI.ScrollRect>();

                var playListButtonsPrefab = Traverse.Create(__instance)
                    .Field("_playListButtonsPrefab")
                    .GetValue<GameObject>();

                var playListButtonsParent = Traverse.Create(__instance)
                    .Field("_playListButtonsParent")
                    .GetValue<GameObject>();

                if (scrollRect == null || playListButtonsPrefab == null || playListButtonsParent == null)
                {
                    Plugin.Log.LogError("Failed to get UI components for VirtualScroll");
                    return;
                }

                var musicManager = ChillUIFramework.Music as MusicUIManager;
                
                // 根据配置初始化对应的控制器
                if (UIFrameworkConfig.EnableAlbumSeparators.Value)
                {
                    // 使用混合虚拟滚动（支持专辑分隔）
                    if (!_mixedComponentsInitialized && musicManager?.MixedVirtualScroll != null)
                    {
                        musicManager.MixedVirtualScroll.BufferCount = UIFrameworkConfig.VirtualScrollBufferSize.Value;
                        musicManager.MixedVirtualScroll.InitializeComponents(scrollRect, playListButtonsPrefab, playListButtonsParent.transform);
                        
                        // 订阅专辑切换事件
                        musicManager.MixedVirtualScroll.OnAlbumToggle += OnAlbumToggleHandler;
                        
                        // 订阅单曲排除状态变化事件
                        MusicService_Excluded_Patch.OnSongExcludedChanged += OnSongExcludedChangedHandler;
                        
                        _mixedComponentsInitialized = true;
                        Plugin.Log.LogInfo("MixedVirtualScrollController initialized (with album separators)");
                    }
                }
                else
                {
                    // 使用普通虚拟滚动
                    if (!_componentsInitialized)
                    {
                        var virtualScroll = ChillUIFramework.Music.VirtualScroll as VirtualScrollController;
                        if (virtualScroll != null)
                        {
                            virtualScroll.ItemHeight = 60f;
                            virtualScroll.BufferCount = UIFrameworkConfig.VirtualScrollBufferSize.Value;
                            virtualScroll.InitializeComponents(scrollRect, playListButtonsPrefab, playListButtonsParent.transform);
                            _componentsInitialized = true;
                            Plugin.Log.LogInfo("VirtualScrollController initialized (no album separators)");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error initializing VirtualScroll: {ex}");
            }
        }

        /// <summary>
        /// Patch MusicUI.ViewPlayList - 使用虚拟滚动替代原实现
        /// </summary>
        [HarmonyPatch(typeof(MusicUI), "ViewPlayList")]
        [HarmonyPrefix]
        static bool ViewPlayList_Prefix(MusicUI __instance)
        {
            // 如果虚拟滚动未启用或框架未初始化，执行原方法
            if (!UIFrameworkConfig.EnableVirtualScroll.Value || !ChillUIFramework.IsInitialized)
            {
                return true; // 执行原方法
            }

            try
            {
                // 获取_facilityMusic
                var facilityMusic = Traverse.Create(__instance)
                    .Field("_facilityMusic")
                    .GetValue<Bulbul.FacilityMusic>();

                if (facilityMusic == null)
                {
                    Plugin.Log.LogError("Failed to get _facilityMusic from MusicUI");
                    return true;
                }

                // 获取播放列表（游戏已经过滤好的）
                var playingList = Traverse.Create(__instance)
                    .Field("_playingList")
                    .GetValue<ObservableCollections.IReadOnlyObservableList<Bulbul.GameAudioInfo>>();

                if (playingList == null)
                {
                    Traverse.Create(__instance).Field("isPlaylistDirty").SetValue(false);
                    return false;
                }

                var musicManager = ChillUIFramework.Music as MusicUIManager;

                if (UIFrameworkConfig.EnableAlbumSeparators.Value && musicManager?.MixedVirtualScroll != null)
                {
                    // 使用混合虚拟滚动
                    _ = ViewPlayListWithAlbumSeparators(__instance, facilityMusic, playingList, musicManager);
                }
                else
                {
                    // 使用普通虚拟滚动
                    var virtualScroll = ChillUIFramework.Music.VirtualScroll as VirtualScrollController;
                    if (virtualScroll != null)
                    {
                        virtualScroll.SetFacilityMusic(facilityMusic);
                        virtualScroll.SetDataSource(playingList);
                    }
                }

                // **关键：清除dirty标志，防止无限循环**
                Traverse.Create(__instance).Field("isPlaylistDirty").SetValue(false);

                // 阻止原方法执行
                return false;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error in ViewPlayList patch: {ex}");
                return true; // 出错时执行原方法
            }
        }

        /// <summary>
        /// 使用带专辑分隔的方式渲染播放列表
        /// </summary>
        private static async Task ViewPlayListWithAlbumSeparators(
            MusicUI musicUI,
            FacilityMusic facilityMusic, 
            ObservableCollections.IReadOnlyObservableList<GameAudioInfo> playingList,
            MusicUIManager musicManager)
        {
            try
            {
                var mixedScroll = musicManager.MixedVirtualScroll;
                mixedScroll.SetFacilityMusic(facilityMusic);

                // 构建带专辑头的列表（根据歌曲UUID查找专辑信息）
                var items = await musicManager.PlaylistListBuilder.BuildWithAlbumHeaders(
                    playingList.ToList(),
                    loadCovers: true
                );

                mixedScroll.SetDataSource(items);
                
                var albumCount = items.Count(i => i.ItemType == PlaylistItemType.AlbumHeader);
                Plugin.Log.LogInfo($"Rendered playlist: {playingList.Count} songs, {albumCount} album headers");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error rendering playlist with album separators: {ex}");
            }
        }

        /// <summary>
        /// 获取当前选中的歌单ID
        /// </summary>
        private static string GetCurrentPlaylistId()
        {
            // 尝试从CustomTagManager获取当前选中的标签
            var tagManager = CustomTagManager.Instance;
            if (tagManager == null)
                return null;

            // TODO: 需要跟踪当前选中的歌单
            // 暂时返回null，使用简单列表
            return null;
        }

        /// <summary>
        /// 专辑切换事件处理器
        /// </summary>
        private static void OnAlbumToggleHandler(string albumId)
        {
            try
            {
                var albumManager = AlbumManager.Instance;
                if (albumManager == null)
                {
                    Plugin.Log.LogWarning("AlbumManager not initialized");
                    return;
                }

                // 获取专辑信息以得到 playlistId (tagId)
                var albumInfo = albumManager.GetAlbum(albumId);
                if (albumInfo == null)
                {
                    Plugin.Log.LogWarning($"Album not found: {albumId}");
                    return;
                }

                var tagId = albumInfo.PlaylistId;
                
                // 切换专辑启用状态
                bool newState = albumManager.ToggleAlbumEnabled(albumId, tagId);
                Plugin.Log.LogInfo($"Album '{albumInfo.DisplayName}' toggled to {(newState ? "enabled" : "disabled")}");

                // 刷新播放列表以更新显示
                RefreshPlaylistDisplay();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error handling album toggle: {ex}");
            }
        }

        /// <summary>
        /// 单曲排除状态变化事件处理器
        /// </summary>
        private static void OnSongExcludedChangedHandler(string songUUID, bool isExcluded)
        {
            try
            {
                Plugin.Log.LogDebug($"Song excluded state changed: {songUUID} -> {(isExcluded ? "excluded" : "included")}");
                
                // 刷新播放列表以更新专辑头的统计信息
                RefreshPlaylistDisplay();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error handling song excluded change: {ex}");
            }
        }

        /// <summary>
        /// 刷新播放列表显示
        /// </summary>
        private static void RefreshPlaylistDisplay()
        {
            try
            {
                // 触发 MusicUI 刷新播放列表
                // 设置 isPlaylistDirty 标志，让下一帧自动刷新
                var musicUI = UnityEngine.Object.FindObjectOfType<MusicUI>();
                if (musicUI != null)
                {
                    Traverse.Create(musicUI).Field("isPlaylistDirty").SetValue(true);
                    Plugin.Log.LogDebug("Playlist refresh triggered");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error refreshing playlist display: {ex}");
            }
        }
    }
}

using Bulbul;
using ChillPatcher.UIFramework;
using ChillPatcher.UIFramework.Music;
using ChillPatcher.ModuleSystem.Registry;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using HarmonyLib;
using NestopiSystem.DIContainers;
using ObservableCollections;
using R3;
using R3.Triggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

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
        /// 是否正在显示队列模式（禁用虚拟滚动，使用原版列表但自定义数据）
        /// </summary>
        public static bool IsShowingQueue { get; set; } = false;
        
        /// <summary>
        /// 是否正在创建队列视图中的按钮（用于阻止原始删除事件绑定）
        /// </summary>
        internal static bool IsCreatingQueueButtons { get; set; } = false;
        
        /// <summary>
        /// 队列模式下要显示的数据（直接使用 PlayQueueManager 作为数据源）
        /// </summary>
        public static IReadOnlyList<GameAudioInfo> QueueDataSource => PlayQueueManager.Instance.Queue;
        
        /// <summary>
        /// 队列项目被移除时的事件（参数: UUID）
        /// </summary>
        public static event Action<string> OnQueueItemRemoved;
        
        /// <summary>
        /// 队列项目被重排时的事件（参数: 新的UUID列表）
        /// </summary>
        public static event Action<List<string>> OnQueueReordered;

        /// <summary>
        /// Patch MusicUI.Setup - 初始化虚拟滚动组件
        /// 使用Prefix确保在Setup内部的ViewPlayList调用之前完成初始化
        /// </summary>
        [HarmonyPatch(typeof(MusicUI), "Setup")]
        [HarmonyPrefix]
        static void Setup_Prefix(MusicUI __instance)
        {

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
                        
                        // 订阅收藏状态变化事件（用于刷新删除按钮显示）
                        MusicService_Favorite_Patch.OnSongFavoriteChanged += OnSongFavoriteChangedHandler;
                        
                        // 订阅滚动到底部事件（用于增长列表）
                        musicManager.MixedVirtualScroll.OnScrollToBottom += OnScrollToBottomHandler;
                        
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
            // 队列模式：使用原版列表但显示自定义数据
            if (IsShowingQueue)
            {
                ViewQueueList(__instance);
                return false; // 阻止原方法
            }
            
            // 如果框架未初始化，执行原方法
            if (!ChillUIFramework.IsInitialized)
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

                // 更新显示顺序列表（用于队列系统）
                musicManager.UpdateDisplayOrderFromItems(items);

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
            // 使用 TagRegistry 检查是否有自定义 Tag
            var tagRegistry = TagRegistry.Instance;
            if (tagRegistry == null)
                return null;

            // TODO: 需要跟踪当前选中的歌单
            // 暂时返回null，使用简单列表
            return null;
        }

        /// <summary>
        /// 专辑切换事件处理器
        /// 切换专辑下所有歌曲的排除状态
        /// </summary>
        private static void OnAlbumToggleHandler(string albumId)
        {
            try
            {
                var albumRegistry = AlbumRegistry.Instance;
                var musicRegistry = MusicRegistry.Instance;
                if (albumRegistry == null || musicRegistry == null)
                {
                    Plugin.Log.LogWarning("Registry not initialized");
                    return;
                }

                // 获取专辑信息
                var albumInfo = albumRegistry.GetAlbum(albumId);
                if (albumInfo == null)
                {
                    Plugin.Log.LogWarning($"Album not found: {albumId}");
                    return;
                }

                // 获取模块的 IFavoriteExcludeHandler
                var moduleLoader = ModuleSystem.ModuleLoader.Instance;
                if (moduleLoader == null)
                {
                    Plugin.Log.LogWarning("ModuleLoader not initialized");
                    return;
                }

                var module = moduleLoader.GetModule(albumInfo.ModuleId);
                var excludeHandler = module as SDK.Interfaces.IFavoriteExcludeHandler;
                if (excludeHandler == null)
                {
                    Plugin.Log.LogWarning($"Module {albumInfo.ModuleId} does not support exclude");
                    return;
                }

                // 获取专辑下的所有歌曲
                var songs = musicRegistry.GetMusicByAlbum(albumId);
                if (songs == null || songs.Count == 0)
                {
                    Plugin.Log.LogWarning($"No songs in album: {albumId}");
                    return;
                }

                // 检查当前状态：如果有任何歌曲未排除，则视为启用状态
                bool hasEnabledSong = songs.Any(s => !excludeHandler.IsExcluded(s.UUID));
                bool newExcludedState = hasEnabledSong; // 如果有启用的歌曲，则全部排除

                Plugin.Log.LogInfo($"Album toggle: {albumInfo.DisplayName}, setting excluded={newExcludedState} for {songs.Count} songs");

                // 批量设置排除状态
                foreach (var song in songs)
                {
                    excludeHandler.SetExcluded(song.UUID, newExcludedState);
                }

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
        /// 单曲收藏状态变化事件处理器
        /// </summary>
        private static void OnSongFavoriteChangedHandler(string songUUID, bool isFavorite)
        {
            try
            {
                Plugin.Log.LogDebug($"Song favorite state changed: {songUUID} -> {(isFavorite ? "favorite" : "unfavorite")}");
                
                // 刷新播放列表以更新删除按钮显示状态
                RefreshPlaylistDisplay();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error handling song favorite change: {ex}");
            }
        }

        /// <summary>
        /// 防抖标志：防止触底事件重复触发
        /// </summary>
        private static bool _isLoadingMore = false;
        private static System.DateTime _lastBottomOutTime = System.DateTime.MinValue;
        private static readonly System.TimeSpan _bottomOutDebounce = System.TimeSpan.FromSeconds(1);

        /// <summary>
        /// 公共方法：异步触发增长列表加载更多
        /// 可供其他 Patch（如 PlayQueuePatch）调用
        /// </summary>
        /// <returns>新加载的歌曲数量，0 表示没有增长列表或加载失败</returns>
        public static async Task<int> TriggerLoadMoreAsync()
        {
            // 获取当前选中的增长列表 Tag
            var tagRegistry = TagRegistry.Instance;
            var growableTag = tagRegistry?.GetCurrentGrowableTag();
            
            // 如果没有通过按钮设置，检查当前选中的 Tag 中是否包含增长 Tag
            if (growableTag == null)
            {
                var currentAudioTag = SaveDataManager.Instance?.MusicSetting?.CurrentAudioTag?.Value;
                if (currentAudioTag != null)
                {
                    var growableTags = tagRegistry?.GetGrowableTags();
                    if (growableTags != null)
                    {
                        foreach (var gt in growableTags)
                        {
                            if (currentAudioTag.Value.HasFlagFast((AudioTag)gt.BitValue))
                            {
                                growableTag = gt;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (growableTag == null || growableTag.LoadMoreCallback == null)
            {
                return 0;
            }

            Plugin.Log.LogInfo($"[GrowableList] 触发加载更多: {growableTag.DisplayName}");

            try
            {
                var loadedCount = await growableTag.LoadMoreCallback();
                
                if (loadedCount > 0)
                {
                    OnGrowableListLoaded(growableTag.TagId, loadedCount);
                }
                
                Plugin.Log.LogInfo($"[GrowableList] 加载更多完成: {loadedCount} 首");
                return loadedCount;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[GrowableList] 加载更多失败: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 检查当前是否在增长列表模式下
        /// </summary>
        public static bool IsInGrowableListMode()
        {
            var tagRegistry = TagRegistry.Instance;
            var growableTag = tagRegistry?.GetCurrentGrowableTag();
            
            if (growableTag != null)
                return true;
            
            // 检查当前选中的 Tag 中是否包含增长 Tag
            var currentAudioTag = SaveDataManager.Instance?.MusicSetting?.CurrentAudioTag?.Value;
            if (currentAudioTag != null)
            {
                var growableTags = tagRegistry?.GetGrowableTags();
                if (growableTags != null)
                {
                    foreach (var gt in growableTags)
                    {
                        if (currentAudioTag.Value.HasFlagFast((AudioTag)gt.BitValue))
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 滚动到底部事件处理器 - 用于增长列表加载更多
        /// 同时支持两种模式：
        /// 1. 回调模式：如果 Tag 有 LoadMoreCallback，直接调用
        /// 2. 事件模式：总是发布 GrowableListBottomOutEvent，模块可以订阅处理
        /// </summary>
        private static async void OnScrollToBottomHandler()
        {
            try
            {
                // 防抖
                var now = System.DateTime.Now;
                if (_isLoadingMore || (now - _lastBottomOutTime) < _bottomOutDebounce)
                    return;

                // 获取当前选中的增长列表 Tag
                var tagRegistry = TagRegistry.Instance;
                
                // 优先使用通过按钮点击设置的当前增长 Tag
                var growableTag = tagRegistry?.GetCurrentGrowableTag();
                
                // 如果没有通过按钮设置，检查当前选中的 Tag 中是否包含增长 Tag
                if (growableTag == null)
                {
                    var currentAudioTag = SaveDataManager.Instance?.MusicSetting?.CurrentAudioTag?.Value;
                    if (currentAudioTag != null)
                    {
                        var growableTags = tagRegistry?.GetGrowableTags();
                        if (growableTags != null)
                        {
                            foreach (var gt in growableTags)
                            {
                                if (currentAudioTag.Value.HasFlagFast((AudioTag)gt.BitValue))
                                {
                                    growableTag = gt;
                                    Plugin.Log.LogInfo($"[GrowableList] 检测到当前选中包含增长 Tag: {gt.DisplayName}");
                                    break; // 只处理第一个匹配的增长 Tag
                                }
                            }
                        }
                    }
                }
                
                if (growableTag == null)
                {
                    // 没有增长列表，忽略
                    return;
                }

                _lastBottomOutTime = now;
                _isLoadingMore = true;

                Plugin.Log.LogInfo($"[GrowableList] 触底: {growableTag.DisplayName}");

                var eventBus = ModuleSystem.EventBus.Instance;
                int loadedCount = 0;
                bool hasCallback = growableTag.LoadMoreCallback != null;

                // 发布触底事件（事件模式），让模块可以订阅
                eventBus?.Publish(new SDK.Events.GrowableListBottomOutEvent
                {
                    TagId = growableTag.TagId,
                    TagInfo = growableTag,
                    CurrentSongCount = MusicRegistry.Instance?.GetMusicByTag(growableTag.TagId)?.Count ?? 0,
                    ReportLoaded = (count) => OnGrowableListLoaded(growableTag.TagId, count)
                });

                // 如果 Tag 有回调，也调用回调（回调模式）
                if (hasCallback)
                {
                    Plugin.Log.LogInfo($"[GrowableList] 使用回调模式加载更多...");
                    loadedCount = await growableTag.LoadMoreCallback();
                    Plugin.Log.LogInfo($"[GrowableList] 回调完成，新增 {loadedCount} 首歌曲");

                    if (loadedCount > 0)
                    {
                        OnGrowableListLoaded(growableTag.TagId, loadedCount);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error handling scroll to bottom: {ex}");
            }
            finally
            {
                _isLoadingMore = false;
            }
        }

        /// <summary>
        /// 增长列表加载完成后的处理
        /// 可以由回调模式调用，也可以由模块通过 ReportLoaded 调用
        /// </summary>
        private static void OnGrowableListLoaded(string tagId, int loadedCount)
        {
            if (loadedCount <= 0)
                return;

            Plugin.Log.LogInfo($"[GrowableList] 加载完成: TagId={tagId}, 新增 {loadedCount} 首歌曲");

            // 发布加载完成事件
            var eventBus = ModuleSystem.EventBus.Instance;
            eventBus?.Publish(new SDK.Events.GrowableListLoadedEvent
            {
                TagId = tagId,
                LoadedCount = loadedCount,
                HasMore = loadedCount > 0
            });

            // 刷新播放列表显示
            RefreshPlaylistDisplay();
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
        
        // 队列列表按钮的引用
        private static List<MusicPlayListButtons> _queueButtonList = new List<MusicPlayListButtons>();
        private static R3.CompositeDisposable _queueListDisposable = new R3.CompositeDisposable();
        
        /// <summary>
        /// 使用原版列表渲染队列（非虚拟滚动）
        /// </summary>
        private static void ViewQueueList(MusicUI musicUI)
        {
            try
            {
                // 清除dirty标志
                Traverse.Create(musicUI).Field("isPlaylistDirty").SetValue(false);
                
                // 获取 prefab 和 parent
                var playListButtonsPrefab = Traverse.Create(musicUI)
                    .Field("_playListButtonsPrefab")
                    .GetValue<GameObject>();
                var playListButtonsParent = Traverse.Create(musicUI)
                    .Field("_playListButtonsParent")
                    .GetValue<GameObject>();
                var facilityMusic = Traverse.Create(musicUI)
                    .Field("_facilityMusic")
                    .GetValue<FacilityMusic>();
                var scrollRect = Traverse.Create(musicUI)
                    .Field("scrollRect")
                    .GetValue<UnityEngine.UI.ScrollRect>();
                    
                if (playListButtonsPrefab == null || playListButtonsParent == null)
                {
                    Plugin.Log.LogError("Failed to get prefab or parent for queue list");
                    return;
                }
                
                // **关键：暂停虚拟滚动控制器并清理其项目**
                var musicManager = ChillUIFramework.Music as MusicUIManager;
                if (musicManager?.MixedVirtualScroll != null)
                {
                    // 暂停虚拟滚动（防止滚动时继续创建项目）
                    musicManager.MixedVirtualScroll.IsPaused = true;
                    // 清空 MixedVirtualScroll 的活动项
                    musicManager.MixedVirtualScroll.ClearAllItems();
                }
                
                // 清空现有按钮（包括剩余的和空提示）
                foreach (Transform child in playListButtonsParent.transform)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                _queueButtonList.Clear();
                _queueListDisposable.Dispose();
                _queueListDisposable = new R3.CompositeDisposable();
                
                // **关键：重置 Content 的位置**
                var contentTransform = playListButtonsParent.GetComponent<RectTransform>();
                if (contentTransform != null)
                {
                    contentTransform.anchoredPosition = Vector2.zero;
                }
                
                // **关键：重置 ScrollRect 滚动位置**
                if (scrollRect != null)
                {
                    scrollRect.verticalNormalizedPosition = 1f; // 滚动到顶部
                }
                
                // **关键：确保 ContentSizeFitter 生效（非虚拟滚动模式需要）**
                var contentSizeFitter = playListButtonsParent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                if (contentSizeFitter == null)
                {
                    contentSizeFitter = playListButtonsParent.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                }
                contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                contentSizeFitter.enabled = true;
                
                // **关键：确保 VerticalLayoutGroup 存在**
                var layoutGroup = playListButtonsParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (layoutGroup == null)
                {
                    layoutGroup = playListButtonsParent.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    layoutGroup.childForceExpandWidth = true;
                    layoutGroup.childForceExpandHeight = false;
                    layoutGroup.childControlWidth = true;
                    layoutGroup.childControlHeight = false;
                }
                layoutGroup.enabled = true;
                
                // 如果队列为空，显示提示
                if (QueueDataSource.Count == 0)
                {
                    CreateEmptyQueueHint(playListButtonsParent.transform);
                    Plugin.Log.LogInfo("Queue is empty, showing hint");
                    return;
                }
                
                // 创建队列项目
                // 设置标志，让 MusicPlayListButtons.Setup 中的删除按钮逻辑被跳过
                IsCreatingQueueButtons = true;
                try
                {
                    foreach (var audioInfo in QueueDataSource)
                    {
                        var buttonObj = UnityEngine.Object.Instantiate(playListButtonsPrefab, playListButtonsParent.transform, false);
                        var localPos = buttonObj.transform.localPosition;
                        localPos.z = 0f;
                        buttonObj.transform.localPosition = localPos;
                        buttonObj.transform.localScale = Vector3.one;
                        
                        var button = buttonObj.GetComponent<MusicPlayListButtons>();
                        button.Setup(audioInfo, facilityMusic);
                        
                        // 强制显示删除按钮（用于从队列移除）
                        var removeInteractableUI = Traverse.Create(button)
                            .Field("removeInteractableUI")
                            .GetValue<InteractableUI>();
                        if (removeInteractableUI != null)
                        {
                            removeInteractableUI.gameObject.SetActive(true);
                        }
                        
                        // 替换删除按钮的行为：从队列移除而非从音乐库移除
                        var removeButton = Traverse.Create(button)
                            .Field("removeButton")
                            .GetValue<ButtonEventObservable>();
                        if (removeButton != null)
                        {
                            // 添加自定义删除逻辑（仅从队列移除）
                            // 注意：原始删除逻辑已被 MusicPlayListButtons_QueueRemove_Patch 阻止
                            var currentAudioInfo = audioInfo; // 捕获闭包
                            removeButton.OnClick
                                .Subscribe(_ => OnQueueItemRemoveClicked(button, currentAudioInfo))
                                .AddTo(_queueListDisposable);
                        }
                        
                        _queueButtonList.Add(button);
                    }
                }
                finally
                {
                    IsCreatingQueueButtons = false;
                }
                
                // 订阅拖拽事件 - 使用 R3.Observable.Merge
                R3.Observable.Merge(_queueButtonList.Select(b => b.OnStartReorder))
                    .Subscribe(x => OnQueueStartReorder(musicUI, x.Item1, x.Item2))
                    .AddTo(_queueListDisposable);
                R3.Observable.Merge(_queueButtonList.Select(b => b.OnReorderDrag))
                    .Subscribe(x => OnQueueDragReorder(musicUI, x.Item1, x.Item2))
                    .AddTo(_queueListDisposable);
                R3.Observable.Merge(_queueButtonList.Select(b => b.OnEndReorder))
                    .Subscribe(x => OnQueueEndReorder(musicUI, x.Item1, x.Item2))
                    .AddTo(_queueListDisposable);
                
                // **关键：强制刷新布局**
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentTransform);
                UnityEngine.Canvas.ForceUpdateCanvases();
                
                Plugin.Log.LogInfo($"Queue list rendered with {QueueDataSource.Count} items");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering queue list: {ex}");
            }
        }
        
        // 拖拽相关状态
        private static MusicPlayListButtons _queueDraggingButton;
        private static int _queueOriginalIndex;
        private static Vector3 _queueDragOffset;
        private static int _currentDropIndex = -1;  // 当前预计的插入位置
        private static List<float> _originalYPositions = new List<float>();  // 所有项目的原始Y位置
        private static UnityEngine.UI.VerticalLayoutGroup _layoutGroup;
        private static UnityEngine.UI.ContentSizeFitter _contentSizeFitter;
        private static UnityEngine.UI.ScrollRect _dragScrollRect;
        
        /// <summary>
        /// 拖拽项目的高度
        /// </summary>
        private const float DragItemHeight = 60f;
        
        private static void OnQueueStartReorder(MusicUI musicUI, MusicPlayListButtons button, PointerEventData eventData)
        {
            _queueDraggingButton = button;
            _queueOriginalIndex = _queueButtonList.IndexOf(button);
            _currentDropIndex = _queueOriginalIndex;
            
            // 获取并暂时禁用布局组件和滚动
            var playListButtonsParent = Traverse.Create(musicUI)
                .Field("_playListButtonsParent")
                .GetValue<GameObject>();
            
            _dragScrollRect = Traverse.Create(musicUI)
                .Field("scrollRect")
                .GetValue<UnityEngine.UI.ScrollRect>();
            
            if (playListButtonsParent != null)
            {
                _layoutGroup = playListButtonsParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                _contentSizeFitter = playListButtonsParent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                
                // 禁用布局组件前，记录所有项目的Y位置
                _originalYPositions.Clear();
                foreach (var btn in _queueButtonList)
                {
                    _originalYPositions.Add(btn.transform.localPosition.y);
                }
                
                // 禁用布局组件
                if (_layoutGroup != null) _layoutGroup.enabled = false;
                if (_contentSizeFitter != null) _contentSizeFitter.enabled = false;
            }
            
            // 禁用 ScrollRect 以防止它抢占拖拽事件
            if (_dragScrollRect != null)
            {
                _dragScrollRect.enabled = false;
            }
            
            // 计算拖拽偏移
            var rectTransform = button.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var worldPoint);
            _queueDragOffset = button.transform.position - worldPoint;
            
            // 将拖拽项放到最上层
            button.transform.SetAsLastSibling();
            
            Plugin.Log.LogDebug($"Start drag: index={_queueOriginalIndex}");
        }
        
        private static void OnQueueDragReorder(MusicUI musicUI, MusicPlayListButtons button, PointerEventData eventData)
        {
            if (_queueDraggingButton != button) return;
            
            // 检测滚轮：如果有滚轮输入，强制中断拖拽
            float scrollDelta = Input.mouseScrollDelta.y;
            if (scrollDelta != 0)
            {
                Plugin.Log.LogDebug("Scroll detected during drag - forcing end reorder");
                OnQueueEndReorder(musicUI, button, eventData);
                return;
            }
            
            var rectTransform = button.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out var worldPoint))
            {
                button.transform.position = worldPoint + _queueDragOffset;
            }
            
            // 计算鼠标在本地坐标系中的位置，用于确定插入点
            // 使用 content 的 transform 来转换坐标
            float mouseLocalY = button.transform.localPosition.y;
            var contentParent = button.transform.parent;
            if (contentParent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                contentParent as RectTransform, eventData.position, eventData.pressEventCamera, out var localPoint))
            {
                mouseLocalY = localPoint.y;
            }
            
            // 计算当前应该插入的位置（基于鼠标位置）
            int newDropIndex = CalculateDropIndexFromLocalY(mouseLocalY);
            
            // 只有当索引变化时才触发动画
            if (newDropIndex != _currentDropIndex)
            {
                Plugin.Log.LogDebug($"Drop index changed: {_currentDropIndex} -> {newDropIndex} (original={_queueOriginalIndex}, mouseY={mouseLocalY})");
                _currentDropIndex = newDropIndex;
                AnimateItemsToNewPositions();
            }
        }
        
        /// <summary>
        /// 动画持续时间（秒）
        /// </summary>
        private const float ItemAnimationDuration = 0.15f;
        
        /// <summary>
        /// 当索引变化时，用动画将项目移动到新位置
        /// </summary>
        private static void AnimateItemsToNewPositions()
        {
            if (_originalYPositions.Count != _queueButtonList.Count) return;
            
            // dropIndex 是插入点的位置
            // 例如：[A,B,C,D] 中把 A(索引0) 拖到 B 和 C 之间，dropIndex = 2
            // 结果是 [B,A,C,D]，B 需要向上移动（到原来 A 的位置）
            
            for (int i = 0; i < _queueButtonList.Count; i++)
            {
                var btn = _queueButtonList[i];
                if (btn == _queueDraggingButton) continue;
                
                float targetY = _originalYPositions[i];
                
                if (_currentDropIndex != _queueOriginalIndex)
                {
                    if (_currentDropIndex > _queueOriginalIndex)
                    {
                        // 拖拽项向下移动（dropIndex 更大）
                        // 在 (originalIndex, dropIndex) 范围内的项目需要向上移动一个位置
                        if (i > _queueOriginalIndex && i < _currentDropIndex)
                        {
                            targetY += DragItemHeight;  // 向上移动（Y 增加）
                        }
                    }
                    else
                    {
                        // 拖拽项向上移动（dropIndex 更小）
                        // 在 [dropIndex, originalIndex) 范围内的项目需要向下移动一个位置
                        if (i >= _currentDropIndex && i < _queueOriginalIndex)
                        {
                            targetY -= DragItemHeight;  // 向下移动（Y 减少）
                        }
                    }
                }
                
                // 使用 DOTween 动画移动到目标位置
                DG.Tweening.DOTween.Kill(btn.transform, complete: false);
                btn.transform.DOLocalMoveY(targetY, ItemAnimationDuration).SetEase(DG.Tweening.Ease.OutQuad);
            }
        }
        
        /// <summary>
        /// 根据本地Y坐标计算插入位置（使用当前视觉位置）
        /// </summary>
        private static int CalculateDropIndexFromLocalY(float localY)
        {
            // 使用项目当前的实际视觉位置来计算
            // 这样动画进行中也能正确判断
            
            int result = _queueButtonList.Count;
            
            for (int i = 0; i < _queueButtonList.Count; i++)
            {
                var btn = _queueButtonList[i];
                if (btn == _queueDraggingButton) continue;  // 跳过正在拖拽的项目
                
                // 使用当前实际位置
                float currentY = btn.transform.localPosition.y;
                float itemCenter = currentY - DragItemHeight / 2f;
                
                if (localY >= itemCenter)
                {
                    // 找到第一个鼠标在其上方的项目，返回它的索引
                    // 但要考虑原始索引：如果这个项目原本在拖拽项目之后，我们要返回原始索引
                    result = i;
                    break;
                }
            }
            
            return result;
        }
        
        private static void OnQueueEndReorder(MusicUI musicUI, MusicPlayListButtons button, PointerEventData eventData)
        {
            if (_queueDraggingButton != button) return;
            
            // 停止所有动画
            foreach (var btn in _queueButtonList)
            {
                if (btn != null)
                {
                    DG.Tweening.DOTween.Kill(btn.transform, complete: true);
                }
            }
            
            int newIndex = _currentDropIndex;
            
            if (newIndex != _queueOriginalIndex && newIndex >= 0 && newIndex <= _queueButtonList.Count)
            {
                // 计算实际插入位置：如果拖拽项在目标位置之前，需要减 1
                int insertIndex = newIndex;
                if (_queueOriginalIndex < newIndex)
                {
                    insertIndex--;
                }
                if (insertIndex > QueueDataSource.Count) insertIndex = QueueDataSource.Count;
                if (insertIndex < 0) insertIndex = 0;
                
                // 调用 PlayQueueManager 移动数据
                PlayQueueManager.Instance.Move(_queueOriginalIndex, insertIndex);
                
                // 更新按钮列表
                _queueButtonList.Remove(button);
                if (insertIndex > _queueButtonList.Count) insertIndex = _queueButtonList.Count;
                if (insertIndex < 0) insertIndex = 0;
                _queueButtonList.Insert(insertIndex, button);
                
                // 触发事件
                OnQueueReordered?.Invoke(QueueDataSource.Select(a => a.UUID).ToList());
                
                Plugin.Log.LogInfo($"Reordered: {_queueOriginalIndex} -> {newIndex} (insertIndex={insertIndex})");
            }
            
            // 恢复滚动
            if (_dragScrollRect != null)
            {
                _dragScrollRect.enabled = true;
            }
            
            // 恢复布局组件
            if (_layoutGroup != null) _layoutGroup.enabled = true;
            if (_contentSizeFitter != null) _contentSizeFitter.enabled = true;
            
            // 刷新显示
            RefreshPlaylistDisplay();
            
            _queueDraggingButton = null;
            _currentDropIndex = -1;
            _originalYPositions.Clear();
        }
        
        private static int CalculateQueueDropIndex(MusicPlayListButtons button)
        {
            var buttonY = button.transform.position.y;
            for (int i = 0; i < _queueButtonList.Count; i++)
            {
                var other = _queueButtonList[i];
                if (other == button) continue;
                
                var otherY = other.transform.position.y;
                if (buttonY > otherY)
                {
                    return i;
                }
            }
            return _queueButtonList.Count - 1;
        }
        
        /// <summary>
        /// 队列项删除按钮点击处理（新版本，带 audioInfo 参数）
        /// </summary>
        private static void OnQueueItemRemoveClicked(MusicPlayListButtons button, GameAudioInfo audioInfo)
        {
            int index = _queueButtonList.IndexOf(button);
            if (index >= 0 && index < QueueDataSource.Count)
            {
                var uuid = audioInfo.UUID;
                bool isCurrentPlaying = (index == 0);  // 是否移除当前正在播放的歌曲
                
                // 调用 PlayQueueManager 删除数据
                PlayQueueManager.Instance.RemoveAt(index);
                
                _queueButtonList.RemoveAt(index);
                UnityEngine.Object.Destroy(button.gameObject);
                
                // 触发事件
                OnQueueItemRemoved?.Invoke(uuid);
                
                Plugin.Log.LogInfo($"Queue item removed: {uuid} (wasPlaying: {isCurrentPlaying})");
                
                // 如果移除的是当前播放的歌曲，自动播放下一首
                if (isCurrentPlaying)
                {
                    PlayNextAfterRemove();
                }
                
                // 如果队列为空，显示提示
                if (QueueDataSource.Count == 0)
                {
                    RefreshPlaylistDisplay();
                }
            }
        }
        
        /// <summary>
        /// 移除当前播放歌曲后，播放下一首
        /// </summary>
        private static void PlayNextAfterRemove()
        {
            // 获取MusicService
            var musicService = RoomLifetimeScope.Resolve<MusicService>();
            if (musicService == null) return;
            
            // 如果队列不为空，播放新的队首（原来的第二首）
            if (QueueDataSource.Count > 0)
            {
                var nextAudio = QueueDataSource[0];
                // 直接设置播放这首歌（不走 SkipCurrentMusic，因为它会再次调用 AdvanceToNext）
                PlayQueuePatch.SetPlayingMusicDirect(musicService, nextAudio, MusicChangeKind.Manual);
                Plugin.Log.LogInfo($"[Queue] Playing next after remove: {nextAudio.AudioClipName}");
            }
            else
            {
                // 队列为空，从播放列表获取下一首
                musicService.SkipCurrentMusic(MusicChangeKind.Manual).Forget();
                Plugin.Log.LogInfo("[Queue] Queue empty after remove, skipping to next from playlist");
            }
        }
        
        /// <summary>
        /// 空队列提示的水平偏移（正值向右）
        /// </summary>
        private const float EmptyHintOffsetX = 80f;
        
        /// <summary>
        /// 创建空队列提示
        /// </summary>
        private static void CreateEmptyQueueHint(Transform parent)
        {
            // 创建一个简单的文字提示
            var hintObj = new GameObject("EmptyQueueHint");
            hintObj.transform.SetParent(parent, false);
            
            // 添加 LayoutElement 并设置 ignoreLayout = true，让它忽略 VerticalLayoutGroup
            var layoutElement = hintObj.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.ignoreLayout = true;
            
            // 添加 RectTransform
            var rectTransform = hintObj.GetComponent<RectTransform>();  // AddComponent 时已经添加了
            
            // 设置锚点为左上角（和 content 一致）
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);  // 居中 pivot 方便定位
            rectTransform.sizeDelta = new Vector2(400, 100);
            
            // 设置位置：向右偏移 EmptyHintOffsetX + 200（文字居中），向下偏移 150
            rectTransform.anchoredPosition = new Vector2(EmptyHintOffsetX + 200f, -150f);
            
            Plugin.Log.LogDebug($"EmptyQueueHint created, anchoredPosition={rectTransform.anchoredPosition}, ignoreLayout={layoutElement.ignoreLayout}");
            
            // 添加 TextMeshPro 文本
            var text = hintObj.AddComponent<TMPro.TextMeshProUGUI>();
            text.text = "播放队列为空\n点击「返回列表」返回播放列表";
            text.fontSize = 24;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(1f, 1f, 1f, 0.6f);
        }
        
        /// <summary>
        /// 切换到队列模式（数据直接从 PlayQueueManager 获取）
        /// </summary>
        public static void SwitchToQueue()
        {
            IsShowingQueue = true;
            
            // 订阅队列变化事件，以便在播放下一曲时刷新 UI
            PlayQueueManager.Instance.OnQueueChanged -= OnPlayQueueChanged;  // 先取消，避免重复订阅
            PlayQueueManager.Instance.OnQueueChanged += OnPlayQueueChanged;
            
            RefreshPlaylistDisplay();
        }
        
        /// <summary>
        /// 队列变化时的回调
        /// </summary>
        private static void OnPlayQueueChanged()
        {
            if (IsShowingQueue)
            {
                Plugin.Log.LogInfo("[MusicUI] Queue changed, refreshing display");
                RefreshPlaylistDisplay();
            }
        }
        
        /// <summary>
        /// 切换到队列模式（兼容旧接口，参数被忽略）
        /// </summary>
        [Obsolete("Use SwitchToQueue() instead. Data is now from PlayQueueManager.")]
        public static void SwitchToQueue(List<GameAudioInfo> dataSource)
        {
            SwitchToQueue();
        }
        
        /// <summary>
        /// 切换回播放列表模式
        /// </summary>
        public static void SwitchToPlaylist()
        {
            IsShowingQueue = false;
            
            // 取消订阅队列变化事件
            PlayQueueManager.Instance.OnQueueChanged -= OnPlayQueueChanged;
            
            // QueueDataSource 现在是只读的，数据由 PlayQueueManager 管理
            _queueListDisposable.Dispose();
            _queueListDisposable = new R3.CompositeDisposable();
            
            var musicUI = UnityEngine.Object.FindObjectOfType<MusicUI>();
            if (musicUI != null)
            {
                var playListButtonsParent = Traverse.Create(musicUI)
                    .Field("_playListButtonsParent")
                    .GetValue<GameObject>();
                    
                if (playListButtonsParent != null)
                {
                    // **关键：清理队列创建的所有项目**
                    foreach (Transform child in playListButtonsParent.transform)
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                    
                    // 禁用手动添加的布局组件（让虚拟滚动接管）
                    var contentSizeFitter = playListButtonsParent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                    if (contentSizeFitter != null)
                        contentSizeFitter.enabled = false;
                    
                    var layoutGroup = playListButtonsParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    if (layoutGroup != null)
                        layoutGroup.enabled = false;
                }
            }
            
            _queueButtonList.Clear();
            
            // **关键：恢复虚拟滚动**
            var musicManager = ChillUIFramework.Music as MusicUIManager;
            if (musicManager?.MixedVirtualScroll != null)
            {
                musicManager.MixedVirtualScroll.IsPaused = false;
            }
            
            RefreshPlaylistDisplay();
        }
    }
    
    /// <summary>
    /// Harmony Patch: 在队列视图创建按钮时阻止原始删除逻辑绑定
    /// 
    /// 问题：MusicPlayListButtons.Setup() 会为本地歌曲绑定 removeButton.OnClick 事件，
    /// 该事件会调用 RemoveLocalMusicItem 删除歌曲文件。
    /// 在队列视图中，删除按钮应该只执行"从队列移除"，而不是删除文件。
    /// 
    /// 解决方案：使用 Transpiler 在创建队列按钮时阻止原始订阅逻辑执行。
    /// </summary>
    [HarmonyPatch(typeof(MusicPlayListButtons))]
    [HarmonyPatch("Setup", typeof(GameAudioInfo), typeof(FacilityMusic))]
    public static class MusicPlayListButtons_QueueRemove_Patch
    {
        /// <summary>
        /// Postfix: 如果正在创建队列按钮，直接销毁 removeButton 组件上的 Button 组件所有监听器
        /// 并禁用底层 Button，这样原始的删除逻辑不会执行
        /// </summary>
        [HarmonyPostfix]
        static void Postfix(MusicPlayListButtons __instance)
        {
            // 只在创建队列按钮时处理
            if (!MusicUI_VirtualScroll_Patch.IsCreatingQueueButtons)
                return;
            
            try
            {
                // 获取 removeButton 字段
                var removeButton = Traverse.Create(__instance)
                    .Field("removeButton")
                    .GetValue<ButtonEventObservable>();
                
                if (removeButton == null)
                    return;
                
                // 直接销毁 ButtonEventObservable 组件，这会断开所有 R3 订阅
                UnityEngine.Object.Destroy(removeButton);
                
                // 获取底层 Button 组件
                var buttonGO = removeButton.gameObject;
                var underlyingButton = buttonGO.GetComponent<UnityEngine.UI.Button>();
                if (underlyingButton != null)
                {
                    // 移除所有 onClick 监听器
                    underlyingButton.onClick.RemoveAllListeners();
                }
                
                // 创建新的 ButtonEventObservable 组件
                var newButtonEventObservable = buttonGO.AddComponent<ButtonEventObservable>();
                
                // 更新 MusicPlayListButtons 中的 removeButton 引用
                Traverse.Create(__instance)
                    .Field("removeButton")
                    .SetValue(newButtonEventObservable);
                
                Plugin.Log.LogDebug($"[QueueRemovePatch] Replaced removeButton component for queue button: {__instance.AudioInfo?.AudioClipName}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[QueueRemovePatch] Error replacing removeButton: {ex}");
            }
        }
    }
}

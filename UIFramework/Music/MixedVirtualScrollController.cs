using System;
using System.Collections.Generic;
using System.Linq;
using Bulbul;
using ChillPatcher.ModuleSystem.Services;
using UnityEngine;
using UnityEngine.UI;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 混合虚拟滚动控制器 - 支持歌曲和专辑头两种类型
    /// </summary>
    public class MixedVirtualScrollController : IDisposable
    {
        #region Constants
        
        /// <summary>
        /// 虚拟滚动模式下 RemoveButton 的水平偏移量（负值向左）
        /// </summary>
        public const float RemoveButtonOffsetX = -80f;
        
        #endregion
        
        #region Fields

        private ScrollRect _scrollRect;
        private RectTransform _contentTransform;
        private RectTransform _viewportTransform;
        private FacilityMusic _facilityMusic;

        // 数据源
        private List<PlaylistListItem> _items = new List<PlaylistListItem>();
        
        // 预先计算的位置缓存
        private List<float> _itemPositions = new List<float>(); // 每个item的Y位置（从0开始）
        private float _totalHeight = 0f;

        // Prefab和对象池
        private GameObject _songButtonPrefab;
        private readonly Dictionary<int, MusicPlayListButtons> _activeSongItems = new Dictionary<int, MusicPlayListButtons>();
        private readonly Dictionary<int, AlbumHeaderView> _activeHeaderItems = new Dictionary<int, AlbumHeaderView>();

        // 状态
        private int _visibleStartIndex = 0;
        private int _visibleEndIndex = 0;
        private float _lastScrollPosition = 0f;
        private bool _isInitialized = false;
        private int _bufferCount = 3;
        private bool _isPaused = false;

        #endregion

        #region Properties

        public int VisibleStartIndex => _visibleStartIndex;
        public int VisibleEndIndex => _visibleEndIndex;
        public int TotalItemCount => _items.Count;
        public int BufferCount
        {
            get => _bufferCount;
            set => _bufferCount = value;
        }
        
        /// <summary>
        /// 是否暂停虚拟滚动（用于队列模式）
        /// </summary>
        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        /// <summary>
        /// 当前滚动百分比 (0=顶部, 1=底部)
        /// </summary>
        public float ScrollPercentage
        {
            get
            {
                if (_scrollRect == null)
                    return 0f;
                // verticalNormalizedPosition: 1=顶部, 0=底部
                return 1f - _scrollRect.verticalNormalizedPosition;
            }
        }

        #endregion

        #region Constants

        private const float SCROLL_TO_BOTTOM_THRESHOLD = 0.95f;

        #endregion

        #region Events

        public event Action<int, int> OnVisibleRangeChanged;
        public event Action<string> OnAlbumToggle;  // 专辑切换事件
        public event Action OnScrollToBottom;       // 滚动到底部事件

        #endregion

        #region Public Methods

        /// <summary>
        /// 初始化组件
        /// </summary>
        public void InitializeComponents(ScrollRect scrollRect, GameObject songButtonPrefab, Transform contentParent)
        {
            if (_isInitialized)
                return;

            _scrollRect = scrollRect;
            _contentTransform = contentParent as RectTransform;
            _viewportTransform = scrollRect.viewport;
            _songButtonPrefab = songButtonPrefab;

            // 禁用ContentSizeFitter和LayoutGroup
            var contentSizeFitter = _contentTransform.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter != null)
            {
                contentSizeFitter.enabled = false;
            }

            var layoutGroup = _contentTransform.GetComponent<LayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.enabled = false;
            }

            // 设置锚点
            _contentTransform.anchorMin = new Vector2(0, 1);
            _contentTransform.anchorMax = new Vector2(1, 1);
            _contentTransform.pivot = new Vector2(0.5f, 1);
            _contentTransform.anchoredPosition = new Vector2(0, 0);

            // 订阅滚动事件
            _scrollRect.onValueChanged.AddListener(OnScrollValueChanged);

            // 订阅 CoverService 封面加载完成事件
            CoverService.Instance.OnAlbumCoverLoaded += OnCoverLoaded;

            _isInitialized = true;
        }

        /// <summary>
        /// 封面加载完成回调
        /// </summary>
        private void OnCoverLoaded(string albumId, Sprite cover)
        {
            if (cover == null || _items == null) return;

            // 1. 更新数据源中的 AlbumHeaderInfo
            foreach (var item in _items)
            {
                if (item.ItemType == PlaylistItemType.AlbumHeader 
                    && item.AlbumHeader != null 
                    && item.AlbumHeader.AlbumId == albumId)
                {
                    item.AlbumHeader.CoverImage = cover;
                    break;
                }
            }

            // 2. 更新当前可见的 AlbumHeaderView
            foreach (var kvp in _activeHeaderItems)
            {
                var headerView = kvp.Value;
                if (headerView != null && headerView.CurrentAlbumId == albumId)
                {
                    headerView.UpdateCover(cover);
                    break;
                }
            }
        }

        /// <summary>
        /// 设置FacilityMusic引用
        /// </summary>
        public void SetFacilityMusic(FacilityMusic facilityMusic)
        {
            _facilityMusic = facilityMusic;
        }

        /// <summary>
        /// 设置数据源
        /// </summary>
        public void SetDataSource(List<PlaylistListItem> items)
        {
            // 清空旧的
            ClearAllActiveItems();

            _items = items ?? new List<PlaylistListItem>();

            // 计算位置
            RecalculatePositions();

            // 更新内容大小
            UpdateContentSize();

            // 刷新可见项
            RefreshVisible();
        }

        /// <summary>
        /// 刷新可见项
        /// </summary>
        public void RefreshVisible()
        {
            // 如果暂停（队列模式），不执行刷新
            if (_isPaused)
                return;
                
            if (!_isInitialized || _items.Count == 0)
                return;

            var (start, end) = CalculateVisibleRange();

            // 回收不可见项
            RecycleInvisibleItems(start, end);

            // 渲染可见项
            RenderVisibleItems(start, end);

            // 更新范围
            if (start != _visibleStartIndex || end != _visibleEndIndex)
            {
                _visibleStartIndex = start;
                _visibleEndIndex = end;
                OnVisibleRangeChanged?.Invoke(start, end);
            }

            // 检查是否滚动到底部
            if (ScrollPercentage >= SCROLL_TO_BOTTOM_THRESHOLD)
            {
                OnScrollToBottom?.Invoke();
            }
        }

        /// <summary>
        /// 滚动到指定项
        /// </summary>
        public void ScrollToItem(int index, bool smooth = true)
        {
            if (!_isInitialized || _scrollRect == null)
                return;

            if (index < 0 || index >= _items.Count)
                return;

            float targetPosition = _itemPositions[index];
            float viewportHeight = _viewportTransform.rect.height;
            float contentHeight = _totalHeight;

            float normalizedPosition = 1f - Mathf.Clamp01(targetPosition / (contentHeight - viewportHeight));

            if (smooth)
            {
                DG.Tweening.DOTween.To(
                    () => _scrollRect.verticalNormalizedPosition,
                    x => _scrollRect.verticalNormalizedPosition = x,
                    normalizedPosition,
                    0.3f
                );
            }
            else
            {
                _scrollRect.verticalNormalizedPosition = normalizedPosition;
            }
        }

        #endregion

        #region Private Methods

        private void OnScrollValueChanged(Vector2 position)
        {
            if (Mathf.Abs(position.y - _lastScrollPosition) > 0.001f)
            {
                _lastScrollPosition = position.y;
                RefreshVisible();
            }
        }

        /// <summary>
        /// 重新计算所有项的位置
        /// </summary>
        private void RecalculatePositions()
        {
            _itemPositions.Clear();
            float currentY = 0f;

            foreach (var item in _items)
            {
                _itemPositions.Add(currentY);
                currentY += item.Height;
            }

            _totalHeight = currentY;
        }

        /// <summary>
        /// 计算可见范围
        /// </summary>
        private (int start, int end) CalculateVisibleRange()
        {
            if (_items.Count == 0)
                return (0, 0);

            float viewportHeight = _viewportTransform.rect.height;
            
            // 如果 viewport 高度无效（还没有布局完成），使用默认值或渲染更多项目
            if (viewportHeight <= 0)
            {
                // 使用屏幕高度作为备用，确保足够的项目被渲染
                viewportHeight = Screen.height;
            }
            
            float scrollPosition = _contentTransform.anchoredPosition.y;

            // 二分查找起始位置
            int start = FindItemAtPosition(scrollPosition);
            start = Mathf.Max(0, start - _bufferCount);

            // 查找结束位置
            float endPosition = scrollPosition + viewportHeight;
            int end = FindItemAtPosition(endPosition);
            end = Mathf.Min(_items.Count, end + _bufferCount + 1);

            return (start, end);
        }

        /// <summary>
        /// 二分查找指定位置的项索引
        /// </summary>
        private int FindItemAtPosition(float position)
        {
            if (_itemPositions.Count == 0)
                return 0;

            int left = 0;
            int right = _itemPositions.Count - 1;

            while (left < right)
            {
                int mid = (left + right + 1) / 2;
                if (_itemPositions[mid] <= position)
                {
                    left = mid;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return left;
        }

        /// <summary>
        /// 回收不可见项
        /// </summary>
        private void RecycleInvisibleItems(int newStart, int newEnd)
        {
            // 回收歌曲项
            var songItemsToRecycle = _activeSongItems
                .Where(pair => pair.Key < newStart || pair.Key >= newEnd)
                .ToList();

            foreach (var pair in songItemsToRecycle)
            {
                UnityEngine.Object.Destroy(pair.Value.gameObject);
                _activeSongItems.Remove(pair.Key);
            }

            // 回收专辑头项
            var headerItemsToRecycle = _activeHeaderItems
                .Where(pair => pair.Key < newStart || pair.Key >= newEnd)
                .ToList();

            foreach (var pair in headerItemsToRecycle)
            {
                UnityEngine.Object.Destroy(pair.Value.gameObject);
                _activeHeaderItems.Remove(pair.Key);
            }
        }

        /// <summary>
        /// 渲染可见项
        /// </summary>
        private void RenderVisibleItems(int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (i >= _items.Count)
                    break;

                var item = _items[i];

                if (item.ItemType == PlaylistItemType.Song)
                {
                    if (!_activeSongItems.ContainsKey(i))
                    {
                        RenderSongItem(i, item);
                    }
                }
                else if (item.ItemType == PlaylistItemType.AlbumHeader)
                {
                    if (!_activeHeaderItems.ContainsKey(i))
                    {
                        RenderAlbumHeader(i, item);
                    }
                }
            }
        }

        /// <summary>
        /// 渲染歌曲项
        /// </summary>
        private void RenderSongItem(int index, PlaylistListItem item)
        {
            if (_songButtonPrefab == null || _facilityMusic == null)
                return;

            var go = UnityEngine.Object.Instantiate(_songButtonPrefab, _contentTransform);
            var button = go.GetComponent<MusicPlayListButtons>();

            if (button == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }

            button.Setup(item.AudioInfo, _facilityMusic);
            PositionItem(go.GetComponent<RectTransform>(), index, item.Height);
            
            // 应用 RemoveButton 水平偏移
            ApplyRemoveButtonOffset(go.transform);

            _activeSongItems[index] = button;
        }
        
        /// <summary>
        /// 应用 RemoveButton 的水平偏移
        /// </summary>
        private void ApplyRemoveButtonOffset(Transform songItemTransform)
        {
            if (Mathf.Abs(RemoveButtonOffsetX) < 0.001f)
                return;

            // 路径: PlayListMusicPlayButton/RemoveButton
            var playButtonTransform = songItemTransform.Find("PlayListMusicPlayButton");
            if (playButtonTransform == null)
                return;

            var removeButtonTransform = playButtonTransform.Find("RemoveButton");
            if (removeButtonTransform == null)
                return;

            var rectTransform = removeButtonTransform as RectTransform;
            if (rectTransform == null)
                return;

            // 应用水平偏移
            var pos = rectTransform.anchoredPosition;
            pos.x += RemoveButtonOffsetX;
            rectTransform.anchoredPosition = pos;
        }

        /// <summary>
        /// 渲染专辑头
        /// </summary>
        private void RenderAlbumHeader(int index, PlaylistListItem item)
        {
            // 创建专辑头GameObject
            var go = new GameObject($"AlbumHeader_{index}");
            go.transform.SetParent(_contentTransform, false);

            var headerView = go.AddComponent<AlbumHeaderView>();
            headerView.Initialize();
            headerView.Setup(item.AlbumHeader);
            
            // 订阅专辑切换事件
            headerView.OnAlbumToggle += (albumId) => OnAlbumToggle?.Invoke(albumId);

            PositionItem(go.GetComponent<RectTransform>(), index, item.Height);

            _activeHeaderItems[index] = headerView;
        }

        /// <summary>
        /// 定位项目
        /// </summary>
        private void PositionItem(RectTransform rt, int index, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = new Vector2(0, -_itemPositions[index]);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 更新内容大小
        /// </summary>
        private void UpdateContentSize()
        {
            if (_contentTransform == null)
                return;

            float currentWidth = _contentTransform.sizeDelta.x;
            _contentTransform.sizeDelta = new Vector2(currentWidth, _totalHeight);
        }

        /// <summary>
        /// 清空所有活动项（公开方法，供外部调用）
        /// </summary>
        public void ClearAllItems()
        {
            ClearAllActiveItems();
        }
        
        /// <summary>
        /// 清空所有活动项
        /// </summary>
        private void ClearAllActiveItems()
        {
            foreach (var pair in _activeSongItems)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
                }
            }
            _activeSongItems.Clear();

            foreach (var pair in _activeHeaderItems)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
                }
            }
            _activeHeaderItems.Clear();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
            }

            // 取消订阅 CoverService 封面加载事件
            CoverService.Instance.OnAlbumCoverLoaded -= OnCoverLoaded;

            ClearAllActiveItems();

            _scrollRect = null;
            _contentTransform = null;
            _viewportTransform = null;
            _songButtonPrefab = null;
            _items = null;
            _itemPositions = null;
        }

        #endregion
    }
}

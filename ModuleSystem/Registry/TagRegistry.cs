using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.ModuleSystem.Registry
{
    /// <summary>
    /// Tag 注册表实现
    /// </summary>
    public class TagRegistry : ITagRegistry
    {
        private static TagRegistry _instance;
        public static TagRegistry Instance => _instance;

        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, TagInfo> _tags = new Dictionary<string, TagInfo>();
        private readonly Dictionary<ulong, TagInfo> _tagsByBitValue = new Dictionary<ulong, TagInfo>();
        private readonly object _lock = new object();

        // 下一个可用的位索引 (从 5 开始，0-4 留给游戏原生 Tag)
        private int _nextBitIndex = 5;
        private const int MAX_BIT_INDEX = 63; // ulong 最多 64 位

        // 当前选中的增长列表 Tag ID
        private string _currentGrowableTagId = null;

        public event Action<TagInfo> OnTagRegistered;
        public event Action<string> OnTagUnregistered;

        public static void Initialize(ManualLogSource logger)
        {
            if (_instance != null)
            {
                logger.LogWarning("TagRegistry 已初始化");
                return;
            }
            _instance = new TagRegistry(logger);
        }

        private TagRegistry(ManualLogSource logger)
        {
            _logger = logger;
        }

        public TagInfo RegisterTag(string tagId, string displayName, string moduleId)
        {
            return RegisterTagInternal(tagId, displayName, moduleId);
        }

        public void SetLoadMoreCallback(string tagId, Func<Task<int>> loadMoreCallback)
        {
            lock (_lock)
            {
                if (_tags.TryGetValue(tagId, out var tag))
                {
                    tag.LoadMoreCallback = loadMoreCallback;
                    _logger.LogInfo($"已设置 Tag '{tagId}' 的加载更多回调");
                }
            }
        }

        public void MarkAsGrowableTag(string tagId, string growableAlbumId)
        {
            lock (_lock)
            {
                if (!_tags.TryGetValue(tagId, out var tag))
                {
                    _logger.LogWarning($"尝试标记不存在的 Tag '{tagId}' 为增长 Tag");
                    return;
                }

                // 检查是否已有增长专辑
                if (tag.IsGrowableList && !string.IsNullOrEmpty(tag.GrowableAlbumId) && tag.GrowableAlbumId != growableAlbumId)
                {
                    throw new InvalidOperationException($"Tag '{tagId}' 已有增长专辑 '{tag.GrowableAlbumId}'，不能再添加增长专辑 '{growableAlbumId}'");
                }

                tag.IsGrowableList = true;
                tag.GrowableAlbumId = growableAlbumId;
                
                // 更新排序（增长 Tag 排在后面）
                if (tag.SortOrder < 1000)
                {
                    tag.SortOrder += 1000;
                }

                _logger.LogInfo($"Tag '{tagId}' 已标记为增长 Tag (增长专辑: {growableAlbumId})");
            }
        }

        private TagInfo RegisterTagInternal(string tagId, string displayName, string moduleId)
        {
            if (string.IsNullOrEmpty(tagId))
                throw new ArgumentException("Tag ID 不能为空", nameof(tagId));

            lock (_lock)
            {
                // 检查是否已存在
                if (_tags.ContainsKey(tagId))
                {
                    _logger.LogWarning($"Tag '{tagId}' 已存在，返回现有 Tag");
                    return _tags[tagId];
                }

                // 检查位数限制
                if (_nextBitIndex > MAX_BIT_INDEX)
                {
                    throw new InvalidOperationException($"已达到 Tag 数量上限 ({MAX_BIT_INDEX - 4} 个自定义 Tag)");
                }

                // 分配位值
                var bitValue = 1UL << _nextBitIndex;
                _nextBitIndex++;

                // 初始排序，增长 Tag 在注册增长专辑时会自动更新排序
                int sortOrder = _tags.Count;

                var tagInfo = new TagInfo
                {
                    TagId = tagId,
                    DisplayName = displayName,
                    ModuleId = moduleId,
                    BitValue = bitValue,
                    SortOrder = sortOrder,
                    IsGrowableList = false, // 初始为非增长，通过 MarkAsGrowableTag 设置
                    GrowableAlbumId = null,
                    LoadMoreCallback = null
                };

                _tags[tagId] = tagInfo;
                _tagsByBitValue[bitValue] = tagInfo;

                _logger.LogInfo($"注册 Tag: {displayName} (ID: {tagId}, Bit: {bitValue}, Module: {moduleId})");

                OnTagRegistered?.Invoke(tagInfo);
                return tagInfo;
            }
        }

        public void UnregisterTag(string tagId)
        {
            lock (_lock)
            {
                if (_tags.TryGetValue(tagId, out var tag))
                {
                    _tags.Remove(tagId);
                    _tagsByBitValue.Remove(tag.BitValue);
                    _logger.LogInfo($"注销 Tag: {tag.DisplayName} ({tagId})");
                    OnTagUnregistered?.Invoke(tagId);
                }
            }
        }

        public TagInfo GetTag(string tagId)
        {
            lock (_lock)
            {
                return _tags.TryGetValue(tagId, out var tag) ? tag : null;
            }
        }

        public IReadOnlyList<TagInfo> GetAllTags()
        {
            lock (_lock)
            {
                return _tags.Values.OrderBy(t => t.SortOrder).ToList();
            }
        }

        public IReadOnlyList<TagInfo> GetTagsByModule(string moduleId)
        {
            lock (_lock)
            {
                return _tags.Values
                    .Where(t => t.ModuleId == moduleId)
                    .OrderBy(t => t.SortOrder)
                    .ToList();
            }
        }

        public bool IsTagRegistered(string tagId)
        {
            lock (_lock)
            {
                return _tags.ContainsKey(tagId);
            }
        }

        public TagInfo GetTagByBitValue(ulong bitValue)
        {
            lock (_lock)
            {
                return _tagsByBitValue.TryGetValue(bitValue, out var tag) ? tag : null;
            }
        }

        /// <summary>
        /// 注销指定模块的所有 Tag
        /// </summary>
        public void UnregisterAllByModule(string moduleId)
        {
            lock (_lock)
            {
                var tagsToRemove = _tags.Values
                    .Where(t => t.ModuleId == moduleId)
                    .Select(t => t.TagId)
                    .ToList();

                foreach (var tagId in tagsToRemove)
                {
                    UnregisterTag(tagId);
                }
            }
        }

        /// <summary>
        /// 获取当前选中的增长列表 Tag
        /// </summary>
        public TagInfo GetCurrentGrowableTag()
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_currentGrowableTagId))
                    return null;
                return _tags.TryGetValue(_currentGrowableTagId, out var tag) ? tag : null;
            }
        }

        /// <summary>
        /// 设置当前选中的增长列表 Tag
        /// 由 UI 层在 Tag 选中状态变化时调用
        /// </summary>
        public void SetCurrentGrowableTag(string tagId)
        {
            lock (_lock)
            {
                // 验证 Tag 存在且是增长列表
                if (!string.IsNullOrEmpty(tagId))
                {
                    if (!_tags.TryGetValue(tagId, out var tag) || !tag.IsGrowableList)
                    {
                        _logger.LogWarning($"Tag '{tagId}' 不存在或不是增长列表");
                        return;
                    }
                }

                var oldTagId = _currentGrowableTagId;
                _currentGrowableTagId = tagId;

                if (oldTagId != tagId)
                {
                    _logger.LogInfo($"当前增长列表 Tag 变更: {oldTagId ?? "(无)"} -> {tagId ?? "(无)"}");
                }
            }
        }

        /// <summary>
        /// 获取所有增长列表 Tag
        /// </summary>
        public IReadOnlyList<TagInfo> GetGrowableTags()
        {
            lock (_lock)
            {
                return _tags.Values
                    .Where(t => t.IsGrowableList)
                    .OrderBy(t => t.SortOrder)
                    .ToList();
            }
        }
    }
}

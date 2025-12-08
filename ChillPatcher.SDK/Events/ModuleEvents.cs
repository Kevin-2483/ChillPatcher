using ChillPatcher.SDK.Models;

namespace ChillPatcher.SDK.Events
{
    /// <summary>
    /// 模块事件基础接口
    /// 所有事件必须实现此接口
    /// </summary>
    public interface IModuleEvent
    {
        /// <summary>
        /// 事件发生的时间戳
        /// </summary>
        long Timestamp { get; }
    }

    /// <summary>
    /// 事件基类
    /// </summary>
    public abstract class ModuleEventBase : IModuleEvent
    {
        public long Timestamp { get; } = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    #region 播放事件

    /// <summary>
    /// 播放开始事件
    /// </summary>
    public class PlayStartedEvent : ModuleEventBase
    {
        /// <summary>
        /// 正在播放的歌曲信息
        /// </summary>
        public MusicInfo Music { get; set; }

        /// <summary>
        /// 播放来源 (队列、随机、用户点击等)
        /// </summary>
        public PlaySource Source { get; set; }
    }

    /// <summary>
    /// 播放结束事件
    /// </summary>
    public class PlayEndedEvent : ModuleEventBase
    {
        /// <summary>
        /// 播放结束的歌曲信息
        /// </summary>
        public MusicInfo Music { get; set; }

        /// <summary>
        /// 结束原因
        /// </summary>
        public PlayEndReason Reason { get; set; }

        /// <summary>
        /// 实际播放时长 (秒)
        /// </summary>
        public float PlayedDuration { get; set; }
    }

    /// <summary>
    /// 播放暂停事件
    /// </summary>
    public class PlayPausedEvent : ModuleEventBase
    {
        public MusicInfo Music { get; set; }
        public bool IsPaused { get; set; }
    }

    /// <summary>
    /// 播放进度变化事件
    /// </summary>
    public class PlayProgressEvent : ModuleEventBase
    {
        public MusicInfo Music { get; set; }
        public float CurrentTime { get; set; }
        public float TotalTime { get; set; }
        public float Progress { get; set; }
    }

    /// <summary>
    /// 播放来源
    /// </summary>
    public enum PlaySource
    {
        /// <summary>
        /// 用户点击
        /// </summary>
        UserClick,

        /// <summary>
        /// 播放队列
        /// </summary>
        Queue,

        /// <summary>
        /// 随机播放
        /// </summary>
        Shuffle,

        /// <summary>
        /// 自动下一首
        /// </summary>
        AutoNext,

        /// <summary>
        /// 上一首
        /// </summary>
        Previous
    }

    /// <summary>
    /// 播放结束原因
    /// </summary>
    public enum PlayEndReason
    {
        /// <summary>
        /// 自然结束
        /// </summary>
        Completed,

        /// <summary>
        /// 用户跳过
        /// </summary>
        Skipped,

        /// <summary>
        /// 用户停止
        /// </summary>
        Stopped,

        /// <summary>
        /// 加载失败
        /// </summary>
        Failed,

        /// <summary>
        /// 被其他歌曲替换
        /// </summary>
        Replaced
    }

    #endregion

    #region Tag 和专辑事件

    /// <summary>
    /// Tag 切换事件
    /// </summary>
    public class TagChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// 之前的 Tag ID
        /// </summary>
        public string OldTagId { get; set; }

        /// <summary>
        /// 当前的 Tag ID
        /// </summary>
        public string NewTagId { get; set; }

        /// <summary>
        /// Tag 信息
        /// </summary>
        public TagInfo Tag { get; set; }
    }

    /// <summary>
    /// 专辑切换事件
    /// </summary>
    public class AlbumChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// 之前的专辑 ID
        /// </summary>
        public string OldAlbumId { get; set; }

        /// <summary>
        /// 当前的专辑 ID
        /// </summary>
        public string NewAlbumId { get; set; }

        /// <summary>
        /// 专辑信息
        /// </summary>
        public AlbumInfo Album { get; set; }
    }

    #endregion

    #region 歌单事件

    /// <summary>
    /// 歌单更新事件
    /// </summary>
    public class PlaylistUpdatedEvent : ModuleEventBase
    {
        /// <summary>
        /// 更新的 Tag ID
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// 更新类型
        /// </summary>
        public PlaylistUpdateType UpdateType { get; set; }

        /// <summary>
        /// 变化的歌曲数量
        /// </summary>
        public int ChangedCount { get; set; }
    }

    /// <summary>
    /// 歌单更新类型
    /// </summary>
    public enum PlaylistUpdateType
    {
        /// <summary>
        /// 完全刷新
        /// </summary>
        FullRefresh,

        /// <summary>
        /// 新增歌曲
        /// </summary>
        Added,

        /// <summary>
        /// 新增歌曲 (别名)
        /// </summary>
        SongAdded = Added,

        /// <summary>
        /// 移除歌曲
        /// </summary>
        Removed,

        /// <summary>
        /// 移除歌曲 (别名)
        /// </summary>
        SongRemoved = Removed,

        /// <summary>
        /// 顺序变化
        /// </summary>
        Reordered
    }

    /// <summary>
    /// 歌单顺序变化事件
    /// </summary>
    public class PlaylistOrderChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// Tag ID
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// 更新类型
        /// </summary>
        public PlaylistUpdateType UpdateType { get; set; }

        /// <summary>
        /// 涉及的歌曲 UUID 列表
        /// </summary>
        public string[] AffectedUUIDs { get; set; }

        /// <summary>
        /// 涉及的歌曲 UUID 列表 (别名)
        /// </summary>
        public string[] AffectedSongUUIDs
        {
            get => AffectedUUIDs;
            set => AffectedUUIDs = value;
        }

        /// <summary>
        /// 模块 ID (可选)
        /// </summary>
        public string ModuleId { get; set; }
    }

    #endregion

    #region 收藏和排除事件

    /// <summary>
    /// 收藏状态变化事件
    /// </summary>
    public class FavoriteChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// 歌曲 UUID
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// 是否收藏
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// 歌曲信息
        /// </summary>
        public MusicInfo Music { get; set; }

        /// <summary>
        /// 模块 ID (可选，用于标识来源模块)
        /// </summary>
        public string ModuleId { get; set; }
    }

    /// <summary>
    /// 排除状态变化事件
    /// </summary>
    public class ExcludeChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// 歌曲 UUID
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// 是否排除
        /// </summary>
        public bool IsExcluded { get; set; }

        /// <summary>
        /// 歌曲信息
        /// </summary>
        public MusicInfo Music { get; set; }

        /// <summary>
        /// 模块 ID (可选，用于标识来源模块)
        /// </summary>
        public string ModuleId { get; set; }
    }

    #endregion

    #region 模块生命周期事件

    /// <summary>
    /// 模块加载完成事件
    /// </summary>
    public class ModuleLoadedEvent : ModuleEventBase
    {
        /// <summary>
        /// 模块 ID
        /// </summary>
        public string ModuleId { get; set; }

        /// <summary>
        /// 模块显示名称
        /// </summary>
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// 模块卸载事件
    /// </summary>
    public class ModuleUnloadedEvent : ModuleEventBase
    {
        public string ModuleId { get; set; }
    }

    /// <summary>
    /// 所有模块加载完成事件
    /// </summary>
    public class AllModulesLoadedEvent : ModuleEventBase
    {
        /// <summary>
        /// 加载的模块数量
        /// </summary>
        public int ModuleCount { get; set; }
    }

    #endregion

    #region 队列事件

    /// <summary>
    /// 队列变化事件
    /// </summary>
    public class QueueChangedEvent : ModuleEventBase
    {
        /// <summary>
        /// 变化类型
        /// </summary>
        public QueueChangeType ChangeType { get; set; }

        /// <summary>
        /// 当前队列长度
        /// </summary>
        public int QueueLength { get; set; }
    }

    /// <summary>
    /// 队列变化类型
    /// </summary>
    public enum QueueChangeType
    {
        Added,
        Removed,
        Cleared,
        Reordered
    }

    #endregion

    #region 增长列表事件

    /// <summary>
    /// 增长列表触底事件
    /// 当用户滚动到增长列表 Tag 对应歌曲的底部时触发
    /// 模块可以选择：
    /// 1. 订阅此事件并调用 ReportLoaded() 来通知完成
    /// 2. 或在注册 Tag 时提供 LoadMoreCallback（两种方式可同时使用）
    /// </summary>
    public class GrowableListBottomOutEvent : ModuleEventBase
    {
        /// <summary>
        /// 触底的 Tag ID (增长列表)
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// 触底的 Tag 信息
        /// </summary>
        public TagInfo TagInfo { get; set; }

        /// <summary>
        /// 当前列表中该 Tag 下的歌曲数量
        /// </summary>
        public int CurrentSongCount { get; set; }

        /// <summary>
        /// 用于报告加载完成的回调
        /// 模块加载完数据后应调用此方法通知主插件刷新 UI
        /// </summary>
        public System.Action<int> ReportLoaded { get; set; }
    }

    /// <summary>
    /// 增长列表加载完成事件
    /// 当 LoadMoreCallback 执行完成后触发
    /// </summary>
    public class GrowableListLoadedEvent : ModuleEventBase
    {
        /// <summary>
        /// Tag ID
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// 新加载的歌曲数量
        /// </summary>
        public int LoadedCount { get; set; }

        /// <summary>
        /// 是否还有更多数据
        /// </summary>
        public bool HasMore { get; set; }
    }

    #endregion
}

namespace ChillPatcher.SDK.Models
{
    /// <summary>
    /// Tag 信息模型
    /// </summary>
    public class TagInfo
    {
        /// <summary>
        /// Tag 唯一标识符
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// Tag 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 所属模块 ID
        /// </summary>
        public string ModuleId { get; set; }

        /// <summary>
        /// Tag 的位值 (用于游戏内部的位运算)
        /// </summary>
        public ulong BitValue { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 图标路径 (如果适用)
        /// </summary>
        public string IconPath { get; set; }

        /// <summary>
        /// Tag 下的专辑数量 (运行时计算)
        /// </summary>
        public int AlbumCount { get; set; }

        /// <summary>
        /// Tag 下的歌曲数量 (运行时计算)
        /// </summary>
        public int SongCount { get; set; }

        /// <summary>
        /// 是否显示在 Tag 列表中
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// 是否为增长列表 (无限滚动列表)
        /// 增长列表的特点：
        /// 1. 一次只能选中一个增长列表 Tag（互斥）
        /// 2. 可以和非增长列表 Tag 一起选中
        /// 3. 增长列表的专辑/歌曲始终排在列表最后
        /// 4. 滚动到底部时会触发加载更多事件
        /// </summary>
        public bool IsGrowableList { get; set; } = false;

        /// <summary>
        /// 增长专辑的 ID
        /// 只有 IsGrowableList=true 时有效
        /// 如果为 null，则该 Tag 下所有专辑都视为增长专辑
        /// 如果指定了值，只有该专辑触发加载更多
        /// </summary>
        public string GrowableAlbumId { get; set; }

        /// <summary>
        /// 增长列表的加载更多回调
        /// 返回新加载的歌曲数量，返回 0 表示没有更多数据
        /// </summary>
        public System.Func<System.Threading.Tasks.Task<int>> LoadMoreCallback { get; set; }

        /// <summary>
        /// 扩展数据 (模块自定义使用)
        /// </summary>
        public object ExtendedData { get; set; }
    }
}

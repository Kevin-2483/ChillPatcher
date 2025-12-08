using System.Collections.Generic;

namespace ChillPatcher.SDK.Models
{
    /// <summary>
    /// 专辑信息模型
    /// </summary>
    public class AlbumInfo
    {
        /// <summary>
        /// 专辑唯一标识符
        /// </summary>
        public string AlbumId { get; set; }

        /// <summary>
        /// 专辑显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 专辑艺术家
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// 所属 Tag ID (已废弃，请使用 TagIds)
        /// 为保持向后兼容，设置此属性会添加到 TagIds 中
        /// </summary>
        public string TagId
        {
            get => TagIds?.Count > 0 ? TagIds[0] : null;
            set
            {
                if (TagIds == null) TagIds = new List<string>();
                if (!string.IsNullOrEmpty(value) && !TagIds.Contains(value))
                {
                    TagIds.Clear();
                    TagIds.Add(value);
                }
            }
        }

        /// <summary>
        /// 所属 Tag ID 列表
        /// 专辑可以同时属于多个 Tag
        /// 同时选中多个 Tag 时，相同专辑的内容会合并显示
        /// </summary>
        public List<string> TagIds { get; set; } = new List<string>();

        /// <summary>
        /// 所属模块 ID
        /// </summary>
        public string ModuleId { get; set; }

        /// <summary>
        /// 专辑目录路径 (如果适用)
        /// </summary>
        public string DirectoryPath { get; set; }

        /// <summary>
        /// 封面图片路径 (如果适用)
        /// </summary>
        public string CoverPath { get; set; }

        /// <summary>
        /// 专辑中的歌曲数量 (运行时计算)
        /// </summary>
        public int SongCount { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 是否是默认专辑 (无专辑归类)
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// 是否为增长专辑
        /// 增长专辑是增长列表 Tag 中触发加载更多的专辑
        /// 特点：
        /// 1. 始终排在 Tag 内所有专辑的最后面
        /// 2. 滚动到底部时触发 LoadMoreCallback
        /// 3. 一个增长 Tag 只能有一个增长专辑
        /// </summary>
        public bool IsGrowableAlbum { get; set; }

        /// <summary>
        /// 扩展数据 (模块自定义使用)
        /// </summary>
        public object ExtendedData { get; set; }
    }
}

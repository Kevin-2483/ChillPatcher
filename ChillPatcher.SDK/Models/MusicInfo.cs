using System;

namespace ChillPatcher.SDK.Models
{
    /// <summary>
    /// 音乐源类型
    /// </summary>
    public enum MusicSourceType
    {
        /// <summary>
        /// 本地文件
        /// </summary>
        File = 0,

        /// <summary>
        /// URL 链接
        /// </summary>
        Url = 1,

        /// <summary>
        /// 已加载的 AudioClip
        /// </summary>
        Clip = 2,

        /// <summary>
        /// 流媒体
        /// </summary>
        Stream = 3
    }

    /// <summary>
    /// 音乐信息模型
    /// </summary>
    public class MusicInfo
    {
        /// <summary>
        /// 歌曲唯一标识符 (UUID)
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// 歌曲标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 艺术家
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// 所属专辑 ID
        /// </summary>
        public string AlbumId { get; set; }

        /// <summary>
        /// 所属 Tag ID
        /// </summary>
        public string TagId { get; set; }

        /// <summary>
        /// 音乐源类型
        /// </summary>
        public MusicSourceType SourceType { get; set; }

        /// <summary>
        /// 源路径 (文件路径或 URL)
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// 时长 (秒)
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// 所属模块 ID
        /// </summary>
        public string ModuleId { get; set; }

        /// <summary>
        /// 是否已解锁 (默认 true)
        /// </summary>
        public bool IsUnlocked { get; set; } = true;

        /// <summary>
        /// 扩展数据 (模块自定义使用)
        /// </summary>
        public object ExtendedData { get; set; }

        /// <summary>
        /// 是否被排除
        /// </summary>
        public bool IsExcluded { get; set; }

        /// <summary>
        /// 是否收藏
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// 是否可删除
        /// 默认跟随模块的 CanDelete 设置，但可以单独覆盖
        /// null 表示使用模块默认设置
        /// </summary>
        public bool? IsDeletable { get; set; } = null;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后播放时间
        /// </summary>
        public DateTime? LastPlayedAt { get; set; }

        /// <summary>
        /// 播放次数
        /// </summary>
        public int PlayCount { get; set; }

        /// <summary>
        /// 生成一个新的 UUID
        /// </summary>
        public static string GenerateUUID()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// 根据文件路径生成确定性 UUID
        /// </summary>
        public static string GenerateUUID(string sourcePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sourcePath));
                return new Guid(hash).ToString("N");
            }
        }
    }
}

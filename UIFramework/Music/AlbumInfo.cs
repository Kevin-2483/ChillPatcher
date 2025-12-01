using System;
using System.Collections.Generic;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 专辑信息
    /// </summary>
    public class AlbumInfo
    {
        /// <summary>
        /// 专辑唯一ID（歌单ID + 专辑文件夹名）
        /// </summary>
        public string AlbumId { get; set; }

        /// <summary>
        /// 专辑显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 所属歌单ID
        /// </summary>
        public string PlaylistId { get; set; }

        /// <summary>
        /// 专辑目录路径
        /// </summary>
        public string DirectoryPath { get; set; }

        /// <summary>
        /// 专辑中的歌曲UUID列表
        /// </summary>
        public List<string> SongUUIDs { get; set; } = new List<string>();

        /// <summary>
        /// 总歌曲数
        /// </summary>
        public int TotalSongCount => SongUUIDs.Count;
    }

    /// <summary>
    /// 专辑状态信息
    /// </summary>
    public class AlbumStatus
    {
        /// <summary>
        /// 专辑ID
        /// </summary>
        public string AlbumId { get; set; }

        /// <summary>
        /// 是否启用（有任意歌曲未排除）
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 启用的歌曲数量
        /// </summary>
        public int EnabledSongCount { get; set; }

        /// <summary>
        /// 总歌曲数量
        /// </summary>
        public int TotalSongCount { get; set; }

        /// <summary>
        /// 排除的歌曲UUID列表
        /// </summary>
        public List<string> ExcludedSongUUIDs { get; set; } = new List<string>();

        /// <summary>
        /// 启用的歌曲UUID列表
        /// </summary>
        public List<string> EnabledSongUUIDs { get; set; } = new List<string>();
    }
}

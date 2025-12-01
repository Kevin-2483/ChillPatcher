using System;
using Bulbul;
using UnityEngine;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 播放列表项类型
    /// </summary>
    public enum PlaylistItemType
    {
        /// <summary>
        /// 歌曲项
        /// </summary>
        Song,
        
        /// <summary>
        /// 专辑分隔头
        /// </summary>
        AlbumHeader
    }

    /// <summary>
    /// 播放列表项（可以是歌曲或专辑分隔）
    /// </summary>
    public class PlaylistListItem
    {
        /// <summary>
        /// 歌曲项高度
        /// </summary>
        public const float SONG_ITEM_HEIGHT = 60f;

        /// <summary>
        /// 项目类型
        /// </summary>
        public PlaylistItemType ItemType { get; }

        /// <summary>
        /// 歌曲信息（仅当 ItemType == Song 时有效）
        /// </summary>
        public GameAudioInfo SongInfo { get; }

        /// <summary>
        /// 专辑头信息（仅当 ItemType == AlbumHeader 时有效）
        /// </summary>
        public AlbumHeaderInfo AlbumHeader { get; }

        /// <summary>
        /// 在原始列表中的索引（用于歌曲）
        /// </summary>
        public int OriginalIndex { get; }

        /// <summary>
        /// 获取项目高度
        /// </summary>
        public float Height
        {
            get
            {
                if (ItemType == PlaylistItemType.Song)
                {
                    return SONG_ITEM_HEIGHT;
                }
                else if (AlbumHeader != null)
                {
                    return AlbumHeader.Height;
                }
                return SONG_ITEM_HEIGHT;
            }
        }

        private PlaylistListItem(PlaylistItemType type, GameAudioInfo songInfo, AlbumHeaderInfo albumHeader, int originalIndex)
        {
            ItemType = type;
            SongInfo = songInfo;
            AlbumHeader = albumHeader;
            OriginalIndex = originalIndex;
        }

        /// <summary>
        /// 创建歌曲项
        /// </summary>
        public static PlaylistListItem CreateSongItem(GameAudioInfo songInfo, int originalIndex)
        {
            return new PlaylistListItem(PlaylistItemType.Song, songInfo, null, originalIndex);
        }

        /// <summary>
        /// 创建专辑头项
        /// </summary>
        public static PlaylistListItem CreateAlbumHeader(AlbumHeaderInfo albumHeader)
        {
            return new PlaylistListItem(PlaylistItemType.AlbumHeader, null, albumHeader, -1);
        }
    }

    /// <summary>
    /// 专辑头信息
    /// </summary>
    public class AlbumHeaderInfo
    {
        /// <summary>
        /// 专辑ID
        /// </summary>
        public string AlbumId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 创作者/艺术家（可选）
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// 启用的歌曲数量
        /// </summary>
        public int EnabledSongCount { get; set; }

        /// <summary>
        /// 总歌曲数量
        /// </summary>
        public int TotalSongCount { get; set; }

        /// <summary>
        /// 专辑目录路径
        /// </summary>
        public string DirectoryPath { get; set; }

        /// <summary>
        /// 封面图片（懒加载）
        /// </summary>
        public Sprite CoverImage { get; set; }

        /// <summary>
        /// 是否为"其他"专辑
        /// </summary>
        public bool IsOtherAlbum { get; set; }

        /// <summary>
        /// 是否有真实封面（非占位图）
        /// </summary>
        public bool HasRealCover => CoverImage != null;

        /// <summary>
        /// 是否有封面（包括占位图，始终为true）
        /// </summary>
        public bool HasCover => true;  // 所有专辑头都使用带封面的布局

        /// <summary>
        /// 头部高度（始终使用带封面的高度）
        /// </summary>
        public float Height => AlbumHeaderView.HeaderHeightWithCover;

        /// <summary>
        /// 统计信息字符串
        /// </summary>
        public string StatsText => $"{EnabledSongCount}/{TotalSongCount}";
    }
}

using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云歌曲注册管理器
    /// 处理歌曲和专辑的注册逻辑
    /// </summary>
    public class NeteaseSongRegistry
    {
        private readonly ManualLogSource _logger;
        private readonly IModuleContext _context;
        private readonly string _moduleId;
        private readonly Dictionary<string, NeteaseBridge.SongInfo> _songInfoMap;
        private readonly NeteaseFavoriteManager _favoriteManager;

        public const string TAG_FAVORITES = "netease_favorites";
        public const string TAG_PERSONAL_FM = "netease_personal_fm";
        public const string FAVORITES_ALBUM_ID = "netease_favorites_album";
        public const string PERSONAL_FM_ALBUM_ID = "netease_personal_fm_album";

        public NeteaseSongRegistry(
            IModuleContext context,
            string moduleId,
            Dictionary<string, NeteaseBridge.SongInfo> songInfoMap,
            NeteaseFavoriteManager favoriteManager,
            ManualLogSource logger)
        {
            _context = context;
            _moduleId = moduleId;
            _songInfoMap = songInfoMap;
            _favoriteManager = favoriteManager;
            _logger = logger;
        }

        /// <summary>
        /// 注册收藏专辑 (普通专辑，同时属于收藏 Tag 和 FM Tag)
        /// </summary>
        public void RegisterFavoritesAlbum(int songCount)
        {
            var album = new AlbumInfo
            {
                AlbumId = FAVORITES_ALBUM_ID,
                DisplayName = "网易云音乐收藏",
                Artist = "网易云音乐",
                // 同时属于收藏 Tag 和 FM Tag
                TagIds = new List<string> { TAG_FAVORITES, TAG_PERSONAL_FM },
                ModuleId = _moduleId,
                SongCount = songCount,
                SortOrder = 0, // 排在增长专辑前面
                IsGrowableAlbum = false,
                ExtendedData = "FAVORITES"
            };
            _context.AlbumRegistry.RegisterAlbum(album, _moduleId);
        }

        /// <summary>
        /// 注册个人FM专辑 (增长专辑，属于 FM Tag，排在最后)
        /// 注册后会自动将 FM Tag 标记为增长 Tag
        /// 注意：收藏专辑已在 RegisterFavoritesAlbum 中同时注册到 FM Tag
        /// </summary>
        public void RegisterFMAlbum(int songCount)
        {
            // 注册 FM 专辑（增长专辑，排在后面）
            var album = new AlbumInfo
            {
                AlbumId = PERSONAL_FM_ALBUM_ID,
                DisplayName = "个人FM",
                Artist = "网易云音乐",
                TagIds = new List<string> { TAG_PERSONAL_FM }, // 仅属于 FM Tag
                ModuleId = _moduleId,
                SongCount = songCount,
                SortOrder = 1000, // 增长专辑排在最后
                IsGrowableAlbum = true, // 这是增长专辑，会自动标记 FM Tag 为增长 Tag
                ExtendedData = "FM"
            };
            _context.AlbumRegistry.RegisterAlbum(album, _moduleId);
        }

        /// <summary>
        /// 注册收藏歌曲列表
        /// 歌曲属于收藏 Tag 的收藏专辑
        /// </summary>
        public List<MusicInfo> RegisterFavoritesSongs(IEnumerable<NeteaseBridge.SongInfo> songs)
        {
            var musicList = new List<MusicInfo>();

            foreach (var song in songs)
            {
                var uuid = GenerateUUID(song.Id);
                var isLiked = _favoriteManager.IsSongLiked(song.Id);

                var musicInfo = new MusicInfo
                {
                    UUID = uuid,
                    Title = song.Name,
                    Artist = song.ArtistName,
                    AlbumId = FAVORITES_ALBUM_ID, // 收藏专辑
                    TagId = TAG_FAVORITES, // 收藏 Tag
                    SourceType = MusicSourceType.Stream,
                    SourcePath = song.Id.ToString(),
                    Duration = (float)song.Duration,
                    ModuleId = _moduleId,
                    IsUnlocked = true,
                    IsFavorite = isLiked,
                    ExtendedData = song
                };

                musicList.Add(musicInfo);
                _songInfoMap[uuid] = song;
                _context.MusicRegistry.RegisterMusic(musicInfo, _moduleId);
            }

            return musicList;
        }

        /// <summary>
        /// 注册FM歌曲列表
        /// </summary>
        public List<MusicInfo> RegisterFMSongs(IEnumerable<NeteaseBridge.SongInfo> songs)
        {
            var musicList = new List<MusicInfo>();

            foreach (var song in songs)
            {
                var uuid = GenerateUUID(song.Id);

                // 如果已经注册过（可能在收藏列表中），跳过
                if (_songInfoMap.ContainsKey(uuid))
                    continue;

                var isLiked = _favoriteManager.IsSongLiked(song.Id);

                var musicInfo = new MusicInfo
                {
                    UUID = uuid,
                    Title = song.Name,
                    Artist = song.ArtistName,
                    AlbumId = PERSONAL_FM_ALBUM_ID,
                    TagId = TAG_PERSONAL_FM,
                    SourceType = MusicSourceType.Stream,
                    SourcePath = song.Id.ToString(),
                    Duration = (float)song.Duration,
                    ModuleId = _moduleId,
                    IsUnlocked = true,
                    IsFavorite = isLiked,
                    IsDeletable = !isLiked,  // 未收藏的 FM 歌曲可以删除（标记为不喜欢）
                    ExtendedData = song
                };

                musicList.Add(musicInfo);
                _songInfoMap[uuid] = song;
                _context.MusicRegistry.RegisterMusic(musicInfo, _moduleId);
            }

            return musicList;
        }

        /// <summary>
        /// 将 FM 歌曲移动到收藏专辑
        /// AlbumId 改变（改为收藏专辑），TagId 保持不变（仍在 FM Tag 下）
        /// </summary>
        public void MoveSongToFavorites(string uuid, List<MusicInfo> fmMusicList, List<MusicInfo> favoritesMusicList)
        {
            // 找到 FM 列表中的歌曲
            var fmMusic = fmMusicList.FirstOrDefault(m => m.UUID == uuid);
            if (fmMusic == null) return;

            // 只更新 AlbumId，TagId 保持不变
            // fmMusic.TagId 保持 TAG_PERSONAL_FM 不变
            fmMusic.AlbumId = FAVORITES_ALBUM_ID; // 改为收藏专辑
            fmMusic.IsFavorite = true;

            // 移动到收藏列表
            fmMusicList.Remove(fmMusic);
            favoritesMusicList.Add(fmMusic);

            // 更新注册信息
            _context.MusicRegistry.UpdateMusic(fmMusic);
        }

        #region 自定义歌单

        /// <summary>
        /// 生成歌单专属的 Tag ID
        /// </summary>
        public static string GeneratePlaylistTagId(long playlistId)
        {
            return $"netease_playlist_{playlistId}";
        }

        /// <summary>
        /// 生成歌单专属的 Album ID
        /// </summary>
        public static string GeneratePlaylistAlbumId(long playlistId)
        {
            return $"netease_playlist_album_{playlistId}";
        }

        /// <summary>
        /// 注册自定义歌单的 Tag
        /// </summary>
        /// <param name="playlistId">歌单 ID</param>
        /// <param name="displayName">显示名称</param>
        public void RegisterPlaylistTag(long playlistId, string displayName)
        {
            var tagId = GeneratePlaylistTagId(playlistId);
            _context.TagRegistry.RegisterTag(tagId, displayName, _moduleId);
            _logger.LogInfo($"[NeteaseSongRegistry] 已注册 Tag: {displayName} ({tagId})");
            
            // 将收藏专辑也注册到这个 Tag 下，这样歌曲收藏后可以正确显示在收藏专辑中
            AddFavoritesAlbumToTag(tagId);
        }
        
        /// <summary>
        /// 将收藏专辑添加到指定 Tag 下
        /// </summary>
        private void AddFavoritesAlbumToTag(string tagId)
        {
            var favoritesAlbum = _context.AlbumRegistry.GetAlbum(FAVORITES_ALBUM_ID);
            if (favoritesAlbum == null)
            {
                _logger.LogWarning($"[NeteaseSongRegistry] 收藏专辑未找到，无法添加到 Tag: {tagId}");
                return;
            }
            
            // 检查是否已包含此 Tag
            if (favoritesAlbum.TagIds.Contains(tagId))
                return;
            
            // 添加新的 TagId
            favoritesAlbum.TagIds.Add(tagId);
            
            // 重新注册专辑以更新索引
            _context.AlbumRegistry.RegisterAlbum(favoritesAlbum, _moduleId);
            _logger.LogInfo($"[NeteaseSongRegistry] 已将收藏专辑添加到 Tag: {tagId}");
        }

        /// <summary>
        /// 注册自定义歌单专辑
        /// </summary>
        public void RegisterPlaylistAlbum(long playlistId, string name, int songCount, string coverUrl)
        {
            var tagId = GeneratePlaylistTagId(playlistId);
            var albumId = GeneratePlaylistAlbumId(playlistId);

            var album = new AlbumInfo
            {
                AlbumId = albumId,
                DisplayName = name,
                Artist = "网易云音乐",
                TagIds = new List<string> { tagId },
                ModuleId = _moduleId,
                SongCount = songCount,
                SortOrder = 0,
                IsGrowableAlbum = false,
                ExtendedData = $"PLAYLIST:{playlistId}:{coverUrl}"
            };
            _context.AlbumRegistry.RegisterAlbum(album, _moduleId);
            _logger.LogInfo($"[NeteaseSongRegistry] 已注册歌单专辑: {name} ({songCount} 首)");
        }

        /// <summary>
        /// 注册自定义歌单中的歌曲
        /// </summary>
        public List<MusicInfo> RegisterPlaylistSongs(long playlistId, IEnumerable<NeteaseBridge.SongInfo> songs)
        {
            var musicList = new List<MusicInfo>();
            var tagId = GeneratePlaylistTagId(playlistId);
            var albumId = GeneratePlaylistAlbumId(playlistId);

            foreach (var song in songs)
            {
                var uuid = GenerateUUID(song.Id);

                // 如果已经注册过（可能在收藏列表或其他歌单中），跳过
                if (_songInfoMap.ContainsKey(uuid))
                {
                    // 获取已存在的 MusicInfo 并添加到列表
                    var existingMusic = _context.MusicRegistry.GetMusic(uuid);
                    if (existingMusic != null)
                    {
                        musicList.Add(existingMusic);
                    }
                    continue;
                }

                var isLiked = _favoriteManager.IsSongLiked(song.Id);

                var musicInfo = new MusicInfo
                {
                    UUID = uuid,
                    Title = song.Name,
                    Artist = song.ArtistName,
                    AlbumId = albumId,
                    TagId = tagId,
                    SourceType = MusicSourceType.Stream,
                    SourcePath = song.Id.ToString(),
                    Duration = (float)song.Duration,
                    ModuleId = _moduleId,
                    IsUnlocked = true,
                    IsFavorite = isLiked,
                    ExtendedData = song
                };

                musicList.Add(musicInfo);
                _songInfoMap[uuid] = song;
                _context.MusicRegistry.RegisterMusic(musicInfo, _moduleId);
            }

            return musicList;
        }

        #endregion

        /// <summary>
        /// 生成确定性 UUID
        /// </summary>
        public static string GenerateUUID(long songId)
        {
            return MusicInfo.GenerateUUID($"netease:{songId}");
        }
    }
}

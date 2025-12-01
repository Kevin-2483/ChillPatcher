using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.UIFramework.Data;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 播放列表构建器 - 将歌曲列表转换为包含专辑分隔的复合列表
    /// </summary>
    public class PlaylistListBuilder
    {
        private static readonly BepInEx.Logging.ManualLogSource Logger = 
            BepInEx.Logging.Logger.CreateLogSource("PlaylistBuilder");
            
        private readonly AlbumManager _albumManager;
        private AlbumCoverLoader _coverLoader;

        public PlaylistListBuilder()
        {
            _albumManager = AlbumManager.Instance;
            // 延迟初始化 CoverLoader，因为数据库可能还没准备好
        }
        
        /// <summary>
        /// 确保 CoverLoader 已初始化
        /// </summary>
        private AlbumCoverLoader GetCoverLoader()
        {
            if (_coverLoader == null)
            {
                var db = CustomPlaylistDataManager.Instance?.GetDatabase();
                _coverLoader = new AlbumCoverLoader(db);
                Logger.LogInfo($"CoverLoader initialized. DB={(db != null ? "OK" : "NULL")}");
            }
            return _coverLoader;
        }

        /// <summary>
        /// 构建带专辑分隔的播放列表（根据歌曲的专辑信息分组）
        /// </summary>
        /// <param name="songs">原始歌曲列表（已经过滤）</param>
        /// <param name="loadCovers">是否加载封面</param>
        /// <returns>包含专辑分隔的复合列表</returns>
        public async Task<List<PlaylistListItem>> BuildWithAlbumHeaders(
            IReadOnlyList<GameAudioInfo> songs,
            bool loadCovers = true)
        {
            var result = new List<PlaylistListItem>();
            Logger.LogInfo($"BuildWithAlbumHeaders called with {songs?.Count ?? 0} songs, loadCovers={loadCovers}");

            if (songs == null || songs.Count == 0)
                return result;

            // 清理动态专辑（未分类歌曲、原生Tag专辑），确保重新构建时数据一致
            _albumManager?.ClearDynamicAlbums();

            var db = CustomPlaylistDataManager.Instance?.GetDatabase();
            if (db == null)
            {
                Logger.LogWarning("Database is null, returning simple song list");
                // 如果没有数据库，直接返回歌曲列表
                for (int i = 0; i < songs.Count; i++)
                {
                    result.Add(PlaylistListItem.CreateSongItem(songs[i], i));
                }
                return result;
            }

            // 按专辑分组歌曲（自定义歌曲）
            var songsByAlbum = new Dictionary<string, List<(GameAudioInfo song, int originalIndex, SongData songData)>>();
            // 未分类的自定义歌曲按歌单分组：tagId -> songs
            var unknownSongsByPlaylist = new Dictionary<string, List<(GameAudioInfo song, int originalIndex)>>();
            // 游戏默认歌曲按原生Tag分组：AudioTag -> songs
            var nativeSongsByTag = new Dictionary<AudioTag, List<(GameAudioInfo song, int originalIndex)>>();

            for (int i = 0; i < songs.Count; i++)
            {
                var song = songs[i];
                
                // 判断是否是自定义歌曲
                bool isCustomSong = Data.CustomPlaylistDataManager.IsCustomTag(song.Tag);
                
                if (isCustomSong)
                {
                    // 自定义歌曲：从数据库中查找专辑信息
                    var songData = FindSongData(db, song.UUID);
                    
                    if (songData != null && !string.IsNullOrEmpty(songData.AlbumId))
                    {
                        if (!songsByAlbum.ContainsKey(songData.AlbumId))
                        {
                            songsByAlbum[songData.AlbumId] = new List<(GameAudioInfo, int, SongData)>();
                        }
                        songsByAlbum[songData.AlbumId].Add((song, i, songData));
                    }
                    else
                    {
                        // 未分类的自定义歌曲按歌单分组
                        var tagId = Data.CustomPlaylistDataManager.GetTagIdFromAudio(song) ?? "_unknown";
                        if (!unknownSongsByPlaylist.ContainsKey(tagId))
                        {
                            unknownSongsByPlaylist[tagId] = new List<(GameAudioInfo, int)>();
                        }
                        unknownSongsByPlaylist[tagId].Add((song, i));
                    }
                }
                else
                {
                    // 游戏默认歌曲：按原生Tag分组（不包括Favorite和Local）
                    var nativeTag = GetPrimaryNativeTag(song.Tag);
                    if (!nativeSongsByTag.ContainsKey(nativeTag))
                    {
                        nativeSongsByTag[nativeTag] = new List<(GameAudioInfo, int)>();
                    }
                    nativeSongsByTag[nativeTag].Add((song, i));
                }
            }

            var totalUnknownSongs = unknownSongsByPlaylist.Values.Sum(list => list.Count);
            var totalNativeSongs = nativeSongsByTag.Values.Sum(list => list.Count);
            Logger.LogInfo($"Grouped songs: {songsByAlbum.Count} albums, {totalUnknownSongs} unknown custom songs, {totalNativeSongs} native songs in {nativeSongsByTag.Count} tags");

            // 如果没有任何专辑信息，返回简单列表
            if (songsByAlbum.Count == 0 && totalUnknownSongs == songs.Count && totalNativeSongs == 0)
            {
                Logger.LogInfo("No album info found, returning simple song list");
                for (int i = 0; i < songs.Count; i++)
                {
                    result.Add(PlaylistListItem.CreateSongItem(songs[i], i));
                }
                return result;
            }

            // 按专辑顺序添加（按歌单ID和专辑ID排序）
            var orderedAlbums = songsByAlbum.Keys
                .Select(albumId => new { AlbumId = albumId, AlbumInfo = db.GetAlbum(albumId) })
                .Where(x => x.AlbumInfo.HasValue)
                .OrderBy(x => x.AlbumInfo.Value.playlistId)
                .ThenBy(x => x.AlbumInfo.Value.isOtherAlbum ? 1 : 0) // "其他"专辑排后面
                .ThenBy(x => x.AlbumInfo.Value.displayName)
                .ToList();

            Logger.LogInfo($"Ordered albums: {orderedAlbums.Count}");

            foreach (var albumEntry in orderedAlbums)
            {
                var albumId = albumEntry.AlbumId;
                var albumInfo = albumEntry.AlbumInfo.Value;
                var albumSongs = songsByAlbum[albumId];

                // 计算启用的歌曲数量（非排除状态的歌曲）
                int enabledCount = 0;
                foreach (var (song, _, _) in albumSongs)
                {
                    var tagId = Data.CustomPlaylistDataManager.GetTagIdFromAudio(song);
                    if (!string.IsNullOrEmpty(tagId))
                    {
                        var manager = Data.CustomPlaylistDataManager.Instance;
                        if (manager == null || !manager.IsExcluded(tagId, song.UUID))
                        {
                            enabledCount++;
                        }
                    }
                    else
                    {
                        // 没有 tagId 时默认视为启用
                        enabledCount++;
                    }
                }

                // 创建专辑头
                var header = new AlbumHeaderInfo
                {
                    AlbumId = albumId,
                    DisplayName = albumInfo.displayName,
                    IsOtherAlbum = albumInfo.isOtherAlbum,
                    EnabledSongCount = enabledCount,
                    TotalSongCount = db.GetSongsByAlbum(albumId).Count,
                    DirectoryPath = albumInfo.directoryPath
                };

                // 尝试提取艺术家（从数据库中的歌曲信息）
                var artistFromDb = GetAlbumArtistFromSongData(albumSongs.Select(s => s.songData).ToList());
                // 如果数据库没有，再从 GameAudioInfo 获取
                if (string.IsNullOrEmpty(artistFromDb))
                {
                    artistFromDb = GetAlbumArtist(albumSongs.Select(s => s.song).ToList());
                }
                header.Artist = artistFromDb;

                Logger.LogInfo($"Album '{header.DisplayName}': Artist='{header.Artist}', Enabled={enabledCount}/{albumSongs.Count}, IsOther={header.IsOtherAlbum}");

                // 加载封面（如果启用且不是"其他"专辑）
                if (loadCovers && !header.IsOtherAlbum && !string.IsNullOrEmpty(header.DirectoryPath))
                {
                    var songPaths = albumSongs.Select(s => s.songData.FilePath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();
                    
                    header.CoverImage = await GetCoverLoader().LoadCoverAsync(albumId, header.DirectoryPath, songPaths);
                }

                // 添加专辑头
                result.Add(PlaylistListItem.CreateAlbumHeader(header));

                // 添加专辑中的歌曲
                foreach (var (song, originalIndex, _) in albumSongs)
                {
                    result.Add(PlaylistListItem.CreateSongItem(song, originalIndex));
                }
            }

            // 添加未分类的歌曲（按歌单分组）
            foreach (var kvp in unknownSongsByPlaylist.OrderBy(x => x.Key))
            {
                var tagId = kvp.Key;
                var playlistSongs = kvp.Value;
                
                if (playlistSongs.Count == 0) continue;

                // 获取歌单显示名称
                string playlistDisplayName = GetPlaylistDisplayName(tagId);
                
                // 为每个歌单创建一个默认专辑（以歌单名称命名）
                // 专辑ID格式: {tagId}_default
                var defaultAlbumId = $"{tagId}_default";
                
                // 计算启用的歌曲数量
                int enabledCount = 0;
                foreach (var (song, _) in playlistSongs)
                {
                    var manager = Data.CustomPlaylistDataManager.Instance;
                    if (manager == null || !manager.IsExcluded(tagId, song.UUID))
                    {
                        enabledCount++;
                    }
                }
                
                var defaultHeader = new AlbumHeaderInfo
                {
                    AlbumId = defaultAlbumId,
                    DisplayName = playlistDisplayName,
                    IsOtherAlbum = false,  // 改为false，允许显示封面
                    EnabledSongCount = enabledCount,
                    TotalSongCount = playlistSongs.Count
                };

                // 尝试从歌单文件夹加载封面（只查找图片文件，不查找嵌入封面）
                if (loadCovers)
                {
                    var playlistPath = GetPlaylistDirectoryPath(tagId);
                    if (!string.IsNullOrEmpty(playlistPath))
                    {
                        defaultHeader.DirectoryPath = playlistPath;
                        defaultHeader.CoverImage = await GetCoverLoader().LoadCoverFromDirectoryOnly(defaultAlbumId, playlistPath);
                    }
                }

                Logger.LogInfo($"Default album for playlist '{playlistDisplayName}': Enabled={enabledCount}/{playlistSongs.Count}, HasCover={defaultHeader.CoverImage != null}");
                
                result.Add(PlaylistListItem.CreateAlbumHeader(defaultHeader));

                foreach (var (song, originalIndex) in playlistSongs)
                {
                    result.Add(PlaylistListItem.CreateSongItem(song, originalIndex));
                }
                
                // 确保默认专辑在 AlbumManager 中注册
                RegisterDefaultAlbumIfNeeded(tagId, defaultAlbumId, playlistDisplayName, playlistSongs);
            }

            // 添加游戏默认歌曲（按原生Tag分组）
            foreach (var kvp in nativeSongsByTag.OrderBy(x => (int)x.Key))
            {
                var nativeTag = kvp.Key;
                var tagSongs = kvp.Value;
                
                if (tagSongs.Count == 0) continue;

                // 获取原生Tag的显示名称
                string tagDisplayName = GetNativeTagDisplayName(nativeTag);
                
                // 专辑ID格式: native_{tagName}
                var nativeAlbumId = $"native_{nativeTag}";
                
                // 计算启用的歌曲数量（使用游戏原生的排除状态检查）
                int enabledCount = 0;
                foreach (var (song, _) in tagSongs)
                {
                    if (!IsNativeSongExcluded(song.UUID))
                    {
                        enabledCount++;
                    }
                }
                
                // 加载嵌入的游戏封面
                var gameCover = _coverLoader.LoadGameCoverFromEmbeddedResource((int)nativeTag);
                
                var nativeHeader = new AlbumHeaderInfo
                {
                    AlbumId = nativeAlbumId,
                    DisplayName = tagDisplayName,
                    Artist = "Chill With You Game",  // 游戏默认歌曲的艺术家
                    IsOtherAlbum = false,  // 有嵌入封面，不是特殊专辑
                    CoverImage = gameCover,
                    EnabledSongCount = enabledCount,
                    TotalSongCount = tagSongs.Count
                };

                Logger.LogInfo($"Native tag album '{tagDisplayName}': Enabled={enabledCount}/{tagSongs.Count}, HasCover={gameCover != null}");
                
                result.Add(PlaylistListItem.CreateAlbumHeader(nativeHeader));

                foreach (var (song, originalIndex) in tagSongs)
                {
                    result.Add(PlaylistListItem.CreateSongItem(song, originalIndex));
                }
                
                // 注册原生Tag专辑到 AlbumManager
                RegisterNativeTagAlbumIfNeeded(nativeTag, nativeAlbumId, tagDisplayName, tagSongs);
            }

            return result;
        }

        /// <summary>
        /// 获取歌单显示名称
        /// </summary>
        private string GetPlaylistDisplayName(string tagId)
        {
            // 从 CustomTagManager 获取显示名称
            var customTags = CustomTagManager.Instance?.GetAllTags();
            if (customTags != null && customTags.TryGetValue(tagId, out var customTag))
            {
                return customTag.DisplayName;
            }
            
            // 如果找不到，尝试从 tagId 提取（格式: playlist_xxx）
            if (tagId.StartsWith("playlist_"))
            {
                return tagId.Substring("playlist_".Length);
            }
            
            return tagId;
        }

        /// <summary>
        /// 获取歌单目录路径
        /// </summary>
        private string GetPlaylistDirectoryPath(string tagId)
        {
            // 从 PlaylistRegistry 获取歌单对应的目录
            var registry = ChillUIFramework.Music?.PlaylistRegistry;
            if (registry == null) return null;
            
            foreach (var kvp in registry.GetAllPlaylists())
            {
                // 检查 provider 是否是 FileSystemPlaylistProvider
                if (kvp.Value is Audio.FileSystemPlaylistProvider fsProvider)
                {
                    // 检查 CustomTagId 是否匹配
                    if (fsProvider.CustomTagId == tagId)
                    {
                        return fsProvider.DirectoryPath;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// 注册默认专辑到 AlbumManager（始终更新，确保数据一致）
        /// </summary>
        private void RegisterDefaultAlbumIfNeeded(string tagId, string albumId, string displayName, List<(GameAudioInfo song, int originalIndex)> songs)
        {
            var albumManager = AlbumManager.Instance;
            if (albumManager == null) return;
            
            // 始终重新注册，确保歌曲列表是最新的
            // RegisterAlbum 内部会先 UnregisterAlbum 再注册
            var songUUIDs = songs.Select(s => s.song.UUID).ToList();
            albumManager.RegisterAlbum(
                albumId,
                displayName,
                tagId,  // 使用 tagId 作为 playlistId
                null,   // 没有目录路径
                songUUIDs
            );
            
            Logger.LogDebug($"Registered/Updated default album: {displayName} ({albumId}) with {songUUIDs.Count} songs");
        }

        /// <summary>
        /// 获取歌曲的主要原生Tag（排除Favorite和Local）
        /// </summary>
        private static AudioTag GetPrimaryNativeTag(AudioTag tag)
        {
            // 移除 Favorite 和 Local 标记
            var cleanTag = tag & ~AudioTag.Favorite & ~AudioTag.Local;
            
            // 按优先级返回主要Tag
            if (cleanTag.HasFlagFast(AudioTag.Original))
                return AudioTag.Original;
            if (cleanTag.HasFlagFast(AudioTag.Special))
                return AudioTag.Special;
            if (cleanTag.HasFlagFast(AudioTag.Other))
                return AudioTag.Other;
            
            // 如果没有匹配，返回Other作为默认
            return AudioTag.Other;
        }

        /// <summary>
        /// 获取原生Tag的显示名称
        /// </summary>
        private static string GetNativeTagDisplayName(AudioTag tag)
        {
            switch (tag)
            {
                case AudioTag.Original:
                    return "原创";
                case AudioTag.Special:
                    return "特别";
                case AudioTag.Other:
                    return "其他";
                default:
                    return tag.ToString();
            }
        }

        /// <summary>
        /// 检查原生歌曲是否被排除（使用游戏存档）
        /// </summary>
        private static bool IsNativeSongExcluded(string uuid)
        {
            try
            {
                var excludedList = SaveDataManager.Instance?.MusicSetting?.ExcludedFromPlaylistUUIDs;
                return excludedList != null && excludedList.Contains(uuid);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 注册原生Tag专辑到 AlbumManager（始终更新，确保数据一致）
        /// </summary>
        private void RegisterNativeTagAlbumIfNeeded(AudioTag nativeTag, string albumId, string displayName, List<(GameAudioInfo song, int originalIndex)> songs)
        {
            var albumManager = AlbumManager.Instance;
            if (albumManager == null) return;
            
            // 始终重新注册，确保歌曲列表是最新的
            // RegisterAlbum 内部会先 UnregisterAlbum 再注册
            var songUUIDs = songs.Select(s => s.song.UUID).ToList();
            albumManager.RegisterAlbum(
                albumId,
                displayName,
                $"native_{nativeTag}",  // 特殊的 playlistId
                null,   // 没有目录路径
                songUUIDs
            );
            
            Logger.LogDebug($"Registered/Updated native tag album: {displayName} ({albumId}) with {songUUIDs.Count} songs");
        }

        /// <summary>
        /// 构建带专辑分隔的播放列表（指定歌单ID）
        /// </summary>
        [Obsolete("Use BuildWithAlbumHeaders(songs, loadCovers) instead")]
        public async Task<List<PlaylistListItem>> BuildWithAlbumHeaders(
            IReadOnlyList<GameAudioInfo> songs,
            string playlistId,
            bool loadCovers = true)
        {
            return await BuildWithAlbumHeaders(songs, loadCovers);
        }

        /// <summary>
        /// 从数据库查找歌曲数据
        /// </summary>
        private SongData FindSongData(PlaylistDatabase db, string songUuid)
        {
            if (db == null || string.IsNullOrEmpty(songUuid))
                return null;

            try
            {
                // 直接通过UUID查询（需要添加这个方法）
                return db.GetSongByUUID(songUuid);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从数据库歌曲数据中提取艺术家
        /// </summary>
        private string GetAlbumArtistFromSongData(IList<SongData> songs)
        {
            if (songs == null || songs.Count == 0)
                return null;

            // 收集所有非空艺术家
            var artists = songs
                .Where(s => !string.IsNullOrEmpty(s.Artist) && s.Artist != "Unknown" && s.Artist.Trim().Length > 0)
                .Select(s => s.Artist.Trim())
                .ToList();

            if (artists.Count == 0)
                return null;

            // 统计艺术家出现次数
            var artistCounts = artists
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            var topArtist = artistCounts.First();

            // 如果大部分歌曲是同一个艺术家，返回该艺术家
            if (topArtist.Count() >= songs.Count * 0.5f)
            {
                return topArtist.Key;
            }

            // 多个艺术家
            if (artistCounts.Count > 2)
            {
                return "Various Artists";
            }

            return topArtist.Key;
        }

        /// <summary>
        /// 从专辑歌曲中提取艺术家
        /// </summary>
        private string GetAlbumArtist(IList<GameAudioInfo> songs)
        {
            if (songs == null || songs.Count == 0)
                return null;

            // 收集所有非空艺术家
            var artists = songs
                .Where(s => !string.IsNullOrEmpty(s.Credit) && s.Credit != "Unknown" && s.Credit.Trim().Length > 0)
                .Select(s => s.Credit.Trim())
                .ToList();

            if (artists.Count == 0)
            {
                // 如果所有歌曲都没有艺术家信息，返回 null
                return null;
            }

            // 统计艺术家出现次数
            var artistCounts = artists
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            var topArtist = artistCounts.First();
            
            // 如果大部分歌曲是同一个艺术家，返回该艺术家
            if (topArtist.Count() >= songs.Count * 0.5f)
            {
                return topArtist.Key;
            }

            // 多个艺术家
            if (artistCounts.Count > 2)
            {
                return "Various Artists";
            }

            // 返回出现最多的艺术家
            return topArtist.Key;
        }
    }
}

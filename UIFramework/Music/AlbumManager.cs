using System;
using System.Collections.Generic;
using System.Linq;
using ChillPatcher.UIFramework.Data;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 专辑管理器 - 管理专辑与歌曲的双向映射
    /// </summary>
    public class AlbumManager : IDisposable
    {
        private static AlbumManager _instance;
        public static AlbumManager Instance => _instance;

        // 专辑ID -> 专辑信息
        private readonly Dictionary<string, AlbumInfo> _albums = new Dictionary<string, AlbumInfo>();
        
        // 歌曲UUID -> 专辑ID（反向映射）
        private readonly Dictionary<string, string> _songToAlbum = new Dictionary<string, string>();
        
        // 歌单ID -> 专辑ID列表
        private readonly Dictionary<string, List<string>> _playlistToAlbums = new Dictionary<string, List<string>>();

        private readonly object _lock = new object();

        /// <summary>
        /// 初始化专辑管理器
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null)
            {
                BepInEx.Logging.Logger.CreateLogSource("AlbumManager").LogWarning("专辑管理器已初始化");
                return;
            }

            _instance = new AlbumManager();
            BepInEx.Logging.Logger.CreateLogSource("AlbumManager").LogInfo("专辑管理器初始化成功");
        }

        private AlbumManager()
        {
        }

        #region 注册/注销专辑

        /// <summary>
        /// 注册专辑
        /// </summary>
        /// <param name="albumId">专辑唯一ID</param>
        /// <param name="displayName">显示名称</param>
        /// <param name="playlistId">所属歌单ID</param>
        /// <param name="directoryPath">专辑目录路径</param>
        /// <param name="songUUIDs">歌曲UUID列表</param>
        public void RegisterAlbum(string albumId, string displayName, string playlistId, string directoryPath, List<string> songUUIDs)
        {
            if (string.IsNullOrEmpty(albumId))
                throw new ArgumentException("专辑ID不能为空", nameof(albumId));

            lock (_lock)
            {
                // 如果已存在，先注销
                if (_albums.ContainsKey(albumId))
                {
                    UnregisterAlbum(albumId);
                }

                // 创建专辑信息
                var album = new AlbumInfo
                {
                    AlbumId = albumId,
                    DisplayName = displayName,
                    PlaylistId = playlistId,
                    DirectoryPath = directoryPath,
                    SongUUIDs = songUUIDs?.ToList() ?? new List<string>()
                };

                _albums[albumId] = album;

                // 建立歌曲到专辑的反向映射
                foreach (var uuid in album.SongUUIDs)
                {
                    _songToAlbum[uuid] = albumId;
                }

                // 建立歌单到专辑的映射
                if (!_playlistToAlbums.ContainsKey(playlistId))
                {
                    _playlistToAlbums[playlistId] = new List<string>();
                }
                _playlistToAlbums[playlistId].Add(albumId);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"注册专辑: {displayName} ({albumId}), 歌曲数: {album.SongUUIDs.Count}");
            }
        }

        /// <summary>
        /// 注销专辑
        /// </summary>
        public void UnregisterAlbum(string albumId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                // 移除歌曲到专辑的映射
                foreach (var uuid in album.SongUUIDs)
                {
                    _songToAlbum.Remove(uuid);
                }

                // 移除歌单到专辑的映射
                if (_playlistToAlbums.TryGetValue(album.PlaylistId, out var albumList))
                {
                    albumList.Remove(albumId);
                }

                _albums.Remove(albumId);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogDebug($"注销专辑: {album.DisplayName} ({albumId})");
            }
        }

        /// <summary>
        /// 清理所有动态专辑（未分类歌曲、原生Tag专辑）
        /// 在重新构建播放列表前调用
        /// </summary>
        public void ClearDynamicAlbums()
        {
            lock (_lock)
            {
                var toRemove = new List<string>();
                
                foreach (var kvp in _albums)
                {
                    var albumId = kvp.Key;
                    // 动态专辑的特征：
                    // 1. 以 _default 结尾（未分类歌曲）
                    // 2. 以 native_ 开头（原生Tag专辑）
                    if (albumId.EndsWith("_default") || albumId.StartsWith("native_"))
                    {
                        toRemove.Add(albumId);
                    }
                }

                foreach (var albumId in toRemove)
                {
                    UnregisterAlbum(albumId);
                }

                if (toRemove.Count > 0)
                {
                    BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                        .LogInfo($"清理动态专辑: {toRemove.Count} 个");
                }
            }
        }

        /// <summary>
        /// 添加歌曲到专辑
        /// </summary>
        public void AddSongToAlbum(string albumId, string songUUID)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                if (!album.SongUUIDs.Contains(songUUID))
                {
                    album.SongUUIDs.Add(songUUID);
                    _songToAlbum[songUUID] = albumId;
                }
            }
        }

        /// <summary>
        /// 从专辑移除歌曲
        /// </summary>
        public void RemoveSongFromAlbum(string albumId, string songUUID)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                if (album.SongUUIDs.Remove(songUUID))
                {
                    _songToAlbum.Remove(songUUID);
                }
            }
        }

        #endregion

        #region 查询API

        /// <summary>
        /// 获取专辑信息
        /// </summary>
        public AlbumInfo GetAlbum(string albumId)
        {
            lock (_lock)
            {
                return _albums.TryGetValue(albumId, out var album) ? album : null;
            }
        }

        /// <summary>
        /// 获取歌曲所属的专辑ID
        /// </summary>
        public string GetAlbumIdBySong(string songUUID)
        {
            lock (_lock)
            {
                return _songToAlbum.TryGetValue(songUUID, out var albumId) ? albumId : null;
            }
        }

        /// <summary>
        /// 获取歌单下的所有专辑
        /// </summary>
        public List<AlbumInfo> GetAlbumsByPlaylist(string playlistId)
        {
            lock (_lock)
            {
                if (!_playlistToAlbums.TryGetValue(playlistId, out var albumIds))
                    return new List<AlbumInfo>();

                return albumIds
                    .Where(id => _albums.ContainsKey(id))
                    .Select(id => _albums[id])
                    .ToList();
            }
        }

        /// <summary>
        /// 获取所有专辑
        /// </summary>
        public List<AlbumInfo> GetAllAlbums()
        {
            lock (_lock)
            {
                return _albums.Values.ToList();
            }
        }

        #endregion

        #region 状态API

        /// <summary>
        /// 获取专辑状态（包含启用/禁用状态和启用的歌曲列表）
        /// </summary>
        /// <param name="albumId">专辑ID</param>
        /// <param name="tagId">歌单的Tag ID（用于查询排除列表）</param>
        public AlbumStatus GetAlbumStatus(string albumId, string tagId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return null;

                // 获取排除列表
                var excludedSongs = CustomPlaylistDataManager.Instance?.GetExcludedSongs(tagId) 
                    ?? new List<string>();
                var excludedSet = new HashSet<string>(excludedSongs);

                // 计算启用和排除的歌曲
                var enabledSongs = new List<string>();
                var excludedInAlbum = new List<string>();

                foreach (var uuid in album.SongUUIDs)
                {
                    if (excludedSet.Contains(uuid))
                    {
                        excludedInAlbum.Add(uuid);
                    }
                    else
                    {
                        enabledSongs.Add(uuid);
                    }
                }

                return new AlbumStatus
                {
                    AlbumId = albumId,
                    IsEnabled = enabledSongs.Count > 0,
                    EnabledSongCount = enabledSongs.Count,
                    TotalSongCount = album.SongUUIDs.Count,
                    EnabledSongUUIDs = enabledSongs,
                    ExcludedSongUUIDs = excludedInAlbum
                };
            }
        }

        /// <summary>
        /// 获取歌单下所有专辑的状态
        /// </summary>
        public List<AlbumStatus> GetAlbumStatusesByPlaylist(string playlistId, string tagId)
        {
            var albums = GetAlbumsByPlaylist(playlistId);
            return albums.Select(a => GetAlbumStatus(a.AlbumId, tagId)).ToList();
        }

        /// <summary>
        /// 检查专辑是否启用（有任意歌曲未被排除）
        /// </summary>
        public bool IsAlbumEnabled(string albumId, string tagId)
        {
            var status = GetAlbumStatus(albumId, tagId);
            return status?.IsEnabled ?? false;
        }

        /// <summary>
        /// 获取专辑中启用的歌曲UUID列表
        /// </summary>
        public List<string> GetEnabledSongs(string albumId, string tagId)
        {
            var status = GetAlbumStatus(albumId, tagId);
            return status?.EnabledSongUUIDs ?? new List<string>();
        }

        #endregion

        #region 启用/禁用切换

        /// <summary>
        /// 切换专辑启用状态
        /// - 如果当前是启用状态（有任意歌曲未排除）：排除全部歌曲
        /// - 如果当前是禁用状态（全部歌曲被排除）：取消排除全部歌曲
        /// </summary>
        /// <param name="albumId">专辑ID</param>
        /// <param name="tagId">歌单的Tag ID（自定义歌曲）或 "native_{Tag}" 格式（原生歌曲）</param>
        /// <returns>切换后的状态（true=启用, false=禁用）</returns>
        public bool ToggleAlbumEnabled(string albumId, string tagId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                {
                    BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                        .LogWarning($"专辑不存在: {albumId}");
                    return false;
                }

                // 检查是否是原生Tag专辑
                bool isNativeTagAlbum = tagId != null && tagId.StartsWith("native_");

                if (isNativeTagAlbum)
                {
                    // 原生Tag专辑：使用游戏存档
                    return ToggleNativeAlbumEnabled(album);
                }
                else
                {
                    // 自定义歌曲专辑：使用我们的数据库
                    return ToggleCustomAlbumEnabled(album, tagId);
                }
            }
        }

        /// <summary>
        /// 切换自定义歌曲专辑的启用状态
        /// </summary>
        private bool ToggleCustomAlbumEnabled(AlbumInfo album, string tagId)
        {
            var dataManager = CustomPlaylistDataManager.Instance;
            if (dataManager == null)
            {
                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogError("数据管理器未初始化");
                return false;
            }

            var currentStatus = GetAlbumStatus(album.AlbumId, tagId);
            if (currentStatus == null)
                return false;

            if (currentStatus.IsEnabled)
            {
                // 当前启用 -> 排除全部歌曲
                dataManager.AddExcludedBatch(tagId, album.SongUUIDs);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"禁用专辑: {album.DisplayName}, 排除 {album.SongUUIDs.Count} 首歌曲");

                return false;
            }
            else
            {
                // 当前禁用 -> 取消排除全部歌曲
                dataManager.RemoveExcludedBatch(tagId, album.SongUUIDs);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"启用专辑: {album.DisplayName}, 恢复 {album.SongUUIDs.Count} 首歌曲");

                return true;
            }
        }

        /// <summary>
        /// 切换原生Tag专辑的启用状态（使用游戏存档）
        /// </summary>
        private bool ToggleNativeAlbumEnabled(AlbumInfo album)
        {
            var excludedList = Bulbul.SaveDataManager.Instance?.MusicSetting?.ExcludedFromPlaylistUUIDs;
            if (excludedList == null)
            {
                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogError("无法访问游戏存档");
                return false;
            }

            // 检查当前状态：如果有任何歌曲未排除，则视为启用
            bool hasEnabledSong = album.SongUUIDs.Any(uuid => !excludedList.Contains(uuid));

            if (hasEnabledSong)
            {
                // 当前启用 -> 排除全部歌曲
                foreach (var uuid in album.SongUUIDs)
                {
                    if (!excludedList.Contains(uuid))
                    {
                        excludedList.Add(uuid);
                    }
                }

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"禁用原生专辑: {album.DisplayName}, 排除 {album.SongUUIDs.Count} 首歌曲");

                // 保存游戏存档
                Bulbul.SaveDataManager.Instance.SaveMusicSetting();

                return false;
            }
            else
            {
                // 当前禁用 -> 取消排除全部歌曲
                foreach (var uuid in album.SongUUIDs)
                {
                    excludedList.Remove(uuid);
                }

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"启用原生专辑: {album.DisplayName}, 恢复 {album.SongUUIDs.Count} 首歌曲");

                // 保存游戏存档
                Bulbul.SaveDataManager.Instance.SaveMusicSetting();

                return true;
            }
        }

        /// <summary>
        /// 启用专辑（取消排除全部歌曲）
        /// </summary>
        public void EnableAlbum(string albumId, string tagId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                var dataManager = CustomPlaylistDataManager.Instance;
                if (dataManager == null)
                    return;

                dataManager.RemoveExcludedBatch(tagId, album.SongUUIDs);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"启用专辑: {album.DisplayName}");
            }
        }

        /// <summary>
        /// 禁用专辑（排除全部歌曲）
        /// </summary>
        public void DisableAlbum(string albumId, string tagId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                var dataManager = CustomPlaylistDataManager.Instance;
                if (dataManager == null)
                    return;

                dataManager.AddExcludedBatch(tagId, album.SongUUIDs);

                BepInEx.Logging.Logger.CreateLogSource("AlbumManager")
                    .LogInfo($"禁用专辑: {album.DisplayName}");
            }
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                _albums.Clear();
                _songToAlbum.Clear();
                _playlistToAlbums.Clear();
            }
        }
    }
}

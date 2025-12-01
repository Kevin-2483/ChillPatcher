using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.UIFramework.Core;
using ChillPatcher.UIFramework.Data;
using ChillPatcher.UIFramework.Music;
using Newtonsoft.Json;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 文件系统歌单提供器 - 使用数据库存储，支持专辑
    /// </summary>
    public class FileSystemPlaylistProvider : IPlaylistProvider, IDisposable
    {
        private const string OTHER_ALBUM_SUFFIX = "_other";
        private const string PLAYLIST_JSON = "playlist.json";
        private const string ALBUM_JSON = "album.json";
        private const string RESCAN_FLAG = "!rescan_playlist";

        private readonly string _directoryPath;
        private readonly IAudioLoader _audioLoader;
        private List<GameAudioInfo> _runtimeSongs;

        // 专辑相关
        private readonly List<string> _registeredAlbumIds = new List<string>();

        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public Sprite Icon { get; private set; }
        public AudioTag Tag { get; set; }
        public bool SupportsLiveUpdate => true;
        public string CustomTagId { get; private set; }
        
        /// <summary>
        /// 歌单目录路径
        /// </summary>
        public string DirectoryPath => _directoryPath;

        public event Action OnPlaylistUpdated;

        private string RescanFlagPath => Path.Combine(_directoryPath, RESCAN_FLAG);
        private string PlaylistJsonPath => Path.Combine(_directoryPath, PLAYLIST_JSON);

        public FileSystemPlaylistProvider(string directoryPath, IAudioLoader audioLoader, AudioTag tag = AudioTag.Local)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

            _directoryPath = directoryPath;
            _audioLoader = audioLoader ?? throw new ArgumentNullException(nameof(audioLoader));

            // 默认ID和名称
            Id = Path.GetFileName(_directoryPath);
            DisplayName = Id;

            // 读取playlist.json获取自定义名称
            LoadPlaylistMetadata();

            // 注册自定义Tag
            CustomTagId = $"playlist_{Id}";
            var customTag = CustomTagManager.Instance.RegisterTag(CustomTagId, DisplayName);
            Tag = customTag.BitValue;
        }

        /// <summary>
        /// 加载歌单元数据（playlist.json）
        /// </summary>
        private void LoadPlaylistMetadata()
        {
            try
            {
                if (File.Exists(PlaylistJsonPath))
                {
                    var json = File.ReadAllText(PlaylistJsonPath);
                    var metadata = JsonConvert.DeserializeObject<PlaylistMetadata>(json);
                    if (metadata != null && !string.IsNullOrEmpty(metadata.DisplayName))
                    {
                        DisplayName = metadata.DisplayName;
                    }
                }
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework")
                    .LogWarning($"[Playlist] 读取playlist.json失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存歌单元数据
        /// </summary>
        private void SavePlaylistMetadata()
        {
            try
            {
                var metadata = new PlaylistMetadata
                {
                    DisplayName = DisplayName
                };
                var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                File.WriteAllText(PlaylistJsonPath, json);
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework")
                    .LogWarning($"[Playlist] 保存playlist.json失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建歌单
        /// </summary>
        public async Task<List<GameAudioInfo>> BuildPlaylist()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");
            
            try
            {
                // 检查是否需要重新扫描
                // 1. 本地重扫描标记不存在
                // 2. 或全局需要重扫描（数据库升级/恢复）
                bool forceRescan = !File.Exists(RescanFlagPath) || 
                                   (CustomPlaylistDataManager.Instance?.NeedsFullRescan ?? false);

                if (forceRescan)
                {
                    logger.LogInfo($"[Playlist] 需要重新扫描: {DisplayName}");
                }

                // 清理之前注册的专辑
                UnregisterAllAlbums();

                if (forceRescan)
                {
                    // 全新扫描并保存到数据库
                    _runtimeSongs = await ScanAndSaveToDatabase();
                    
                    // 创建重扫描标记
                    CreateRescanFlag();
                    
                    // 如果没有playlist.json，创建一个
                    if (!File.Exists(PlaylistJsonPath))
                    {
                        SavePlaylistMetadata();
                    }
                }
                else
                {
                    // 从数据库加载
                    _runtimeSongs = await LoadFromDatabase();
                }

                // 注册专辑到AlbumManager
                RegisterAlbumsToManager();

                logger.LogInfo($"[Playlist] 加载完成 '{DisplayName}': {_runtimeSongs.Count} 首歌曲, {_registeredAlbumIds.Count} 个专辑");

                OnPlaylistUpdated?.Invoke();
                return _runtimeSongs;
            }
            catch (Exception ex)
            {
                logger.LogError($"[Playlist] 构建歌单失败 '{DisplayName}': {ex}");
                return new List<GameAudioInfo>();
            }
        }

        /// <summary>
        /// 强制刷新歌单
        /// </summary>
        public async Task Refresh()
        {
            // 删除重扫描标记，触发重新扫描
            if (File.Exists(RescanFlagPath))
            {
                File.Delete(RescanFlagPath);
            }
            _runtimeSongs = null;
            await BuildPlaylist();
        }

        /// <summary>
        /// 扫描目录并保存到数据库
        /// </summary>
        private async Task<List<GameAudioInfo>> ScanAndSaveToDatabase()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");
            var allSongs = new List<GameAudioInfo>();
            var db = CustomPlaylistDataManager.Instance;
            
            // 获取数据库中已有的歌曲（用于保留UUID）
            var existingSongs = db?.GetDatabase()?.GetSongsByPlaylist(Id) ?? new List<SongData>();
            var existingByPath = existingSongs.ToDictionary(s => s.FilePath, s => s);

            // 清理数据库中此歌单的过时数据
            CleanupStaleData(existingByPath);

            // 1. 扫描根目录的歌曲（归入"其他"专辑）
            var otherAlbumId = Id + OTHER_ALBUM_SUFFIX;
            var rootSongs = await ScanDirectoryForSongs(_directoryPath, otherAlbumId, existingByPath, 0);
            
            if (rootSongs.Count > 0)
            {
                // 保存"其他"专辑到数据库
                db?.GetDatabase()?.SaveAlbum(otherAlbumId, Id, _directoryPath, "其他", true);
                _registeredAlbumIds.Add(otherAlbumId);
                allSongs.AddRange(rootSongs);
                logger.LogInfo($"[Playlist] '其他'专辑: {rootSongs.Count} 首歌曲");
            }

            // 2. 扫描子目录作为专辑
            var subdirs = Directory.GetDirectories(_directoryPath);
            foreach (var albumDir in subdirs)
            {
                var albumName = Path.GetFileName(albumDir);
                var albumId = $"{Id}_{albumName}";
                
                // 读取album.json获取自定义名称
                var displayName = LoadAlbumDisplayName(albumDir, albumName);

                // 扫描专辑目录（递归一层）
                var albumSongs = await ScanDirectoryForSongs(albumDir, albumId, existingByPath, 1);

                if (albumSongs.Count > 0)
                {
                    // 保存专辑到数据库
                    db?.GetDatabase()?.SaveAlbum(albumId, Id, albumDir, displayName, false);
                    _registeredAlbumIds.Add(albumId);
                    allSongs.AddRange(albumSongs);
                    logger.LogInfo($"[Playlist] 专辑 '{displayName}': {albumSongs.Count} 首歌曲");
                }
            }

            return allSongs;
        }

        /// <summary>
        /// 扫描目录获取歌曲（支持递归一层）
        /// </summary>
        private async Task<List<GameAudioInfo>> ScanDirectoryForSongs(
            string directory, 
            string albumId, 
            Dictionary<string, SongData> existingByPath,
            int maxDepth)
        {
            var songs = new List<GameAudioInfo>();
            var db = CustomPlaylistDataManager.Instance;

            // 扫描当前目录
            await ScanSingleDirectory(directory, albumId, existingByPath, songs);

            // 如果允许递归，扫描子目录
            if (maxDepth > 0)
            {
                var subdirs = Directory.GetDirectories(directory);
                foreach (var subdir in subdirs)
                {
                    await ScanSingleDirectory(subdir, albumId, existingByPath, songs);
                }
            }

            return songs;
        }

        /// <summary>
        /// 扫描单个目录
        /// </summary>
        private async Task ScanSingleDirectory(
            string directory,
            string albumId,
            Dictionary<string, SongData> existingByPath,
            List<GameAudioInfo> songs)
        {
            var db = CustomPlaylistDataManager.Instance;
            var files = Directory.GetFiles(directory)
                .Where(f => _audioLoader.IsSupportedFormat(f))
                .ToList();

            foreach (var filePath in files)
            {
                try
                {
                    string uuid;
                    
                    // 检查是否已有UUID
                    if (existingByPath.TryGetValue(filePath, out var existing))
                    {
                        uuid = existing.UUID;
                    }
                    else
                    {
                        uuid = Guid.NewGuid().ToString();
                    }

                    var audioInfo = await _audioLoader.LoadFromFile(filePath, uuid);
                    if (audioInfo != null)
                    {
                        // 尝试获取更详细的艺术家信息
                        string artist = audioInfo.Credit;
                        if (string.IsNullOrEmpty(artist))
                        {
                            artist = GetArtistFromFile(filePath);
                        }
                        
                        // 保存到数据库
                        var fileInfo = new FileInfo(filePath);
                        db?.GetDatabase()?.SaveSong(
                            uuid, Id, albumId,
                            Path.GetFileName(filePath), filePath,
                            audioInfo.Title, artist,
                            fileInfo.LastWriteTime
                        );

                        songs.Add(audioInfo);
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework")
                        .LogError($"[Playlist] 加载歌曲失败 '{filePath}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 从数据库加载歌单
        /// </summary>
        private async Task<List<GameAudioInfo>> LoadFromDatabase()
        {
            var songs = new List<GameAudioInfo>();
            var db = CustomPlaylistDataManager.Instance;
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");

            // 获取所有专辑
            var albums = db?.GetDatabase()?.GetAlbumsByPlaylist(Id) ?? new List<(string, string, bool)>();
            var staleAlbums = new List<string>();
            
            foreach (var (albumId, displayName, isOther) in albums)
            {
                // 检查专辑目录是否存在
                var albumInfo = db?.GetDatabase()?.GetAlbum(albumId);
                if (albumInfo.HasValue && !string.IsNullOrEmpty(albumInfo.Value.directoryPath))
                {
                    if (!Directory.Exists(albumInfo.Value.directoryPath))
                    {
                        staleAlbums.Add(albumId);
                        logger.LogWarning($"[Playlist] 专辑目录不存在: {albumInfo.Value.directoryPath}");
                        continue;
                    }
                }
                _registeredAlbumIds.Add(albumId);
            }
            
            // 清理不存在的专辑
            foreach (var albumId in staleAlbums)
            {
                db?.GetDatabase()?.DeleteAlbum(albumId);
                logger.LogInfo($"[Playlist] 清理不存在的专辑: {albumId}");
            }

            // 获取所有歌曲
            var songDataList = db?.GetDatabase()?.GetSongsByPlaylist(Id) ?? new List<SongData>();
            var staleSongs = new List<string>();
            
            foreach (var songData in songDataList)
            {
                // 检查文件是否存在
                if (!File.Exists(songData.FilePath))
                {
                    staleSongs.Add(songData.UUID);
                    logger.LogWarning($"[Playlist] 歌曲文件不存在: {songData.FilePath}");
                    continue;
                }

                try
                {
                    var audioInfo = await _audioLoader.LoadFromFile(songData.FilePath, songData.UUID);
                    if (audioInfo != null)
                    {
                        // 使用数据库中的元数据
                        if (!string.IsNullOrEmpty(songData.Title))
                            audioInfo.Title = songData.Title;
                        if (!string.IsNullOrEmpty(songData.Artist))
                            audioInfo.Credit = songData.Artist;

                        songs.Add(audioInfo);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"[Playlist] 加载歌曲失败 '{songData.FilePath}': {ex.Message}");
                }
            }
            
            // 清理不存在的歌曲
            foreach (var uuid in staleSongs)
            {
                db?.GetDatabase()?.DeleteSong(uuid);
                db?.RemoveFavorite(CustomTagId, uuid);
                db?.RemoveExcluded(CustomTagId, uuid);
            }
            
            if (staleSongs.Count > 0 || staleAlbums.Count > 0)
            {
                logger.LogInfo($"[Playlist] 清理过时数据: {staleSongs.Count} 首歌曲, {staleAlbums.Count} 个专辑");
            }

            return songs;
        }

        /// <summary>
        /// 清理数据库中的过时数据
        /// </summary>
        private void CleanupStaleData(Dictionary<string, SongData> existingByPath)
        {
            var db = CustomPlaylistDataManager.Instance;
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");
            
            int removedSongs = 0;
            int removedAlbums = 0;

            // 检查歌曲文件是否存在
            foreach (var kvp in existingByPath)
            {
                if (!File.Exists(kvp.Key))
                {
                    db?.GetDatabase()?.DeleteSong(kvp.Value.UUID);
                    
                    // 同时清理相关的收藏和排除
                    db?.RemoveFavorite(CustomTagId, kvp.Value.UUID);
                    db?.RemoveExcluded(CustomTagId, kvp.Value.UUID);
                    
                    removedSongs++;
                }
            }

            // 检查专辑目录是否存在
            var albums = db?.GetDatabase()?.GetAlbumsByPlaylist(Id) ?? new List<(string, string, bool)>();
            foreach (var (albumId, displayName, isOther) in albums)
            {
                var albumInfo = db?.GetDatabase()?.GetAlbum(albumId);
                if (albumInfo.HasValue && !Directory.Exists(albumInfo.Value.directoryPath))
                {
                    db?.GetDatabase()?.DeleteAlbum(albumId);
                    removedAlbums++;
                }
            }

            if (removedSongs > 0 || removedAlbums > 0)
            {
                logger.LogInfo($"[Playlist] 清理过时数据: {removedSongs} 首歌曲, {removedAlbums} 个专辑");
            }
        }

        /// <summary>
        /// 读取专辑的自定义名称
        /// </summary>
        private string LoadAlbumDisplayName(string albumDir, string defaultName)
        {
            try
            {
                var albumJsonPath = Path.Combine(albumDir, ALBUM_JSON);
                if (File.Exists(albumJsonPath))
                {
                    var json = File.ReadAllText(albumJsonPath);
                    var metadata = JsonConvert.DeserializeObject<AlbumMetadata>(json);
                    if (metadata != null && !string.IsNullOrEmpty(metadata.DisplayName))
                    {
                        return metadata.DisplayName;
                    }
                }
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework")
                    .LogWarning($"[Playlist] 读取album.json失败: {ex.Message}");
            }
            return defaultName;
        }

        /// <summary>
        /// 创建重扫描标记文件
        /// </summary>
        private void CreateRescanFlag()
        {
            try
            {
                File.WriteAllText(RescanFlagPath,
                    $"# ChillPatcher Playlist Scan Flag\n" +
                    $"# 此文件标识该歌单已完成扫描\n" +
                    $"# 删除此文件后，下次启动将重新扫描\n" +
                    $"# Last scanned: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework")
                    .LogWarning($"[Playlist] 创建标志文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册专辑到AlbumManager
        /// </summary>
        private void RegisterAlbumsToManager()
        {
            var albumManager = AlbumManager.Instance;
            if (albumManager == null) return;

            var db = CustomPlaylistDataManager.Instance;
            
            foreach (var albumId in _registeredAlbumIds)
            {
                var albumInfo = db?.GetDatabase()?.GetAlbum(albumId);
                if (!albumInfo.HasValue) continue;

                var songUUIDs = db?.GetDatabase()?.GetSongsByAlbum(albumId)
                    .Select(s => s.UUID)
                    .ToList() ?? new List<string>();

                albumManager.RegisterAlbum(
                    albumId,
                    albumInfo.Value.displayName,
                    CustomTagId,  // 使用 CustomTagId (playlist_{Id}) 而不是 Id
                    albumInfo.Value.directoryPath,
                    songUUIDs
                );
            }
        }

        /// <summary>
        /// 注销所有专辑
        /// </summary>
        private void UnregisterAllAlbums()
        {
            var albumManager = AlbumManager.Instance;
            if (albumManager == null) return;

            foreach (var albumId in _registeredAlbumIds)
            {
                albumManager.UnregisterAlbum(albumId);
            }
            _registeredAlbumIds.Clear();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            UnregisterAllAlbums();

            if (!string.IsNullOrEmpty(CustomTagId))
            {
                CustomTagManager.Instance.UnregisterTag(CustomTagId);
            }
        }

        /// <summary>
        /// 从音频文件直接读取艺术家信息
        /// </summary>
        private string GetArtistFromFile(string filePath)
        {
            try
            {
                using (var file = TagLib.File.Create(filePath))
                {
                    // 优先使用 AlbumArtists，然后是 Performers
                    if (file.Tag.AlbumArtists != null && file.Tag.AlbumArtists.Length > 0)
                    {
                        return string.Join(", ", file.Tag.AlbumArtists);
                    }
                    if (file.Tag.Performers != null && file.Tag.Performers.Length > 0)
                    {
                        return string.Join(", ", file.Tag.Performers);
                    }
                    // 回退到 FirstPerformer
                    return file.Tag.FirstPerformer;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

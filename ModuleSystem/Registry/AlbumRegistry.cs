using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.ModuleSystem.Registry
{
    /// <summary>
    /// 专辑注册表实现
    /// </summary>
    public class AlbumRegistry : IAlbumRegistry
    {
        private static AlbumRegistry _instance;
        public static AlbumRegistry Instance => _instance;

        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, AlbumInfo> _albums = new Dictionary<string, AlbumInfo>();
        private readonly Dictionary<string, List<string>> _albumsByTag = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _albumsByModule = new Dictionary<string, List<string>>();
        private readonly object _lock = new object();

        public event Action<AlbumInfo> OnAlbumRegistered;
        public event Action<string> OnAlbumUnregistered;

        public static void Initialize(ManualLogSource logger)
        {
            if (_instance != null)
            {
                logger.LogWarning("AlbumRegistry 已初始化");
                return;
            }
            _instance = new AlbumRegistry(logger);
        }

        private AlbumRegistry(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void RegisterAlbum(AlbumInfo album, string moduleId)
        {
            if (album == null)
                throw new ArgumentNullException(nameof(album));
            if (string.IsNullOrEmpty(album.AlbumId))
                throw new ArgumentException("专辑 ID 不能为空");

            lock (_lock)
            {
                // 如果已存在，先注销
                if (_albums.ContainsKey(album.AlbumId))
                {
                    _logger.LogDebug($"专辑 '{album.AlbumId}' 已存在，更新信息");
                    UnregisterAlbum(album.AlbumId);
                }

                album.ModuleId = moduleId;
                _albums[album.AlbumId] = album;

                // 按 Tag 索引（支持多 Tag）
                if (album.TagIds != null && album.TagIds.Count > 0)
                {
                    foreach (var tagId in album.TagIds)
                    {
                        if (string.IsNullOrEmpty(tagId)) continue;
                        
                        if (!_albumsByTag.ContainsKey(tagId))
                        {
                            _albumsByTag[tagId] = new List<string>();
                        }
                        if (!_albumsByTag[tagId].Contains(album.AlbumId))
                        {
                            _albumsByTag[tagId].Add(album.AlbumId);
                        }

                        // 如果是增长专辑，自动标记 Tag 为增长 Tag
                        if (album.IsGrowableAlbum)
                        {
                            TagRegistry.Instance?.MarkAsGrowableTag(tagId, album.AlbumId);
                        }
                    }
                }

                // 按模块索引
                if (!_albumsByModule.ContainsKey(moduleId))
                {
                    _albumsByModule[moduleId] = new List<string>();
                }
                _albumsByModule[moduleId].Add(album.AlbumId);

                var growableInfo = album.IsGrowableAlbum ? " [增长专辑]" : "";
                var tagsInfo = album.TagIds != null && album.TagIds.Count > 0 
                    ? string.Join(", ", album.TagIds) 
                    : "无";
                _logger.LogInfo($"注册专辑: {album.DisplayName} (ID: {album.AlbumId}, Tags: [{tagsInfo}], Module: {moduleId}){growableInfo}");

                OnAlbumRegistered?.Invoke(album);
            }
        }

        public void UnregisterAlbum(string albumId)
        {
            lock (_lock)
            {
                if (!_albums.TryGetValue(albumId, out var album))
                    return;

                // 从所有 Tag 索引中移除
                if (album.TagIds != null)
                {
                    foreach (var tagId in album.TagIds)
                    {
                        if (!string.IsNullOrEmpty(tagId) && _albumsByTag.TryGetValue(tagId, out var tagAlbums))
                        {
                            tagAlbums.Remove(albumId);
                        }
                    }
                }

                // 从模块索引中移除
                if (!string.IsNullOrEmpty(album.ModuleId) && _albumsByModule.TryGetValue(album.ModuleId, out var moduleAlbums))
                {
                    moduleAlbums.Remove(albumId);
                }

                _albums.Remove(albumId);
                _logger.LogDebug($"注销专辑: {album.DisplayName} ({albumId})");

                OnAlbumUnregistered?.Invoke(albumId);
            }
        }

        public AlbumInfo GetAlbum(string albumId)
        {
            lock (_lock)
            {
                return _albums.TryGetValue(albumId, out var album) ? album : null;
            }
        }

        public IReadOnlyList<AlbumInfo> GetAllAlbums()
        {
            lock (_lock)
            {
                return _albums.Values.OrderBy(a => a.SortOrder).ToList();
            }
        }

        public IReadOnlyList<AlbumInfo> GetAlbumsByTag(string tagId)
        {
            lock (_lock)
            {
                if (!_albumsByTag.TryGetValue(tagId, out var albumIds))
                    return new List<AlbumInfo>();

                return albumIds
                    .Select(id => _albums.TryGetValue(id, out var a) ? a : null)
                    .Where(a => a != null)
                    .OrderBy(a => a.SortOrder)
                    .ToList();
            }
        }

        public IReadOnlyList<AlbumInfo> GetAlbumsByModule(string moduleId)
        {
            lock (_lock)
            {
                if (!_albumsByModule.TryGetValue(moduleId, out var albumIds))
                    return new List<AlbumInfo>();

                return albumIds
                    .Select(id => _albums.TryGetValue(id, out var a) ? a : null)
                    .Where(a => a != null)
                    .OrderBy(a => a.SortOrder)
                    .ToList();
            }
        }

        public bool IsAlbumRegistered(string albumId)
        {
            lock (_lock)
            {
                return _albums.ContainsKey(albumId);
            }
        }

        /// <summary>
        /// 注销指定模块的所有专辑
        /// </summary>
        public void UnregisterAllByModule(string moduleId)
        {
            lock (_lock)
            {
                if (!_albumsByModule.TryGetValue(moduleId, out var albumIds))
                    return;

                foreach (var albumId in albumIds.ToList())
                {
                    UnregisterAlbum(albumId);
                }
            }
        }

        /// <summary>
        /// 更新专辑的歌曲数量
        /// </summary>
        public void UpdateSongCount(string albumId, int count)
        {
            lock (_lock)
            {
                if (_albums.TryGetValue(albumId, out var album))
                {
                    album.SongCount = count;
                }
            }
        }
    }
}

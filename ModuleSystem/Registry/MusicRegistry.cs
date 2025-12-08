using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.ModuleSystem.Registry
{
    /// <summary>
    /// 歌曲注册表实现
    /// </summary>
    public class MusicRegistry : IMusicRegistry
    {
        private static MusicRegistry _instance;
        public static MusicRegistry Instance => _instance;

        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, MusicInfo> _music = new Dictionary<string, MusicInfo>();
        private readonly Dictionary<string, List<string>> _musicByAlbum = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _musicByTag = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _musicByModule = new Dictionary<string, List<string>>();
        private readonly object _lock = new object();

        public event Action<MusicInfo> OnMusicRegistered;
        public event Action<string> OnMusicUnregistered;
        public event Action<MusicInfo> OnMusicUpdated;

        public static void Initialize(ManualLogSource logger)
        {
            if (_instance != null)
            {
                logger.LogWarning("MusicRegistry 已初始化");
                return;
            }
            _instance = new MusicRegistry(logger);
        }

        private MusicRegistry(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void RegisterMusic(MusicInfo music, string moduleId)
        {
            if (music == null)
                throw new ArgumentNullException(nameof(music));
            if (string.IsNullOrEmpty(music.UUID))
                throw new ArgumentException("歌曲 UUID 不能为空");

            lock (_lock)
            {
                // 如果已存在，先注销
                if (_music.ContainsKey(music.UUID))
                {
                    UnregisterMusic(music.UUID);
                }

                music.ModuleId = moduleId;
                _music[music.UUID] = music;

                // 按专辑索引
                if (!string.IsNullOrEmpty(music.AlbumId))
                {
                    if (!_musicByAlbum.ContainsKey(music.AlbumId))
                    {
                        _musicByAlbum[music.AlbumId] = new List<string>();
                    }
                    _musicByAlbum[music.AlbumId].Add(music.UUID);
                }

                // 按 Tag 索引
                if (!string.IsNullOrEmpty(music.TagId))
                {
                    if (!_musicByTag.ContainsKey(music.TagId))
                    {
                        _musicByTag[music.TagId] = new List<string>();
                    }
                    _musicByTag[music.TagId].Add(music.UUID);
                }

                // 按模块索引
                if (!_musicByModule.ContainsKey(moduleId))
                {
                    _musicByModule[moduleId] = new List<string>();
                }
                _musicByModule[moduleId].Add(music.UUID);

                OnMusicRegistered?.Invoke(music);
            }
        }

        public void RegisterMusicBatch(IEnumerable<MusicInfo> musicList, string moduleId)
        {
            if (musicList == null)
                throw new ArgumentNullException(nameof(musicList));

            int count = 0;
            foreach (var music in musicList)
            {
                RegisterMusic(music, moduleId);
                count++;
            }

            _logger.LogInfo($"批量注册 {count} 首歌曲 (模块: {moduleId})");
        }

        public void UnregisterMusic(string uuid)
        {
            lock (_lock)
            {
                if (!_music.TryGetValue(uuid, out var music))
                    return;

                // 从专辑索引中移除
                if (!string.IsNullOrEmpty(music.AlbumId) && _musicByAlbum.TryGetValue(music.AlbumId, out var albumMusic))
                {
                    albumMusic.Remove(uuid);
                }

                // 从 Tag 索引中移除
                if (!string.IsNullOrEmpty(music.TagId) && _musicByTag.TryGetValue(music.TagId, out var tagMusic))
                {
                    tagMusic.Remove(uuid);
                }

                // 从模块索引中移除
                if (!string.IsNullOrEmpty(music.ModuleId) && _musicByModule.TryGetValue(music.ModuleId, out var moduleMusic))
                {
                    moduleMusic.Remove(uuid);
                }

                _music.Remove(uuid);

                OnMusicUnregistered?.Invoke(uuid);
            }
        }

        public MusicInfo GetMusic(string uuid)
        {
            lock (_lock)
            {
                return _music.TryGetValue(uuid, out var music) ? music : null;
            }
        }

        /// <summary>
        /// 根据 UUID 获取歌曲信息 (GetMusic 的别名)
        /// </summary>
        public MusicInfo GetByUUID(string uuid) => GetMusic(uuid);

        public IReadOnlyList<MusicInfo> GetAllMusic()
        {
            lock (_lock)
            {
                return _music.Values.ToList();
            }
        }

        public IReadOnlyList<MusicInfo> GetMusicByAlbum(string albumId)
        {
            lock (_lock)
            {
                if (!_musicByAlbum.TryGetValue(albumId, out var uuids))
                    return new List<MusicInfo>();

                return uuids
                    .Select(id => _music.TryGetValue(id, out var m) ? m : null)
                    .Where(m => m != null)
                    .ToList();
            }
        }

        public IReadOnlyList<MusicInfo> GetMusicByTag(string tagId)
        {
            lock (_lock)
            {
                if (!_musicByTag.TryGetValue(tagId, out var uuids))
                    return new List<MusicInfo>();

                return uuids
                    .Select(id => _music.TryGetValue(id, out var m) ? m : null)
                    .Where(m => m != null)
                    .ToList();
            }
        }

        public IReadOnlyList<MusicInfo> GetMusicByModule(string moduleId)
        {
            lock (_lock)
            {
                if (!_musicByModule.TryGetValue(moduleId, out var uuids))
                    return new List<MusicInfo>();

                return uuids
                    .Select(id => _music.TryGetValue(id, out var m) ? m : null)
                    .Where(m => m != null)
                    .ToList();
            }
        }

        public bool IsMusicRegistered(string uuid)
        {
            lock (_lock)
            {
                return _music.ContainsKey(uuid);
            }
        }

        public void UpdateMusic(MusicInfo music)
        {
            if (music == null || string.IsNullOrEmpty(music.UUID))
                return;

            lock (_lock)
            {
                if (_music.TryGetValue(music.UUID, out var oldMusic))
                {
                    // 如果 AlbumId 发生变化，需要更新索引
                    if (oldMusic.AlbumId != music.AlbumId)
                    {
                        // 从旧专辑的索引中移除
                        if (!string.IsNullOrEmpty(oldMusic.AlbumId) && _musicByAlbum.TryGetValue(oldMusic.AlbumId, out var oldAlbumList))
                        {
                            oldAlbumList.Remove(music.UUID);
                        }
                        
                        // 添加到新专辑的索引
                        if (!string.IsNullOrEmpty(music.AlbumId))
                        {
                            if (!_musicByAlbum.ContainsKey(music.AlbumId))
                            {
                                _musicByAlbum[music.AlbumId] = new List<string>();
                            }
                            if (!_musicByAlbum[music.AlbumId].Contains(music.UUID))
                            {
                                _musicByAlbum[music.AlbumId].Add(music.UUID);
                            }
                        }
                    }
                    
                    _music[music.UUID] = music;
                    OnMusicUpdated?.Invoke(music);
                }
            }
        }

        /// <summary>
        /// 注销指定模块的所有歌曲
        /// </summary>
        public void UnregisterAllByModule(string moduleId)
        {
            lock (_lock)
            {
                if (!_musicByModule.TryGetValue(moduleId, out var uuids))
                    return;

                foreach (var uuid in uuids.ToList())
                {
                    UnregisterMusic(uuid);
                }
            }
        }

        /// <summary>
        /// 获取歌曲总数
        /// </summary>
        public int GetTotalCount()
        {
            lock (_lock)
            {
                return _music.Count;
            }
        }

        /// <summary>
        /// 获取指定专辑的歌曲数量
        /// </summary>
        public int GetCountByAlbum(string albumId)
        {
            lock (_lock)
            {
                return _musicByAlbum.TryGetValue(albumId, out var uuids) ? uuids.Count : 0;
            }
        }
    }
}

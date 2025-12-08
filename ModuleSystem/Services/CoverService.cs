using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Logging;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.UIFramework.Core;
using UnityEngine;

namespace ChillPatcher.ModuleSystem.Services
{
    /// <summary>
    /// 统一封面服务
    /// 提供歌曲和专辑封面的中心化获取
    /// 自动处理模块封面和默认封面的回退逻辑
    /// 支持加载占位图和异步更新通知
    /// </summary>
    public class CoverService
    {
        private static CoverService _instance;
        public static CoverService Instance => _instance ??= new CoverService();

        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, (byte[] data, string mimeType)> _bytesCache = new Dictionary<string, (byte[], string)>();

        /// <summary>
        /// 专辑封面加载完成事件
        /// 参数: (albumId, cover)
        /// </summary>
        public event Action<string, Sprite> OnAlbumCoverLoaded;

        /// <summary>
        /// 歌曲封面加载完成事件
        /// 参数: (uuid, cover)
        /// </summary>
        public event Action<string, Sprite> OnMusicCoverLoaded;

        private CoverService()
        {
            _logger = BepInEx.Logging.Logger.CreateLogSource("CoverService");
        }

        #region Loading Placeholder

        /// <summary>
        /// 获取加载中占位图
        /// </summary>
        public Sprite LoadingPlaceholder => EmbeddedResources.LoadingPlaceholder;

        #endregion

        #region Synchronous Placeholder Access

        /// <summary>
        /// 获取专辑封面（同步，如果未缓存则返回占位图并触发异步加载）
        /// </summary>
        /// <param name="albumId">专辑 ID</param>
        /// <returns>已缓存的封面或加载占位图</returns>
        public Sprite GetAlbumCoverOrPlaceholder(string albumId)
        {
            if (string.IsNullOrEmpty(albumId))
                return GetDefaultAlbumCover();

            var cacheKey = $"album:{albumId}";
            if (_spriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            // 未缓存，触发异步加载并返回占位图
            _ = LoadAlbumCoverAsync(albumId);
            return LoadingPlaceholder;
        }

        /// <summary>
        /// 获取歌曲封面（同步，如果未缓存则返回占位图并触发异步加载）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>已缓存的封面或加载占位图</returns>
        public Sprite GetMusicCoverOrPlaceholder(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return GetDefaultMusicCover();

            var cacheKey = $"music:{uuid}";
            if (_spriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            // 未缓存，触发异步加载并返回占位图
            _ = LoadMusicCoverAsync(uuid);
            return LoadingPlaceholder;
        }

        /// <summary>
        /// 异步加载专辑封面并触发事件
        /// </summary>
        private async Task LoadAlbumCoverAsync(string albumId)
        {
            // 强制异步执行，确保在 SetDataSource 和 UI 渲染之后触发事件
            // 这对于同步返回封面的模块（如网易云模块）至关重要
            await Task.Yield();
            
            try
            {
                var sprite = await TryGetAlbumCoverFromModuleAsync(albumId);
                var cacheKey = $"album:{albumId}";
                
                if (sprite != null)
                {
                    _spriteCache[cacheKey] = sprite;
                    OnAlbumCoverLoaded?.Invoke(albumId, sprite);
                }
                else
                {
                    // 使用默认封面
                    var defaultCover = GetDefaultAlbumCover();
                    _spriteCache[cacheKey] = defaultCover;
                    OnAlbumCoverLoaded?.Invoke(albumId, defaultCover);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"LoadAlbumCoverAsync failed [{albumId}]: {ex.Message}");
                OnAlbumCoverLoaded?.Invoke(albumId, GetDefaultAlbumCover());
            }
        }

        /// <summary>
        /// 异步加载歌曲封面并触发事件
        /// </summary>
        private async Task LoadMusicCoverAsync(string uuid)
        {
            // 强制异步执行，确保在 SetDataSource 和 UI 渲染之后触发事件
            // 这对于同步返回封面的模块至关重要
            await Task.Yield();
            
            try
            {
                var sprite = await TryGetMusicCoverFromModuleAsync(uuid);
                var cacheKey = $"music:{uuid}";
                
                if (sprite != null)
                {
                    _spriteCache[cacheKey] = sprite;
                    OnMusicCoverLoaded?.Invoke(uuid, sprite);
                }
                else
                {
                    // 使用默认封面
                    var defaultCover = GetDefaultMusicCover();
                    _spriteCache[cacheKey] = defaultCover;
                    OnMusicCoverLoaded?.Invoke(uuid, defaultCover);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"LoadMusicCoverAsync failed [{uuid}]: {ex.Message}");
                OnMusicCoverLoaded?.Invoke(uuid, GetDefaultMusicCover());
            }
        }

        #endregion

        #region Async API

        /// <summary>
        /// 获取歌曲封面 Sprite
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>封面 Sprite，如果没有返回默认封面</returns>
        public async Task<Sprite> GetMusicCoverAsync(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return GetDefaultMusicCover();

            var cacheKey = $"music:{uuid}";
            if (_spriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            // 尝试从模块获取
            var sprite = await TryGetMusicCoverFromModuleAsync(uuid);
            if (sprite != null)
            {
                _spriteCache[cacheKey] = sprite;
                return sprite;
            }

            // 返回默认封面
            return GetDefaultMusicCover();
        }

        /// <summary>
        /// 获取专辑封面 Sprite
        /// </summary>
        /// <param name="albumId">专辑 ID</param>
        /// <returns>封面 Sprite，如果没有返回默认封面</returns>
        public async Task<Sprite> GetAlbumCoverAsync(string albumId)
        {
            if (string.IsNullOrEmpty(albumId))
                return GetDefaultAlbumCover();

            var cacheKey = $"album:{albumId}";
            if (_spriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;

            // 尝试从模块获取
            var sprite = await TryGetAlbumCoverFromModuleAsync(albumId);
            if (sprite != null)
            {
                _spriteCache[cacheKey] = sprite;
                return sprite;
            }

            // 返回默认封面
            return GetDefaultAlbumCover();
        }

        /// <summary>
        /// 获取歌曲封面的原始字节数据（用于 SMTC 等场景）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <returns>封面字节数据和 MIME 类型</returns>
        public async Task<(byte[] data, string mimeType)> GetMusicCoverBytesAsync(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
                return (null, null);

            var cacheKey = $"music_bytes:{uuid}";
            if (_bytesCache.TryGetValue(cacheKey, out var cached) && cached.data != null)
                return cached;

            // 尝试从模块获取
            var result = await TryGetMusicCoverBytesFromModuleAsync(uuid);
            if (result.data != null)
            {
                _bytesCache[cacheKey] = result;
                return result;
            }

            return (null, null);
        }

        /// <summary>
        /// 获取默认歌曲封面（用于游戏原生歌曲或无封面情况）
        /// </summary>
        public Sprite GetDefaultMusicCover()
        {
            return DefaultCoverProvider.Instance?.DefaultMusicCover;
        }

        /// <summary>
        /// 获取默认专辑封面
        /// </summary>
        public Sprite GetDefaultAlbumCover()
        {
            return DefaultCoverProvider.Instance?.DefaultAlbumCover;
        }

        /// <summary>
        /// 获取本地音乐专用封面
        /// </summary>
        public Sprite GetLocalMusicCover()
        {
            return DefaultCoverProvider.Instance?.LocalMusicCover;
        }

        /// <summary>
        /// 根据游戏音频标签获取封面
        /// </summary>
        /// <param name="audioTag">音频标签 (Original=1, Special=2, Other=4)</param>
        public Sprite GetGameCover(int audioTag)
        {
            return DefaultCoverProvider.Instance?.GetGameCover(audioTag);
        }

        /// <summary>
        /// 根据游戏音频标签获取封面字节数据
        /// </summary>
        public (byte[] data, string mimeType) GetGameCoverBytes(int audioTag)
        {
            return DefaultCoverProvider.Instance?.GetGameCoverBytes(audioTag) ?? (null, null);
        }

        /// <summary>
        /// 获取默认封面的字节数据
        /// </summary>
        /// <param name="isLocal">是否是本地导入歌曲</param>
        public (byte[] data, string mimeType) GetDefaultCoverBytes(bool isLocal)
        {
            return DefaultCoverProvider.Instance?.GetDefaultCoverBytes(isLocal) ?? (null, null);
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearCache()
        {
            foreach (var sprite in _spriteCache.Values)
            {
                if (sprite != null && sprite != GetDefaultMusicCover() 
                    && sprite != GetDefaultAlbumCover() 
                    && sprite != GetLocalMusicCover())
                {
                    if (sprite.texture != null)
                        UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            _spriteCache.Clear();
            _bytesCache.Clear();
        }

        /// <summary>
        /// 移除指定歌曲的封面缓存
        /// 用于歌曲切换时清理不再需要的封面资源
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        public void RemoveMusicCover(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;

            // 通知模块清理其内部缓存
            var music = MusicRegistry.Instance?.GetMusic(uuid);
            if (music != null)
            {
                var provider = GetCoverProvider(music.ModuleId);
                provider?.RemoveMusicCoverCache(uuid);
            }

            // 清理 CoverService 的 Sprite 缓存
            // 注意：不再销毁 Sprite，因为模块已经负责销毁
            var spriteKey = $"music:{uuid}";
            if (_spriteCache.ContainsKey(spriteKey))
            {
                _spriteCache.Remove(spriteKey);
                _logger.LogDebug($"Removed music cover cache: {uuid}");
            }

            // 清理字节缓存
            var bytesKey = $"music_bytes:{uuid}";
            if (_bytesCache.ContainsKey(bytesKey))
            {
                _bytesCache.Remove(bytesKey);
            }
        }

        /// <summary>
        /// 移除指定专辑的封面缓存
        /// </summary>
        /// <param name="albumId">专辑 ID</param>
        public void RemoveAlbumCover(string albumId)
        {
            if (string.IsNullOrEmpty(albumId)) return;

            // 通知模块清理其内部缓存
            var album = AlbumRegistry.Instance?.GetAlbum(albumId);
            if (album != null)
            {
                var provider = GetCoverProvider(album.ModuleId);
                provider?.RemoveAlbumCoverCache(albumId);
            }

            // 清理 CoverService 的 Sprite 缓存
            var spriteKey = $"album:{albumId}";
            if (_spriteCache.ContainsKey(spriteKey))
            {
                _spriteCache.Remove(spriteKey);
                _logger.LogDebug($"Removed album cover cache: {albumId}");
            }
        }

        #endregion

        #region Private Methods

        private async Task<Sprite> TryGetMusicCoverFromModuleAsync(string uuid)
        {
            var music = MusicRegistry.Instance?.GetMusic(uuid);
            if (music == null) return null;

            var provider = GetCoverProvider(music.ModuleId);
            if (provider == null) return null;

            try
            {
                return await provider.GetMusicCoverAsync(uuid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"获取歌曲封面失败 [{uuid}]: {ex.Message}");
                return null;
            }
        }

        private async Task<Sprite> TryGetAlbumCoverFromModuleAsync(string albumId)
        {
            var album = AlbumRegistry.Instance?.GetAlbum(albumId);
            if (album == null) return null;

            var provider = GetCoverProvider(album.ModuleId);
            if (provider == null) return null;

            try
            {
                return await provider.GetAlbumCoverAsync(albumId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"获取专辑封面失败 [{albumId}]: {ex.Message}");
                return null;
            }
        }

        private async Task<(byte[] data, string mimeType)> TryGetMusicCoverBytesFromModuleAsync(string uuid)
        {
            var music = MusicRegistry.Instance?.GetMusic(uuid);
            if (music == null) return (null, null);

            var provider = GetCoverProvider(music.ModuleId);
            if (provider == null) return (null, null);

            try
            {
                return await provider.GetMusicCoverBytesAsync(uuid);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"获取歌曲封面字节失败 [{uuid}]: {ex.Message}");
                return (null, null);
            }
        }

        private ICoverProvider GetCoverProvider(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;
            return ModuleLoader.Instance?.GetProvider<ICoverProvider>(moduleId);
        }

        #endregion
    }
}

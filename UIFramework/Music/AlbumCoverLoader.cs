using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using ChillPatcher.UIFramework.Data;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 封面来源类型
    /// </summary>
    public enum CoverSourceType
    {
        None = 0,
        ImageFile = 1,      // 图片文件
        AudioEmbedded = 2   // 音频内嵌封面
    }

    /// <summary>
    /// 封面缓存数据
    /// </summary>
    public class CoverCacheData
    {
        public string AlbumId { get; set; }
        public string CoverPath { get; set; }       // 图片文件路径 或 音频文件路径
        public CoverSourceType SourceType { get; set; }
    }

    /// <summary>
    /// 专辑封面加载器 - 支持优先级搜索和数据库缓存
    /// </summary>
    public class AlbumCoverLoader
    {
        private static readonly BepInEx.Logging.ManualLogSource Logger = 
            BepInEx.Logging.Logger.CreateLogSource("AlbumCoverLoader");

        // 优先级前缀（按顺序搜索）
        private static readonly string[] CoverPrefixes = new string[]
        {
            "cover",
            "folder",
            "front",
            "album",
            "thumb"
        };

        // 支持的图片扩展名
        private static readonly string[] ImageExtensions = new string[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
        };

        // 内存缓存
        private readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, CoverCacheData> _pathCache = new Dictionary<string, CoverCacheData>();
        
        // 数据库引用
        private readonly PlaylistDatabase _database;

        // Sprite 缓存
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

        public AlbumCoverLoader(PlaylistDatabase database)
        {
            _database = database;
        }

        /// <summary>
        /// 异步加载专辑封面（返回 Sprite）
        /// </summary>
        public async Task<Sprite> LoadCoverAsync(string albumId, string directoryPath, string[] audioFiles = null)
        {
            if (string.IsNullOrEmpty(albumId) || string.IsNullOrEmpty(directoryPath))
                return null;

            // 检查 Sprite 缓存
            if (_spriteCache.TryGetValue(albumId, out var cachedSprite) && cachedSprite != null)
                return cachedSprite;

            var texture = await LoadCoverTextureAsync(albumId, directoryPath, audioFiles);
            if (texture == null)
                return null;

            // 在主线程创建 Sprite
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            
            _spriteCache[albumId] = sprite;
            return sprite;
        }

        /// <summary>
        /// 仅从目录加载封面（不查找嵌入封面）
        /// 用于歌单文件夹等不需要查找音频嵌入封面的场景
        /// </summary>
        public async Task<Sprite> LoadCoverFromDirectoryOnly(string albumId, string directoryPath)
        {
            if (string.IsNullOrEmpty(albumId) || string.IsNullOrEmpty(directoryPath))
                return null;

            // 检查 Sprite 缓存
            if (_spriteCache.TryGetValue(albumId, out var cachedSprite) && cachedSprite != null)
                return cachedSprite;

            try
            {
                // 只搜索目录中的图片文件，不查找嵌入封面
                var (coverPath, sourceType) = await SearchCoverFromDirectoryOnlyAsync(directoryPath);
                
                if (!string.IsNullOrEmpty(coverPath) && sourceType == CoverSourceType.ImageFile)
                {
                    var imageBytes = await LoadImageBytesAsync(coverPath);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        var texture = CreateTextureFromBytes(imageBytes);
                        if (texture != null)
                        {
                            _textureCache[albumId] = texture;
                            
                            // 创建 Sprite
                            var sprite = Sprite.Create(
                                texture,
                                new Rect(0, 0, texture.width, texture.height),
                                new Vector2(0.5f, 0.5f),
                                100f
                            );
                            _spriteCache[albumId] = sprite;
                            
                            Logger.LogInfo($"Loaded playlist cover: {albumId} from {coverPath}");
                            return sprite;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load playlist cover [{albumId}]: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 仅从目录搜索封面图片（不查找嵌入封面）
        /// </summary>
        private async Task<(string path, CoverSourceType type)> SearchCoverFromDirectoryOnlyAsync(string directoryPath)
        {
            return await Task.Run(() =>
            {
                if (!Directory.Exists(directoryPath))
                    return (null, CoverSourceType.None);

                try
                {
                    // 获取目录下所有图片文件
                    var allImages = new List<string>();
                    foreach (var ext in ImageExtensions)
                    {
                        allImages.AddRange(Directory.GetFiles(directoryPath, $"*{ext}", SearchOption.TopDirectoryOnly));
                    }

                    if (allImages.Count == 0)
                        return (null, CoverSourceType.None);

                    // 按优先级前缀搜索
                    foreach (var prefix in CoverPrefixes)
                    {
                        var match = allImages.FirstOrDefault(f => 
                            Path.GetFileNameWithoutExtension(f).Equals(prefix, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            return (match, CoverSourceType.ImageFile);
                    }

                    // 返回第一个图片
                    return (allImages.First(), CoverSourceType.ImageFile);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"搜索封面失败 [{directoryPath}]: {ex.Message}");
                    return (null, CoverSourceType.None);
                }
            });
        }

        /// <summary>
        /// 异步加载专辑封面（返回 Texture2D）
        /// </summary>
        public async Task<Texture2D> LoadCoverTextureAsync(string albumId, string directoryPath, string[] audioFiles = null)
        {
            if (string.IsNullOrEmpty(albumId) || string.IsNullOrEmpty(directoryPath))
                return null;

            // 1. 检查内存缓存
            if (_textureCache.TryGetValue(albumId, out var cached) && cached != null)
                return cached;

            try
            {
                // 2. 检查数据库缓存
                var dbCache = await GetCoverCacheFromDatabaseAsync(albumId);
                if (dbCache != null && !string.IsNullOrEmpty(dbCache.CoverPath))
                {
                    var texture = await LoadFromCacheDataAsync(dbCache);
                    if (texture != null)
                    {
                        _textureCache[albumId] = texture;
                        return texture;
                    }
                    // 缓存失效，清除
                    await RemoveCoverCacheAsync(albumId);
                }

                // 3. 搜索封面文件
                var (coverPath, sourceType) = await SearchCoverAsync(directoryPath, audioFiles);
                
                if (!string.IsNullOrEmpty(coverPath))
                {
                    // 加载封面字节（后台线程）
                    byte[] imageBytes = null;
                    if (sourceType == CoverSourceType.ImageFile)
                    {
                        imageBytes = await LoadImageBytesAsync(coverPath);
                    }
                    else if (sourceType == CoverSourceType.AudioEmbedded)
                    {
                        imageBytes = await ExtractAudioCoverBytesAsync(coverPath);
                    }

                    // 在主线程创建纹理
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        var texture = CreateTextureFromBytes(imageBytes);
                        if (texture != null)
                        {
                            // 保存到缓存
                            _textureCache[albumId] = texture;
                            await SaveCoverCacheAsync(albumId, coverPath, sourceType);
                            return texture;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"加载封面失败 [{albumId}]: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 搜索封面文件（按优先级）
        /// </summary>
        private async Task<(string path, CoverSourceType type)> SearchCoverAsync(string directoryPath, string[] audioFiles)
        {
            return await Task.Run(() =>
            {
                if (!Directory.Exists(directoryPath))
                    return (null, CoverSourceType.None);

                try
                {
                    // 获取目录下所有图片文件
                    var allImages = new List<string>();
                    foreach (var ext in ImageExtensions)
                    {
                        allImages.AddRange(Directory.GetFiles(directoryPath, $"*{ext}", SearchOption.TopDirectoryOnly));
                    }

                    if (allImages.Count == 0 && (audioFiles == null || audioFiles.Length == 0))
                        return (null, CoverSourceType.None);

                    // 按优先级前缀搜索
                    foreach (var prefix in CoverPrefixes)
                    {
                        var match = allImages.FirstOrDefault(f => 
                            Path.GetFileNameWithoutExtension(f).Equals(prefix, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            return (match, CoverSourceType.ImageFile);
                    }

                    // 按优先级前缀搜索（包含前缀）
                    foreach (var prefix in CoverPrefixes)
                    {
                        var match = allImages.FirstOrDefault(f => 
                            Path.GetFileNameWithoutExtension(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                            return (match, CoverSourceType.ImageFile);
                    }

                    // 使用任意图片文件
                    if (allImages.Count > 0)
                        return (allImages[0], CoverSourceType.ImageFile);

                    // 尝试从音频文件提取
                    if (audioFiles != null && audioFiles.Length > 0)
                    {
                        // 优先使用第一个音频文件
                        var firstAudio = audioFiles.FirstOrDefault(f => File.Exists(f));
                        if (firstAudio != null)
                            return (firstAudio, CoverSourceType.AudioEmbedded);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"搜索封面时出错 [{directoryPath}]: {ex.Message}");
                }

                return (null, CoverSourceType.None);
            });
        }

        /// <summary>
        /// 从缓存数据加载
        /// </summary>
        private async Task<Texture2D> LoadFromCacheDataAsync(CoverCacheData cache)
        {
            if (cache == null || string.IsNullOrEmpty(cache.CoverPath))
                return null;

            if (!File.Exists(cache.CoverPath))
                return null;

            byte[] imageBytes = null;
            
            if (cache.SourceType == CoverSourceType.ImageFile)
                imageBytes = await LoadImageBytesAsync(cache.CoverPath);
            else if (cache.SourceType == CoverSourceType.AudioEmbedded)
                imageBytes = await ExtractAudioCoverBytesAsync(cache.CoverPath);

            if (imageBytes != null && imageBytes.Length > 0)
                return CreateTextureFromBytes(imageBytes);
            
            return null;
        }

        /// <summary>
        /// 加载图片文件字节（后台线程）
        /// </summary>
        private async Task<byte[]> LoadImageBytesAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                        return null;

                    return File.ReadAllBytes(filePath);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"加载图片失败 [{filePath}]: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// 从音频文件提取封面字节（后台线程）
        /// </summary>
        private async Task<byte[]> ExtractAudioCoverBytesAsync(string audioFilePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(audioFilePath))
                        return null;

                    using (var file = TagLib.File.Create(audioFilePath))
                    {
                        if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                        {
                            var picture = file.Tag.Pictures[0];
                            return picture.Data.Data;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"提取音频封面失败 [{audioFilePath}]: {ex.Message}");
                }
                return null;
            });
        }

        /// <summary>
        /// 从字节数组创建纹理（必须在主线程调用）
        /// </summary>
        private Texture2D CreateTextureFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes))
            {
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }

            UnityEngine.Object.Destroy(texture);
            return null;
        }

        #region 数据库缓存操作

        /// <summary>
        /// 从数据库获取封面缓存
        /// </summary>
        private async Task<CoverCacheData> GetCoverCacheFromDatabaseAsync(string albumId)
        {
            if (_database == null)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    return _database.GetAlbumCoverCache(albumId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"读取封面缓存失败 [{albumId}]: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// 保存封面缓存到数据库
        /// </summary>
        private async Task SaveCoverCacheAsync(string albumId, string coverPath, CoverSourceType sourceType)
        {
            if (_database == null)
                return;

            await Task.Run(() =>
            {
                try
                {
                    _database.SaveAlbumCoverCache(albumId, coverPath, (int)sourceType);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"保存封面缓存失败 [{albumId}]: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 移除封面缓存
        /// </summary>
        private async Task RemoveCoverCacheAsync(string albumId)
        {
            if (_database == null)
                return;

            await Task.Run(() =>
            {
                try
                {
                    _database.RemoveAlbumCoverCache(albumId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"移除封面缓存失败 [{albumId}]: {ex.Message}");
                }
            });
        }

        #endregion

        /// <summary>
        /// 检查专辑是否有封面（快速检查，不加载纹理）
        /// </summary>
        public async Task<bool> HasCoverAsync(string albumId, string directoryPath, string[] audioFiles = null)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return false;

            // 检查内存缓存
            if (_textureCache.ContainsKey(albumId))
                return true;

            // 检查路径缓存
            if (_pathCache.TryGetValue(albumId, out var cached) && cached != null)
                return !string.IsNullOrEmpty(cached.CoverPath);

            // 检查数据库缓存
            var dbCache = await GetCoverCacheFromDatabaseAsync(albumId);
            if (dbCache != null)
            {
                _pathCache[albumId] = dbCache;
                return !string.IsNullOrEmpty(dbCache.CoverPath) && File.Exists(dbCache.CoverPath);
            }

            // 搜索封面
            var (path, type) = await SearchCoverAsync(directoryPath, audioFiles);
            var hasCover = !string.IsNullOrEmpty(path);

            // 缓存结果
            _pathCache[albumId] = new CoverCacheData
            {
                AlbumId = albumId,
                CoverPath = path,
                SourceType = type
            };

            return hasCover;
        }

        /// <summary>
        /// 清除指定专辑的缓存
        /// </summary>
        public void ClearCache(string albumId)
        {
            if (_spriteCache.TryGetValue(albumId, out var sprite) && sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
            }
            if (_textureCache.TryGetValue(albumId, out var texture) && texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            _spriteCache.Remove(albumId);
            _textureCache.Remove(albumId);
            _pathCache.Remove(albumId);
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearAllCache()
        {
            foreach (var kvp in _spriteCache)
            {
                if (kvp.Value != null)
                    UnityEngine.Object.Destroy(kvp.Value);
            }
            foreach (var kvp in _textureCache)
            {
                if (kvp.Value != null)
                    UnityEngine.Object.Destroy(kvp.Value);
            }
            _spriteCache.Clear();
            _textureCache.Clear();
            _pathCache.Clear();
        }

        /// <summary>
        /// 获取缓存统计
        /// </summary>
        public (int textureCount, int pathCount, int spriteCount) GetCacheStats()
        {
            return (_textureCache.Count, _pathCache.Count, _spriteCache.Count);
        }

        /// <summary>
        /// 从嵌入资源加载游戏封面 - 用于原生Tag专辑
        /// </summary>
        /// <param name="audioTag">游戏原生Tag (1=Original, 2=Special, 4=Other)</param>
        /// <returns>对应的封面 Sprite</returns>
        public Sprite LoadGameCoverFromEmbeddedResource(int audioTag)
        {
            // 根据 AudioTag 确定资源名称
            // Original=1 -> gamecover1.jpg
            // Special=2 -> gamecover2.jpg
            // Other=4 -> gamecover3.png
            string resourceName;
            switch (audioTag)
            {
                case 1: // Original
                    resourceName = "ChillPatcher.Resources.gamecover1.jpg";
                    break;
                case 2: // Special
                    resourceName = "ChillPatcher.Resources.gamecover2.jpg";
                    break;
                case 4: // Other
                    resourceName = "ChillPatcher.Resources.gamecover3.png";
                    break;
                default:
                    Logger.LogWarning($"Unknown AudioTag for game cover: {audioTag}");
                    return null;
            }

            // 检查缓存
            string cacheKey = $"__gamecover_{audioTag}__";
            if (_spriteCache.TryGetValue(cacheKey, out var cachedSprite) && cachedSprite != null)
                return cachedSprite;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Logger.LogWarning($"Game cover resource not found: {resourceName}");
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    var texture = new Texture2D(2, 2);
                    if (texture.LoadImage(bytes))
                    {
                        _textureCache[cacheKey] = texture;
                        
                        var sprite = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f),
                            100f
                        );
                        _spriteCache[cacheKey] = sprite;
                        
                        Logger.LogInfo($"Loaded game cover from embedded resource: {resourceName}");
                        return sprite;
                    }
                    else
                    {
                        Logger.LogWarning($"Failed to load game cover texture: {resourceName}");
                        UnityEngine.Object.Destroy(texture);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load game cover from embedded resource: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 从嵌入资源加载纹理（公开方法）
        /// </summary>
        /// <param name="resourceName">嵌入资源的完整名称，例如 "ChillPatcher.Resources.defaultcover.png"</param>
        /// <returns>加载的 Texture2D，失败返回 null</returns>
        public static Texture2D LoadEmbeddedCoverTexture(string resourceName)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Logger.LogWarning($"Embedded resource not found: {resourceName}");
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    var texture = new Texture2D(2, 2);
                    if (texture.LoadImage(bytes))
                    {
                        Logger.LogDebug($"Loaded embedded texture: {resourceName}");
                        return texture;
                    }
                    else
                    {
                        Logger.LogWarning($"Failed to load embedded texture: {resourceName}");
                        UnityEngine.Object.Destroy(texture);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Error loading embedded texture {resourceName}: {ex.Message}");
            }

            return null;
        }
    }
}

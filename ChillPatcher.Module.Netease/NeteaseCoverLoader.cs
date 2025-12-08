using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云封面加载器
    /// 负责加载模块默认封面和从网络获取歌曲/专辑封面
    /// </summary>
    public class NeteaseCoverLoader
    {
        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, Sprite> _coverCache = new Dictionary<string, Sprite>();
        
        // 收藏专辑专用封面（同时作为默认封面）
        private Sprite _favoritesCover;
        private byte[] _favoritesCoverBytes;

        // FM 专辑专用封面
        private Sprite _fmCover;
        private byte[] _fmCoverBytes;

        public NeteaseCoverLoader(ManualLogSource logger)
        {
            _logger = logger;
            LoadDefaultCover();
            LoadFavoritesCover();
            LoadFMCover();
        }

        /// <summary>
        /// 默认封面（使用收藏封面）
        /// </summary>
        public Sprite DefaultCover => _favoritesCover;

        /// <summary>
        /// 默认封面字节数据（用于 SMTC）
        /// </summary>
        public byte[] DefaultCoverBytes => _favoritesCoverBytes;

        /// <summary>
        /// 收藏专辑封面
        /// </summary>
        public Sprite FavoritesCover => _favoritesCover;

        /// <summary>
        /// 收藏专辑封面字节数据
        /// </summary>
        public byte[] FavoritesCoverBytes => _favoritesCoverBytes;

        /// <summary>
        /// FM 专辑封面
        /// </summary>
        public Sprite FMCover => _fmCover ?? _favoritesCover;

        /// <summary>
        /// FM 专辑封面字节数据
        /// </summary>
        public byte[] FMCoverBytes => _fmCoverBytes ?? _favoritesCoverBytes;

        private void LoadDefaultCover()
        {
            // 默认封面使用收藏封面（网易云模块只有收藏和 FM 两个专辑）
            // 实际的默认封面会在 LoadFavoritesCover 中设置
            _logger.LogDebug("[NeteaseCoverLoader] 默认封面将使用收藏封面");
        }

        private void LoadFavoritesCover()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "ChillPatcher.Module.Netease.Resources.FAVORITES.png";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning("[NeteaseCoverLoader] 收藏封面资源未找到");
                        return;
                    }

                    _favoritesCoverBytes = new byte[stream.Length];
                    stream.Read(_favoritesCoverBytes, 0, _favoritesCoverBytes.Length);

                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(_favoritesCoverBytes);

                    _favoritesCover = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));

                    _logger.LogInfo($"[NeteaseCoverLoader] 已加载收藏封面: {texture.width}x{texture.height}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseCoverLoader] 加载收藏封面失败: {ex.Message}");
            }
        }

        private void LoadFMCover()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "ChillPatcher.Module.Netease.Resources.FM.png";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        _logger.LogWarning("[NeteaseCoverLoader] FM封面资源未找到");
                        return;
                    }

                    _fmCoverBytes = new byte[stream.Length];
                    stream.Read(_fmCoverBytes, 0, _fmCoverBytes.Length);

                    var texture = new Texture2D(2, 2);
                    texture.LoadImage(_fmCoverBytes);

                    _fmCover = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));

                    _logger.LogInfo($"[NeteaseCoverLoader] 已加载FM封面: {texture.width}x{texture.height}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseCoverLoader] 加载FM封面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 URL 获取封面
        /// </summary>
        public async Task<Sprite> GetCoverFromUrlAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
                return _favoritesCover;

            // 将 HTTP 转换为 HTTPS（避免 "Insecure connection not allowed" 错误）
            url = EnsureHttps(url);

            // 检查缓存
            if (_coverCache.TryGetValue(url, out var cached))
                return cached;

            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Delay(10);

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var texture = DownloadHandlerTexture.GetContent(request);
                        var sprite = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f));

                        _coverCache[url] = sprite;
                        return sprite;
                    }
                    else
                    {
                        _logger.LogWarning($"[NeteaseCoverLoader] 封面下载失败: {request.error}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseCoverLoader] 封面加载异常: {ex.Message}");
            }

            return _favoritesCover;
        }

        /// <summary>
        /// 从 URL 获取封面字节数据
        /// </summary>
        public async Task<(byte[] data, string mimeType)> GetCoverBytesFromUrlAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
                return (_favoritesCoverBytes, "image/png");

            // 将 HTTP 转换为 HTTPS
            url = EnsureHttps(url);

            try
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Delay(10);

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var data = request.downloadHandler.data;
                        var mimeType = request.GetResponseHeader("Content-Type") ?? "image/jpeg";
                        return (data, mimeType);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseCoverLoader] 获取封面字节失败: {ex.Message}");
            }

            return (_favoritesCoverBytes, "image/png");
        }

        /// <summary>
        /// 清除封面缓存
        /// </summary>
        public void ClearCache()
        {
            foreach (var sprite in _coverCache.Values)
            {
                if (sprite != null && sprite.texture != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            _coverCache.Clear();
        }

        /// <summary>
        /// 移除指定 URL 的缓存
        /// </summary>
        public void RemoveCache(string url)
        {
            if (_coverCache.TryGetValue(url, out var sprite))
            {
                if (sprite != null && sprite.texture != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
                _coverCache.Remove(url);
            }
        }

        /// <summary>
        /// 确保 URL 使用 HTTPS 协议
        /// 避免 Unity "Insecure connection not allowed" 错误
        /// </summary>
        private static string EnsureHttps(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + url.Substring(7);
            }

            return url;
        }
    }
}

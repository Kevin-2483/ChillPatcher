using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OmniMixPlayer.Module.Kugou
{
    public sealed class KugouCoverLoader
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly ILogger _logger;
        private readonly Dictionary<string, KugouBridge.SongInfo> _songs;
        private readonly ConcurrentDictionary<string, (byte[] data, string mimeType)> _cache = new();

        public KugouCoverLoader(ILogger logger, Dictionary<string, KugouBridge.SongInfo> songs)
        {
            _logger = logger;
            _songs = songs;
        }

        public async Task<(byte[] data, string mimeType)> GetAsync(string uuid)
        {
            if (_cache.TryGetValue(uuid, out var cached)) return cached;
            if (!_songs.TryGetValue(uuid, out var song) || string.IsNullOrWhiteSpace(song.CoverUrl)) return (null, null);
            try
            {
                using var response = await Client.GetAsync(song.CoverUrl);
                if (!response.IsSuccessStatusCode) return (null, null);
                var result = (await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
                _cache[uuid] = result;
                return result;
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "[酷狗音乐] 封面下载失败"); return (null, null); }
        }

        public void Clear() => _cache.Clear();
        public void Remove(string key) => _cache.TryRemove(key, out _);
    }
}

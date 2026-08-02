using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using OmniMixPlayer.Backend.Audio;
using OmniMixPlayer.SDK.Interfaces;

namespace OmniMixPlayer.Backend.ModuleSystem.Services.Streaming
{
    /// <summary>
    /// PCM 流式读取器 — "瓶子 + 水龙头" 模型。
    /// 
    /// HttpAudioCache 往缓存文件写 → OmniFileDecoder 从缓存文件读。
    /// 不切换解码器，不切换数据源。Seek 始终可用。
    /// 
    /// 状态机:
    ///   WaitingForData → Playing → Ended
    /// </summary>
    public class CorePcmStreamReader : IPcmStreamReader
    {
        private ILogger _logger;
        private string _url;
        private string _format;
        private float _durationHint;
        private readonly object _lock = new();
        private readonly object _decoderLock = new();

        private HttpAudioCache _cache;
        private DecoderEngine.OmniFileDecoder _decoder;

        private volatile bool _disposed;
        private int _disposeState;
        private volatile bool _isReady;
        private volatile bool _isEndOfStream;
        private volatile bool _completionTransition;
        private volatile bool _finalDecoderReady;
        private PcmStreamInfo _info;

        private ulong _currentFrame;
        private long _pendingSeek = -1;
        private volatile bool _seekBuffering; // true after seek, cleared when enough data buffered

        // ===== Properties =====

        public PcmStreamInfo Info => _info;
        public ulong CurrentFrame { get { lock (_lock) return _currentFrame; } }
        public bool IsEndOfStream => _isEndOfStream;
        public bool IsReady => _isReady;
        public bool CanSeek => true;
        public bool HasPendingSeek => _pendingSeek >= 0 || _seekBuffering;
        public long PendingSeekFrame => _pendingSeek;
        public double CacheProgress => _cache?.Progress ?? -1;
        public bool IsCacheComplete => _cache?.IsComplete ?? false;

        // ===== Constructors =====

        public CorePcmStreamReader(string url, string format, float durationSeconds,
            string cacheKey, Dictionary<string, string> headers = null, ILogger logger = null)
        {
            var cachePath = Path.Combine(HttpAudioCache.GetCacheDirectory(),
                $"{cacheKey}.v2.{format.ToLowerInvariant()}");
            Init(url, format, durationSeconds, cachePath, headers, logger);
        }

        public CorePcmStreamReader(string url, string format, float durationSeconds,
            string cachePath, Dictionary<string, string> headers, bool useCachePath,
            ILogger logger = null)
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Init(url, format, durationSeconds, cachePath, headers, logger);
        }

        private void Init(string url, string format, float durationSeconds,
            string cachePath, Dictionary<string, string> headers, ILogger logger)
        {
            _logger = logger;
            _url = url;
            _format = format.ToLowerInvariant();
            _durationHint = durationSeconds;

            // Placeholder info — real values filled when decoder opens
            _info = new PcmStreamInfo
            {
                SampleRate = 0,
                Channels = 0,
                TotalFrames = 0,
                Format = _format,
                CanSeek = true
            };

            // Already cached?
            if (File.Exists(cachePath))
            {
                try
                {
                    OpenDecoder(cachePath, false);
                    _isReady = true;
                    _logger?.LogInformation("Using cached file: {Path}", cachePath);
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Cached file invalid, re-downloading: {Msg}", ex.Message);
                }
            }

            // Is it a local file?
            if (IsLocalFile(url))
            {
                try
                {
                    string localPath = ResolveLocalPath(url);
                    OpenDecoder(localPath, false);
                    _isReady = true;
                    _logger?.LogInformation("Using local file: {Path}", localPath);
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to open local file: {Url}", url);
                    return;
                }
            }

            // Start HTTP download
            _cache = new HttpAudioCache(url, cachePath, headers, _logger);
            _cache.OnCommitting += OnCacheCommitting;
            _cache.OnComplete += OnCacheComplete;
            _cache.StartDownload();

            StartInitialDecoderOpen();
        }

        // ===== Bottle + Tap: open once there is enough data, without blocking creation =====

        private void StartInitialDecoderOpen()
        {
            var thread = new Thread(InitialDecoderOpenLoop)
            {
                IsBackground = true,
                Name = "CorePcmStreamReader_Init"
            };
            thread.Start();
        }

        private void InitialDecoderOpenLoop()
        {
            var cache = _cache;
            if (cache == null) return;

            // Wait up to 30s for first 64KB or download complete
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!_disposed && DateTime.UtcNow < deadline)
            {
                if (cache.Downloaded >= 65536 || cache.IsComplete)
                    break;
                Thread.Sleep(100);
            }

            if (_disposed) return;

            try
            {
                TryOpenGrowingDecoder(cache);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to open decoder during download: {Msg} — will retry on ReadFrames", ex.Message);
                // Don't set _isReady — ReadFrames will try again
            }
        }

        private void TryOpenGrowingDecoder(HttpAudioCache cache)
        {
            lock (_decoderLock)
            {
                if (_disposed || _completionTransition || _finalDecoderReady || _decoder != null)
                    return;
                OpenDecoderLocked(cache.PartialPath, true);
                _isReady = true;
            }
        }

        private void OpenDecoder(string path, bool isGrowing)
        {
            lock (_decoderLock)
            {
                OpenDecoderLocked(path, isGrowing);
            }
        }

        private void OpenDecoderLocked(string path, bool isGrowing)
        {
            _decoder?.Dispose();
            _decoder = new DecoderEngine.OmniFileDecoder(path, isGrowing);

            lock (_lock)
            {
                _info.SampleRate = _decoder.SampleRate;
                _info.Channels = _decoder.Channels;
                _info.TotalFrames = _decoder.TotalFrames > 0
                    ? _decoder.TotalFrames
                    : (_durationHint > 0
                        ? (ulong)(_decoder.SampleRate * _durationHint)
                        : 0);
                _info.Format = _decoder.Format;
                _isEndOfStream = false;

                if (_pendingSeek >= 0)
                {
                    _decoder.Seek((ulong)_pendingSeek);
                    _currentFrame = (ulong)_pendingSeek;
                    _pendingSeek = -1;
                    _seekBuffering = _cache != null && !_cache.IsComplete;
                }
            }
        }

        // ===== OnCacheComplete: just a flag, no switch =====

        private void OnCacheCommitting()
        {
            lock (_decoderLock)
            {
                _completionTransition = true;
                _decoder?.Dispose();
                _decoder = null;
            }
        }

        private void OnCacheComplete()
        {
            var cache = _cache;
            if (_disposed || cache == null) return;

            try
            {
                lock (_decoderLock)
                {
                    if (_disposed) return;
                    ulong resumeFrame;
                    lock (_lock) { resumeFrame = _currentFrame; }
                    OpenDecoderLocked(cache.FinalPath, false);
                    if (resumeFrame > 0 && _decoder.Seek(resumeFrame))
                    {
                        lock (_lock) { _currentFrame = resumeFrame; }
                    }
                    _finalDecoderReady = true;
                    _completionTransition = false;
                    _isReady = true;
                }
                _logger?.LogInformation("Cache complete — reopened final file at frame {Frame}", CurrentFrame);
            }
            catch (Exception ex)
            {
                _completionTransition = false;
                _logger?.LogWarning("Failed to reopen completed cache: {Msg}", ex.Message);
            }
        }

        // ===== ReadFrames: single data path =====

        public long ReadFrames(float[] buffer, int framesToRead)
        {
            if (_disposed) return 0;

            // Lazy init: if the background open did not finish yet, try now.
            if (_completionTransition)
                return 0;

            if (_decoder == null && _cache != null && _cache.IsComplete && !_finalDecoderReady)
            {
                try { OnCacheComplete(); }
                catch { }
            }

            if (_decoder == null && _cache != null && !_cache.IsComplete &&
                _cache.Downloaded >= 65536)
            {
                try { TryOpenGrowingDecoder(_cache); }
                catch { /* will retry next call */ }
            }

            if (_decoder == null)
                return 0; // not enough data yet

            if (!_isReady)
                _isReady = true;

            int channels = _info.Channels > 0 ? _info.Channels : 2;

            // Anti-stutter: if we just seeked and cache is still downloading,
            // wait until enough data is buffered past the current position.
            if (_seekBuffering && _cache != null && !_cache.IsComplete)
            {
                if (HasEnoughBuffer())
                    _seekBuffering = false;
                else
                    return 0; // tell PlaybackLoopAsync to wait
            }

            long read;
            lock (_decoderLock)
            {
                if (_decoder == null)
                    return 0;
                read = _decoder.ReadFrames(buffer, framesToRead);
            }

            if (read > 0)
            {
                lock (_lock) { _currentFrame += (ulong)read; }
                return read;
            }

            // read == 0: 可能是增长文件没数据，也可能是真 EOF
            if (_cache != null && _cache.IsComplete)
            {
                // Cache done + decoder returns 0 → real EOF
                lock (_lock) { _isEndOfStream = true; }
            }
            else if (_cache == null && _decoder != null)
            {
                // Local file + decoder returns 0 → real EOF
                lock (_lock) { _isEndOfStream = true; }
            }
            // else: cache still downloading, decoder returns 0 → data not arrived yet, caller retries

            // Zero-fill remainder
            int remaining = (framesToRead - (int)read) * channels;
            if (remaining > 0)
                Array.Clear(buffer, (int)read * channels, remaining);

            return read;
        }

        // ===== Seek: always works =====

        public bool Seek(ulong frameIndex)
        {
            if (_disposed) return false;

            lock (_decoderLock)
            {
                if (_decoder != null)
                {
                    bool ok = _decoder.Seek(frameIndex);
                    if (ok)
                    {
                        lock (_lock)
                        {
                            _currentFrame = frameIndex;
                            _isEndOfStream = false;
                            _seekBuffering = _cache != null && !_cache.IsComplete;
                        }
                    }
                    return ok;
                }
            }

            lock (_lock)
            {
                // Decoder not yet open — store as pending
                _pendingSeek = (long)frameIndex;
                _currentFrame = frameIndex;
                _isEndOfStream = false;
                _seekBuffering = true;
                return true;
            }
        }

        public void CancelPendingSeek()
        {
            _pendingSeek = -1;
        }

        // ===== Helpers =====

        private static bool IsLocalFile(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return true;
            return !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && File.Exists(url);
        }

        private static string ResolveLocalPath(string url)
        {
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                return uri.LocalPath;
            }
            return url;
        }

        // ===== Anti-stutter buffering check =====

        /// <summary>
        /// 检查下载进度是否已超过当前播放位置足够远，避免 seek 后边下边播的抖动。
        /// </summary>
        private bool HasEnoughBuffer()
        {
            if (_cache == null || _cache.IsComplete) return true;

            double cacheProgress = _cache.Progress; // 0~100, -1=unknown
            if (cacheProgress < 0) return true; // unknown size, try anyway

            // Estimate total frames
            double totalFrames = _info.TotalFrames > 0
                ? _info.TotalFrames
                : (_durationHint > 0 ? _info.SampleRate * _durationHint : 0);
            if (totalFrames <= 0) return true;

            double positionPercent = _currentFrame / totalFrames * 100.0;

            // Require at least 3% buffer ahead (≈ 6s for 200s track), min 2s equivalent
            double bufferFrames = Math.Max(2.0 * _info.SampleRate, totalFrames * 0.03);
            double targetPercent = positionPercent + (bufferFrames / totalFrames * 100.0);

            return cacheProgress >= targetPercent;
        }

        // ===== Dispose =====

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
            _disposed = true;

            var cache = _cache;
            if (cache != null)
            {
                cache.OnCommitting -= OnCacheCommitting;
                cache.OnComplete -= OnCacheComplete;
            }
            lock (_decoderLock)
            {
                _decoder?.Dispose();
                _decoder = null;
            }
            cache?.Dispose();
            _cache = null;
        }
    }
}

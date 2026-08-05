using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OmniMixPlayer.Backend.ModuleSystem.Services.Streaming
{
    /// <summary>
    /// HTTP 音频缓存: 后台下载音频文件到本地缓存, 同时允许读取已下载部分
    /// </summary>
    public class HttpAudioCache : IDisposable
    {
        private readonly ILogger _logger;
        private static readonly HttpClient SharedClient = new HttpClient();

        private readonly string _url;
        private readonly string _cachePath;
        private readonly string _partialPath;
        private readonly Dictionary<string, string> _headers;

        private FileStream _writeStream;
        private readonly object _lock = new object();
        private long _downloaded;
        private long _totalSize;
        private bool _isComplete;
        private bool _disposed;
        private CancellationTokenSource _cts;
        private Task _downloadTask;
        private Exception _error;
        private bool _committed;

        /// <summary>已下载的字节数</summary>
        public long Downloaded { get { lock (_lock) return _downloaded; } }

        /// <summary>文件总大小 (未知时为 -1)</summary>
        public long TotalSize { get { lock (_lock) return _totalSize; } }

        /// <summary>下载是否完成</summary>
        public bool IsComplete { get { lock (_lock) return _isComplete; } }

        /// <summary>缓存文件路径</summary>
        public string CachePath => IsComplete ? _cachePath : _partialPath;

        public string FinalPath => _cachePath;

        public string PartialPath => _partialPath;

        /// <summary>下载进度 0-100, 未知大小返回 -1</summary>
        public double Progress
        {
            get
            {
                lock (_lock)
                {
                    if (_isComplete) return 100.0;
                    if (_totalSize <= 0) return -1;
                    return (double)_downloaded / _totalSize * 100.0;
                }
            }
        }

        /// <summary>下载过程中的错误</summary>
        public Exception Error { get { lock (_lock) return _error; } }

        public event Action OnComplete;
        public event Action OnCommitting;

        public HttpAudioCache(string url, string cachePath,
            Dictionary<string, string> headers = null,
            ILogger logger = null)
        {
            _url = url;
            _cachePath = cachePath;
            _partialPath = $"{cachePath}.{Guid.NewGuid():N}.partial";
            _headers = headers;
            _logger = logger;
            _totalSize = -1;
        }

        /// <summary>开始后台下载</summary>
        public void StartDownload()
        {
            _cts = new CancellationTokenSource();

            var dir = Path.GetDirectoryName(_cachePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _writeStream = new FileStream(_partialPath, FileMode.Create,
                FileAccess.Write, FileShare.Read);

            _downloadTask = Task.Run(() => DownloadLoop(_cts.Token));
        }

        private const int MaxRetries = 3;
        private static readonly int[] RetryDelaysMs = { 1000, 3000, 5000 };

        private async Task DownloadLoop(CancellationToken ct)
        {
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        int delay = RetryDelaysMs[Math.Min(attempt - 1, RetryDelaysMs.Length - 1)];
                        _logger?.LogInformation($"Retry #{attempt} after {delay}ms...");
                        await Task.Delay(delay, ct);

                        // Reopen file for append at the position we left off
                        lock (_lock)
                        {
                            if (_writeStream == null)
                            {
                                _writeStream = new FileStream(_partialPath, FileMode.OpenOrCreate,
                                    FileAccess.Write, FileShare.Read);
                                _writeStream.Seek(_downloaded, SeekOrigin.Begin);
                            }
                        }
                    }

                    using (var request = new HttpRequestMessage(HttpMethod.Get, _url))
                    {
                        if (_headers != null)
                        {
                            foreach (var kv in _headers)
                                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }

                        // Resume from where we left off
                        long resumeFrom;
                        lock (_lock) { resumeFrom = _downloaded; }
                        if (resumeFrom > 0)
                        {
                            request.Headers.Range =
                                new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
                        }

                        using (var response = await SharedClient.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            response.EnsureSuccessStatusCode();

                            // Validate Range resume: if we requested a range but got 200 (not 206),
                            // the server doesn't support resume - restart from beginning
                            if (resumeFrom > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                _logger?.LogWarning("Server does not support Range requests, restarting download");
                                lock (_lock)
                                {
                                    _downloaded = 0;
                                    _writeStream?.Dispose();
                                    _writeStream = new FileStream(_partialPath, FileMode.Create,
                                        FileAccess.Write, FileShare.Read);
                                }
                                resumeFrom = 0;
                            }

                            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                            {
                                var range = response.Content.Headers.ContentRange;
                                if (range?.From != resumeFrom)
                                    throw new InvalidDataException($"Unexpected Content-Range start: {range?.From}, expected {resumeFrom}");
                                if (range.Length.HasValue)
                                {
                                    lock (_lock) { _totalSize = range.Length.Value; }
                                }
                            }
                            else if (resumeFrom == 0)
                            {
                                var contentLength = response.Content.Headers.ContentLength;
                                lock (_lock) { _totalSize = contentLength ?? -1; }
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync())
                            {
                                var buffer = new byte[8192];
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(
                                    buffer, 0, buffer.Length, ct)) > 0)
                                {
                                    lock (_lock)
                                    {
                                        _writeStream.Write(buffer, 0, bytesRead);
                                        _writeStream.Flush(true); // Ensure metadata is updated immediately
                                        _downloaded += bytesRead;
                                    }
                                }
                            }
                        }
                    }

                    long downloaded;
                    long totalSize;
                    lock (_lock)
                    {
                        _writeStream?.Flush(true);
                        _writeStream?.Dispose();
                        _writeStream = null;
                        downloaded = _downloaded;
                        totalSize = _totalSize;
                    }

                    if (totalSize >= 0 && downloaded != totalSize)
                        throw new InvalidDataException($"Incomplete download: {downloaded} of {totalSize} bytes");

                    OnCommitting?.Invoke();

                    if (File.Exists(_cachePath))
                        File.Delete(_partialPath);
                    else
                        File.Move(_partialPath, _cachePath);

                    lock (_lock) { _committed = true; }
                    OnComplete?.Invoke();
                    lock (_lock) { _isComplete = true; }
                    _logger?.LogInformation($"Download complete: {downloaded} bytes -> {_cachePath}");
                    return; // success, exit retry loop
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogInformation("Download cancelled");
                    CleanupPartial();
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Download attempt {attempt + 1} failed: {ex.Message}");
                    lock (_lock)
                    {
                        _error = ex;
                        // Close stream so retry can reopen it
                        _writeStream?.Dispose();
                        _writeStream = null;
                    }

                    if (attempt == MaxRetries)
                    {
                        _logger?.LogError($"Download failed after {MaxRetries + 1} attempts: {ex.Message}");
                        CleanupPartial();
                    }
                }
            }
        }

        private void CleanupPartial()
        {
            try
            {
                bool committed;
                lock (_lock) { committed = _committed; }
                if (!committed && File.Exists(_partialPath))
                    File.Delete(_partialPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            lock (_lock)
            {
                _writeStream?.Dispose();
                _writeStream = null;
            }
            var task = _downloadTask;
            if (task == null || task.IsCompleted)
                CleanupPartial();
            else
                _ = task.ContinueWith(_ => CleanupPartial(), TaskScheduler.Default);
            _cts?.Dispose();
        }

        /// <summary>获取缓存目录路径</summary>
        public static string GetCacheDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "chillpatcher_audio_cache");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>清理过期缓存文件</summary>
        /// <param name="maxAgeHours">最大保留时间 (小时)</param>
        public static void CleanupCache(double maxAgeHours = 24)
        {
            try
            {
                var dir = GetCacheDirectory();
                var cutoff = DateTime.UtcNow.AddHours(-maxAgeHours);

                foreach (var file in Directory.GetFiles(dir))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        try { File.Delete(file); }
                        catch { /* file in use, skip */ }
                    }
                }
            }
            catch { /* file in use, skip */ }
        }
    }
}

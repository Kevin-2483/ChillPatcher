using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ChillPatcher.Native;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// URL FLAC 加载器
    /// 实现边下边播功能：
    /// 1. 后台下载 FLAC 文件到缓存
    /// 2. 等待足够的缓冲数据后开始解码
    /// 3. 播放过程中监控缓冲状态
    /// 4. 缓冲不足时自动静音（不影响 UI 播放状态）
    /// </summary>
    public class UrlFlacLoader : IDisposable
    {
        #region 常量配置

        /// <summary>开始播放前需要的最小缓冲字节数（默认 32KB）</summary>
        public const int MIN_BUFFER_BEFORE_PLAY = 32 * 1024;

        /// <summary>播放中暂停阈值（低于此值暂停等待）</summary>
        public const int BUFFER_PAUSE_THRESHOLD = 8 * 1024;

        /// <summary>播放中恢复阈值（高于此值才恢复播放）</summary>
        public const int BUFFER_RESUME_THRESHOLD = 16 * 1024;

        /// <summary>等待缓冲的检查间隔（毫秒）</summary>
        private const int BUFFER_CHECK_INTERVAL_MS = 50;

        /// <summary>下载缓冲区大小</summary>
        private const int DOWNLOAD_BUFFER_SIZE = 8192;

        #endregion

        #region 字段

        private readonly string _url;
        private readonly string _cacheFilePath;
        private FileStream _writeStream;
        private FlacDecoder.FlacStreamReader _flacReader;
        
        private volatile bool _disposed;
        private volatile bool _downloadComplete;
        private volatile bool _downloadFailed;
        private volatile bool _isBuffering;  // 是否处于缓冲等待状态（滞后控制）
        private long _downloadedBytes;  // 使用 Interlocked 操作
        private volatile string _downloadError;

        private CancellationTokenSource _downloadCts;
        private Task _downloadTask;

        #endregion

        #region 属性

        /// <summary>已下载的字节数</summary>
        public long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);

        /// <summary>下载是否完成</summary>
        public bool IsDownloadComplete => _downloadComplete;

        /// <summary>下载是否失败</summary>
        public bool IsDownloadFailed => _downloadFailed;

        /// <summary>下载错误消息</summary>
        public string DownloadError => _downloadError;

        /// <summary>FLAC 流读取器</summary>
        public FlacDecoder.FlacStreamReader FlacReader => _flacReader;

        /// <summary>缓存文件路径</summary>
        public string CacheFilePath => _cacheFilePath;

        /// <summary>是否正在缓冲等待</summary>
        public bool IsBuffering => _isBuffering;

        /// <summary>是否已准备好播放</summary>
        public bool IsReadyToPlay => _flacReader != null && DownloadedBytes >= MIN_BUFFER_BEFORE_PLAY;

        /// <summary>
        /// 当前可用的缓冲字节数
        /// 计算方法：已下载字节 - 当前读取位置对应的字节
        /// </summary>
        public long AvailableBufferBytes
        {
            get
            {
                if (_flacReader == null) return 0;
                
                // 估算：假设平均每帧占用的字节数
                // FLAC 压缩比通常在 50-70%，这里用保守估计
                var bytesPerFrame = _flacReader.Channels * 2.5f; // 16bit stereo ≈ 4 bytes/frame, 压缩后约 2.5
                var currentBytePos = (long)(_flacReader.CurrentFrame * bytesPerFrame);
                var availableBytes = Interlocked.Read(ref _downloadedBytes) - currentBytePos;
                
                return Math.Max(0, availableBytes);
            }
        }

        /// <summary>
        /// 是否应该暂停等待缓冲（带滞后控制）
        /// 低于 BUFFER_PAUSE_THRESHOLD 时进入缓冲状态
        /// 高于 BUFFER_RESUME_THRESHOLD 时才恢复播放
        /// </summary>
        public bool ShouldWaitForBuffer
        {
            get
            {
                if (_downloadComplete) return false; // 下载完成，不需要等待
                
                var availableBytes = AvailableBufferBytes;
                
                // 滞后控制逻辑
                if (_isBuffering)
                {
                    // 已在缓冲状态，需要达到恢复阈值才能退出
                    return availableBytes < BUFFER_RESUME_THRESHOLD;
                }
                else
                {
                    // 正常播放状态，低于暂停阈值才进入缓冲
                    return availableBytes < BUFFER_PAUSE_THRESHOLD;
                }
            }
        }

        #endregion

        #region 构造与销毁

        public UrlFlacLoader(string url)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            
            // 创建临时缓存文件
            var cacheDir = Path.Combine(Path.GetTempPath(), "ChillPatcher", "flac_cache");
            Directory.CreateDirectory(cacheDir);
            _cacheFilePath = Path.Combine(cacheDir, $"stream_{Guid.NewGuid():N}.flac");
            
            Plugin.Log.LogDebug($"[UrlFlacLoader] Cache file: {_cacheFilePath}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // 取消下载
                _downloadCts?.Cancel();
                
                // 等待下载任务完成
                try { _downloadTask?.Wait(1000); } catch { }

                // 关闭 FLAC 读取器
                _flacReader?.Dispose();
                _flacReader = null;

                // 关闭写入流
                _writeStream?.Dispose();
                _writeStream = null;

                // 删除缓存文件
                try
                {
                    if (File.Exists(_cacheFilePath))
                    {
                        File.Delete(_cacheFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[UrlFlacLoader] Failed to delete cache: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[UrlFlacLoader] Dispose error: {ex.Message}");
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 开始加载 URL FLAC 文件
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否成功初始化</returns>
        public async Task<bool> StartLoadingAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UrlFlacLoader));

            try
            {
                Plugin.Log.LogInfo($"[UrlFlacLoader] Starting download: {_url}");

                // 创建写入流（允许共享读取）
                _writeStream = new FileStream(
                    _cacheFilePath, 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.Read,
                    DOWNLOAD_BUFFER_SIZE,
                    FileOptions.WriteThrough);

                // 启动后台下载
                _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _downloadTask = DownloadAsync(_downloadCts.Token);

                // 等待足够的缓冲数据
                Plugin.Log.LogInfo($"[UrlFlacLoader] Waiting for {MIN_BUFFER_BEFORE_PLAY} bytes buffer...");
                
                while (Interlocked.Read(ref _downloadedBytes) < MIN_BUFFER_BEFORE_PLAY)
                {
                    if (_downloadFailed)
                    {
                        Plugin.Log.LogError($"[UrlFlacLoader] Download failed: {_downloadError}");
                        return false;
                    }

                    if (_downloadComplete && Interlocked.Read(ref _downloadedBytes) < MIN_BUFFER_BEFORE_PLAY)
                    {
                        Plugin.Log.LogError("[UrlFlacLoader] Download complete but insufficient data");
                        return false;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(BUFFER_CHECK_INTERVAL_MS, cancellationToken);
                }

                var downloadedBytes = Interlocked.Read(ref _downloadedBytes);
                Plugin.Log.LogInfo($"[UrlFlacLoader] Buffer ready ({downloadedBytes} bytes), opening FLAC decoder...");

                // 打开 FLAC 解码器
                _flacReader = new FlacDecoder.FlacStreamReader(_cacheFilePath);

                Plugin.Log.LogInfo($"[UrlFlacLoader] ✅ Ready: {_flacReader.SampleRate}Hz, {_flacReader.Channels}ch, {_flacReader.TotalPcmFrames} frames");

                return true;
            }
            catch (OperationCanceledException)
            {
                Plugin.Log.LogDebug("[UrlFlacLoader] Loading cancelled");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[UrlFlacLoader] StartLoading failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 读取 PCM 帧（带缓冲检测和滞后控制）
        /// 如果缓冲不足，阻塞等待直到数据可用
        /// 使用滞后阈值：低于 8KB 暂停，高于 16KB 才恢复
        /// </summary>
        /// <param name="buffer">输出缓冲区</param>
        /// <param name="framesToRead">要读取的帧数</param>
        /// <returns>实际读取的帧数</returns>
        public long ReadFramesWithBuffering(float[] buffer, int framesToRead)
        {
            if (_flacReader == null || _disposed)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return framesToRead; // 返回请求的帧数但填充静音
            }

            // 检查是否需要等待缓冲（带滞后控制）
            if (ShouldWaitForBuffer)
            {
                // 进入缓冲状态
                if (!_isBuffering)
                {
                    _isBuffering = true;
                    Plugin.Log.LogDebug($"[UrlFlacLoader] 缓冲不足，开始等待... ({AvailableBufferBytes} bytes available)");
                }
                
                // 使用 SpinWait 阻塞等待（无超时，直到数据足够或失败/取消）
                var spinWait = new SpinWait();
                var startTime = Environment.TickCount;
                
                while (ShouldWaitForBuffer && !_disposed && !_downloadFailed)
                {
                    // 检查是否下载完成（下载完了就不用等了）
                    if (_downloadComplete)
                    {
                        break;
                    }
                    
                    // SpinWait 会在多次自旋后自动 yield
                    spinWait.SpinOnce();
                    
                    // 每隔一段时间让出 CPU
                    if (spinWait.NextSpinWillYield)
                    {
                        Thread.Sleep(10); // 短暂休眠，避免 CPU 空转
                    }
                }
                
                // 如果因为失败或销毁退出，返回静音
                if (_disposed || _downloadFailed)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    return framesToRead;
                }
                
                // 缓冲恢复，退出缓冲状态
                var waitTime = Environment.TickCount - startTime;
                _isBuffering = false;
                Plugin.Log.LogDebug($"[UrlFlacLoader] 缓冲恢复，等待了 {waitTime}ms ({AvailableBufferBytes} bytes available)");
            }

            // 正常读取
            try
            {
                return _flacReader.ReadFrames(buffer, (ulong)framesToRead);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[UrlFlacLoader] Read error: {ex.Message}");
                Array.Clear(buffer, 0, buffer.Length);
                return framesToRead;
            }
        }

        /// <summary>
        /// 定位到指定帧
        /// </summary>
        public bool Seek(ulong frameIndex)
        {
            return _flacReader?.Seek(frameIndex) ?? false;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 后台下载任务
        /// </summary>
        private async Task DownloadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var request = WebRequest.CreateHttp(_url);
                request.Method = "GET";
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                using (var response = await request.GetResponseAsync())
                using (var responseStream = response.GetResponseStream())
                {
                    var buffer = new byte[DOWNLOAD_BUFFER_SIZE];
                    int bytesRead;

                    while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await _writeStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        await _writeStream.FlushAsync(cancellationToken);
                        
                        Interlocked.Add(ref _downloadedBytes, bytesRead);
                    }
                }

                _downloadComplete = true;
                Plugin.Log.LogInfo($"[UrlFlacLoader] Download complete: {Interlocked.Read(ref _downloadedBytes)} bytes");
            }
            catch (OperationCanceledException)
            {
                Plugin.Log.LogDebug("[UrlFlacLoader] Download cancelled");
            }
            catch (Exception ex)
            {
                _downloadError = ex.Message;
                _downloadFailed = true;
                Plugin.Log.LogError($"[UrlFlacLoader] Download failed: {ex.Message}");
            }
        }

        #endregion
    }
}

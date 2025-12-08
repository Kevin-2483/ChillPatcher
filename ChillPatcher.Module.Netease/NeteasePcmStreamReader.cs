using System;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云 PCM 流读取器
    /// 实现 IPcmStreamReader 接口，用于流式播放
    /// </summary>
    public class NeteasePcmStreamReader : IPcmStreamReader
    {
        private readonly NeteaseBridge _bridge;
        private readonly long _streamId;
        private readonly PcmStreamInfo _info;
        private ulong _currentFrame;
        private bool _isEndOfStream;
        private bool _disposed;

        public PcmStreamInfo Info => _info;
        public ulong CurrentFrame => _currentFrame;
        public bool IsEndOfStream => _isEndOfStream;
        public bool IsReady => _bridge.IsPcmStreamReady(_streamId);

        /// <summary>
        /// 创建 PCM 流读取器
        /// </summary>
        /// <param name="bridge">网易云桥接</param>
        /// <param name="streamId">流 ID</param>
        /// <param name="sampleRate">采样率</param>
        /// <param name="channels">声道数</param>
        /// <param name="durationSeconds">歌曲时长（秒），用于计算预估总帧数</param>
        public NeteasePcmStreamReader(NeteaseBridge bridge, long streamId, int sampleRate, int channels, float durationSeconds = 0)
        {
            _bridge = bridge;
            _streamId = streamId;
            
            // 基于时长计算预估总帧数
            ulong estimatedFrames = durationSeconds > 0 
                ? (ulong)(sampleRate * durationSeconds) 
                : 0;
            
            _info = new PcmStreamInfo
            {
                SampleRate = sampleRate,
                Channels = channels,
                TotalFrames = estimatedFrames
            };
        }

        /// <summary>
        /// 等待流准备好
        /// </summary>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>是否准备好</returns>
        public bool WaitForReady(int timeoutMs = 5000)
        {
            var startTime = DateTime.Now;
            while (!IsReady)
            {
                if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
                    return false;
                System.Threading.Thread.Sleep(10);
            }

            // 更新流信息（采样率和声道数）
            var info = _bridge.GetPcmStreamInfo(_streamId);
            if (info != null)
            {
                // 保存原始预估的时长（基于 API 返回的歌曲时长）
                // 这个值是准确的，比 FLAC 头部的 NSamples 更可靠
                var estimatedDuration = _info.TotalFrames > 0 
                    ? (float)_info.TotalFrames / 44100 
                    : 0;
                
                _info.SampleRate = info.SampleRate;
                _info.Channels = info.Channels;
                _info.Format = info.Format;
                _info.CanSeek = info.CanSeek;
                
                // 始终优先使用基于 API 歌曲时长计算的预估帧数
                // 因为 FLAC 流式解码时头部可能不完整，导致 NSamples 值不正确
                if (estimatedDuration > 0)
                {
                    // 根据实际采样率重新计算帧数
                    _info.TotalFrames = (ulong)(info.SampleRate * estimatedDuration);
                }
                else if (info.TotalFrames > 0)
                {
                    // 如果没有预估值，才使用 Go 端返回的 TotalFrames
                    _info.TotalFrames = info.TotalFrames;
                }
            }

            return true;
        }

        public long ReadFrames(float[] buffer, int framesToRead)
        {
            if (_disposed || _isEndOfStream)
                return 0;

            var result = _bridge.ReadPcmFrames(_streamId, buffer, framesToRead);

            if (result == -2) // EOF
            {
                _isEndOfStream = true;
                return 0;
            }

            if (result == -1) // Error
            {
                _isEndOfStream = true;
                return -1;
            }

            if (result > 0)
            {
                _currentFrame += (ulong)result;
            }

            return result;
        }

        public bool Seek(ulong frameIndex)
        {
            if (_disposed)
                return false;

            // 尝试 Seek
            var result = _bridge.SeekPcmStream(_streamId, (long)frameIndex);
            
            if (result == 0)
            {
                // Seek 成功
                _currentFrame = frameIndex;
                _isEndOfStream = false;
                return true;
            }
            else if (result == -3)
            {
                // 延迟 Seek 已设置，更新当前帧位置（UI 显示用）
                _currentFrame = frameIndex;
                _isEndOfStream = false;
                return true; // 返回 true 让 UI 更新位置
            }
            else if (result == -2)
            {
                // 缓存还没下载完，不支持 Seek（旧行为，不应该再触发）
                return false;
            }
            
            // 其他错误
            return false;
        }

        /// <summary>
        /// 检查是否可以 Seek（缓存是否下载完成）
        /// </summary>
        public bool CanSeek => _bridge.CanSeekPcmStream(_streamId);

        /// <summary>
        /// 获取缓存下载进度（0-100）
        /// </summary>
        public double CacheProgress => _bridge.GetCacheProgress(_streamId);

        /// <summary>
        /// 缓存是否下载完成
        /// </summary>
        public bool IsCacheComplete => CacheProgress >= 100;

        /// <summary>
        /// 检查是否有待定的 Seek
        /// </summary>
        public bool HasPendingSeek => _bridge.HasPendingSeek(_streamId);

        /// <summary>
        /// 获取待定 Seek 的目标帧
        /// </summary>
        public long PendingSeekFrame => _bridge.GetPendingSeekFrame(_streamId);

        /// <summary>
        /// 取消待定的 Seek
        /// </summary>
        public void CancelPendingSeek() => _bridge.CancelPendingSeek(_streamId);

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _bridge.ClosePcmStream(_streamId);
            }
        }
    }
}

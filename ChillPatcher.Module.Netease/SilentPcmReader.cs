using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 静音 PCM 读取器 - 生成静音音频流
    /// 用于登录歌曲播放时的占位音频
    /// </summary>
    public class SilentPcmReader : IPcmStreamReader
    {
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly ulong _totalFrames;
        private ulong _currentFrame;
        private bool _isDisposed;

        public SilentPcmReader(int sampleRate, int channels, float durationSeconds)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _totalFrames = (ulong)(sampleRate * durationSeconds);
            _currentFrame = 0;
        }

        public PcmStreamInfo Info => new PcmStreamInfo
        {
            SampleRate = _sampleRate,
            Channels = _channels,
            TotalFrames = _totalFrames
        };

        public bool IsReady => true;

        public ulong CurrentFrame => _currentFrame;

        public bool IsEndOfStream => _currentFrame >= _totalFrames;

        public bool CanSeek => true;

        public double CacheProgress => 100.0; // 静音不需要缓存

        public bool IsCacheComplete => true;

        public bool HasPendingSeek => false;

        public long PendingSeekFrame => -1;

        public long ReadFrames(float[] buffer, int framesToRead)
        {
            if (_isDisposed || _currentFrame >= _totalFrames)
                return 0;

            ulong remainingFrames = _totalFrames - _currentFrame;
            int actualFrames = (int)System.Math.Min((ulong)framesToRead, remainingFrames);
            int samplesToWrite = actualFrames * _channels;

            // 填充静音 (0.0f)
            for (int i = 0; i < samplesToWrite; i++)
            {
                buffer[i] = 0.0f;
            }

            _currentFrame += (ulong)actualFrames;
            return actualFrames;
        }

        public bool Seek(ulong frameIndex)
        {
            if (frameIndex > _totalFrames)
                frameIndex = _totalFrames;

            _currentFrame = frameIndex;
            return true;
        }

        public void CancelPendingSeek()
        {
            // 静音流没有待定的 Seek
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}

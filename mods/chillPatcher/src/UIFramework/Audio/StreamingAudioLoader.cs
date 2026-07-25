using System;
using System.Threading;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.SDK.Ipc;
using ChillPatcher.SDK.Native;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 流媒体音频加载器 (IPC 版本)
    /// 通过 SharedMemoryReader 从 OmniMixPlayer 后端读取 PCM 数据
    /// </summary>
    public static class StreamingAudioLoader
    {
        public static bool IsStreamingSource(GameAudioInfo audioInfo)
        {
            if (audioInfo == null) return false;
            // OmniMix streaming songs use PathType=LocalPc with empty LocalPath.
            // Game-native songs use PathType=Normal with empty LocalPath — must NOT
            // be treated as streaming, otherwise onPlayMusic will call Play(uuid)
            // instead of StopBackendPlayback(), breaking song transitions.
            return audioInfo.PathType == AudioMode.LocalPc
                && !string.IsNullOrEmpty(audioInfo.UUID)
                && string.IsNullOrEmpty(audioInfo.LocalPath);
        }

        public static async Task<AudioClip> LoadFromSharedMemoryAsync(
            string uuid,
            int sampleRate = 44100,
            int channels = 2,
            CancellationToken cancellationToken = default)
        {
            var shm = OmniMixIntegration.Instance?.SharedMemory;
            if (shm == null)
            {
                Plugin.Log.LogWarning("[StreamingLoader] Shared memory not available");
                return null;
            }

            sampleRate = shm.Info.SampleRate > 0 ? shm.Info.SampleRate : sampleRate;
            channels = shm.Info.Channels > 0 ? shm.Info.Channels : channels;

            var ready = await WaitForStreamReadyAsync(shm, uuid, cancellationToken);
            if (!ready)
            {
                Plugin.Log.LogWarning($"[StreamingLoader] Shared memory stream not ready for {uuid}");
                return null;
            }

            var reader = new SharedMemoryPcmStreamReader(shm, uuid);
            var info = reader.Info;
            sampleRate = info.SampleRate > 0 ? info.SampleRate : sampleRate;
            channels = info.Channels > 0 ? info.Channels : channels;

            // Get dynamic track duration from OmniMixIntegration
            float duration = info.Duration > 0 ? info.Duration : (OmniMixIntegration.Instance != null ? OmniMixIntegration.Instance.CurrentTrackDuration : 0f);
            if (duration <= 0)
            {
                duration = 1800f; // 30 minutes fallback
            }

            // Create clip with duration plus a 5-second safety margin
            const double maxClipDurationSeconds = 12d * 60d * 60d;
            double clipDuration = Math.Min((double)duration + 5d, maxClipDurationSeconds);
            int bufferFrames = (int)Math.Min(sampleRate * clipDuration, int.MaxValue - 1024d);
            Plugin.Log.LogInfo($"[StreamingLoader] Creating streaming AudioClip for {uuid} with duration {duration:F1}s (bufferFrames: {bufferFrames})");

            var clip = AudioClip.Create(
                $"pcm_stream_{uuid}",
                bufferFrames,
                channels,
                sampleRate,
                true,
                (data) => OnPcmDataRead(data, reader),
                (pos) => OnPcmSetPosition(reader, pos));

            AudioResourceManager.Instance?.RegisterPcmStreamReader(uuid, clip, reader);
            Patches.UIFramework.MusicService_SetProgress_Patch.SetActivePcmReader(reader);

            // 等待共享内存缓冲区有足够数据后再返回 clip，
            // 避免 Unity 音频线程刚开始拉数据时缓冲区为空导致丢帧/静音
            await WaitForBufferReadyAsync(shm, sampleRate, cancellationToken);
            return clip;
        }

        /// <summary>
        /// 等待共享内存缓冲区中累积足够 PCM 数据后再返回 clip。
        /// 缓冲区大小 = max(dspBufferSize * 4, sampleRate * 0.1)，保证前 4 次 Unity 音频回调不会丢帧。
        /// </summary>
        private static async UniTask WaitForBufferReadyAsync(
            OmniPcmShared shm,
            int sampleRate,
            CancellationToken cancellationToken)
        {
            // 动态计算最小缓冲帧数：4 倍 DSP 缓冲大小（覆盖前几次回调），最低 100ms
            int dspBufferSize = 1024;
            try { dspBufferSize = AudioSettings.GetConfiguration().dspBufferSize; } catch { }
            long minBufferedFrames = Math.Max(dspBufferSize * 4L, sampleRate / 10);
            var deadline = Time.realtimeSinceStartup + 10f;

            while (!cancellationToken.IsCancellationRequested && Time.realtimeSinceStartup < deadline)
            {
                var snap = shm.Snapshot;
                long availableFrames = snap.WriteCursor - snap.ReadCursor;

                if (availableFrames >= minBufferedFrames)
                {
                    Plugin.Log.LogInfo(
                        $"[StreamingLoader] Buffer ready: {availableFrames} frames available (min={minBufferedFrames})");
                    return;
                }

                // 如果 decoder 已经 EOF 且所有数据都写完（WriteCursor 不再增长），就直接返回
                if (snap.FinalWriteCursor > 0 && snap.WriteCursor >= snap.FinalWriteCursor)
                {
                    Plugin.Log.LogInfo(
                        $"[StreamingLoader] Buffer final: {availableFrames} frames (EOF reached, final write cursor={snap.FinalWriteCursor})");
                    return;
                }

                await UniTask.Delay(25, cancellationToken: cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
                Plugin.Log.LogWarning("[StreamingLoader] Buffer wait cancelled");
            else
                Plugin.Log.LogWarning("[StreamingLoader] Buffer wait timed out, returning clip anyway");
        }

        private static async Task<bool> WaitForStreamReadyAsync(
            OmniPcmShared reader,
            string targetUuid,
            CancellationToken cancellationToken)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            reader.BindStream(targetUuid);
            while (!cancellationToken.IsCancellationRequested && Time.realtimeSinceStartup < deadline)
            {
                if (reader.CurrentUuid == targetUuid && reader.IsFormatReady())
                    return true;

                // If the server aborted playback (e.g. track failed to resolve),
                // stop waiting immediately instead of blocking for 10 s.
                if (reader.HasError())
                {
                    Plugin.Log.LogWarning(
                        $"[StreamingLoader] Server signaled error for {targetUuid}, aborting wait");
                    return false;
                }

                await UniTask.Delay(25, cancellationToken: cancellationToken);
            }
            return false;
        }

        private static void OnPcmDataRead(float[] data, SharedMemoryPcmStreamReader reader)
        {
            try
            {
                if (reader == null)
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                int channels = reader.Info.Channels > 0 ? reader.Info.Channels : 2;
                int framesToRead = data.Length / channels;
                long read = reader.ReadFrames(data, framesToRead);

                if (read < framesToRead)
                {
                    int samplesRead = (int)Math.Max(0, read) * channels;
                    if (samplesRead < data.Length)
                    {
                        Array.Clear(data, samplesRead, data.Length - samplesRead);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[StreamingLoader] PCM read error: {ex.Message}");
                Array.Clear(data, 0, data.Length);
            }
        }

        private static void OnPcmSetPosition(SharedMemoryPcmStreamReader reader, int position)
        {
            if (reader == null) return;
            if (Patches.UIFramework.MusicService_SetProgress_Patch.IsSeekingFromSetProgress)
                return;
            if (position == 0 && reader.CurrentFrame == 0)
                return;
            reader.Seek((ulong)Math.Max(0, position));
        }

        public static bool IsModuleStreamingEnabled(string moduleId)
        {
            // Always enabled when IPC connected
            return OmniMixIntegration.Instance?.IsConnected ?? false;
        }

        public static async Task<AudioClip> SmartLoadAsync(
            GameAudioInfo audioInfo,
            CancellationToken cancellationToken = default)
        {
            if (audioInfo == null) return null;

            // 如果已经有 AudioClip，直接返回
            if (audioInfo.AudioClip != null)
            {
                return audioInfo.AudioClip;
            }

            // 流媒体源
            if (IsStreamingSource(audioInfo))
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Streaming: {audioInfo.AudioClipName}");
                var clip = await LoadFromSharedMemoryAsync(audioInfo.UUID, cancellationToken: cancellationToken);
                if (clip != null)
                {
                    audioInfo.AudioClip = clip;
                }
                return clip;
            }

            // 本地文件源 - 使用原有的 GetAudioClip 方法
            if (audioInfo.PathType == AudioMode.LocalPc && !string.IsNullOrEmpty(audioInfo.LocalPath))
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Local file: {audioInfo.AudioClipName}");
                return await audioInfo.GetAudioClip(cancellationToken);
            }

            // 游戏原生音频
            if (audioInfo.AudioClip != null || audioInfo.PathType == AudioMode.Normal)
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Game audio: {audioInfo.AudioClipName}");
                return audioInfo.AudioClip;
            }

            Plugin.Log.LogWarning($"[StreamingAudioLoader] Unknown source type for: {audioInfo.AudioClipName}");
            return null;
        }
    }
}

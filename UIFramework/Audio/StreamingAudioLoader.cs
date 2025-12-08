using System;
using System.Threading;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.ModuleSystem;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 流媒体音频加载器
    /// 处理流媒体模块的音频加载逻辑
    /// </summary>
    public static class StreamingAudioLoader
    {
        /// <summary>
        /// 检查歌曲是否是流媒体源
        /// </summary>
        public static bool IsStreamingSource(GameAudioInfo audioInfo)
        {
            if (audioInfo == null) return false;
            
            var music = MusicRegistry.Instance?.GetMusic(audioInfo.UUID);
            if (music == null) return false;
            
            return music.SourceType == MusicSourceType.Stream || 
                   music.SourceType == MusicSourceType.Url;
        }

        /// <summary>
        /// 检查模块是否支持流媒体
        /// </summary>
        public static bool IsModuleStreamingEnabled(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            
            // 流媒体模块应该实现 IStreamingMusicSourceProvider
            var provider = ModuleLoader.Instance?.GetProvider<IStreamingMusicSourceProvider>(moduleId);
            return provider != null;
        }

        /// <summary>
        /// 获取流媒体提供器
        /// </summary>
        public static IPlayableSourceResolver GetStreamingResolver(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;
            return ModuleLoader.Instance?.GetProvider<IPlayableSourceResolver>(moduleId);
        }

        /// <summary>
        /// 从流媒体源加载 AudioClip
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载的 AudioClip</returns>
        public static async Task<AudioClip> LoadFromStreamingAsync(
            string uuid, 
            CancellationToken cancellationToken = default)
        {
            var music = MusicRegistry.Instance?.GetMusic(uuid);
            if (music == null)
            {
                Plugin.Log.LogWarning($"[StreamingAudioLoader] Music not found: {uuid}");
                return null;
            }

            var resolver = GetStreamingResolver(music.ModuleId);
            if (resolver == null)
            {
                Plugin.Log.LogWarning($"[StreamingAudioLoader] Streaming resolver not found for module: {music.ModuleId}");
                return null;
            }

            try
            {
                // 解析可播放源
                var source = await resolver.ResolveAsync(
                    uuid, 
                    AudioQuality.ExHigh, 
                    cancellationToken);

                if (source == null)
                {
                    Plugin.Log.LogError($"[StreamingAudioLoader] Failed to resolve playable source for: {uuid}");
                    return null;
                }

                // 检查 URL 过期
                if (source.IsRemote && source.IsExpired)
                {
                    Plugin.Log.LogInfo($"[StreamingAudioLoader] URL expired, refreshing: {uuid}");
                    source = await resolver.RefreshUrlAsync(
                        uuid, 
                        AudioQuality.ExHigh, 
                        cancellationToken);
                }

                if (source == null)
                {
                    Plugin.Log.LogError($"[StreamingAudioLoader] Failed to refresh URL for: {uuid}");
                    return null;
                }

                Plugin.Log.LogInfo($"[StreamingAudioLoader] Loading from {source.SourceType}, Format: {source.Format}");

                // 根据来源类型加载
                switch (source.SourceType)
                {
                    case PlayableSourceType.PcmStream:
                        // PCM 流式播放 - 模块提供解码后的数据
                        return CreateAudioClipFromPcmStream(source);

                    case PlayableSourceType.Remote:
                        // 远程 URL
                        var coreLoader = ModuleSystem.Services.CoreAudioLoader.Instance;
                        
                        // 检查是否是 FLAC URL（需要边下边播）
                        if (source.Format == AudioFormat.Flac || coreLoader.IsFlacUrl(source.Url))
                        {
                            Plugin.Log.LogInfo($"[StreamingAudioLoader] Using URL FLAC loader for: {uuid}");
                            var (clip, loader) = await coreLoader.LoadFromUrlFlacAsync(
                                source.Url, 
                                uuid, 
                                cancellationToken);
                            
                            if (clip != null && loader != null)
                            {
                                // 注册到资源管理器
                                AudioResourceManager.Instance?.RegisterUrlFlacLoader(uuid, clip, loader);
                            }
                            return clip;
                        }
                        
                        // 其他格式 - Unity 原生加载
                        return await coreLoader.LoadFromUrlAsync(source.Url, cancellationToken);

                    case PlayableSourceType.Local:
                    case PlayableSourceType.Cached:
                    default:
                        // 本地/缓存文件
                        return await ModuleSystem.Services.CoreAudioLoader.Instance
                            .LoadFromFileAsync(source.LocalPath);
                }
            }
            catch (OperationCanceledException)
            {
                Plugin.Log.LogDebug($"[StreamingAudioLoader] Load cancelled: {uuid}");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StreamingAudioLoader] Load failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 PCM 流创建 AudioClip
        /// 使用 Unity 的 PCMReaderCallback 实现流式播放
        /// </summary>
        private static AudioClip CreateAudioClipFromPcmStream(PlayableSource source)
        {
            if (source?.PcmReader == null)
            {
                Plugin.Log.LogError("[StreamingAudioLoader] PCM reader is null");
                return null;
            }

            var reader = source.PcmReader;
            var info = reader.Info;

            if (info.SampleRate <= 0 || info.Channels <= 0)
            {
                Plugin.Log.LogError($"[StreamingAudioLoader] Invalid PCM info: {info.SampleRate}Hz, {info.Channels}ch");
                return null;
            }

            // 计算 AudioClip 长度
            int lengthSamples;
            bool isStreaming;

            if (info.TotalFrames > 0)
            {
                // 已知长度 - 创建固定大小的 clip
                lengthSamples = (int)info.TotalFrames;
                isStreaming = true; // 仍然使用流式读取
            }
            else
            {
                // 未知长度 - 使用较大的缓冲区
                lengthSamples = info.SampleRate * 600; // 10 分钟缓冲
                isStreaming = true;
            }

            Plugin.Log.LogInfo($"[StreamingAudioLoader] Creating PCM stream clip: " +
                $"{info.SampleRate}Hz, {info.Channels}ch, {lengthSamples} samples, streaming={isStreaming}");

            try
            {
                // 创建带 PCMReaderCallback 的 AudioClip
                var clip = AudioClip.Create(
                    $"pcm_stream_{source.UUID}",
                    lengthSamples,
                    info.Channels,
                    info.SampleRate,
                    isStreaming,
                    (data) => OnPcmReaderCallback(reader, data),
                    (position) => OnPcmSetPositionCallback(reader, position)
                );

                if (clip == null)
                {
                    Plugin.Log.LogError("[StreamingAudioLoader] Failed to create AudioClip");
                    return null;
                }

                // 注册到 AudioResourceManager 以便后续清理
                AudioResourceManager.Instance?.RegisterPcmStreamReader(source.UUID, clip, reader);
                
                // 设置活跃的 PCM 读取器（用于 Seek 操作）
                Patches.UIFramework.MusicService_SetProgress_Patch.SetActivePcmReader(reader);

                Plugin.Log.LogInfo($"[StreamingAudioLoader] ✅ PCM stream clip created: {clip.name}");
                return clip;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StreamingAudioLoader] Failed to create PCM stream clip: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PCM 数据读取回调
        /// Unity 会在需要音频数据时调用此方法
        /// </summary>
        private static void OnPcmReaderCallback(IPcmStreamReader reader, float[] data)
        {
            if (reader == null || reader.IsEndOfStream)
            {
                // 填充静音
                Array.Clear(data, 0, data.Length);
                return;
            }

            try
            {
                int framesToRead = data.Length / reader.Info.Channels;
                long framesRead = reader.ReadFrames(data, framesToRead);

                // 如果读取的帧数少于请求的，填充静音
                if (framesRead < framesToRead)
                {
                    int samplesRead = (int)(framesRead * reader.Info.Channels);
                    Array.Clear(data, samplesRead, data.Length - samplesRead);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StreamingAudioLoader] PCM read error: {ex.Message}");
                Array.Clear(data, 0, data.Length);
            }
        }

        /// <summary>
        /// PCM 位置设置回调
        /// Unity 在 Seek 时调用此方法
        /// </summary>
        private static void OnPcmSetPositionCallback(IPcmStreamReader reader, int position)
        {
            if (reader == null) return;

            // 如果是从 SetProgress 发起的 Seek，跳过这个回调
            // 因为 SetProgress 已经调用过 reader.Seek() 了
            if (Patches.UIFramework.MusicService_SetProgress_Patch.IsSeekingFromSetProgress)
            {
                return;
            }

            try
            {
                reader.Seek((ulong)position);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[StreamingAudioLoader] PCM seek error: {ex.Message}");
            }
        }

        /// <summary>
        /// 智能加载音频 - 根据音源类型自动选择加载方式
        /// </summary>
        /// <param name="audioInfo">游戏音频信息</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>加载的 AudioClip</returns>
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

            var music = MusicRegistry.Instance?.GetMusic(audioInfo.UUID);
            
            // 流媒体源
            if (music != null && (music.SourceType == MusicSourceType.Stream || 
                                   music.SourceType == MusicSourceType.Url))
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Streaming: {audioInfo.Title}");
                var clip = await LoadFromStreamingAsync(audioInfo.UUID, cancellationToken);
                if (clip != null)
                {
                    audioInfo.AudioClip = clip;
                }
                return clip;
            }

            // 本地文件源 - 使用原有的 GetAudioClip 方法
            if (audioInfo.PathType == AudioMode.LocalPc && !string.IsNullOrEmpty(audioInfo.LocalPath))
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Local file: {audioInfo.Title}");
                return await audioInfo.GetAudioClip(cancellationToken);
            }

            // 游戏原生音频
            if (audioInfo.AudioClip != null || audioInfo.PathType == AudioMode.Normal)
            {
                Plugin.Log.LogInfo($"[StreamingAudioLoader] Smart load - Game audio: {audioInfo.Title}");
                return audioInfo.AudioClip;
            }

            Plugin.Log.LogWarning($"[StreamingAudioLoader] Unknown source type for: {audioInfo.Title}");
            return null;
        }
    }
}

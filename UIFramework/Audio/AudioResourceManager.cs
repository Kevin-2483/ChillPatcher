using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ChillPatcher.Native;
using ChillPatcher.SDK.Interfaces;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 音频资源管理器
    /// 负责跟踪和清理流式 AudioClip 及其关联资源（如 FlacStreamReader, IPcmStreamReader, UrlFlacLoader）
    /// </summary>
    public class AudioResourceManager
    {
        private static AudioResourceManager _instance;
        public static AudioResourceManager Instance => _instance ??= new AudioResourceManager();

        private readonly ManualLogSource _logger;
        
        /// <summary>
        /// 跟踪 AudioClip 和其关联的 FlacStreamReader（本地 FLAC）
        /// </summary>
        private readonly Dictionary<int, FlacDecoder.FlacStreamReader> _flacStreamReaders = new Dictionary<int, FlacDecoder.FlacStreamReader>();

        /// <summary>
        /// 跟踪 AudioClip 和其关联的 IPcmStreamReader（模块提供的 PCM 流）
        /// </summary>
        private readonly Dictionary<int, IPcmStreamReader> _pcmStreamReaders = new Dictionary<int, IPcmStreamReader>();

        /// <summary>
        /// 跟踪 AudioClip 和其关联的 UrlFlacLoader（URL FLAC 边下边播）
        /// </summary>
        private readonly Dictionary<int, UrlFlacLoader> _urlFlacLoaders = new Dictionary<int, UrlFlacLoader>();
        
        /// <summary>
        /// 跟踪 UUID 和其关联的 AudioClip InstanceID
        /// </summary>
        private readonly Dictionary<string, int> _uuidToClipId = new Dictionary<string, int>();

        private AudioResourceManager()
        {
            _logger = BepInEx.Logging.Logger.CreateLogSource("AudioResourceManager");
        }

        /// <summary>
        /// 注册流式 AudioClip 及其关联的 FlacStreamReader（本地 FLAC 文件）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="clip">AudioClip</param>
        /// <param name="streamReader">FlacStreamReader</param>
        public void RegisterStreamingClip(string uuid, AudioClip clip, FlacDecoder.FlacStreamReader streamReader)
        {
            if (clip == null) return;

            var clipId = clip.GetInstanceID();
            
            _flacStreamReaders[clipId] = streamReader;
            
            if (!string.IsNullOrEmpty(uuid))
            {
                // 如果这个 UUID 已经有关联的 clip，先清理旧的
                if (_uuidToClipId.TryGetValue(uuid, out var oldClipId) && oldClipId != clipId)
                {
                    CleanupClipInternal(oldClipId);
                }
                _uuidToClipId[uuid] = clipId;
            }

            _logger.LogDebug($"Registered FLAC streaming clip: {uuid} (ClipID: {clipId})");
        }

        /// <summary>
        /// 注册 PCM 流式 AudioClip（模块提供的解码流）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="clip">AudioClip</param>
        /// <param name="pcmReader">模块提供的 PCM 读取器</param>
        public void RegisterPcmStreamReader(string uuid, AudioClip clip, IPcmStreamReader pcmReader)
        {
            if (clip == null) return;

            var clipId = clip.GetInstanceID();
            
            _pcmStreamReaders[clipId] = pcmReader;
            
            if (!string.IsNullOrEmpty(uuid))
            {
                // 如果这个 UUID 已经有关联的 clip，先清理旧的
                if (_uuidToClipId.TryGetValue(uuid, out var oldClipId) && oldClipId != clipId)
                {
                    CleanupClipInternal(oldClipId);
                }
                _uuidToClipId[uuid] = clipId;
            }

            _logger.LogDebug($"Registered PCM stream clip: {uuid} (ClipID: {clipId})");
        }

        /// <summary>
        /// 注册 URL FLAC 加载器（边下边播）
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        /// <param name="clip">AudioClip</param>
        /// <param name="loader">UrlFlacLoader</param>
        public void RegisterUrlFlacLoader(string uuid, AudioClip clip, UrlFlacLoader loader)
        {
            if (clip == null) return;

            var clipId = clip.GetInstanceID();
            
            _urlFlacLoaders[clipId] = loader;
            
            if (!string.IsNullOrEmpty(uuid))
            {
                // 如果这个 UUID 已经有关联的 clip，先清理旧的
                if (_uuidToClipId.TryGetValue(uuid, out var oldClipId) && oldClipId != clipId)
                {
                    CleanupClipInternal(oldClipId);
                }
                _uuidToClipId[uuid] = clipId;
            }

            _logger.LogDebug($"Registered URL FLAC loader: {uuid} (ClipID: {clipId})");
        }

        /// <summary>
        /// 清理指定歌曲的音频资源
        /// </summary>
        /// <param name="uuid">歌曲 UUID</param>
        public void CleanupByUUID(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;

            if (_uuidToClipId.TryGetValue(uuid, out var clipId))
            {
                CleanupClipInternal(clipId);
                _uuidToClipId.Remove(uuid);
                _logger.LogDebug($"Cleaned up audio resources for: {uuid}");
            }
        }

        /// <summary>
        /// 清理指定 AudioClip 的资源
        /// </summary>
        /// <param name="clip">AudioClip</param>
        public void CleanupClip(AudioClip clip)
        {
            if (clip == null) return;
            CleanupClipInternal(clip.GetInstanceID());
        }

        /// <summary>
        /// 内部清理方法
        /// </summary>
        private void CleanupClipInternal(int clipId)
        {
            // 清理 FLAC StreamReader
            if (_flacStreamReaders.TryGetValue(clipId, out var flacReader))
            {
                try
                {
                    flacReader?.Dispose();
                    _logger.LogDebug($"Disposed FlacStreamReader for ClipID: {clipId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing FlacStreamReader: {ex.Message}");
                }
                _flacStreamReaders.Remove(clipId);
            }

            // 清理 PCM StreamReader
            if (_pcmStreamReaders.TryGetValue(clipId, out var pcmReader))
            {
                try
                {
                    pcmReader?.Dispose();
                    _logger.LogDebug($"Disposed IPcmStreamReader for ClipID: {clipId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing IPcmStreamReader: {ex.Message}");
                }
                _pcmStreamReaders.Remove(clipId);
            }

            // 清理 URL FLAC Loader
            if (_urlFlacLoaders.TryGetValue(clipId, out var urlFlacLoader))
            {
                try
                {
                    urlFlacLoader?.Dispose();
                    _logger.LogDebug($"Disposed UrlFlacLoader for ClipID: {clipId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing UrlFlacLoader: {ex.Message}");
                }
                _urlFlacLoaders.Remove(clipId);
            }
        }

        /// <summary>
        /// 清理所有资源
        /// </summary>
        public void CleanupAll()
        {
            foreach (var reader in _flacStreamReaders.Values)
            {
                try
                {
                    reader?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing FlacStreamReader: {ex.Message}");
                }
            }
            _flacStreamReaders.Clear();

            foreach (var reader in _pcmStreamReaders.Values)
            {
                try
                {
                    reader?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing IPcmStreamReader: {ex.Message}");
                }
            }
            _pcmStreamReaders.Clear();

            foreach (var loader in _urlFlacLoaders.Values)
            {
                try
                {
                    loader?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error disposing UrlFlacLoader: {ex.Message}");
                }
            }
            _urlFlacLoaders.Clear();

            _uuidToClipId.Clear();
            _logger.LogInfo("Cleaned up all audio resources");
        }

        /// <summary>
        /// 获取当前跟踪的资源数量
        /// </summary>
        public int TrackedCount => _flacStreamReaders.Count + _pcmStreamReaders.Count + _urlFlacLoaders.Count;
    }
}

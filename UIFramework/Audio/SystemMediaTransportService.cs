using System;
using System.IO;
using BepInEx.Logging;
using Bulbul;
using ChillPatcher.Native;
using ChillPatcher.UIFramework.Music;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 系统媒体传输控制 (SMTC) 服务
    /// 将游戏的音乐播放状态同步到 Windows 系统媒体控制
    /// </summary>
    public class SystemMediaTransportService : IDisposable
    {
        private static SystemMediaTransportService _instance;
        public static SystemMediaTransportService Instance => _instance ??= new SystemMediaTransportService();

        private readonly ManualLogSource _log;
        private bool _initialized;
        private bool _disposed;
        
        // 当前播放信息缓存
        private string _currentTitle;
        private string _currentArtist;
        private string _currentAlbum;
        private bool _isPlaying;

        // 游戏服务引用
        private MusicService _musicService;
        private Bulbul.FacilityMusic _facilityMusic;

        private SystemMediaTransportService()
        {
            _log = BepInEx.Logging.Logger.CreateLogSource("SmtcService");
        }

        /// <summary>
        /// 初始化 SMTC 服务
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            
            if (!PluginConfig.EnableSystemMediaTransport.Value)
            {
                _log.LogInfo("系统媒体控制功能已禁用");
                return;
            }

            try
            {
                // 初始化原生桥接
                if (!SmtcBridge.Initialize())
                {
                    _log.LogWarning("SMTC 初始化失败，可能缺少 ChillSmtcBridge.dll");
                    return;
                }

                // 注册按钮事件
                SmtcBridge.OnButtonPressed += OnButtonPressed;
                
                // 设置媒体类型
                SmtcBridge.SetMediaType(SmtcBridge.MediaType.Music);
                
                _initialized = true;
                _log.LogInfo("SMTC 服务已初始化");
            }
            catch (Exception ex)
            {
                _log.LogError($"SMTC 服务初始化异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置游戏服务引用
        /// </summary>
        public void SetGameServices(MusicService musicService, Bulbul.FacilityMusic facilityMusic)
        {
            _musicService = musicService;
            _facilityMusic = facilityMusic;
        }

        /// <summary>
        /// 更新媒体信息
        /// </summary>
        public void UpdateMediaInfo(string title, string artist, string album)
        {
            if (!_initialized) return;

            _currentTitle = title ?? "";
            _currentArtist = artist ?? "";
            _currentAlbum = album ?? "";

            SmtcBridge.SetMusicInfo(_currentTitle, _currentArtist, _currentAlbum);
            SmtcBridge.UpdateDisplay();
            
            _log.LogDebug($"更新媒体信息: {_currentTitle} - {_currentArtist}");
        }

        /// <summary>
        /// 更新媒体信息（从 GameAudioInfo）
        /// </summary>
        public void UpdateMediaInfo(GameAudioInfo audioInfo)
        {
            if (audioInfo == null) return;

            string title = audioInfo.Title ?? audioInfo.AudioClipName;
            string artist = audioInfo.Credit ?? "Unknown Artist";
            
            // 尝试从 AlbumManager 获取专辑名称
            string album = GetAlbumName(audioInfo);

            UpdateMediaInfo(title, artist, album);

            // 尝试设置封面
            TrySetThumbnail(audioInfo);
        }
        
        /// <summary>
        /// 获取歌曲的专辑名称
        /// </summary>
        private string GetAlbumName(GameAudioInfo audioInfo)
        {
            try
            {
                // 首先尝试从本地文件的 TagLib 读取专辑信息
                if (!string.IsNullOrEmpty(audioInfo.LocalPath) && System.IO.File.Exists(audioInfo.LocalPath))
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(audioInfo.LocalPath);
                        if (!string.IsNullOrEmpty(tagFile.Tag.Album))
                        {
                            return tagFile.Tag.Album;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug($"无法从文件读取专辑信息: {ex.Message}");
                    }
                }
                
                // 然后尝试从 AlbumManager 获取
                if (!string.IsNullOrEmpty(audioInfo.UUID))
                {
                    var albumManager = AlbumManager.Instance;
                    if (albumManager != null)
                    {
                        var albumId = albumManager.GetAlbumIdBySong(audioInfo.UUID);
                        if (!string.IsNullOrEmpty(albumId))
                        {
                            var albumInfo = albumManager.GetAlbum(albumId);
                            if (albumInfo != null && !string.IsNullOrEmpty(albumInfo.DisplayName))
                            {
                                return albumInfo.DisplayName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning($"获取专辑名称失败: {ex.Message}");
            }
            
            // 默认返回游戏名
            return "Chill With You";
        }

        /// <summary>
        /// 尝试设置封面图片
        /// </summary>
        private void TrySetThumbnail(GameAudioInfo audioInfo)
        {
            try
            {
                bool thumbnailSet = false;
                
                // 1. 如果有文件路径，尝试从文件读取内嵌封面
                if (!string.IsNullOrEmpty(audioInfo.LocalPath) && File.Exists(audioInfo.LocalPath))
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(audioInfo.LocalPath);
                        var pictures = tagFile.Tag.Pictures;
                        if (pictures != null && pictures.Length > 0)
                        {
                            var picture = pictures[0];
                            string mimeType = picture.MimeType ?? "image/jpeg";
                            if (SmtcBridge.SetThumbnailFromMemory(picture.Data.Data, mimeType))
                            {
                                thumbnailSet = true;
                                _log.LogDebug($"从文件内嵌封面设置缩略图成功");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug($"读取内嵌封面失败: {ex.Message}");
                    }
                    
                    // 2. 如果没有内嵌封面，尝试查找同目录下的封面文件
                    if (!thumbnailSet)
                    {
                        var coverPath = TryFindCoverFile(audioInfo.LocalPath);
                        if (!string.IsNullOrEmpty(coverPath))
                        {
                            var coverData = File.ReadAllBytes(coverPath);
                            var coverMime = GetMimeType(coverPath);
                            if (SmtcBridge.SetThumbnailFromMemory(coverData, coverMime))
                            {
                                thumbnailSet = true;
                                _log.LogDebug($"从封面文件设置缩略图成功: {coverPath}");
                            }
                        }
                    }
                }

                // 3. 如果是游戏原生歌曲，使用游戏内置封面
                if (!thumbnailSet && audioInfo.PathType == AudioMode.Normal && audioInfo.Tag != AudioTag.Local)
                {
                    thumbnailSet = TrySetGameCover((int)audioInfo.Tag);
                }

                // 4. 如果还是没有封面，使用默认封面
                if (!thumbnailSet)
                {
                    thumbnailSet = TrySetDefaultCover(audioInfo.Tag == AudioTag.Local);
                }
                
                SmtcBridge.UpdateDisplay();
            }
            catch (Exception ex)
            {
                _log.LogWarning($"设置封面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试设置游戏内置封面
        /// </summary>
        private bool TrySetGameCover(int audioTag)
        {
            try
            {
                // 根据 AudioTag 确定资源名称
                // Original=1 -> gamecover1.jpg
                // Special=2 -> gamecover2.jpg
                // Other=4 -> gamecover3.png
                string resourceName = audioTag switch
                {
                    1 => "ChillPatcher.Resources.gamecover1.jpg", // Original
                    2 => "ChillPatcher.Resources.gamecover2.jpg", // Special
                    4 => "ChillPatcher.Resources.gamecover3.png", // Other
                    _ => null
                };

                if (resourceName == null)
                    return false;

                var coverData = LoadEmbeddedResourceBytes(resourceName);
                if (coverData != null && coverData.Length > 0)
                {
                    string mimeType = resourceName.EndsWith(".png") ? "image/png" : "image/jpeg";
                    if (SmtcBridge.SetThumbnailFromMemory(coverData, mimeType))
                    {
                        _log.LogDebug($"从游戏内置封面设置缩略图成功: tag={audioTag}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug($"设置游戏封面失败: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 尝试设置默认封面
        /// </summary>
        private bool TrySetDefaultCover(bool isLocal)
        {
            try
            {
                // 本地歌曲使用 localcover，其他使用 defaultcover
                string resourceName = isLocal 
                    ? "ChillPatcher.Resources.localcover.jpg"
                    : "ChillPatcher.Resources.defaultcover.png";

                var coverData = LoadEmbeddedResourceBytes(resourceName);
                if (coverData != null && coverData.Length > 0)
                {
                    string mimeType = resourceName.EndsWith(".png") ? "image/png" : "image/jpeg";
                    if (SmtcBridge.SetThumbnailFromMemory(coverData, mimeType))
                    {
                        _log.LogDebug($"使用默认封面设置缩略图成功: {resourceName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug($"设置默认封面失败: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 从嵌入资源加载字节数组
        /// </summary>
        private byte[] LoadEmbeddedResourceBytes(string resourceName)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _log.LogDebug($"嵌入资源未找到: {resourceName}");
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
            catch (Exception ex)
            {
                _log.LogDebug($"加载嵌入资源失败 [{resourceName}]: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 尝试在音频文件同目录下查找封面文件
        /// </summary>
        private string TryFindCoverFile(string audioPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(audioPath);
                if (string.IsNullOrEmpty(directory)) return null;
                
                // 常见的封面文件名（与 AlbumCoverLoader 一致）
                string[] coverNames = { "cover", "folder", "front", "album", "thumb", "artwork" };
                string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
                
                foreach (var name in coverNames)
                {
                    foreach (var ext in extensions)
                    {
                        var coverPath = Path.Combine(directory, name + ext);
                        if (File.Exists(coverPath))
                            return coverPath;
                        
                        // 也检查大写版本
                        coverPath = Path.Combine(directory, name.ToUpper() + ext);
                        if (File.Exists(coverPath))
                            return coverPath;
                    }
                }
                
                // 也尝试查找任意图片文件
                foreach (var ext in extensions)
                {
                    var images = Directory.GetFiles(directory, $"*{ext}", SearchOption.TopDirectoryOnly);
                    if (images.Length > 0)
                        return images[0];
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug($"查找封面文件失败: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// 根据文件扩展名获取 MIME 类型
        /// </summary>
        private string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        /// <summary>
        /// 设置播放状态
        /// </summary>
        public void SetPlaybackStatus(bool isPlaying)
        {
            if (!_initialized) return;

            _isPlaying = isPlaying;
            var status = isPlaying ? SmtcBridge.PlaybackStatus.Playing : SmtcBridge.PlaybackStatus.Paused;
            SmtcBridge.SetPlaybackStatus(status);
        }

        /// <summary>
        /// 更新时间线
        /// </summary>
        public void UpdateTimeline(long durationMs, long positionMs)
        {
            if (!_initialized) return;
            SmtcBridge.SetTimelineProperties(0, durationMs, positionMs);
        }

        /// <summary>
        /// 处理按钮事件
        /// </summary>
        private void OnButtonPressed(SmtcBridge.ButtonType buttonType)
        {
            _log.LogDebug($"SMTC 按钮按下: {buttonType}");

            // 在主线程执行
            MainThreadDispatcher.Instance?.Enqueue(() => HandleButtonPress(buttonType));
        }

        /// <summary>
        /// 在主线程处理按钮事件
        /// </summary>
        private void HandleButtonPress(SmtcBridge.ButtonType buttonType)
        {
            try
            {
                if (_facilityMusic == null || _musicService == null)
                {
                    _log.LogWarning("游戏服务未设置，无法处理按钮事件");
                    return;
                }

                switch (buttonType)
                {
                    case SmtcBridge.ButtonType.Play:
                        if (_facilityMusic.IsPaused)
                        {
                            _facilityMusic.UnPauseMusic();
                            SetPlaybackStatus(true);
                        }
                        break;

                    case SmtcBridge.ButtonType.Pause:
                        if (!_facilityMusic.IsPaused)
                        {
                            _facilityMusic.PauseMusic();
                            SetPlaybackStatus(false);
                        }
                        break;

                    case SmtcBridge.ButtonType.Next:
                        _musicService.PlayNextMusic(1, MusicChangeKind.Manual);
                        break;

                    case SmtcBridge.ButtonType.Previous:
                        _musicService.PlayNextMusic(-1, MusicChangeKind.Manual);
                        break;

                    case SmtcBridge.ButtonType.Stop:
                        _facilityMusic.PauseMusic();
                        SetPlaybackStatus(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"处理按钮事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关闭服务
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized) return;

            try
            {
                SmtcBridge.OnButtonPressed -= OnButtonPressed;
                SmtcBridge.SetPlaybackStatus(SmtcBridge.PlaybackStatus.Closed);
                SmtcBridge.Shutdown();
                
                _initialized = false;
                _log.LogInfo("SMTC 服务已关闭");
            }
            catch (Exception ex)
            {
                _log.LogError($"SMTC 服务关闭异常: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Shutdown();
            _log.Dispose();
        }
    }
}

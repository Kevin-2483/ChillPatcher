using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Configuration;
using ChillPatcher.Module.LocalFolder.Services;
using ChillPatcher.Module.LocalFolder.Services.Cover;
using ChillPatcher.Module.LocalFolder.Services.Scanner;
using ChillPatcher.SDK.Attributes;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Module.LocalFolder
{
    /// <summary>
    /// 本地文件夹音乐模块
    /// 扫描本地文件夹中的音乐文件并提供给主程序
    /// 
    /// 目录结构:
    /// 根目录/
    /// ├── 歌单目录A/
    /// │   ├── playlist.json (可选, 自定义歌单名称)
    /// │   ├── !rescan_playlist (重扫描标记)
    /// │   ├── cover.jpg (歌单封面)
    /// │   ├── 散装歌曲.mp3 → 默认专辑 (歌单名称)
    /// │   └── 专辑目录/
    /// │       ├── album.json (可选, 自定义专辑名称)
    /// │       ├── cover.jpg (专辑封面)
    /// │       ├── 歌曲1.mp3
    /// │       └── 子目录/ (扫描两层)
    /// │           └── 歌曲2.mp3
    /// ├── 歌单目录B/
    /// │   └── ...
    /// └── 散装歌曲.mp3 → default 歌单
    /// </summary>
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME, 
        Version = ModuleInfo.MODULE_VERSION, 
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 10)]
    public class LocalFolderModule : IMusicModule, IMusicSourceProvider, ICoverProvider, IFavoriteExcludeHandler, IDeleteHandler
    {
        private IModuleContext _context;
        private FolderScanner _scanner;
        private CoverLoader _coverLoader;
        private LocalDatabase _database;
        private string _dataPath;

        // 配置项
        private ConfigEntry<string> _rootFolder;
        private ConfigEntry<bool> _forceRescan;

        #region IMusicModule

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 10;

        public ModuleCapabilities Capabilities => new ModuleCapabilities
        {
            CanDelete = false,  // 本地文件模块禁用删除
            CanFavorite = true,
            CanExclude = true,
            SupportsLiveUpdate = false,
            ProvidesCover = true,
            ProvidesAlbum = true
        };

        public async Task InitializeAsync(IModuleContext context)
        {
            _context = context;

            // 加载原生依赖 (SQLite.Interop.dll)
            LoadNativeDependencies();

            // 注册配置项 (先注册，因为后面需要使用 _rootFolder.Value)
            RegisterConfig();

            // 数据库直接放在音乐根目录
            // 不同的音乐库使用不同的数据库，目录迁移时配置也随之迁移
            // 数据库文件不会被识别为音频文件，不会影响扫描
            _dataPath = _rootFolder.Value;

            // 初始化数据库 (放在音乐根目录中)
            var dbPath = Path.Combine(_dataPath, ".localfolder.db");
            _database = new LocalDatabase(dbPath, context.Logger);

            context.Logger.LogInfo($"[{DisplayName}] 数据库位置: {dbPath}");

            // 初始化封面加载器
            _coverLoader = new CoverLoader(_database, context.DefaultCover, context.Logger);

            // 初始化文件夹扫描器
            _scanner = new FolderScanner(
                _rootFolder.Value,
                _forceRescan.Value,
                _database,
                context.AudioLoader,
                context.Logger
            );

            // 订阅事件
            SubscribeEvents();

            // 扫描并注册
            await ScanAndRegisterAsync();

            context.Logger.LogInfo($"[{DisplayName}] 初始化完成");
        }

        public void OnEnable()
        {
            _context.Logger.LogInfo($"[{DisplayName}] 已启用");
        }

        public void OnDisable()
        {
            _context.Logger.LogInfo($"[{DisplayName}] 已禁用");
        }

        public void OnUnload()
        {
            // 清理资源
            _coverLoader?.ClearCache();
            _database?.Dispose();

            _context.Logger.LogInfo($"[{DisplayName}] 已卸载");
        }

        #endregion

        #region Config

        private void RegisterConfig()
        {
            var config = _context.ConfigManager;

            // 使用 BindDefault 绑定到模块默认 section: [Module:com.chillpatcher.localfolder]
            // 或使用 Bind(null, key, ...) 也可以
            _rootFolder = config.BindDefault(
                "RootFolder",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "ChillWithYou"),
                "本地音乐根目录。\n子目录将作为歌单，歌单下的子目录将作为专辑。"
            );

            _forceRescan = config.BindDefault(
                "ForceRescan",
                false,
                "是否每次启动都强制重新扫描（忽略重扫描标记和数据库缓存）。"
            );

            // 确保根目录存在
            if (!Directory.Exists(_rootFolder.Value))
            {
                Directory.CreateDirectory(_rootFolder.Value);
                _context.Logger.LogInfo($"创建音乐根目录: {_rootFolder.Value}");
            }
        }

        private void LoadNativeDependencies()
        {
            try
            {
                // 使用模块程序集位置确定原生库路径
                var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var moduleDir = Path.GetDirectoryName(assemblyLocation);
                var arch = IntPtr.Size == 8 ? "x64" : "x86";
                var sqlitePath = Path.Combine(moduleDir, "native", arch, "SQLite.Interop.dll");
                
                if (File.Exists(sqlitePath))
                {
                    // 直接使用 kernel32 LoadLibrary
                    var handle = NativeMethods.LoadLibrary(sqlitePath);
                    if (handle != IntPtr.Zero)
                    {
                        _context.Logger.LogInfo($"[{DisplayName}] 已加载原生依赖: SQLite.Interop.dll");
                    }
                    else
                    {
                        _context.Logger.LogWarning($"[{DisplayName}] 无法加载 SQLite.Interop.dll (LoadLibrary 失败)");
                    }
                }
                else
                {
                    _context.Logger.LogWarning($"[{DisplayName}] 未找到 SQLite.Interop.dll: {sqlitePath}");
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogError($"[{DisplayName}] 加载原生依赖失败: {ex.Message}");
            }
        }

        // P/Invoke for LoadLibrary
        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr LoadLibrary(string libFilename);
        }

        #endregion

        #region Events

        private void SubscribeEvents()
        {
            // 订阅播放事件
            _context.EventBus.Subscribe<PlayStartedEvent>(OnPlayStarted);
            _context.EventBus.Subscribe<PlayEndedEvent>(OnPlayEnded);
        }

        private void OnPlayStarted(PlayStartedEvent evt)
        {
            // 如果是本模块的歌曲，更新播放统计
            if (evt.Music?.ModuleId == ModuleId)
            {
                _database.UpdatePlayCount(evt.Music.UUID);
            }
        }

        private void OnPlayEnded(PlayEndedEvent evt)
        {
            // 可以记录播放历史等
        }

        #endregion

        #region Scan and Register

        private async Task ScanAndRegisterAsync()
        {
            _context.Logger.LogInfo($"[{DisplayName}] 开始扫描: {_rootFolder.Value}");

            // 扫描文件夹
            var scanResult = await _scanner.ScanAsync();

            // 注册 Tag (歌单) - 检查 JSON 更新显示名称
            foreach (var playlist in scanResult.Playlists)
            {
                // 检查 playlist.json 获取最新显示名称
                var jsonDisplayName = MetadataReader.ReadPlaylistName(playlist.DirectoryPath);
                var finalDisplayName = !string.IsNullOrEmpty(jsonDisplayName) ? jsonDisplayName : playlist.DisplayName;

                var tagInfo = _context.TagRegistry.RegisterTag(
                    playlist.TagId,
                    finalDisplayName,
                    ModuleId
                );
                
                _context.Logger.LogInfo($"注册歌单 Tag: {finalDisplayName}");
            }

            // 注册专辑 - 检查 JSON 更新显示名称和艺术家
            foreach (var album in scanResult.Albums)
            {
                // 检查 album.json 获取最新显示名称和艺术家
                if (!string.IsNullOrEmpty(album.DirectoryPath))
                {
                    var jsonDisplayName = MetadataReader.ReadAlbumName(album.DirectoryPath);
                    var jsonArtist = MetadataReader.ReadAlbumArtist(album.DirectoryPath);
                    
                    if (!string.IsNullOrEmpty(jsonDisplayName))
                    {
                        album.DisplayName = jsonDisplayName;
                    }
                    if (!string.IsNullOrEmpty(jsonArtist))
                    {
                        album.Artist = jsonArtist;
                    }
                }
                
                _context.AlbumRegistry.RegisterAlbum(album, ModuleId);
            }

            // 注册歌曲
            foreach (var music in scanResult.Music)
            {
                // 恢复收藏和排除状态
                if (_database.IsFavorite(music.UUID))
                {
                    music.ExtendedData = new LocalMusicData { IsFavorite = true };
                }

                _context.MusicRegistry.RegisterMusic(music, ModuleId);
            }

            // 清理孤儿记录（不再存在的歌曲的收藏/排除/播放统计）
            CleanupOrphanRecords(scanResult);

            _context.Logger.LogInfo($"[{DisplayName}] 扫描完成: {scanResult.Music.Count} 首歌曲, {scanResult.Albums.Count} 个专辑");
        }

        /// <summary>
        /// 清理孤儿记录
        /// </summary>
        private void CleanupOrphanRecords(ScanResult scanResult)
        {
            try
            {
                // 收集所有有效的 UUID 和 TagId
                var validUuids = new HashSet<string>(scanResult.Music.Select(m => m.UUID));
                var validTagIds = new HashSet<string>(scanResult.Playlists.Select(p => p.TagId));

                // 清理不存在的歌曲的收藏/排除/播放统计
                var (favorites, excluded, playStats) = _database.CleanupOrphanRecords(validUuids);
                if (favorites > 0 || excluded > 0 || playStats > 0)
                {
                    _context.Logger.LogInfo($"[{DisplayName}] 清理孤儿记录: 收藏={favorites}, 排除={excluded}, 播放统计={playStats}");
                }

                // 清理不存在的歌单的缓存
                var staleCount = _database.CleanupStalePlaylistCache(validTagIds);
                if (staleCount > 0)
                {
                    _context.Logger.LogInfo($"[{DisplayName}] 清理过期歌单缓存: {staleCount}");
                }
            }
            catch (System.Exception ex)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 清理孤儿记录失败: {ex.Message}");
            }
        }

        #endregion

        #region IMusicSourceProvider

        public MusicSourceType SourceType => MusicSourceType.File;

        public async Task<List<MusicInfo>> GetMusicListAsync()
        {
            return new List<MusicInfo>(_context.MusicRegistry.GetMusicByModule(ModuleId));
        }

        public async Task<UnityEngine.AudioClip> LoadAudioAsync(string uuid)
        {
            return await LoadAudioAsync(uuid, System.Threading.CancellationToken.None);
        }

        public async Task<UnityEngine.AudioClip> LoadAudioAsync(string uuid, System.Threading.CancellationToken cancellationToken)
        {
            var music = _context.MusicRegistry.GetMusic(uuid);
            if (music == null || music.ModuleId != ModuleId)
                return null;

            return await _context.AudioLoader.LoadFromFileAsync(music.SourcePath);
        }

        public void UnloadAudio(string uuid)
        {
            // 音频由主程序管理
        }

        public async Task RefreshAsync()
        {
            // 清除当前注册的内容
            _context.TagRegistry.UnregisterAllByModule(ModuleId);
            _context.AlbumRegistry.UnregisterAllByModule(ModuleId);
            _context.MusicRegistry.UnregisterAllByModule(ModuleId);

            // 重新扫描
            _scanner.UpdateRootPath(_rootFolder.Value);
            await ScanAndRegisterAsync();

            // 发布刷新事件
            _context.EventBus.Publish(new PlaylistUpdatedEvent
            {
                TagId = null,  // 所有歌单
                UpdateType = PlaylistUpdateType.FullRefresh
            });
        }

        #endregion

        #region ICoverProvider

        public async Task<UnityEngine.Sprite> GetMusicCoverAsync(string uuid)
        {
            var music = _context.MusicRegistry.GetMusic(uuid);
            if (music == null || music.ModuleId != ModuleId)
                return null;

            return await _coverLoader.GetMusicCoverAsync(music.SourcePath);
        }

        public async Task<UnityEngine.Sprite> GetAlbumCoverAsync(string albumId)
        {
            var album = _context.AlbumRegistry.GetAlbum(albumId);
            if (album == null || album.ModuleId != ModuleId)
                return null;

            return await _coverLoader.GetAlbumCoverAsync(album.DirectoryPath);
        }

        public async Task<(byte[] data, string mimeType)> GetMusicCoverBytesAsync(string uuid)
        {
            var music = _context.MusicRegistry.GetMusic(uuid);
            if (music == null || music.ModuleId != ModuleId)
                return (null, null);

            return await _coverLoader.GetMusicCoverBytesAsync(music.SourcePath);
        }

        public void ClearCache()
        {
            _coverLoader?.ClearCache();
        }

        #endregion

        #region IFavoriteExcludeHandler

        public bool IsFavorite(string uuid)
        {
            return _database.IsFavorite(uuid);
        }

        public void SetFavorite(string uuid, bool isFavorite)
        {
            if (isFavorite)
            {
                _database.AddFavorite(uuid);
            }
            else
            {
                _database.RemoveFavorite(uuid);
            }

            // 发布事件
            _context.EventBus.Publish(new FavoriteChangedEvent
            {
                UUID = uuid,
                IsFavorite = isFavorite,
                Music = _context.MusicRegistry.GetMusic(uuid)
            });
        }

        public bool IsExcluded(string uuid)
        {
            return _database.IsExcluded(uuid);
        }

        public void SetExcluded(string uuid, bool isExcluded)
        {
            if (isExcluded)
            {
                _database.AddExcluded(uuid);
            }
            else
            {
                _database.RemoveExcluded(uuid);
            }

            // 发布事件
            _context.EventBus.Publish(new ExcludeChangedEvent
            {
                UUID = uuid,
                IsExcluded = isExcluded,
                Music = _context.MusicRegistry.GetMusic(uuid)
            });
        }

        public IReadOnlyList<string> GetFavorites()
        {
            return _database.GetAllFavorites();
        }

        public IReadOnlyList<string> GetExcluded()
        {
            return _database.GetAllExcluded();
        }

        #endregion

        #region IDeleteHandler

        public bool CanDelete => false;  // 本地文件模块禁用删除

        public bool Delete(string uuid)
        {
            // 不支持删除
            return false;
        }

        public string GetDeleteConfirmMessage(string uuid)
        {
            return "本地文件模块不支持删除操作";
        }

        #endregion
    }

    /// <summary>
    /// 本地音乐扩展数据
    /// </summary>
    public class LocalMusicData
    {
        public bool IsFavorite { get; set; }
        public bool IsExcluded { get; set; }
    }
}

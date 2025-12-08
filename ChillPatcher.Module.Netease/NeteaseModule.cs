using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using ChillPatcher.SDK.Attributes;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;
using UnityEngine;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云音乐模块
    /// 从网易云音乐收藏歌单加载音乐
    /// </summary>
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME,
        Version = ModuleInfo.MODULE_VERSION,
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 50)]
    public class NeteaseModule : IMusicModule, IStreamingMusicSourceProvider, ICoverProvider, IFavoriteExcludeHandler, IDeleteHandler
    {
        private IModuleContext _context;
        private NeteaseBridge _bridge;
        private NeteaseCoverLoader _coverLoader;
        private PersonalFMManager _fmManager;
        private NeteaseFavoriteManager _favoriteManager;
        private NeteaseSongRegistry _songRegistry;
        private QRLoginManager _qrLoginManager;
        
        private List<MusicInfo> _musicList = new List<MusicInfo>();
        private List<MusicInfo> _fmMusicList = new List<MusicInfo>();
        private Dictionary<string, NeteaseBridge.SongInfo> _songInfoMap = new Dictionary<string, NeteaseBridge.SongInfo>();
        private bool _isReady = false;
        private bool _isLoggedIn = false;

        // 登录歌曲常量
        private const string LOGIN_SONG_UUID = "netease_qr_login_song";
        private const string LOGIN_SONG_TITLE = "二维码登录";
        private const float LOGIN_SONG_DURATION = 60f; // 1 分钟

        // 配置项
        private ConfigEntry<string> _dataDir;
        private ConfigEntry<int> _audioQuality;
        private ConfigEntry<string> _satonePlaylistKeywords;  // 献给聪音歌单关键词
        private ConfigEntry<string> _customPlaylistIds;  // 直接指定歌单 ID

        // 自定义歌单
        private Dictionary<long, List<MusicInfo>> _customPlaylistMusicLists = new Dictionary<long, List<MusicInfo>>();

        #region IMusicModule

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 50;

        public ModuleCapabilities Capabilities => new ModuleCapabilities
        {
            CanDelete = false,
            CanFavorite = true,  // 支持收藏操作，同步到网易云
            CanExclude = false,
            SupportsLiveUpdate = false,
            ProvidesCover = true,
            ProvidesAlbum = true
        };

        public async Task InitializeAsync(IModuleContext context)
        {
            _context = context;
            _bridge = new NeteaseBridge(context.Logger);
            _coverLoader = new NeteaseCoverLoader(context.Logger);

            // 注册配置项
            RegisterConfig();

            // 使用 DependencyLoader 加载原生 DLL
            var loaded = context.DependencyLoader.LoadNativeLibraryFromModulePath(
                "ChillNetease.dll", 
                ModuleId);
            
            if (!loaded)
            {
                context.Logger.LogError($"[{DisplayName}] 无法加载 ChillNetease.dll");
                context.Logger.LogInfo($"[{DisplayName}] 请确保 DLL 位于模块的 native/x64/ 目录中");
                return;
            }

            // 初始化桥接
            if (!_bridge.Initialize(_dataDir.Value))
            {
                context.Logger.LogError($"[{DisplayName}] 初始化失败");
                return;
            }

            // 注册 Tags（无论是否登录都需要）
            RegisterTags();

            // 检查登录状态
            _isLoggedIn = _bridge.IsLoggedIn;
            if (!_isLoggedIn)
            {
                context.Logger.LogWarning($"[{DisplayName}] 未登录网易云音乐，显示二维码登录");
                
                // 初始化二维码登录管理器
                _qrLoginManager = new QRLoginManager(_bridge, context.Logger);
                _qrLoginManager.OnLoginSuccess += OnQRLoginSuccess;
                _qrLoginManager.OnStatusChanged += OnQRLoginStatusChanged;
                
                // 注册收藏专辑（包含登录歌曲）
                RegisterLoginSongAlbum();
                
                // 注册登录歌曲
                RegisterLoginSong("请点击播放扫码登录");
                
                _isReady = true;
                OnReadyStateChanged?.Invoke(_isReady);
                
                context.Logger.LogInfo($"[{DisplayName}] ✅ 初始化完成（未登录模式）");
                return;
            }

            // 获取用户信息
            var userInfo = _bridge.GetUserInfo();
            if (userInfo != null)
            {
                context.Logger.LogInfo($"[{DisplayName}] 已登录: {userInfo.Nickname} (ID: {userInfo.UserId})");
            }

            // 初始化辅助管理器
            _favoriteManager = new NeteaseFavoriteManager(_bridge, context.Logger, _songInfoMap);
            _songRegistry = new NeteaseSongRegistry(context, ModuleId, _songInfoMap, _favoriteManager, context.Logger);
            _fmManager = new PersonalFMManager(_bridge);

            // 注册 Tags
            RegisterTags();

            // 获取并缓存收藏歌曲 ID 列表
            await _favoriteManager.LoadLikeListAsync();

            // 扫描并注册收藏歌曲
            await ScanAndRegisterAsync();

            // 初始化个人 FM 并注册初始歌曲
            await InitializePersonalFMAsync();

            // 搜索并注册自定义歌单（如"献给聪音"）
            await SearchAndRegisterCustomPlaylistsAsync();

            // 根据 ID 导入指定歌单
            await ImportPlaylistsByIdAsync();

            // 订阅收藏变化事件
            SubscribeToFavoriteEvents();

            _isReady = true;
            OnReadyStateChanged?.Invoke(_isReady);

            // 统计自定义歌单歌曲数
            var customSongCount = _customPlaylistMusicLists.Values.Sum(list => list.Count);
            context.Logger.LogInfo($"[{DisplayName}] ✅ 初始化完成，收藏 {_musicList.Count} 首，FM {_fmMusicList.Count} 首，自定义歌单 {customSongCount} 首");
        }

        public void OnEnable()
        {
            _context?.Logger.LogInfo($"[{DisplayName}] 已启用");
        }

        public void OnDisable()
        {
            _context?.Logger.LogInfo($"[{DisplayName}] 已禁用");
        }

        public void OnUnload()
        {
            _musicList.Clear();
            _fmMusicList.Clear();
            _songInfoMap.Clear();
            _isReady = false;
        }

        #endregion

        #region IStreamingMusicSourceProvider

        public bool IsReady => _isReady;
        public event Action<bool> OnReadyStateChanged;

        public MusicSourceType SourceType => MusicSourceType.Url;

        public Task<List<MusicInfo>> GetMusicListAsync()
        {
            return Task.FromResult(_musicList.ToList());
        }

        public Task<AudioClip> LoadAudioAsync(string uuid)
        {
            // 流媒体模块不直接加载 AudioClip，使用 ResolveAsync
            return Task.FromResult<AudioClip>(null);
        }

        public Task<AudioClip> LoadAudioAsync(string uuid, CancellationToken cancellationToken)
        {
            return Task.FromResult<AudioClip>(null);
        }

        public void UnloadAudio(string uuid)
        {
            // 流媒体无需卸载
        }

        public async Task RefreshAsync()
        {
            _context.Logger.LogInfo($"[{DisplayName}] 刷新歌曲列表...");
            
            // 清除旧数据
            _musicList.Clear();
            _songInfoMap.Clear();
            
            // 重新注销并注册
            _context.MusicRegistry.UnregisterAllByModule(ModuleId);
            _context.AlbumRegistry.UnregisterAllByModule(ModuleId);
            
            // 重新扫描
            await ScanAndRegisterAsync();
        }

        #endregion

        #region IPlayableSourceResolver

        public async Task<PlayableSource> ResolveAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            // 处理登录歌曲
            if (uuid == LOGIN_SONG_UUID)
            {
                return await ResolveLoginSongAsync(cancellationToken);
            }

            if (!_songInfoMap.TryGetValue(uuid, out var songInfo))
            {
                _context.Logger.LogWarning($"[{DisplayName}] 未找到歌曲: {uuid}");
                return null;
            }

            // 使用用户配置的音质（如果没有指定则使用配置值）
            var effectiveQuality = GetEffectiveQuality(quality);
            var bridgeQuality = MapQuality(effectiveQuality);
            
            // 创建 PCM 流
            var streamId = _bridge.CreatePcmStream(songInfo.Id, bridgeQuality);
            if (streamId < 0)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 创建 PCM 流失败: {songInfo.Name}");
                return null;
            }

            // 创建 PCM 读取器，传入歌曲时长用于计算预估总帧数
            var reader = new NeteasePcmStreamReader(_bridge, streamId, 44100, 2, (float)songInfo.Duration);

            // 等待流准备好
            var ready = await Task.Run(() => reader.WaitForReady(5000), cancellationToken);
            if (!ready)
            {
                _context.Logger.LogWarning($"[{DisplayName}] PCM 流准备超时: {songInfo.Name}");
                reader.Dispose();
                return null;
            }

            _context.Logger.LogInfo($"[{DisplayName}] PCM 流已就绪: {songInfo.Name} [{reader.Info.SampleRate}Hz, {reader.Info.Channels}ch, {reader.Info.Format ?? "mp3"}]");

            // 根据实际格式返回正确的 AudioFormat
            var audioFormat = string.Equals(reader.Info.Format, "flac", StringComparison.OrdinalIgnoreCase)
                ? AudioFormat.Flac
                : AudioFormat.Mp3;

            // 返回 PCM 流源
            return PlayableSource.FromPcmStream(uuid, reader, audioFormat);
        }

        public async Task<PlayableSource> RefreshUrlAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            return await ResolveAsync(uuid, quality, cancellationToken);
        }

        /// <summary>
        /// 处理登录歌曲的播放 - 返回静音流并启动二维码登录
        /// </summary>
        private async Task<PlayableSource> ResolveLoginSongAsync(CancellationToken cancellationToken)
        {
            _context.Logger.LogInfo($"[{DisplayName}] 开始二维码登录流程...");

            // 启动二维码登录
            if (_qrLoginManager != null)
            {
                var success = await _qrLoginManager.StartLoginAsync();
                if (success)
                {
                    UpdateLoginSongStatus("请使用网易云 APP 扫码");
                }
                else
                {
                    UpdateLoginSongStatus("获取二维码失败，请重试");
                }
            }

            // 返回静音流
            var silentReader = new SilentPcmReader(44100, 2, LOGIN_SONG_DURATION);
            return PlayableSource.FromPcmStream(LOGIN_SONG_UUID, silentReader, AudioFormat.Mp3);
        }

        #endregion

        #region ICoverProvider

        public async Task<Sprite> GetMusicCoverAsync(string uuid)
        {
            // 登录歌曲使用二维码作为封面
            if (uuid == LOGIN_SONG_UUID)
            {
                return _qrLoginManager?.QRCodeSprite ?? _coverLoader.FavoritesCover;
            }

            // 歌曲封面：从网易云下载
            if (!_songInfoMap.TryGetValue(uuid, out var songInfo))
            {
                _context.Logger.LogDebug($"[{DisplayName}] Cover: UUID not in songInfoMap: {uuid}");
                return _coverLoader.FavoritesCover;
            }

            if (string.IsNullOrEmpty(songInfo.CoverUrl))
            {
                _context.Logger.LogDebug($"[{DisplayName}] Cover: CoverUrl is empty for: {uuid}");
                return _coverLoader.FavoritesCover;
            }

            _context.Logger.LogDebug($"[{DisplayName}] Cover: Loading from URL for: {uuid}");
            return await _coverLoader.GetCoverFromUrlAsync(songInfo.CoverUrl);
        }

        public async Task<Sprite> GetAlbumCoverAsync(string albumId)
        {
            // 收藏专辑使用 FAVORITES.png 封面
            if (albumId == NeteaseSongRegistry.FAVORITES_ALBUM_ID)
            {
                return _coverLoader.FavoritesCover;
            }
            // FM 专辑使用 FM.png 封面
            if (albumId == NeteaseSongRegistry.PERSONAL_FM_ALBUM_ID)
            {
                return _coverLoader.FMCover;
            }
            
            // 自定义歌单：从 AlbumRegistry 获取封面 URL
            if (albumId.StartsWith("netease_playlist_album_"))
            {
                var album = _context.AlbumRegistry.GetAlbum(albumId);
                if (album != null && album.ExtendedData is string extData)
                {
                    // ExtendedData 格式: "PLAYLIST:{playlistId}:{coverUrl}"
                    var parts = extData.Split(new[] { ':' }, 3);
                    if (parts.Length >= 3 && parts[0] == "PLAYLIST" && !string.IsNullOrEmpty(parts[2]))
                    {
                        var coverUrl = parts[2];
                        return await _coverLoader.GetCoverFromUrlAsync(coverUrl);
                    }
                }
            }
            
            return _coverLoader.DefaultCover;
        }

        public async Task<(byte[] data, string mimeType)> GetMusicCoverBytesAsync(string uuid)
        {
            // 歌曲封面字节数据：从网易云下载（用于 SMTC）
            if (!_songInfoMap.TryGetValue(uuid, out var songInfo))
                return (_coverLoader.FavoritesCoverBytes, "image/png");

            if (string.IsNullOrEmpty(songInfo.CoverUrl))
                return (_coverLoader.DefaultCoverBytes, "image/png");

            return await _coverLoader.GetCoverBytesFromUrlAsync(songInfo.CoverUrl);
        }

        public void ClearCache()
        {
            _coverLoader?.ClearCache();
        }

        public void RemoveMusicCoverCache(string uuid)
        {
            if (_songInfoMap.TryGetValue(uuid, out var songInfo) && !string.IsNullOrEmpty(songInfo.CoverUrl))
            {
                // 需要使用 HTTPS 版本的 URL，因为缓存时已转换
                var httpsUrl = EnsureHttps(songInfo.CoverUrl);
                _coverLoader?.RemoveCache(httpsUrl);
            }
        }

        public void RemoveAlbumCoverCache(string albumId)
        {
            // 专辑使用嵌入封面，无需清理缓存
        }

        /// <summary>
        /// 确保 URL 使用 HTTPS 协议
        /// </summary>
        private static string EnsureHttps(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + url.Substring(7);
            }

            return url;
        }

        #endregion

        #region Private Methods

        private void RegisterConfig()
        {
            var configManager = _context.ConfigManager;

            _dataDir = configManager.Bind(
                "",  // 使用默认 section (Module:com.chillpatcher.netease)
                "DataDirectory",
                "",
                "网易云音乐数据目录 (留空使用默认 musicfox 路径)");

            _audioQuality = configManager.Bind(
                "",  // 使用默认 section
                "AudioQuality",
                2,
                "音质等级: 0=标准, 1=较高, 2=极高, 3=无损, 4=Hi-Res, 5=高清环绕声, 6=沉浸环绕声, 7=超清母带");

            _satonePlaylistKeywords = configManager.Bind(
                "",  // 使用默认 section
                "SatonePlaylistKeywords",
                "For Satone|献给聪音",
                "自动注册包含这些关键词的歌单 (多个关键词用 | 分隔)，留空则不注册");

            _customPlaylistIds = configManager.Bind(
                "",  // 使用默认 section
                "CustomPlaylistIds",
                "",
                "直接指定要导入的歌单 ID (多个 ID 用 , 分隔)");
        }

        private void RegisterTags()
        {
            // 注册"收藏歌曲" Tag（普通 Tag）
            _context.TagRegistry.RegisterTag(
                NeteaseSongRegistry.TAG_FAVORITES,
                "网易云收藏",
                ModuleId);
            _context.Logger.LogInfo($"[{DisplayName}] 已注册 Tag: 网易云收藏");

            // 注册"个人FM" Tag
            // 会在注册增长专辑 (IsGrowableAlbum=true) 时自动标记为增长 Tag
            _context.TagRegistry.RegisterTag(
                NeteaseSongRegistry.TAG_PERSONAL_FM,
                "个人FM ∞",
                ModuleId);
            
            // 设置加载更多回调
            _context.TagRegistry.SetLoadMoreCallback(
                NeteaseSongRegistry.TAG_PERSONAL_FM,
                LoadMoreFMSongsAsync);
            
            _context.Logger.LogInfo($"[{DisplayName}] 已注册 Tag: 个人FM ∞");
        }

        #region Login Song Methods

        /// <summary>
        /// 注册登录歌曲所在的专辑
        /// </summary>
        private void RegisterLoginSongAlbum()
        {
            var album = new AlbumInfo
            {
                AlbumId = NeteaseSongRegistry.FAVORITES_ALBUM_ID,
                DisplayName = "网易云音乐登录",
                Artist = "请扫码登录",
                TagIds = new List<string> { NeteaseSongRegistry.TAG_FAVORITES },
                ModuleId = ModuleId,
                SongCount = 1,
                SortOrder = 0,
                IsGrowableAlbum = false,
                ExtendedData = "LOGIN"
            };
            _context.AlbumRegistry.RegisterAlbum(album, ModuleId);
        }

        /// <summary>
        /// 注册登录歌曲
        /// </summary>
        private void RegisterLoginSong(string statusText)
        {
            var loginMusic = new MusicInfo
            {
                UUID = LOGIN_SONG_UUID,
                Title = LOGIN_SONG_TITLE,
                Artist = statusText,
                AlbumId = NeteaseSongRegistry.FAVORITES_ALBUM_ID,
                TagId = NeteaseSongRegistry.TAG_FAVORITES,
                SourceType = MusicSourceType.Stream,
                SourcePath = "login",
                Duration = LOGIN_SONG_DURATION,
                ModuleId = ModuleId,
                IsUnlocked = true,
                IsFavorite = false,
                IsDeletable = false,
                ExtendedData = "QR_LOGIN"
            };

            _musicList.Add(loginMusic);
            _context.MusicRegistry.RegisterMusic(loginMusic, ModuleId);
            _context.Logger.LogInfo($"[{DisplayName}] 已注册登录歌曲");
        }

        /// <summary>
        /// 更新登录歌曲的状态文本
        /// </summary>
        private void UpdateLoginSongStatus(string statusText)
        {
            var loginMusic = _musicList.FirstOrDefault(m => m.UUID == LOGIN_SONG_UUID);
            if (loginMusic != null)
            {
                loginMusic.Artist = statusText;
                _context.MusicRegistry.UpdateMusic(loginMusic);
            }
        }

        /// <summary>
        /// 删除登录歌曲
        /// </summary>
        private void RemoveLoginSong()
        {
            var loginMusic = _musicList.FirstOrDefault(m => m.UUID == LOGIN_SONG_UUID);
            if (loginMusic != null)
            {
                _musicList.Remove(loginMusic);
                _context.MusicRegistry.UnregisterMusic(LOGIN_SONG_UUID);
                _context.Logger.LogInfo($"[{DisplayName}] 已删除登录歌曲");
            }
        }

        /// <summary>
        /// 二维码登录成功回调
        /// </summary>
        private async void OnQRLoginSuccess()
        {
            _context.Logger.LogInfo($"[{DisplayName}] 二维码登录成功！");
            _isLoggedIn = true;

            // 获取用户信息
            var userInfo = _bridge.GetUserInfo();
            if (userInfo != null)
            {
                _context.Logger.LogInfo($"[{DisplayName}] 已登录: {userInfo.Nickname} (ID: {userInfo.UserId})");
            }

            // 删除登录歌曲
            RemoveLoginSong();

            // 注销旧的专辑
            _context.AlbumRegistry.UnregisterAllByModule(ModuleId);

            // 初始化辅助管理器
            _favoriteManager = new NeteaseFavoriteManager(_bridge, _context.Logger, _songInfoMap);
            _songRegistry = new NeteaseSongRegistry(_context, ModuleId, _songInfoMap, _favoriteManager, _context.Logger);
            _fmManager = new PersonalFMManager(_bridge);

            // 获取并缓存收藏歌曲 ID 列表
            await _favoriteManager.LoadLikeListAsync();

            // 扫描并注册收藏歌曲
            await ScanAndRegisterAsync();

            // 初始化个人 FM 并注册初始歌曲
            await InitializePersonalFMAsync();

            // 搜索并注册自定义歌单（如"献给聪音"）
            await SearchAndRegisterCustomPlaylistsAsync();

            // 根据 ID 导入指定歌单
            await ImportPlaylistsByIdAsync();

            // 订阅收藏变化事件
            SubscribeToFavoriteEvents();

            // 统计自定义歌单歌曲数
            var customSongCount = _customPlaylistMusicLists.Values.Sum(list => list.Count);
            _context.Logger.LogInfo($"[{DisplayName}] ✅ 登录后初始化完成，收藏 {_musicList.Count} 首，FM {_fmMusicList.Count} 首，自定义歌单 {customSongCount} 首");

            // 发布刷新事件
            _context.EventBus.Publish(new SDK.Events.PlaylistUpdatedEvent
            {
                TagId = NeteaseSongRegistry.TAG_FAVORITES,
                UpdateType = SDK.Events.PlaylistUpdateType.FullRefresh
            });
        }

        /// <summary>
        /// 二维码登录状态变化回调
        /// </summary>
        private void OnQRLoginStatusChanged(string status)
        {
            _context.Logger.LogInfo($"[{DisplayName}] 登录状态: {status}");
            UpdateLoginSongStatus(status);
        }

        #endregion

        private void SubscribeToFavoriteEvents()
        {
            _context.EventBus.Subscribe<FavoriteChangedEvent>(OnFavoriteChanged);
        }

        private void OnFavoriteChanged(FavoriteChangedEvent evt)
        {
            // 委托给 FavoriteManager 处理
            _favoriteManager.HandleFavoriteChanged(evt, ModuleId, (uuid, isFavorite) =>
            {
                UpdateMusicFavoriteState(uuid, isFavorite);
                
                // FM 歌曲收藏时，移动到收藏专辑
                if (isFavorite)
                {
                    _songRegistry.MoveSongToFavorites(uuid, _fmMusicList, _musicList);
                }
            });
        }

        private async Task ScanAndRegisterAsync()
        {
            var songs = _bridge.GetLikeSongs(true);
            if (songs == null || songs.Count == 0)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 未获取到收藏歌曲");
                return;
            }

            _context.Logger.LogInfo($"[{DisplayName}] 获取到 {songs.Count} 首收藏歌曲");

            // 使用 SongRegistry 注册专辑和歌曲
            _songRegistry.RegisterFavoritesAlbum(songs.Count);
            _musicList = _songRegistry.RegisterFavoritesSongs(songs);

            _context.Logger.LogInfo($"[{DisplayName}] 已注册 1 个专辑(歌单), {_musicList.Count} 首歌曲");
        }

        private async Task InitializePersonalFMAsync()
        {
            // 使用异步版本初始化，避免阻塞主线程
            if (!await _fmManager.InitializeAsync())
            {
                _context.Logger.LogWarning($"[{DisplayName}] 个人FM 初始化失败");
                return;
            }

            // 使用 SongRegistry 注册 FM 专辑和歌曲
            _songRegistry.RegisterFMAlbum(_fmManager.Count);
            _fmMusicList = _songRegistry.RegisterFMSongs(_fmManager.Songs);

            _context.Logger.LogInfo($"[{DisplayName}] 个人FM 已初始化，{_fmMusicList.Count} 首歌曲");
        }

        private async Task<int> LoadMoreFMSongsAsync()
        {
            var previousCount = _fmManager.Count;
            
            // 使用异步版本，避免阻塞主线程
            var loaded = await _fmManager.LoadMoreAsync();

            if (loaded <= 0)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 个人FM 加载更多失败");
                return 0;
            }

            // 注册新加载的歌曲
            var newSongs = _fmManager.Songs.Skip(previousCount).ToList();
            var newMusicList = _songRegistry.RegisterFMSongs(newSongs);
            _fmMusicList.AddRange(newMusicList);

            _context.Logger.LogInfo($"[{DisplayName}] 个人FM 已加载 {loaded} 首新歌曲");
            return loaded;
        }

        /// <summary>
        /// 搜索并注册自定义歌单（根据配置的关键词）
        /// </summary>
        private async Task SearchAndRegisterCustomPlaylistsAsync()
        {
            var keywords = _satonePlaylistKeywords?.Value;
            if (string.IsNullOrWhiteSpace(keywords))
            {
                _context.Logger.LogInfo($"[{DisplayName}] 未配置自定义歌单关键词，跳过");
                return;
            }

            _context.Logger.LogInfo($"[{DisplayName}] 搜索包含关键词的歌单: {keywords}");

            // 在后台线程中搜索歌单
            var playlists = await Task.Run(() => _bridge.SearchPlaylistsByKeyword(keywords));

            if (playlists == null || playlists.Count == 0)
            {
                _context.Logger.LogInfo($"[{DisplayName}] 未找到匹配的歌单");
                return;
            }

            _context.Logger.LogInfo($"[{DisplayName}] 找到 {playlists.Count} 个匹配的歌单");

            // 注册每个歌单
            foreach (var playlist in playlists)
            {
                await RegisterCustomPlaylistAsync(playlist);
            }
        }

        /// <summary>
        /// 注册单个自定义歌单
        /// </summary>
        private async Task RegisterCustomPlaylistAsync(NeteaseBridge.PlaylistInfo playlist)
        {
            _context.Logger.LogInfo($"[{DisplayName}] 正在注册歌单: {playlist.Name} ({playlist.SongCount} 首)");

            // 注册 Tag
            _songRegistry.RegisterPlaylistTag(playlist.Id, playlist.Name);

            // 获取歌单中的歌曲
            var songs = await Task.Run(() => _bridge.GetPlaylistSongs(playlist.Id, true));
            if (songs == null || songs.Count == 0)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 歌单 {playlist.Name} 获取歌曲失败或为空");
                return;
            }

            // 注册专辑
            _songRegistry.RegisterPlaylistAlbum(playlist.Id, playlist.Name, songs.Count, playlist.CoverUrl);

            // 注册歌曲
            var musicList = _songRegistry.RegisterPlaylistSongs(playlist.Id, songs);
            _customPlaylistMusicLists[playlist.Id] = musicList;

            _context.Logger.LogInfo($"[{DisplayName}] ✅ 歌单 {playlist.Name} 已注册，{musicList.Count} 首歌曲");
        }

        /// <summary>
        /// 根据 ID 导入指定歌单（根据配置的 ID 列表）
        /// </summary>
        private async Task ImportPlaylistsByIdAsync()
        {
            var idsConfig = _customPlaylistIds?.Value;
            if (string.IsNullOrWhiteSpace(idsConfig))
            {
                _context.Logger.LogInfo($"[{DisplayName}] 未配置自定义歌单 ID，跳过");
                return;
            }

            // 解析 ID 列表
            var ids = new List<long>();
            foreach (var part in idsConfig.Split(','))
            {
                var trimmed = part.Trim();
                if (long.TryParse(trimmed, out var id))
                {
                    ids.Add(id);
                }
            }

            if (ids.Count == 0)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 配置的歌单 ID 格式无效: {idsConfig}");
                return;
            }

            _context.Logger.LogInfo($"[{DisplayName}] 正在导入 {ids.Count} 个歌单...");

            foreach (var id in ids)
            {
                await ImportPlaylistByIdAsync(id);
            }
        }

        /// <summary>
        /// 根据 ID 导入单个歌单
        /// </summary>
        private async Task ImportPlaylistByIdAsync(long playlistId)
        {
            _context.Logger.LogInfo($"[{DisplayName}] 正在获取歌单详情: {playlistId}");

            // 在后台线程中获取歌单详情
            var detail = await Task.Run(() => _bridge.GetPlaylistDetail(playlistId));

            if (detail == null)
            {
                _context.Logger.LogWarning($"[{DisplayName}] 无法获取歌单 {playlistId} 的详情");
                return;
            }

            _context.Logger.LogInfo($"[{DisplayName}] 歌单: {detail.Name} ({detail.Songs?.Count ?? 0} 首)");

            // 检查是否已经注册（避免重复）
            if (_customPlaylistMusicLists.ContainsKey(playlistId))
            {
                _context.Logger.LogInfo($"[{DisplayName}] 歌单 {detail.Name} 已经注册，跳过");
                return;
            }

            // 注册 Tag
            _songRegistry.RegisterPlaylistTag(playlistId, detail.Name);

            // 注册专辑
            _songRegistry.RegisterPlaylistAlbum(playlistId, detail.Name, detail.SongCount, detail.CoverUrl);

            // 注册歌曲
            if (detail.Songs != null && detail.Songs.Count > 0)
            {
                var musicList = _songRegistry.RegisterPlaylistSongs(playlistId, detail.Songs);
                _customPlaylistMusicLists[playlistId] = musicList;
                _context.Logger.LogInfo($"[{DisplayName}] ✅ 歌单 {detail.Name} 已导入，{musicList.Count} 首歌曲");
            }
            else
            {
                _context.Logger.LogWarning($"[{DisplayName}] 歌单 {detail.Name} 没有歌曲");
            }
        }

        private void UpdateMusicFavoriteState(string uuid, bool isFavorite)
        {
            var music = _musicList.FirstOrDefault(m => m.UUID == uuid) 
                     ?? _fmMusicList.FirstOrDefault(m => m.UUID == uuid);
            if (music != null)
            {
                music.IsFavorite = isFavorite;
                
                // 取消收藏后，允许删除（从列表移除）
                // 收藏时，不允许删除
                music.IsDeletable = !isFavorite;
                
                _context.MusicRegistry.UpdateMusic(music);
            }
        }

        /// <summary>
        /// 获取有效的音质设置
        /// 如果传入的是默认值，则使用用户配置的音质
        /// </summary>
        private AudioQuality GetEffectiveQuality(AudioQuality requestedQuality)
        {
            // 如果有配置的音质设置，使用配置值
            if (_audioQuality != null)
            {
                var configuredQuality = _audioQuality.Value;
                // 配置值: 0=标准, 1=较高, 2=极高, 3=无损, 4=Hi-Res, 5=高清环绕声, 6=沉浸环绕声, 7=超清母带
                return configuredQuality switch
                {
                    0 => AudioQuality.Standard,
                    1 => AudioQuality.Higher,
                    2 => AudioQuality.ExHigh,
                    3 => AudioQuality.Lossless,
                    4 => AudioQuality.HiRes,
                    5 => AudioQuality.JYEffect,
                    6 => AudioQuality.Sky,
                    7 => AudioQuality.JYMaster,
                    _ => requestedQuality
                };
            }
            return requestedQuality;
        }

        private NeteaseBridge.Quality MapQuality(AudioQuality quality)
        {
            return quality switch
            {
                AudioQuality.Standard => NeteaseBridge.Quality.Standard,
                AudioQuality.Higher => NeteaseBridge.Quality.Higher,
                AudioQuality.ExHigh => NeteaseBridge.Quality.ExHigh,
                AudioQuality.Lossless => NeteaseBridge.Quality.Lossless,
                AudioQuality.HiRes => NeteaseBridge.Quality.HiRes,
                AudioQuality.JYEffect => NeteaseBridge.Quality.JYEffect,
                AudioQuality.Sky => NeteaseBridge.Quality.Sky,
                AudioQuality.JYMaster => NeteaseBridge.Quality.JYMaster,
                _ => NeteaseBridge.Quality.ExHigh
            };
        }

        #endregion

        #region IFavoriteExcludeHandler

        public bool IsFavorite(string uuid) => _favoriteManager.IsFavorite(uuid);

        public void SetFavorite(string uuid, bool isFavorite)
        {
            _favoriteManager.SetFavorite(uuid, isFavorite);
            UpdateMusicFavoriteState(uuid, isFavorite);
            
            // 收藏时，将歌曲移动到收藏专辑（Tag 不变）
            if (isFavorite)
            {
                // FM 歌曲
                _songRegistry.MoveSongToFavorites(uuid, _fmMusicList, _musicList);
                
                // 自定义歌单歌曲
                foreach (var customList in _customPlaylistMusicLists.Values)
                {
                    _songRegistry.MoveSongToFavorites(uuid, customList, _musicList);
                }
            }
        }

        public bool IsExcluded(string uuid) => _favoriteManager?.IsExcluded(uuid) ?? false;

        public void SetExcluded(string uuid, bool isExcluded) 
        {
            _favoriteManager?.SetExcluded(uuid, isExcluded);
        }

        public IReadOnlyList<string> GetFavorites() => _favoriteManager?.GetFavorites() ?? Array.Empty<string>();

        public IReadOnlyList<string> GetExcluded() => _favoriteManager?.GetExcluded() ?? Array.Empty<string>();

        #endregion

        #region IDeleteHandler

        /// <summary>
        /// 是否支持删除（模块级别设置）
        /// 实际删除权限由每首歌曲的 IsDeletable 控制
        /// </summary>
        public bool CanDelete => true;  // 允许删除（由歌曲级别控制）

        /// <summary>
        /// 删除歌曲
        /// - FM 专辑中的歌曲：调用 FMTrash API（不喜欢）+ 从列表移除
        /// - 收藏专辑中的歌曲（取消收藏后）：仅从列表移除，不调用 API
        /// </summary>
        public bool Delete(string uuid)
        {
            try
            {
                // 查找歌曲
                var music = _musicList.FirstOrDefault(m => m.UUID == uuid);
                var isFromMusicList = music != null;
                
                if (music == null)
                {
                    music = _fmMusicList.FirstOrDefault(m => m.UUID == uuid);
                }

                if (music == null)
                {
                    _context.Logger.LogWarning($"[{DisplayName}] 未找到歌曲: {uuid}");
                    return false;
                }

                // 判断是否在 FM 专辑中（需要调用不喜欢 API）
                bool isInFMAlbum = music.AlbumId == NeteaseSongRegistry.PERSONAL_FM_ALBUM_ID;
                
                // 如果在 FM 专辑中，调用 FMTrash API
                if (isInFMAlbum)
                {
                    // 从 UUID 解析出网易云歌曲 ID
                    if (_songInfoMap.TryGetValue(uuid, out var songInfo))
                    {
                        _context.Logger.LogInfo($"[{DisplayName}] 将歌曲标记为不喜欢: {music.Title} (ID: {songInfo.Id})");
                        var trashResult = _bridge.FMTrash(songInfo.Id);
                        if (!trashResult)
                        {
                            _context.Logger.LogWarning($"[{DisplayName}] FMTrash API 调用失败，但仍会从本地列表移除");
                        }
                    }
                    else
                    {
                        _context.Logger.LogWarning($"[{DisplayName}] 未找到歌曲信息，无法调用 FMTrash API: {uuid}");
                    }
                }
                else
                {
                    // 不在 FM 专辑中（已收藏后取消收藏的歌曲），只从列表移除
                    _context.Logger.LogInfo($"[{DisplayName}] 仅从列表移除（不调用不喜欢 API）: {music.Title}");
                }

                // 从本地列表移除
                if (isFromMusicList)
                {
                    _musicList.Remove(music);
                }
                else
                {
                    _fmMusicList.Remove(music);
                }

                // 从 songInfoMap 移除
                _songInfoMap.Remove(uuid);

                // 从 MusicRegistry 注销
                _context.MusicRegistry.UnregisterMusic(uuid);

                // 清理封面缓存
                RemoveMusicCoverCache(uuid);

                _context.Logger.LogInfo($"[{DisplayName}] 已从列表移除: {uuid}");
                return true;
            }
            catch (Exception ex)
            {
                _context.Logger.LogError($"[{DisplayName}] 删除失败: {ex}");
                return false;
            }
        }

        #endregion
    }
}
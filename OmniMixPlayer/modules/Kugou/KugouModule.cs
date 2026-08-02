using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OmniMixPlayer.SDK.Attributes;
using OmniMixPlayer.SDK.Events;
using OmniMixPlayer.SDK.Interfaces;
using OmniMixPlayer.SDK.Protos.Models;

namespace OmniMixPlayer.Module.Kugou
{
    [MusicModule(ModuleInfo.MODULE_ID, ModuleInfo.MODULE_NAME,
        Version = ModuleInfo.MODULE_VERSION,
        Author = ModuleInfo.MODULE_AUTHOR,
        Description = ModuleInfo.MODULE_DESCRIPTION,
        Priority = 50)]
    public sealed class KugouModule : IMusicModule, IStreamingMusicSourceProvider, ICoverProvider, ILyricProvider, IModuleUIProvider
    {
        private const string StandardQuality = "128";

        private IModuleContext _context;
        private ILogger _logger;
        private KugouBridge _bridge;
        private KugouCoverLoader _covers;
        private QRLoginManager _login;
        private readonly Dictionary<string, KugouBridge.SongInfo> _songs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KugouBridge.PlaylistInfo> _playlists = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _standardPlaybackHashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _scanLock = new(1, 1);
        private readonly SemaphoreSlim _vipClaimLock = new(1, 1);
        private bool _loggedIn;
        private bool _ready;
        private int _loginHandling;
        private long _qrVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private string _ssaCode;
        private string _ssaVerificationUrl;
        private string _ssaStatus;
        private KugouBridge.SongInfo _ssaProbeSong;
        private int _ssaProbeRunning;
        private string _vipStatus;

        public string ModuleId => ModuleInfo.MODULE_ID;
        public string DisplayName => ModuleInfo.MODULE_NAME;
        public string Version => ModuleInfo.MODULE_VERSION;
        public int Priority => 50;
        public bool IsReady => _ready;
        public SourceType SourceType => SourceType.Stream;
        public event Action<bool> OnReadyStateChanged;
        public ModuleCapabilities Capabilities => new ModuleCapabilities
        {
            CanDelete = false,
            CanFavorite = false,
            CanExclude = false,
            SupportsLiveUpdate = false,
            ProvidesCover = true,
            ProvidesAlbum = false,
            ProvidesPlaylist = true
        };

        public async Task InitializeAsync(IModuleContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = context.Logger;
            if (context.DependencyLoader?.LoadNativeLibrary(ModuleInfo.NATIVE_DLL + ".dll", ModuleId) != true)
            {
                _logger.LogError("[酷狗音乐] 无法加载原生桥接");
                return;
            }
            _bridge = new KugouBridge(_logger);
            if (!_bridge.Initialize(context.GetModuleDataPath(ModuleId)))
            {
                _logger.LogError("[酷狗音乐] 桥接初始化失败：{Error}", _bridge.GetLastError());
                return;
            }
            _covers = new KugouCoverLoader(_logger, _songs);
            _loggedIn = _bridge.IsLoggedIn;
            if (_loggedIn)
            {
                await EnsureDailyVipAsync();
                await ScanAndRegisterAsync();
            }
            else
            {
                SetupLogin();
            }
            _ready = true;
            OnReadyStateChanged?.Invoke(true);
        }

        public void OnEnable() => _logger?.LogInformation("[酷狗音乐] 模块已启用");
        public void OnDisable() => _logger?.LogInformation("[酷狗音乐] 模块已禁用");
        public void OnUnload()
        {
            _login?.Cancel();
            _covers?.Clear();
            _scanLock.Dispose();
            _vipClaimLock.Dispose();
        }

        public Task<List<Track>> GetMusicListAsync() => Task.FromResult(_context.Library.QueryTracks(new TrackQuery { ModuleId = ModuleId, Limit = 0 }).ToList());
        public void UnloadAudio(string uuid) { }

        public async Task RefreshAsync()
        {
            if (!_loggedIn) return;
            await EnsureDailyVipAsync();
            await ScanAndRegisterAsync();
            foreach (var playlist in _playlists.Keys)
            {
                _context.EventBus.Publish(new PlaylistUpdatedEvent { SourceRefId = PlaylistId(playlist), UpdateType = PlaylistUpdateType.FullRefresh });
            }
        }

        public async Task<PlayableSource> ResolveAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default)
        {
            if (!_songs.TryGetValue(uuid, out var song)) return null;
            var playbackHash = song.Hash;
            if (!_standardPlaybackHashes.TryGetValue(song.Hash, out var cachedHash))
            {
                var privilegeHash = await Task.Run(
                    () => _bridge.GetQualityHash(song, StandardQuality),
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(privilegeHash))
                {
                    playbackHash = privilegeHash;
                    _standardPlaybackHashes.TryAdd(song.Hash, privilegeHash);
                }
            }
            else
            {
                playbackHash = cachedHash;
            }

            var source = await Task.Run(
                () => _bridge.GetSongUrl(song, StandardQuality, playbackHash),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(source?.Url))
            {
                return new PlayableSource
                {
                    UUID = uuid,
                    SourceType = PlayableSourceType.Remote,
                    Url = source.Url,
                    Format = AudioFormat.Mp3,
                    Headers = new Dictionary<string, string> { ["User-Agent"] = "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi", ["Referer"] = "https://www.kugou.com/" },
                    CacheKey = $"kugou_{playbackHash}_{StandardQuality}"
                };
            }

            var errorInfo = _bridge.GetLastErrorInfo();
            if (errorInfo?.RequiresVerification == true)
            {
                _ssaCode = errorInfo.SsaCode;
                _ssaVerificationUrl = errorInfo.VerificationUrl;
                _ssaProbeSong = song;
                _ssaStatus = "酷狗要求完成账号安全验证。请打开验证页面并按提示操作。";
                PushUI?.Invoke(BuildUI());
                _logger.LogError("[酷狗音乐概念版] 播放请求触发账号安全验证：{Error}", JsonConvert.SerializeObject(errorInfo));
                throw new InvalidOperationException("酷狗音乐概念版要求完成账号安全验证");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (errorInfo?.ApiCode == 30003)
                _logger.LogWarning("[酷狗音乐概念版] 标准音质仍无播放权限：{Name}", song.Name);
            _logger.LogWarning("[酷狗音乐] 播放解析失败：{Name}，{Error}", song.Name, _bridge.GetLastError());
            return null;
        }

        public Task<PlayableSource> RefreshUrlAsync(string uuid, AudioQuality quality = AudioQuality.ExHigh, CancellationToken cancellationToken = default) => ResolveAsync(uuid, quality, cancellationToken);
        public Task<(byte[] data, string mimeType)> GetMusicCoverAsync(string uuid) => _covers.GetAsync(uuid);
        public Task<(byte[] data, string mimeType)> GetMusicCoverBytesAsync(string uuid) => _covers.GetAsync(uuid);
        public Task<(byte[] data, string mimeType)> GetAlbumCoverAsync(string albumId) => Task.FromResult<(byte[], string)>((null, null));
        public void ClearCache() => _covers.Clear();
        public void RemoveMusicCoverCache(string uuid) => _covers.Remove(uuid);
        public void RemoveAlbumCoverCache(string albumId) => _covers.Remove(albumId);

        public string GetLyric(string uuid)
        {
            if (!_songs.TryGetValue(uuid, out var song)) return null;
            try
            {
                var value = _bridge.GetLyric(song);
                if (string.IsNullOrWhiteSpace(value)) return null;
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
                catch { return value; }
            }
            catch { return null; }
        }

        private void SetupLogin()
        {
            _login?.Cancel();
            _login = new QRLoginManager(_bridge, _logger);
            _login.Changed += () => PushUI?.Invoke(BuildUI());
            _login.LoginSucceeded += OnLoginSucceeded;
        }

        private async void OnLoginSucceeded()
        {
            if (Interlocked.Exchange(ref _loginHandling, 1) == 1) return;
            try
            {
                _loggedIn = true;
                _login?.Cancel();
                PushUI?.Invoke(BuildUI());
                _context.Library.UnregisterModule(ModuleId);
                await EnsureDailyVipAsync();
                await ScanAndRegisterAsync();
                PushUI?.Invoke(BuildUI());
                _context.EventBus.Publish(new CoverInvalidatedEvent { Reason = "Kugou login completed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[酷狗音乐] 登录后初始化失败");
            }
            finally { Interlocked.Exchange(ref _loginHandling, 0); }
        }

        private async Task ScanAndRegisterAsync()
        {
            await _scanLock.WaitAsync();
            try
            {
                var remotePlaylists = await Task.Run(_bridge.GetUserPlaylists);
                if (remotePlaylists == null)
                    throw new InvalidOperationException($"酷狗歌单列表同步失败：{_bridge.GetLastError()}");
                var orderedPlaylists = remotePlaylists
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .GroupBy(x => x.Id)
                    .Select(x => x.First())
                    .OrderByDescending(x => x.Favorite)
                    .ToList();
                var songSnapshots = new Dictionary<string, List<KugouBridge.SongInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var playlist in orderedPlaylists)
                {
                    var songs = await Task.Run(() => _bridge.GetPlaylistSongs(playlist));
                    if (songs == null)
                        throw new InvalidOperationException($"酷狗歌单《{playlist.Name}》同步失败：{_bridge.GetLastError()}");
                    songSnapshots[playlist.Id] = songs;
                }

                var activePlaylistIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingPlaylists = _context.Library.QueryPlaylists(new PlaylistQuery { ModuleId = ModuleId, Limit = 0 });
                var existingTracks = _context.Library.QueryTracks(new TrackQuery { ModuleId = ModuleId, Limit = 0 });
                var existingAlbums = _context.Library.QueryAlbums(new AlbumQuery { ModuleId = ModuleId, Limit = 0 });
                _playlists.Clear();
                foreach (var playlist in orderedPlaylists)
                {
                    activePlaylistIds.Add(playlist.Id);
                    _playlists[playlist.Id] = playlist;
                    _context.Library.UpsertPlaylist(new Playlist
                    {
                        Id = PlaylistId(playlist.Id),
                        Name = playlist.Name,
                        ModuleId = ModuleId,
                        Kind = PlaylistKind.Imported,
                        CoverUri = playlist.CoverUrl ?? ""
                    });
                    var songs = songSnapshots[playlist.Id];
                    var entries = new List<PlaylistEntrySpec>();
                    var position = 0;
                    foreach (var song in songs.Where(x => !string.IsNullOrWhiteSpace(x.Hash)).GroupBy(x => x.Hash).Select(x => x.First()))
                    {
                        var uuid = TrackId(song);
                        seenTracks.Add(uuid);
                        _songs[uuid] = song;
                        var track = _context.Library.GetTrack(uuid) ?? new Track { Uuid = uuid };
                        track.Title = song.Name ?? "";
                        track.Artist = song.Artist ?? "";
                        track.AlbumId = "";
                        track.Duration = song.Duration;
                        track.SourceType = SourceType.Stream;
                        track.SourcePath = song.Hash;
                        track.ModuleId = ModuleId;
                        track.CoverUri = song.CoverUrl ?? "";
                        track.IsFavorite = false;
                        _context.Library.UpsertTrack(track);
                        entries.Add(new PlaylistEntrySpec { TrackUuid = uuid, Position = position++ });
                    }
                    _context.Library.ReplacePlaylistEntries(PlaylistId(playlist.Id), entries);
                    _logger.LogInformation("[酷狗音乐] 映射歌单 {Name}：{Count} 首{Mode}", playlist.Name, entries.Count, playlist.ReadOnly ? "（只读）" : "");
                }
                foreach (var stale in existingPlaylists.Where(x => !activePlaylistIds.Contains(RemotePlaylistId(x.Id))))
                {
                    _context.Library.DeletePlaylist(stale.Id);
                    _context.EventBus.Publish(new PlaylistUpdatedEvent { SourceRefId = stale.Id, UpdateType = PlaylistUpdateType.FullRefresh });
                }
                foreach (var stale in _songs.Keys.Where(x => !seenTracks.Contains(x)).ToList()) _songs.Remove(stale);
                foreach (var stale in existingTracks.Where(x => !seenTracks.Contains(x.Uuid))) _context.Library.DeleteTrack(stale.Uuid);
                foreach (var stale in existingAlbums) _context.Library.DeleteAlbum(stale.Id);
                _logger.LogInformation("[酷狗音乐] 稳定注册完成：{Playlists} 个歌单，{Tracks} 首去重歌曲", activePlaylistIds.Count, seenTracks.Count);
            }
            finally { _scanLock.Release(); }
        }

        private static string TrackId(KugouBridge.SongInfo song) => "kugou_" + (song.AudioId > 0 ? song.AudioId.ToString() : song.Hash.ToUpperInvariant());
        private static string PlaylistId(string id) => "kugou_playlist_" + id;
        private static string RemotePlaylistId(string id) => id?.StartsWith("kugou_playlist_", StringComparison.OrdinalIgnoreCase) == true ? id.Substring("kugou_playlist_".Length) : id;

        public bool HasSettingsUI => true;
        public Action<SlintNode> PushUI { get; set; }
        public SlintNode BuildUI()
        {
            if (!_loggedIn)
            {
                return SlintUi.Column(spacing: 16, padding: 20)
                    .AddChild(SlintUi.Text("酷狗音乐概念版登录", fontSize: 18))
                    .AddChild(SlintUi.Text(_login?.StatusMessage ?? "请获取二维码", fontSize: 12, color: "#94a3b8"))
                    .AddChild(SlintUi.Image("qr_image", $"/api/modules/{ModuleId}/content/qr-image?v={_qrVersion}", width: 200, height: 200))
                    .AddChild(SlintUi.Button("qr_refresh", _login?.QRCodeBytes == null ? "获取二维码" : "刷新二维码"));
            }
            var user = _bridge.GetUserInfo();
            var column = SlintUi.Column(spacing: 16, padding: 20)
                .AddChild(SlintUi.Text("酷狗音乐概念版", fontSize: 18))
                .AddChild(SlintUi.Text($"已登录：{user?.Nickname ?? user?.UserId.ToString()}", fontSize: 12, color: "#4caf50"))
                .AddChild(SlintUi.Text($"已映射 {_playlists.Count} 个概念版账号歌单，全部只读", fontSize: 12, color: "#94a3b8"))
                .AddChild(SlintUi.Text("音质：标准（128 kbps，固定）", fontSize: 12, color: "#94a3b8"));
            if (!string.IsNullOrWhiteSpace(_vipStatus))
                column.AddChild(SlintUi.Text(_vipStatus, fontSize: 12, color: "#4caf50"));
            if (!string.IsNullOrWhiteSpace(_ssaCode))
            {
                column
                    .AddChild(SlintUi.Text(_ssaStatus ?? "需要账号安全验证", fontSize: 12, color: "#f59e0b"));
                if (!string.IsNullOrWhiteSpace(_ssaVerificationUrl))
                    column.AddChild(new SlintNode { Id = "ssa_verify_link", NodeType = "ExternalLink", Text = "打开酷狗安全验证页面", Value = _ssaVerificationUrl, ButtonVariant = "primary" });
                column.AddChild(SlintUi.Button("ssa_verify_done", "检查验证结果"));
            }
            return column
                .AddChild(SlintUi.Button("refresh", "刷新账号歌单"))
                .AddChild(SlintUi.Button("logout", "退出登录", variant: "danger"));
        }

        public void HandleUIEvent(string nodeId, string action, string value)
        {
            switch (nodeId)
            {
                case "qr_refresh": _qrVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); _ = _login.StartAsync(); break;
                case "refresh": _ = RefreshAsync(); break;
                case "ssa_verify_link": _ssaStatus = "请在浏览器中完成验证，然后返回点击“我已完成验证”。"; break;
                case "ssa_verify_done": _ = CheckVerificationAsync(); break;
                case "logout": Logout(); break;
            }
            PushUI?.Invoke(BuildUI());
        }

        public async Task<byte[]> ServeRawContent(string path)
        {
            if (path != "qr-image") return null;
            if (_login?.QRCodeBytes == null || !_login.IsPolling) await _login.StartAsync();
            return _login.QRCodeBytes;
        }

        public string ServeRawContentType(string path) => path == "qr-image" ? "image/png" : null;

        private async Task EnsureDailyVipAsync()
        {
            await _vipClaimLock.WaitAsync();
            try
            {
                if (!_loggedIn || _bridge.ActiveUserId <= 0) return;

                var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var savedDay = _context.ConfigManager.GetValue("VipClaimDate", "");
                var received = string.Equals(savedDay, today, StringComparison.Ordinal) &&
                    _context.ConfigManager.GetValue("VipClaimReceived", false);
                var upgraded = string.Equals(savedDay, today, StringComparison.Ordinal) &&
                    _context.ConfigManager.GetValue("VipClaimUpgraded", false);

                if (!string.Equals(savedDay, today, StringComparison.Ordinal))
                {
                    _context.ConfigManager.SetValue("VipClaimDate", today);
                    _context.ConfigManager.SetValue("VipClaimReceived", false);
                    _context.ConfigManager.SetValue("VipClaimUpgraded", false);
                    _context.ConfigManager.Save();
                }

                if (!received)
                {
                    _vipStatus = "正在领取今日 VIP 权益……";
                    PushUI?.Invoke(BuildUI());
                    var claimSucceeded = await Task.Run(() => _bridge.ClaimDailyVip(today));
                    var error = claimSucceeded ? null : _bridge.GetLastErrorInfo();
                    received = claimSucceeded || IsAlreadyCompleted(error);
                    if (!received)
                    {
                        _vipStatus = $"今日 VIP 领取失败：{error?.Message ?? "未知错误"}";
                        _logger.LogWarning("[酷狗音乐概念版] 每日 VIP 领取失败：{Error}", _bridge.GetLastError());
                        return;
                    }
                    _context.ConfigManager.SetValue("VipClaimReceived", true);
                    _context.ConfigManager.Save();
                }

                if (!upgraded)
                {
                    await Task.Delay(500);
                    var upgradeSucceeded = await Task.Run(_bridge.UpgradeDailyVip);
                    var error = upgradeSucceeded ? null : _bridge.GetLastErrorInfo();
                    upgraded = upgradeSucceeded || IsAlreadyCompleted(error);
                    if (!upgraded)
                    {
                        _vipStatus = $"VIP 已领取，奖励升级失败：{error?.Message ?? "未知错误"}";
                        _logger.LogWarning("[酷狗音乐概念版] 每日 VIP 奖励升级失败：{Error}", _bridge.GetLastError());
                        return;
                    }
                    _context.ConfigManager.SetValue("VipClaimUpgraded", true);
                    _context.ConfigManager.Save();
                }

                _standardPlaybackHashes.Clear();
                _vipStatus = $"今日 VIP 权益已就绪（{today}）";
                _logger.LogInformation("[酷狗音乐概念版] 今日 VIP 权益领取与升级完成：{Date}", today);
            }
            catch (Exception ex)
            {
                _vipStatus = $"今日 VIP 自动领取异常：{ex.Message}";
                _logger.LogWarning(ex, "[酷狗音乐概念版] 每日 VIP 自动领取异常");
            }
            finally
            {
                _vipClaimLock.Release();
                PushUI?.Invoke(BuildUI());
            }
        }

        private static bool IsAlreadyCompleted(KugouBridge.BridgeErrorInfo error)
        {
            if (error?.ApiCode == 131001)
                return true;
            var text = $"{error?.Message} {error?.Cause}";
            return text.Contains("已领取", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("已完成", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("重复", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("already", StringComparison.OrdinalIgnoreCase);
        }

        private async Task CheckVerificationAsync()
        {
            if (Interlocked.Exchange(ref _ssaProbeRunning, 1) == 1) return;
            try
            {
                _ssaStatus = "正在确认验证状态……";
                PushUI?.Invoke(BuildUI());
                var source = _ssaProbeSong == null ? null : await Task.Run(() => _bridge.GetSongUrl(_ssaProbeSong, StandardQuality));
                if (!string.IsNullOrWhiteSpace(source?.Url))
                {
                    var name = _ssaProbeSong?.Name;
                    _ssaCode = null;
                    _ssaVerificationUrl = null;
                    _ssaProbeSong = null;
                    _ssaStatus = $"验证已通过，请重新播放《{name}》。";
                }
                else
                {
                    var error = _bridge.GetLastErrorInfo();
                    _ssaStatus = error?.RequiresVerification == true ? "酷狗尚未确认验证，请检查浏览器中的验证结果后重试。" : $"验证检查失败：{error?.Message ?? "未获得播放地址"}";
                }
            }
            finally
            {
                Interlocked.Exchange(ref _ssaProbeRunning, 0);
                PushUI?.Invoke(BuildUI());
            }
        }

        private void Logout()
        {
            _login?.Cancel();
            _bridge.Logout();
            _loggedIn = false;
            _songs.Clear();
            _playlists.Clear();
            _standardPlaybackHashes.Clear();
            _vipStatus = null;
            _ssaCode = null;
            _ssaVerificationUrl = null;
            _ssaStatus = null;
            _ssaProbeSong = null;
            _context.Library.UnregisterModule(ModuleId);
            SetupLogin();
        }
    }
}

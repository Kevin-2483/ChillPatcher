using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bulbul;
using ChillPatcher.Patches.UIFramework;
using ChillPatcher.SDK.Ipc;
using ChillPatcher.SDK.Models;
using ChillPatcher.SDK.Native;
using ChillPatcher.UIFramework.Music;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ChillPatcher
{
    public class OmniMixIntegration : IDisposable
    {
        private static OmniMixIntegration _instance;
        public static OmniMixIntegration Instance => _instance ??= new OmniMixIntegration();

        private OmniMixIntegration()
        {
            _clientId = ReadInstanceId() ?? "chillpatcher-" + Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Read the instance ID from .omnimix_instance_id in the game root.
        /// Falls back to a random GUID if the file doesn't exist.
        /// </summary>
        private static string ReadInstanceId()
        {
            try
            {
                var dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    var gameRoot = Path.GetDirectoryName(dataPath);
                    if (!string.IsNullOrEmpty(gameRoot))
                    {
                        var idFile = Path.Combine(gameRoot, ".omnimix_instance_id");
                        if (File.Exists(idFile))
                        {
                            var id = File.ReadAllText(idFile).Trim();
                            if (!string.IsNullOrEmpty(id))
                            {
                                Plugin.Log?.LogInfo($"[OmniMix] Using instance ID from file: {id}");
                                return id;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[OmniMix] Failed to read instance ID: {ex.Message}");
            }
            return null;
        }

        private OmniMixPlayerClient _client;
        private OmniPcmShared _shmReader;
        private CancellationTokenSource _heartbeatCts;
        private CancellationTokenSource _syncDebounceCts; // deduplicate queue.changed + instances.changed
        private readonly string _clientId;
        private bool _connected;
        private bool _disposed;
        private readonly SemaphoreSlim _importSemaphore = new SemaphoreSlim(1, 1);

        // Playlist state (synced from backend) — replaces old tag-based system.
        // Each playlist is mapped to a game AudioTag bit for in-game tag filtering.
        private List<TagInfo> _allPlaylists = new List<TagInfo>();
        private Dictionary<string, ulong> _playlistBitMap = new Dictionary<string, ulong>();
        private ulong _nextPlaylistBit = 1uL << 36; // Start at bit 36 to avoid built-in tags

        public IReadOnlyList<TagInfo> GetAllPlaylists() => _allPlaylists;

        /// <summary>Get or assign the game AudioTag bit for a playlist (module) id.</summary>
        public ulong GetPlaylistBit(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return 0;
            lock (_playlistBitMap)
            {
                if (_playlistBitMap.TryGetValue(moduleId, out var bit)) return bit;
                // Assign a new bit (up to 27 playlists, bits 36-62)
                if (_playlistBitMap.Count >= 27) return 0;
                bit = _nextPlaylistBit;
                _nextPlaylistBit <<= 1;
                _playlistBitMap[moduleId] = bit;
                return bit;
            }
        }

        // Song and Album cache
        private Dictionary<string, MusicInfo> _songsCache = new Dictionary<string, MusicInfo>();
        private Dictionary<string, AlbumInfo> _albumsCache = new Dictionary<string, AlbumInfo>();

        public MusicInfo GetCachedSong(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return null;
            return _songsCache.TryGetValue(uuid, out var song) ? song : null;
        }

        public AlbumInfo GetCachedAlbum(string albumId)
        {
            if (string.IsNullOrEmpty(albumId)) return null;
            return _albumsCache.TryGetValue(albumId, out var album) ? album : null;
        }

        // In-memory exclusions (synced from backend)
        private readonly HashSet<string> _excludedUuids = new HashSet<string>();

        public bool IsExcluded(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return false;
            lock (_excludedUuids)
            {
                return _excludedUuids.Contains(uuid);
            }
        }

        private float _currentTrackDuration;
        private float _currentTrackPosition;
        private bool _currentIsPlaying;
        private float _lastPositionUpdateTime;

        public float CurrentTrackDuration => _currentTrackDuration;
        public float CurrentTrackPosition
        {
            get
            {
                if (!_currentIsPlaying)
                    return _currentTrackPosition;
                float elapsed = Time.time - _lastPositionUpdateTime;
                return Mathf.Min(_currentTrackPosition + elapsed, _currentTrackDuration);
            }
        }
        public bool CurrentIsPlaying => _currentIsPlaying;

        public async UniTask Seek(float positionSeconds)
        {
            if (_client != null)
            {
                try
                {
                    await _client.Seek(positionSeconds);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[OmniMix] Seek request failed: {ex.Message}");
                }
            }
        }

        public bool IsConnected => _connected && _client?.IsConnected == true;
        public OmniPcmShared SharedMemory => _shmReader;

        /// <summary>Cached target latency for pre-buffering calculations.</summary>
        public float CurrentTargetLatency { get; private set; } = 0.4f;

        /// <summary>
        /// Unified Unix socket path (fallback IPC).
        /// Windows: %PUBLIC%/OmniMixPlayer/omnimix.sock | Others: /tmp/omnimix.sock
        /// </summary>
        private static string ResolveSocketPath()
        {
            var publicDir = Environment.GetEnvironmentVariable("PUBLIC") ?? Path.GetTempPath();
            var dir = Path.Combine(publicDir, "OmniMixPlayer");
            return Path.Combine(dir, "omnimix.sock");
        }

        /// <summary>
        /// Read IPC port from omni_port.txt in known directories.
        /// </summary>
        private static int? ReadPortFile()
        {
            var dirs = new List<string>
            {
                Path.GetDirectoryName(typeof(OmniMixIntegration).Assembly.Location) ?? "",
                Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? Path.GetTempPath(), "OmniMixPlayer"),
                Path.GetTempPath(),
            };

            // Also check the game root directory (where Flutter writes the port file)
            try
            {
                var dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    var gameRoot = Path.GetDirectoryName(dataPath);
                    if (!string.IsNullOrEmpty(gameRoot))
                        dirs.Insert(0, gameRoot);
                }
            }
            catch { /* Unity may not be ready yet */ }

            foreach (var dir in dirs)
            {
                try
                {
                    var filePath = Path.Combine(dir, "omnimix_port.txt");
                    if (File.Exists(filePath))
                    {
                        var text = File.ReadAllText(filePath).Trim();
                        if (int.TryParse(text, out var port) && port > 0 && port < 65536)
                            return port;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 3-step discovery: port file → default port → socket.
        /// Returns a configured OmniMixPlayerClient, or null.
        /// </summary>
        private static OmniMixPlayerClient DiscoverClient()
        {
            // Step 1: port file
            var port = ReadPortFile();
            if (port.HasValue)
                return new OmniMixPlayerClient(port.Value);

            // Step 2: default port
            return new OmniMixPlayerClient(17890);
        }

        /// <summary>
        /// Try to do a quick TCP health check to see if backend is reachable.
        /// </summary>
        private static bool QuickTcpProbe(int port)
        {
            try
            {
                using var sock = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                sock.Connect("127.0.0.1", port);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Check if a Unix Domain Socket file exists.
        /// On Windows they are IO_REPARSE_TAG_AF_UNIX reparse points —
        /// File.Exists may return false. Use FileInfo as fallback.
        /// </summary>
        private static bool SocketFileExists(string path)
        {
            try
            {
                if (File.Exists(path)) return true;
                // Fallback for reparse points on Windows
                var fi = new FileInfo(path);
                return fi.Exists;
            }
            catch { return false; }
        }

        private CancellationTokenSource _connectionLoopCts;
        private bool _isConnectionLoopRunning;
        private readonly SemaphoreSlim _connectSemaphore = new SemaphoreSlim(1, 1);

        private void StartConnectionLoop()
        {
            if (_isConnectionLoopRunning) return;
            _isConnectionLoopRunning = true;

            // Try starting Windows Service in the background at startup
            _ = System.Threading.Tasks.Task.Run(() => TryStartWindowsService());

            _connectionLoopCts = new CancellationTokenSource();
            ConnectionLoop(_connectionLoopCts.Token).Forget();
        }

        private void StopConnectionLoop()
        {
            _connectionLoopCts?.Cancel();
            _connectionLoopCts?.Dispose();
            _connectionLoopCts = null;
            _isConnectionLoopRunning = false;
        }

        private async UniTaskVoid ConnectionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool alive = false;
                try
                {
                    var port = ReadPortFile();
                    alive = (port.HasValue && QuickTcpProbe(port.Value))
                            || QuickTcpProbe(17890)
                            || SocketFileExists(ResolveSocketPath());
                }
                catch { }

                if (!alive)
                {
                    if (_connected)
                    {
                        Plugin.Log?.LogWarning("[OmniMix] Backend connection lost, cleaning up...");
                        await DisconnectInternalAsync();
                    }
                }
                else
                {
                    if (!_connected)
                    {
                        Plugin.Log?.LogInfo("[OmniMix] Backend detected, attempting to connect...");
                        bool success = await ConnectInternalAsync();
                        if (success)
                        {
                            Plugin.Log?.LogInfo("[OmniMix] Reconnected to backend. Syncing songs...");
                            try
                            {
                                await ImportSongsToGame(replace: true);
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log?.LogWarning($"[OmniMix] Reconnect song sync failed: {ex.Message}");
                            }
                        }
                    }
                }

                await UniTask.Delay(TimeSpan.FromSeconds(5), cancellationToken: ct);
            }
        }

        public async UniTask<bool> ConnectAsync()
        {
            if (_connected && _client?.IsConnected == true)
            {
                StartConnectionLoop();
                return true;
            }

            var connected = await ConnectInternalAsync();
            // Start monitoring only after the explicit first connection
            // attempt completes; otherwise both paths can connect at once.
            StartConnectionLoop();
            return connected;
        }

        private async UniTask<bool> ConnectInternalAsync()
        {
            await _connectSemaphore.WaitAsync();
            try
            {
                if (_connected && _client?.IsConnected == true)
                    return true;
                return await ConnectInternalCoreAsync();
            }
            finally
            {
                _connectSemaphore.Release();
            }
        }

        private async UniTask<bool> ConnectInternalCoreAsync()
        {
            try
            {
                // Step 1: Try port file → TCP
                var port = ReadPortFile();
                if (port.HasValue && QuickTcpProbe(port.Value))
                {
                    Plugin.Log?.LogInfo($"[OmniMix] Backend detected via port file (port {port.Value}), connecting TCP...");
                    _client = new OmniMixPlayerClient(port.Value);
                    goto connect;
                }

                // Step 2: Try default port
                if (QuickTcpProbe(17890))
                {
                    Plugin.Log?.LogInfo("[OmniMix] Backend detected via default port 17890, connecting TCP...");
                    _client = new OmniMixPlayerClient(17890);
                    goto connect;
                }

                // Step 3: Try Unix socket fallback
                var socketPath = ResolveSocketPath();
                if (SocketFileExists(socketPath))
                {
                    Plugin.Log?.LogInfo("[OmniMix] Backend detected via Unix socket, connecting...");
                    _client = new OmniMixPlayerClient(socketPath, useSocket: true);
                    goto connect;
                }

                _connected = false;
                return false;

            connect:
                await _client.ConnectAsync();

                var connectResult = await _client.ConnectInstance(_clientId, "audio", "client");
                var sharedMemoryName = connectResult.GetStringIgnoreCase("sharedMemoryName");

                _connected = true;

                _client.OnTrackChanged += OnTrackChanged;
                _client.OnStateChanged += OnStateChanged;
                _client.OnQueueChanged += OnQueueChanged;
                _client.OnPosition += OnPosition;
                _client.OnPlaylistUpdated += OnPlaylistUpdated;
                _client.OnExcludeChanged += OnExcludeChanged;
                _client.OnInstancesChanged += OnInstancesChanged;

                // 3. Open this instance's shared memory for PCM audio
                _shmReader = new OmniPcmShared(sharedMemoryName);
                StartHeartbeatLoop();

                // 4. Ensure minimum target latency for stable streaming (only if not already set higher)
                try
                {
                    var currentLatency = await _client.GetTargetLatency();
                    if (currentLatency < 0.4f)
                    {
                        await _client.SetTargetLatency(0.4f);
                        CurrentTargetLatency = 0.4f;
                        Plugin.Log?.LogInfo($"[OmniMix] Target latency adjusted: {currentLatency:F2}s -> 0.40s");
                    }
                    else
                    {
                        CurrentTargetLatency = currentLatency;
                    }
                }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[OmniMix] TargetLatency init failed: {ex.Message}"); }

                Plugin.Log?.LogInfo($"[OmniMix] Connected to OmniMixPlayer backend instance {_client.InstanceId} (ClientManaged mode)");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[OmniMix] Failed to connect internally: {ex.Message}");
                await DisconnectInternalAsync();
                return false;
            }
        }

        public async UniTask DisconnectAsync()
        {
            StopConnectionLoop();
            await DisconnectInternalAsync();
        }

        private async UniTask DisconnectInternalAsync()
        {
            try
            {
                if (_client != null)
                {
                    _client.OnTrackChanged -= OnTrackChanged;
                    _client.OnStateChanged -= OnStateChanged;
                    _client.OnQueueChanged -= OnQueueChanged;
                    _client.OnPosition -= OnPosition;
                    _client.OnPlaylistUpdated -= OnPlaylistUpdated;
                    _client.OnExcludeChanged -= OnExcludeChanged;
                    _client.OnInstancesChanged -= OnInstancesChanged;

                    StopSyncDebounce();
                    StopHeartbeatLoop();
                    try { await _client.DisconnectInstance(); } catch { }
                    await _client.DisconnectAsync();
                }
            }
            catch { }
            finally
            {
                _shmReader?.Dispose();
                _shmReader = null;
                _connected = false;

                // Clean up game audio resources and cover cache
                try
                {
                    CleanupStreamAudioClips();
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[OmniMix] Failed to clean up stream audio clips: {ex.Message}");
                }

                try
                {
                    UIFramework.Music.CoverService.Instance.ClearCache();
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[OmniMix] Failed to clear cover cache: {ex.Message}");
                }

                Plugin.Log?.LogInfo("[OmniMix] Disconnected from OmniMixPlayer backend and cleared cached resources");
            }
        }

        private void StartHeartbeatLoop()
        {
            StopHeartbeatLoop();
            _heartbeatCts = new CancellationTokenSource();
            var token = _heartbeatCts.Token;
            UniTask.Void(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: token);
                        if (_client == null || !_connected) continue;
                        var ok = await _client.HeartbeatInstance();
                        if (!ok)
                        {
                            Plugin.Log?.LogWarning("[OmniMix] Instance heartbeat rejected; disconnecting local integration");
                            await DisconnectInternalAsync();
                            break;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[OmniMix] Instance heartbeat failed: {ex.Message}");
                    }
                }
            });
        }

        private void StopHeartbeatLoop()
        {
            try { _heartbeatCts?.Cancel(); } catch { }
            try { _heartbeatCts?.Dispose(); } catch { }
            _heartbeatCts = null;
        }

        private static void TryStartWindowsService()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return;

            try
            {
                // Check if the service is installed
                var queryPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "query OmniMixPlayerBackend",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var queryProcess = System.Diagnostics.Process.Start(queryPsi);
                if (queryProcess == null) { TryDirectExeLaunch(); return; }

                string output = queryProcess.StandardOutput.ReadToEnd();
                queryProcess.WaitForExit();

                if (queryProcess.ExitCode == 0) // Service is installed
                {
                    if (!output.ToLower().Contains("running"))
                    {
                        Plugin.Log?.LogInfo("[OmniMix] Service 'OmniMixPlayerBackend' is installed but not running. Attempting to start it...");

                        var startPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "sc.exe",
                            Arguments = "start OmniMixPlayerBackend",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var startProcess = System.Diagnostics.Process.Start(startPsi);
                        startProcess?.WaitForExit();
                    }
                    else
                    {
                        Plugin.Log?.LogInfo("[OmniMix] Service 'OmniMixPlayerBackend' is already running.");
                    }
                }
                else
                {
                    Plugin.Log?.LogDebug("[OmniMix] Service 'OmniMixPlayerBackend' is not installed. Trying direct exe launch...");
                    TryDirectExeLaunch();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[OmniMix] Failed to check/start Windows service: {ex.Message}");
                TryDirectExeLaunch();
            }
        }

        /// <summary>
        /// Try to launch OmniMixPlayer.Backend.exe directly as a fallback
        /// when the Windows Service is not installed.
        /// </summary>
        private static void TryDirectExeLaunch()
        {
            try
            {
                // Look for the backend exe relative to the mod assembly
                var modDir = Path.GetDirectoryName(typeof(OmniMixIntegration).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;

                // Walk up to find OmniMixPlayer.Backend.exe
                var candidateDirs = new List<string>
                {
                    modDir,
                    Path.Combine(modDir, ".."),
                    Path.Combine(modDir, "..", ".."),
                    Path.Combine(modDir, "OmniMixPlayer"),
                };

                foreach (var dir in candidateDirs)
                {
                    try
                    {
                        var exePath = Path.GetFullPath(Path.Combine(dir, "OmniMixPlayer.Backend.exe"));
                        if (File.Exists(exePath))
                        {
                            Plugin.Log?.LogInfo($"[OmniMix] Launching backend: {exePath}");
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = exePath,
                                UseShellExecute = true,
                                CreateNoWindow = true,
                                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                            };
                            System.Diagnostics.Process.Start(psi);
                            return;
                        }
                    }
                    catch { /* try next candidate */ }
                }

                Plugin.Log?.LogDebug("[OmniMix] OmniMixPlayer.Backend.exe not found in any candidate directory.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[OmniMix] Direct exe launch failed: {ex.Message}");
            }
        }

        private static void CleanupStreamAudioClips()
        {
            var musicService = Patches.UIFramework.MusicService_RemoveLimit_Patch.CurrentInstance;
            if (musicService != null)
            {
                var allMusicList = Traverse.Create(musicService)
                    .Field("_allMusicList")
                    .GetValue<List<GameAudioInfo>>();
                if (allMusicList != null)
                {
                    foreach (var ga in allMusicList)
                    {
                        if (ga != null && UIFramework.Audio.StreamingAudioLoader.IsStreamingSource(ga))
                        {
                            if (ga.AudioClip != null)
                            {
                                UnityEngine.Object.Destroy(ga.AudioClip);
                                ga.AudioClip = null;
                            }
                        }
                    }
                }
            }
        }

        #region Import songs into game MusicService

        /// <summary>
        /// The SDK query methods execute synchronous P/Invoke calls despite
        /// their Task-based signatures. Call this only from a worker thread.
        /// </summary>
        private JToken RunLibraryQuery(Func<Task<JToken>> query)
        {
            return query().GetAwaiter().GetResult();
        }

        private JToken QueryPlaylistSourcesWithRetry()
        {
            JToken result = null;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                result = RunLibraryQuery(() => _client.GetPlaylistSources());
                if (result is JArray array && array.Count > 0)
                    return result;

                Plugin.Log?.LogDebug($"[OmniMix] Playlist sources not ready; retry {attempt}/5");
                if (attempt < 5)
                    Thread.Sleep(200);
            }
            return result ?? new JArray();
        }

        public async UniTask<int> ImportSongsToGame(bool replace = false)
        {
            await _importSemaphore.WaitAsync();
            try
            {
                var musicService = MusicService_RemoveLimit_Patch.CurrentInstance;
                if (musicService == null)
                {
                    Plugin.Log?.LogWarning("[OmniMix] MusicService not available, cannot import songs");
                    return 0;
                }

                // ── Phase 1: All native queries (thread-pool safe, P/Invoke) ──
                var sourcesJson = await UniTask.RunOnThreadPool(QueryPlaylistSourcesWithRetry);
                var albumsJson = await UniTask.RunOnThreadPool(
                    () => RunLibraryQuery(() => _client.GetAlbums()));
                var queueJson = await UniTask.RunOnThreadPool(
                    () => RunLibraryQuery(() => _client.GetQueue()));

                // Build source → tag bit mapping (lock-protected, thread-safe)
                var sourceTagBits = new List<(string refId, ulong bit, string name)>();
                lock (_playlistBitMap) { _playlistBitMap.Clear(); _nextPlaylistBit = 1uL << 5; }
                _allPlaylists.Clear();

                if (sourcesJson is JArray srcArr && srcArr.Count > 0)
                {
                    foreach (var s in srcArr)
                    {
                        var refId = s.GetStringIgnoreCase("refId") ?? "";
                        if (string.IsNullOrEmpty(refId)) continue;
                        var name = s.GetStringIgnoreCase("name") ?? refId;
                        var bit = GetPlaylistBit(refId);
                        sourceTagBits.Add((refId, bit, name));
                    }
                }

                // Query all tracks once for metadata, then per-playlist for tag membership
                var allTracksJson = await UniTask.RunOnThreadPool(
                    () => RunLibraryQuery(() => _client.GetSongs()));
                var perSourceResults = new List<(ulong bit, JToken tracks)>();
                foreach (var (refId, bit, _) in sourceTagBits)
                {
                    var tracks = await UniTask.RunOnThreadPool(
                        () => RunLibraryQuery(() => _client.GetSongsByPlaylist(refId)));
                    perSourceResults.Add((bit, tracks));
                }

                // ── Phase 2: Main thread for Unity object creation ──
                await UniTask.SwitchToMainThread();

                // Build tag list (must be on main thread for UI)
                foreach (var (refId, bit, name) in sourceTagBits)
                {
                    _allPlaylists.Add(new TagInfo
                    {
                        TagId = refId,
                        DisplayName = name,
                        ModuleId = refId,
                        BitValue = bit,
                        IsGrowableList = false,
                    });
                }
                Plugin.Log?.LogInfo($"[OmniMix] Built {_allPlaylists.Count} playlist tags from sources");

                // Parse album cache
                var albumDict = new Dictionary<string, AlbumInfo>();
                if (albumsJson is JArray albumsArr)
                {
                    foreach (var a in albumsArr)
                    {
                        var id = a.GetStringIgnoreCase("id");
                        if (string.IsNullOrEmpty(id)) continue;
                        albumDict[id] = new AlbumInfo
                        {
                            AlbumId = id,
                            DisplayName = a.GetStringIgnoreCase("name") ?? "",
                            Artist = a.GetStringIgnoreCase("artist") ?? "",
                            CoverPath = a.GetStringIgnoreCase("coverPath") ?? "",
                            ModuleId = a.GetStringIgnoreCase("moduleId") ?? "",
                        };
                    }
                }

                // uuid → accumulated tag bits
                var uuidTagBits = new Dictionary<string, ulong>();
                var songDict = new Dictionary<string, MusicInfo>();
                var seenUuids = new HashSet<string>();
                var orderedUuids = new List<string>();
                var orderedUuidSet = new HashSet<string>();

                if (allTracksJson is JArray allTracksArr)
                {
                    foreach (var s in allTracksArr)
                    {
                        var uuid = s.GetStringIgnoreCase("uuid");
                        if (string.IsNullOrEmpty(uuid) || !seenUuids.Add(uuid)) continue;
                        songDict[uuid] = new MusicInfo
                        {
                            UUID = uuid,
                            Title = s.GetStringIgnoreCase("title") ?? "",
                            Artist = s.GetStringIgnoreCase("artist") ?? "",
                            AlbumId = s.GetStringIgnoreCase("albumId") ?? "",
                            Duration = s.GetValueIgnoreCase<float>("duration"),
                            ModuleId = s.GetStringIgnoreCase("moduleId") ?? "",
                            SourceType = MusicSourceType.Stream,
                            IsUnlocked = true,
                            IsFavorite = s.GetValueIgnoreCase<bool>("isFavorite"),
                        };
                        uuidTagBits[uuid] = 0;
                    }
                }
                Plugin.Log?.LogInfo($"[OmniMix] Loaded {songDict.Count} tracks from library");

                // Accumulate tag bits from each playlist source
                foreach (var (bit, tracksJson) in perSourceResults)
                {
                    int matched = 0;
                    if (tracksJson is JArray tracksArr)
                    {
                        foreach (var t in tracksArr)
                        {
                            var uuid = t.GetStringIgnoreCase("uuid");
                            if (!string.IsNullOrEmpty(uuid) && uuidTagBits.ContainsKey(uuid))
                            {
                                uuidTagBits[uuid] |= bit;
                                if (orderedUuidSet.Add(uuid))
                                    orderedUuids.Add(uuid);
                                matched++;
                            }
                        }
                    }
                    Plugin.Log?.LogInfo($"[OmniMix] Source bit={bit} matched {matched} tracks");
                }

                // Build lookup: uuid → tag value (no GameAudioInfo allocation yet)
                var uuidTagMap = new Dictionary<string, ulong>();
                foreach (var kvp in songDict)
                {
                    var mi = kvp.Value;
                    var tagValue = uuidTagBits.TryGetValue(mi.UUID, out var b) ? b : 0;
                    if (tagValue != 0)
                    {
                        mi.TagIds = new List<string> { $"bit_{tagValue}" };
                        uuidTagMap[mi.UUID] = tagValue;
                    }
                }
                Plugin.Log?.LogInfo($"[OmniMix] Built {uuidTagMap.Count} uuid→tag mappings");

                // Supplement with queue items
                if (queueJson is JArray queueArr)
                {
                    foreach (var item in queueArr)
                    {
                        var uuid = item.GetStringIgnoreCase("uuid");
                        if (string.IsNullOrEmpty(uuid) || !seenUuids.Add(uuid)) continue;

                        var moduleId = item.GetStringIgnoreCase("moduleId") ?? "";
                        var tagValue = GetPlaylistBit(moduleId);

                        var mi = new MusicInfo
                        {
                            UUID = uuid,
                            Title = item.GetStringIgnoreCase("title") ?? "",
                            Artist = item.GetStringIgnoreCase("artist") ?? "",
                            AlbumId = item.GetStringIgnoreCase("albumId") ?? "",
                            Duration = item.GetValueIgnoreCase<float>("duration"),
                            ModuleId = moduleId,
                            SourceType = MusicSourceType.Stream,
                            IsUnlocked = true,
                            TagIds = new List<string> { moduleId },
                        };
                        if (!songDict.ContainsKey(uuid))
                            songDict[uuid] = mi;

                        uuidTagMap[uuid] = tagValue;
                        if (orderedUuidSet.Add(uuid))
                            orderedUuids.Add(uuid);
                    }
                }

                // Include queue-only or otherwise unassigned imported items
                // after all source-ordered entries.
                foreach (var uuid in uuidTagMap.Keys)
                {
                    if (orderedUuidSet.Add(uuid))
                        orderedUuids.Add(uuid);
                }

                // Cache for UI
                _songsCache = songDict;
                _albumsCache = albumDict;

                // ── Step 6: Inject into game MusicService ──
                var allMusicList = Traverse.Create(musicService)
                    .Field("_allMusicList")
                    .GetValue<List<GameAudioInfo>>();

                if (allMusicList == null)
                {
                    Plugin.Log?.LogWarning("[OmniMix] Cannot access _allMusicList");
                    return 0;
                }

                // Build UUID → existing GameAudioInfo index for O(1) lookup
                var existingIndex = new Dictionary<string, int>();
                for (int i = 0; i < allMusicList.Count; i++)
                {
                    var a = allMusicList[i];
                    if (a != null && !string.IsNullOrEmpty(a.UUID))
                        existingIndex[a.UUID] = i;
                }

                if (replace)
                {
                    allMusicList.RemoveAll(a => ((ulong)a.Tag & ~31UL) != 0);
                    existingIndex.Clear();
                }

                var newUuids = new HashSet<string>(uuidTagMap.Keys);
                int addedCount = 0;

                foreach (var uuid in orderedUuids)
                {
                    if (!uuidTagMap.TryGetValue(uuid, out var tagValue))
                        continue;
                    if (tagValue == 0) continue;

                    if (existingIndex.TryGetValue(uuid, out var idx))
                    {
                        // Update tag bits in-place — no allocation
                        allMusicList[idx].Tag = (AudioTag)tagValue;
                    }
                    else
                    {
                        // Only allocate GameAudioInfo for genuinely new songs
                        if (songDict.TryGetValue(uuid, out var mi))
                            allMusicList.Add(ConvertToGameAudio(mi, tagValue));
                        addedCount++;
                    }
                }
                Plugin.Log?.LogInfo($"[OmniMix] Updated {uuidTagMap.Count - addedCount} existing, added {addedCount} new songs");

                // Remove imported songs no longer in any playlist source
                allMusicList.RemoveAll(a => ((ulong)a.Tag & ~31UL) != 0 && !newUuids.Contains(a.UUID));

                // The per-source queries already follow playlist_entries.Position.
                // Rebuild only the imported segment in that order; dictionary
                // iteration would otherwise fall back to global-library order.
                var importedByUuid = new Dictionary<string, GameAudioInfo>();
                foreach (var audio in allMusicList)
                {
                    if (audio != null && ((ulong)audio.Tag & ~31UL) != 0 &&
                        !string.IsNullOrEmpty(audio.UUID) && !importedByUuid.ContainsKey(audio.UUID))
                    {
                        importedByUuid[audio.UUID] = audio;
                    }
                }
                allMusicList.RemoveAll(a => a != null && ((ulong)a.Tag & ~31UL) != 0);
                foreach (var uuid in orderedUuids)
                {
                    if (importedByUuid.TryGetValue(uuid, out var audio))
                        allMusicList.Add(audio);
                }

                // Sync to current playlist (use same uuid→tag map, no extra allocations)
                var currentPlayList = musicService.CurrentPlayList;
                if (currentPlayList != null)
                {
                    if (replace)
                    {
                        for (int i = currentPlayList.Count - 1; i >= 0; i--)
                        {
                            if (((ulong)currentPlayList[i].Tag & ~31UL) != 0)
                                currentPlayList.RemoveAt(i);
                        }
                    }

                    // Build index for currentPlayList
                    var cpIndex = new Dictionary<string, int>();
                    for (int i = 0; i < currentPlayList.Count; i++)
                    {
                        var a = currentPlayList[i];
                        if (a != null && !string.IsNullOrEmpty(a.UUID))
                            cpIndex[a.UUID] = i;
                    }

                    var currentTag = SaveDataManager.Instance?.MusicSetting?.CurrentAudioTag?.CurrentValue ?? AudioTag.All;
                    foreach (var uuid in orderedUuids)
                    {
                        if (!uuidTagMap.TryGetValue(uuid, out var tagValue) || tagValue == 0)
                            continue;
                        if (cpIndex.TryGetValue(uuid, out var idx))
                        {
                            currentPlayList[idx].Tag = (AudioTag)tagValue;
                        }
                        else if (currentTag.HasFlagFast((AudioTag)tagValue))
                        {
                            if (songDict.TryGetValue(uuid, out var mi))
                                currentPlayList.Add(ConvertToGameAudio(mi, tagValue));
                        }
                    }
                    // Remove stale imported songs from current playlist
                    for (int i = currentPlayList.Count - 1; i >= 0; i--)
                    {
                        if (((ulong)currentPlayList[i].Tag & ~31UL) != 0 && !newUuids.Contains(currentPlayList[i].UUID))
                            currentPlayList.RemoveAt(i);
                    }

                    var currentImportedByUuid = new Dictionary<string, GameAudioInfo>();
                    for (int i = 0; i < currentPlayList.Count; i++)
                    {
                        var audio = currentPlayList[i];
                        if (audio != null && ((ulong)audio.Tag & ~31UL) != 0 &&
                            !string.IsNullOrEmpty(audio.UUID) && !currentImportedByUuid.ContainsKey(audio.UUID))
                        {
                            currentImportedByUuid[audio.UUID] = audio;
                        }
                    }
                    for (int i = currentPlayList.Count - 1; i >= 0; i--)
                    {
                        var audio = currentPlayList[i];
                        if (audio != null && ((ulong)audio.Tag & ~31UL) != 0)
                            currentPlayList.RemoveAt(i);
                    }
                    foreach (var uuid in orderedUuids)
                    {
                        if (currentImportedByUuid.TryGetValue(uuid, out var audio))
                            currentPlayList.Add(audio);
                    }
                }

                int totalSynced = uuidTagMap.Count;
                Plugin.Log?.LogInfo($"[OmniMix] Synced {totalSynced} songs to game MusicService");

                try { MusicUI_VirtualScroll_Patch.RefreshPlaylistDisplay(); }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[OmniMix] Failed to refresh playlist display: {ex.Message}"); }

                try { MusicTagListUI_Patches.RefreshCustomTagButtons(); }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[OmniMix] Failed to refresh custom tag buttons: {ex.Message}"); }

                return totalSynced;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[OmniMix] Failed to import songs: {ex}");
                return 0;
            }
            finally
            {
                _importSemaphore.Release();
            }
        }

        private static GameAudioInfo ConvertToGameAudio(MusicInfo mi, ulong tagValue)
        {
            if (mi.IsFavorite)
                tagValue |= (ulong)AudioTag.Favorite;

            return new GameAudioInfo
            {
                UUID = mi.UUID,
                Title = mi.Title ?? "",
                Credit = mi.Artist ?? "",
                Tag = (AudioTag)tagValue,
                IsUnlocked = true,
                PathType = AudioMode.LocalPc,
                LocalPath = "",
                AudioClip = null
            };
        }

        #endregion

        #region Playlist query (for JSApi)

        public async UniTask<string> GetAlbumsJson(string tagId = null)
        {
            try { var r = await _client.GetAlbums(tagId); return r.ToString(); } catch { return "[]"; }
        }

        public async UniTask<string> GetSongsJson(string albumId = null, string tagId = null)
        {
            try { var r = await _client.GetSongs(albumId, tagId); return r.ToString(); } catch { return "[]"; }
        }

        public async UniTask<(byte[] data, string mime)> GetCoverAsync(string uuid)
        {
            try { return await _client.GetTrackCover(uuid); } catch { return (null, null); }
        }

        public async UniTask<(byte[] data, string mime)> GetAlbumCoverAsync(string coverPath)
        {
            if (string.IsNullOrEmpty(coverPath)) return (null, null);
            try
            {
                var path = "/" + coverPath.TrimStart('/');
                return await _client.GetBytesAsync(path);
            }
            catch { return (null, null); }
        }

        #endregion

        #region Playback control (for JSApi)

        /// <summary>Play a specific track. The game decides the UUID via its own queue system.</summary>
        public async UniTask Play(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            try { await _client.Play(uuid); } catch { }
        }
        public async UniTask Pause() { try { await _client.Pause(); } catch { } }
        public async UniTask Resume() { try { await _client.Resume(); } catch { } }

        // NOTE: Next/Prev/Toggle are NOT exposed — the game manages its own queue and
        // playback flow. The game determines the next UUID and calls Play(uuid).

        public async UniTask SetFavorite(string uuid, bool fav)
        {
            try { await _client.PostAsync($"/favorite", new { uuid, isFavorite = fav }); } catch { }
        }
        public async UniTask SetExcluded(string uuid, bool excluded)
        {
            try { await _client.PostAsync($"/exclude", new { uuid, isFavorite = excluded }); } catch { }
        }

        #endregion

        #region Playlist Management


        // ── Backward-compat stubs for patches that still reference old tag API ──

        /// <summary>[Compat] Alias for GetAllPlaylists.</summary>
        public IReadOnlyList<TagInfo> GetAllTags() => _allPlaylists;

        /// <summary>[Compat] Growable tags are not yet ported to playlist system.</summary>
        public IReadOnlyList<TagInfo> GetGrowableTags() => new List<TagInfo>();

        /// <summary>[Compat] Returns null until growable playlist support is added.</summary>
        public TagInfo GetCurrentGrowableTag() => null;

        /// <summary>[Compat] No-op until growable playlist support is added.</summary>
        public UniTask SetCurrentGrowableTag(string tagId) => UniTask.CompletedTask;

        /// <summary>[Compat] Returns 0 until growable playlist support is added.</summary>
        public UniTask<int> TriggerGrowableLoadMore(string tagId) => UniTask.FromResult(0);

        #endregion

        #region Events

        public event Action<string, string, string, float> OnTrackUpdated;
        public event Action<bool, float, float> OnStateUpdated;
        public event Action OnQueueUpdated;

        private void OnTrackChanged(object sender, TrackChangedEventArgs e)
        {
            _currentTrackDuration = e.Duration;
            _currentTrackPosition = 0f;
            _lastPositionUpdateTime = Time.time;
            OnTrackUpdated?.Invoke(e.Uuid, e.Title, e.Artist, e.Duration);

            // 同步播放状态到游戏内，传入事件参数以防未导入时动态生成虚拟轨道
            SyncTrackToGameAsync(e.Uuid, e.Title, e.Artist).Forget();
        }

        private async UniTaskVoid SyncTrackToGameAsync(string uuid, string title, string artist)
        {
            if (string.IsNullOrEmpty(uuid)) return;

            await UniTask.SwitchToMainThread();

            var musicService = MusicService_RemoveLimit_Patch.CurrentInstance;
            if (musicService == null) return;

            // 如果已经是当前播放的歌曲，无需重复触发
            if (musicService.PlayingMusic != null && musicService.PlayingMusic.UUID == uuid)
                return;

            var targetAudio = musicService.AllMusicList?.FirstOrDefault(m => m != null && m.UUID == uuid);
            if (targetAudio == null)
            {
                // 1. 如果找不到，说明歌单可能落后，尝试从后端增量同步
                Plugin.Log?.LogInfo($"[OmniMix] Track {uuid} not found in game list, forcing a playlist refresh...");
                await ImportSongsToGame(replace: false);
                targetAudio = musicService.AllMusicList?.FirstOrDefault(m => m != null && m.UUID == uuid);
            }

            if (targetAudio == null)
            {
                // 2. 如果依然找不到（例如是通过外部临时播放的单曲/搜索结果），动态生成一个虚拟歌曲信息注入游戏，确保 UI 能够正确显示
                Plugin.Log?.LogInfo($"[OmniMix] Track {uuid} still not found after refresh, dynamically creating temporary track info...");

                var virtualMusic = new MusicInfo
                {
                    UUID = uuid,
                    Title = string.IsNullOrEmpty(title) ? "OmniMix Track" : title,
                    Artist = string.IsNullOrEmpty(artist) ? "" : artist,
                    Duration = _currentTrackDuration,
                    SourceType = MusicSourceType.Stream,
                    IsUnlocked = true
                };

                var allMusicList = Traverse.Create(musicService)
                    .Field("_allMusicList")
                    .GetValue<List<GameAudioInfo>>();

                if (allMusicList != null)
                {
                    targetAudio = ConvertToGameAudio(virtualMusic, 0);
                    allMusicList.Add(targetAudio);
                }
            }

            if (targetAudio != null)
            {
                Plugin.Log?.LogInfo($"[OmniMix] Syncing track change from backend: {targetAudio.AudioClipName}");
                musicService.PlayArugumentMusic(targetAudio, MusicChangeKind.Manual);
            }
        }

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            // During Unity shutdown the native SDK thread may still fire events.
            // Calling UnityEngine.Time.time after the engine has started tearing
            // down causes a native access violation that cannot be caught.
            if (_disposed) return;

            _currentIsPlaying = e.IsPlaying;
            _currentTrackPosition = e.Position;
            _lastPositionUpdateTime = Time.time;
            OnStateUpdated?.Invoke(e.IsPlaying, e.Position, e.Volume);
        }

        private void OnQueueChanged(object sender, QueueChangedEventArgs e)
        {
            OnQueueUpdated?.Invoke();
            // queue.changed may arrive together with instances.changed — debounce to avoid double import
            DebouncedSyncSongs();
        }

        private void OnInstancesChanged(object sender, InstancesChangedEventArgs e)
        {
            Plugin.Log?.LogInfo($"[OmniMix] Instances changed (count={e.InstanceCount}), triggering playlist sync...");
            // instances.changed may arrive together with queue.changed — debounce to avoid double import
            DebouncedSyncSongs();
        }

        /// <summary>
        /// Debounced song/playlist sync.  When queue.changed and instances.changed arrive
        /// close together (which they often do), only one full ImportSongsToGame runs.
        /// </summary>
        private void DebouncedSyncSongs()
        {
            StopSyncDebounce();
            _syncDebounceCts = new CancellationTokenSource();
            var token = _syncDebounceCts.Token;
            UniTask.Void(async () =>
            {
                try
                {
                    // Wait a short window so that rapidly-firing events coalesce
                    await UniTask.Delay(TimeSpan.FromMilliseconds(250), cancellationToken: token);
                    if (token.IsCancellationRequested) return;
                    Plugin.Log?.LogInfo("[OmniMix] Debounced sync: importing songs...");
                    await ImportSongsToGame(false);
                }
                catch (OperationCanceledException) { /* coalesced */ }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[OmniMix] Debounced sync failed: {ex.Message}");
                }
            });
        }

        private void StopSyncDebounce()
        {
            if (_syncDebounceCts != null)
            {
                _syncDebounceCts.Cancel();
                _syncDebounceCts.Dispose();
                _syncDebounceCts = null;
            }
        }

        private void OnPosition(object sender, PositionEventArgs e)
        {
            // During Unity shutdown the native SDK thread may still fire events.
            // Calling UnityEngine.Time.time after the engine has started tearing
            // down causes a native access violation that cannot be caught.
            if (_disposed) return;

            _currentTrackPosition = e.Position;
            _lastPositionUpdateTime = Time.time;
        }

        private void OnPlaylistUpdated(object sender, PlaylistUpdatedEventArgs e)
        {
            // When backend pushes new songs (e.g. from growable list load-more),
            // re-import them into the game's MusicService
            Plugin.Log?.LogInfo("[OmniMix] Playlist updated, re-importing songs...");
            DebouncedSyncSongs();
        }

        private void OnExcludeChanged(object sender, ExcludeChangedEventArgs e)
        {
            lock (_excludedUuids)
            {
                if (e.IsExcluded)
                    _excludedUuids.Add(e.Uuid);
                else
                    _excludedUuids.Remove(e.Uuid);
            }

            try
            {
                MusicService_Excluded_Patch.RaiseOnSongExcludedChanged(e.Uuid, e.IsExcluded);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[OmniMix] Failed to raise UI exclusion change event: {ex}");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSyncDebounce();
            _ = DisconnectAsync();
        }
    }
}

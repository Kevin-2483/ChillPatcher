using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云音乐桥接 - Go DLL 互操作
    /// DLL 由 IDependencyLoader 加载，此类只负责调用
    /// </summary>
    public class NeteaseBridge
    {
        private const string DLL_NAME = "ChillNetease";
        private bool _initialized = false;
        private readonly ManualLogSource _logger;

        #region P/Invoke Declarations

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int NeteaseInit(string dataDir);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseIsLoggedIn();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetUserInfo();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseRefreshLogin();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetLikeSongs(int getAll);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr NeteaseGetSongURL(long songId, string quality);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetLastError();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NeteaseFreeString(IntPtr ptr);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int NeteaseSetCookie(string cookieStr);

        // PCM 流 API
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern long NeteaseCreatePcmStream(long songId, string quality);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetPcmStreamInfo(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseReadPcmFrames(long streamId, IntPtr buffer, int framesToRead);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NeteaseClosePcmStream(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseIsPcmStreamReady(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetPcmStreamError(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseSeekPcmStream(long streamId, long frameIndex);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseCanSeekPcmStream(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern double NeteaseGetCacheProgress(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseHasPendingSeek(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern long NeteaseGetPendingSeekFrame(long streamId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NeteaseCancelPendingSeek(long streamId);

        // 收藏相关 API
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseLikeSong(long songId, int like);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetLikeList();

        // 个人 FM 相关 API
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetPersonalFM();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NeteaseFMTrash(long songId);

        // 二维码登录 API
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseQRGetKey();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseQRCheckStatus();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NeteaseQRCancelLogin();

        // 歌单相关 API
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetUserPlaylists(int limit, int offset);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetPlaylistSongs(long playlistId, int getAll);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NeteaseGetPlaylistDetail(long playlistId);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr NeteaseSearchPlaylistsByKeyword(string keyword);

        #endregion

        #region Data Classes

        /// <summary>
        /// 网易云用户信息
        /// </summary>
        public class UserInfo
        {
            [JsonProperty("userId")]
            public long UserId { get; set; }

            [JsonProperty("nickname")]
            public string Nickname { get; set; }

            [JsonProperty("avatarUrl")]
            public string AvatarUrl { get; set; }
        }

        /// <summary>
        /// 网易云歌曲信息
        /// </summary>
        public class SongInfo
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("duration")]
            public double Duration { get; set; } // 秒

            [JsonProperty("artists")]
            public List<string> Artists { get; set; }

            [JsonProperty("album")]
            public string Album { get; set; }

            [JsonProperty("albumId")]
            public long AlbumId { get; set; }

            [JsonProperty("coverUrl")]
            public string CoverUrl { get; set; }

            /// <summary>
            /// 获取艺术家名称字符串
            /// </summary>
            public string ArtistName => Artists != null ? string.Join(", ", Artists) : "";
        }

        /// <summary>
        /// 歌曲播放 URL
        /// </summary>
        public class SongUrl
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; } // mp3, flac, etc.
        }

        /// <summary>
        /// 音质等级
        /// </summary>
        public enum Quality
        {
            /// <summary>标准 128kbps</summary>
            Standard,
            /// <summary>较高 192kbps</summary>
            Higher,
            /// <summary>极高 320kbps</summary>
            ExHigh,
            /// <summary>无损 FLAC</summary>
            Lossless,
            /// <summary>Hi-Res 24bit</summary>
            HiRes,
            /// <summary>高清环绕声</summary>
            JYEffect,
            /// <summary>沉浸环绕声</summary>
            Sky,
            /// <summary>超清母带</summary>
            JYMaster
        }

        /// <summary>
        /// PCM 流信息
        /// </summary>
        public class PcmStreamInfo
        {
            [JsonProperty("streamId")]
            public long StreamId { get; set; }

            [JsonProperty("sampleRate")]
            public int SampleRate { get; set; }

            [JsonProperty("channels")]
            public int Channels { get; set; }

            [JsonProperty("totalFrames")]
            public ulong TotalFrames { get; set; }

            [JsonProperty("isReady")]
            public bool IsReady { get; set; }

            [JsonProperty("canSeek")]
            public bool CanSeek { get; set; }

            [JsonProperty("isEOF")]
            public bool IsEOF { get; set; }

            [JsonProperty("format")]
            public string Format { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; }
        }

        /// <summary>
        /// 二维码登录状态
        /// </summary>
        public class QRLoginState
        {
            [JsonProperty("uniKey")]
            public string UniKey { get; set; }

            [JsonProperty("qrcodeUrl")]
            public string QRCodeURL { get; set; }

            [JsonProperty("statusCode")]
            public int StatusCode { get; set; }

            [JsonProperty("statusMsg")]
            public string StatusMsg { get; set; }

            /// <summary>
            /// 是否已失效
            /// </summary>
            public bool IsExpired => StatusCode == 800;

            /// <summary>
            /// 是否等待扫码
            /// </summary>
            public bool IsWaitingScan => StatusCode == 801;

            /// <summary>
            /// 是否已扫码等待确认
            /// </summary>
            public bool IsWaitingConfirm => StatusCode == 802;

            /// <summary>
            /// 是否登录成功
            /// </summary>
            public bool IsSuccess => StatusCode == 803;
        }

        /// <summary>
        /// 歌单信息
        /// </summary>
        public class PlaylistInfo
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("songCount")]
            public int SongCount { get; set; }

            [JsonProperty("coverUrl")]
            public string CoverUrl { get; set; }

            [JsonProperty("creatorId")]
            public long CreatorId { get; set; }
        }

        /// <summary>
        /// 用户歌单列表响应
        /// </summary>
        public class PlaylistsResponse
        {
            [JsonProperty("playlists")]
            public List<PlaylistInfo> Playlists { get; set; }

            [JsonProperty("hasMore")]
            public bool HasMore { get; set; }
        }

        /// <summary>
        /// 歌单详情（包含歌曲）
        /// </summary>
        public class PlaylistDetail
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("songCount")]
            public int SongCount { get; set; }

            [JsonProperty("coverUrl")]
            public string CoverUrl { get; set; }

            [JsonProperty("creatorId")]
            public long CreatorId { get; set; }

            [JsonProperty("songs")]
            public List<SongInfo> Songs { get; set; }
        }

        #endregion

        public NeteaseBridge(ManualLogSource logger)
        {
            _logger = logger;
        }

        #region Public API

        /// <summary>
        /// 初始化网易云桥接
        /// </summary>
        /// <param name="dataDir">数据目录，null 使用默认路径</param>
        /// <returns>是否成功</returns>
        public bool Initialize(string dataDir = null)
        {
            if (_initialized)
                return true;

            try
            {
                var result = NeteaseInit(dataDir ?? "");
                _initialized = result == 1;

                if (!_initialized)
                {
                    _logger.LogError($"[NeteaseBridge] Init failed: {GetLastErrorMessage()}");
                }
                else
                {
                    _logger.LogInfo("[NeteaseBridge] ✅ Initialized successfully");
                }

                return _initialized;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] Init exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// 检查是否已登录
        /// </summary>
        public bool IsLoggedIn
        {
            get
            {
                if (!_initialized) return false;
                try
                {
                    return NeteaseIsLoggedIn() == 1;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取用户信息
        /// </summary>
        public UserInfo GetUserInfo()
        {
            if (!_initialized) return null;

            try
            {
                var ptr = NeteaseGetUserInfo();
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetUserInfo failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                return JsonConvert.DeserializeObject<UserInfo>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetUserInfo exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 刷新登录状态
        /// </summary>
        public bool RefreshLogin()
        {
            if (!_initialized) return false;

            try
            {
                var result = NeteaseRefreshLogin();
                if (result != 1)
                {
                    _logger.LogWarning($"[NeteaseBridge] RefreshLogin failed: {GetLastErrorMessage()}");
                }
                return result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] RefreshLogin exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 获取收藏歌曲列表
        /// </summary>
        /// <param name="getAll">是否获取全部（否则只获取部分）</param>
        public List<SongInfo> GetLikeSongs(bool getAll = true)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetLikeSongs(getAll ? 1 : 0);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetLikeSongs failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var songs = JsonConvert.DeserializeObject<List<SongInfo>>(json);
                _logger.LogInfo($"[NeteaseBridge] Got {songs?.Count ?? 0} like songs");
                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetLikeSongs exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 获取歌曲播放 URL
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <param name="quality">音质</param>
        public SongUrl GetSongUrl(long songId, Quality quality = Quality.ExHigh)
        {
            if (!_initialized) return null;

            try
            {
                var qualityStr = quality switch
                {
                    Quality.Standard => "standard",
                    Quality.Higher => "higher",
                    Quality.ExHigh => "exhigh",
                    Quality.Lossless => "lossless",
                    Quality.HiRes => "hires",
                    Quality.JYEffect => "jyeffect",
                    Quality.Sky => "sky",
                    Quality.JYMaster => "jymaster",
                    _ => "exhigh"
                };

                var ptr = NeteaseGetSongURL(songId, qualityStr);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetSongUrl failed for {songId}: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                return JsonConvert.DeserializeObject<SongUrl>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetSongUrl exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 设置 Cookie
        /// </summary>
        public bool SetCookie(string cookieStr)
        {
            if (!_initialized) return false;

            try
            {
                var result = NeteaseSetCookie(cookieStr);
                return result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] SetCookie exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 获取最后一个错误消息
        /// </summary>
        public string GetLastErrorMessage()
        {
            try
            {
                var ptr = NeteaseGetLastError();
                if (ptr == IntPtr.Zero) return null;

                var error = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);
                return error;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 收藏歌曲
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <returns>是否成功</returns>
        public bool LikeSong(long songId)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return false;
            }

            try
            {
                var result = NeteaseLikeSong(songId, 1);
                if (result == 1)
                {
                    _logger.LogInfo($"[NeteaseBridge] ✅ Liked song {songId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"[NeteaseBridge] Failed to like song {songId}: {GetLastErrorMessage()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] LikeSong exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 取消收藏歌曲
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <returns>是否成功</returns>
        public bool UnlikeSong(long songId)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return false;
            }

            try
            {
                var result = NeteaseLikeSong(songId, 0);
                if (result == 1)
                {
                    _logger.LogInfo($"[NeteaseBridge] ✅ Unliked song {songId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"[NeteaseBridge] Failed to unlike song {songId}: {GetLastErrorMessage()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] UnlikeSong exception: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 设置歌曲收藏状态
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <param name="like">true = 收藏, false = 取消收藏</param>
        /// <returns>是否成功</returns>
        public bool SetLikeStatus(long songId, bool like)
        {
            return like ? LikeSong(songId) : UnlikeSong(songId);
        }

        /// <summary>
        /// 获取用户收藏的歌曲 ID 列表
        /// </summary>
        /// <returns>收藏歌曲的 ID 列表，失败返回 null</returns>
        public List<long> GetLikeList()
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetLikeList();
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetLikeList failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var likeIds = JsonConvert.DeserializeObject<List<long>>(json);
                _logger.LogInfo($"[NeteaseBridge] Got {likeIds?.Count ?? 0} liked song IDs");
                return likeIds;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetLikeList exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 检查歌曲是否已收藏
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <param name="likeList">收藏列表（可选，不提供则自动获取）</param>
        /// <returns>是否已收藏</returns>
        public bool IsSongLiked(long songId, List<long> likeList = null)
        {
            if (likeList == null)
            {
                likeList = GetLikeList();
            }
            return likeList?.Contains(songId) ?? false;
        }

        /// <summary>
        /// 获取个人 FM 推荐歌曲
        /// </summary>
        /// <returns>推荐歌曲列表，失败返回 null</returns>
        public List<SongInfo> GetPersonalFM()
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetPersonalFM();
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetPersonalFM failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var songs = JsonConvert.DeserializeObject<List<SongInfo>>(json);
                _logger.LogInfo($"[NeteaseBridge] Got {songs?.Count ?? 0} FM songs");
                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetPersonalFM exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 将歌曲标记为不喜欢（FM 垃圾桶）
        /// 此操作会影响后续 FM 推荐
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <returns>是否成功</returns>
        public bool FMTrash(long songId)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return false;
            }

            try
            {
                var result = NeteaseFMTrash(songId);
                if (result == 1)
                {
                    _logger.LogInfo($"[NeteaseBridge] ✅ FM trashed song {songId}");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"[NeteaseBridge] Failed to FM trash song {songId}: {GetLastErrorMessage()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] FMTrash exception: {ex}");
                return false;
            }
        }

        #endregion

        #region PCM Stream API

        /// <summary>
        /// 创建 PCM 流
        /// </summary>
        /// <param name="songId">歌曲 ID</param>
        /// <param name="quality">音质</param>
        /// <returns>流 ID，失败返回 -1</returns>
        public long CreatePcmStream(long songId, Quality quality = Quality.ExHigh)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return -1;
            }

            try
            {
                var qualityStr = quality switch
                {
                    Quality.Standard => "standard",
                    Quality.Higher => "higher",
                    Quality.ExHigh => "exhigh",
                    Quality.Lossless => "lossless",
                    Quality.HiRes => "hires",
                    _ => "exhigh"
                };

                var streamId = NeteaseCreatePcmStream(songId, qualityStr);
                if (streamId < 0)
                {
                    _logger.LogWarning($"[NeteaseBridge] CreatePcmStream failed: {GetLastErrorMessage()}");
                }
                else
                {
                    _logger.LogInfo($"[NeteaseBridge] Created PCM stream {streamId} for song {songId}");
                }
                return streamId;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] CreatePcmStream exception: {ex}");
                return -1;
            }
        }

        /// <summary>
        /// 获取 PCM 流信息
        /// </summary>
        public PcmStreamInfo GetPcmStreamInfo(long streamId)
        {
            try
            {
                var ptr = NeteaseGetPcmStreamInfo(streamId);
                if (ptr == IntPtr.Zero) return null;

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);
                return JsonConvert.DeserializeObject<PcmStreamInfo>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetPcmStreamInfo exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 读取 PCM 帧数据
        /// </summary>
        /// <param name="streamId">流 ID</param>
        /// <param name="buffer">float 数组 (交错格式: L,R,L,R...)</param>
        /// <param name="framesToRead">要读取的帧数</param>
        /// <returns>实际读取的帧数，0 表示暂无数据，-1 表示错误，-2 表示 EOF</returns>
        public int ReadPcmFrames(long streamId, float[] buffer, int framesToRead)
        {
            try
            {
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    return NeteaseReadPcmFrames(streamId, handle.AddrOfPinnedObject(), framesToRead);
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] ReadPcmFrames exception: {ex}");
                return -1;
            }
        }

        /// <summary>
        /// 检查 PCM 流是否准备好
        /// </summary>
        public bool IsPcmStreamReady(long streamId)
        {
            try
            {
                return NeteaseIsPcmStreamReady(streamId) == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 关闭 PCM 流
        /// </summary>
        public void ClosePcmStream(long streamId)
        {
            try
            {
                NeteaseClosePcmStream(streamId);
                _logger.LogInfo($"[NeteaseBridge] Closed PCM stream {streamId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] ClosePcmStream exception: {ex}");
            }
        }

        /// <summary>
        /// 获取 PCM 流错误信息
        /// </summary>
        public string GetPcmStreamError(long streamId)
        {
            try
            {
                var ptr = NeteaseGetPcmStreamError(streamId);
                if (ptr == IntPtr.Zero) return null;

                var error = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);
                return error;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Seek PCM 流到指定帧
        /// </summary>
        /// <param name="streamId">流 ID</param>
        /// <param name="frameIndex">目标帧索引</param>
        /// <returns>0=成功, -1=错误, -2=不支持 Seek, -3=延迟 Seek 已设置</returns>
        public int SeekPcmStream(long streamId, long frameIndex)
        {
            try
            {
                var result = NeteaseSeekPcmStream(streamId, frameIndex);
                if (result == 0)
                {
                    _logger.LogInfo($"[NeteaseBridge] Seeked PCM stream {streamId} to frame {frameIndex}");
                }
                else if (result == -3)
                {
                    _logger.LogInfo($"[NeteaseBridge] Pending seek set for PCM stream {streamId} to frame {frameIndex} (waiting for cache)");
                }
                else if (result == -2)
                {
                    _logger.LogWarning($"[NeteaseBridge] PCM stream {streamId} does not support seek yet (cache not complete)");
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] SeekPcmStream exception: {ex}");
                return -1;
            }
        }

        /// <summary>
        /// 检查 PCM 流是否可以 Seek
        /// </summary>
        public bool CanSeekPcmStream(long streamId)
        {
            try
            {
                return NeteaseCanSeekPcmStream(streamId) == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取缓存下载进度
        /// </summary>
        /// <returns>0-100 的百分比，-1 表示错误</returns>
        public double GetCacheProgress(long streamId)
        {
            try
            {
                return NeteaseGetCacheProgress(streamId);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 检查是否有待定的 Seek
        /// </summary>
        public bool HasPendingSeek(long streamId)
        {
            try
            {
                return NeteaseHasPendingSeek(streamId) == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取待定 Seek 的目标帧
        /// </summary>
        public long GetPendingSeekFrame(long streamId)
        {
            try
            {
                return NeteaseGetPendingSeekFrame(streamId);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 取消待定的 Seek
        /// </summary>
        public void CancelPendingSeek(long streamId)
        {
            try
            {
                NeteaseCancelPendingSeek(streamId);
            }
            catch
            {
            }
        }

        #endregion

        #region QR Code Login API

        /// <summary>
        /// 开始二维码登录 - 获取二维码 Key 和 URL
        /// </summary>
        /// <returns>二维码登录状态，包含 QRCodeURL 供生成二维码</returns>
        public QRLoginState StartQRLogin()
        {
            if (!_initialized)
            {
                _logger.LogError("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseQRGetKey();
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogError($"[NeteaseBridge] StartQRLogin failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var state = JsonConvert.DeserializeObject<QRLoginState>(json);
                _logger.LogInfo($"[NeteaseBridge] QR login started, waiting for scan...");
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] StartQRLogin exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检查二维码扫码状态
        /// </summary>
        /// <returns>登录状态</returns>
        public QRLoginState CheckQRLoginStatus()
        {
            if (!_initialized)
            {
                _logger.LogError("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseQRCheckStatus();
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogError($"[NeteaseBridge] CheckQRLoginStatus failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var state = JsonConvert.DeserializeObject<QRLoginState>(json);
                _logger.LogDebug($"[NeteaseBridge] QR status: {state.StatusCode} - {state.StatusMsg}");
                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] CheckQRLoginStatus exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 取消二维码登录
        /// </summary>
        public void CancelQRLogin()
        {
            if (!_initialized)
                return;

            try
            {
                NeteaseQRCancelLogin();
                _logger.LogInfo("[NeteaseBridge] QR login cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] CancelQRLogin exception: {ex.Message}");
            }
        }

        #region 歌单 API

        /// <summary>
        /// 获取用户歌单列表
        /// </summary>
        /// <param name="limit">每页数量（默认 30）</param>
        /// <param name="offset">偏移量</param>
        /// <returns>歌单列表响应，失败返回 null</returns>
        public PlaylistsResponse GetUserPlaylists(int limit = 30, int offset = 0)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetUserPlaylists(limit, offset);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetUserPlaylists failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var response = JsonConvert.DeserializeObject<PlaylistsResponse>(json);
                _logger.LogInfo($"[NeteaseBridge] Got {response?.Playlists?.Count ?? 0} playlists, hasMore: {response?.HasMore}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetUserPlaylists exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 获取所有用户歌单
        /// </summary>
        /// <returns>所有歌单列表，失败返回空列表</returns>
        public List<PlaylistInfo> GetAllUserPlaylists()
        {
            var allPlaylists = new List<PlaylistInfo>();
            int offset = 0;
            const int limit = 50;

            while (true)
            {
                var response = GetUserPlaylists(limit, offset);
                if (response == null || response.Playlists == null || response.Playlists.Count == 0)
                    break;

                allPlaylists.AddRange(response.Playlists);
                offset += response.Playlists.Count;

                if (!response.HasMore)
                    break;
            }

            _logger.LogInfo($"[NeteaseBridge] Got total {allPlaylists.Count} playlists");
            return allPlaylists;
        }

        /// <summary>
        /// 获取歌单中的歌曲
        /// </summary>
        /// <param name="playlistId">歌单 ID</param>
        /// <param name="getAll">是否获取全部歌曲</param>
        /// <returns>歌曲列表，失败返回 null</returns>
        public List<SongInfo> GetPlaylistSongs(long playlistId, bool getAll = true)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetPlaylistSongs(playlistId, getAll ? 1 : 0);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetPlaylistSongs failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var songs = JsonConvert.DeserializeObject<List<SongInfo>>(json);
                _logger.LogInfo($"[NeteaseBridge] Got {songs?.Count ?? 0} songs from playlist {playlistId}");
                return songs;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetPlaylistSongs exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 根据关键词搜索用户歌单
        /// </summary>
        /// <param name="keywords">搜索关键词，多个关键词用 | 分隔</param>
        /// <returns>匹配的歌单列表，失败返回 null</returns>
        public List<PlaylistInfo> SearchPlaylistsByKeyword(string keywords)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            if (string.IsNullOrWhiteSpace(keywords))
            {
                _logger.LogWarning("[NeteaseBridge] Keywords is empty");
                return null;
            }

            try
            {
                var ptr = NeteaseSearchPlaylistsByKeyword(keywords);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] SearchPlaylistsByKeyword failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var playlists = JsonConvert.DeserializeObject<List<PlaylistInfo>>(json);
                _logger.LogInfo($"[NeteaseBridge] Found {playlists?.Count ?? 0} playlists matching '{keywords}'");
                return playlists;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] SearchPlaylistsByKeyword exception: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 根据 ID 获取歌单详情（包含歌曲）
        /// </summary>
        /// <param name="playlistId">歌单 ID</param>
        /// <returns>歌单详情，失败返回 null</returns>
        public PlaylistDetail GetPlaylistDetail(long playlistId)
        {
            if (!_initialized)
            {
                _logger.LogWarning("[NeteaseBridge] Not initialized");
                return null;
            }

            try
            {
                var ptr = NeteaseGetPlaylistDetail(playlistId);
                if (ptr == IntPtr.Zero)
                {
                    _logger.LogWarning($"[NeteaseBridge] GetPlaylistDetail failed: {GetLastErrorMessage()}");
                    return null;
                }

                var json = Marshal.PtrToStringAnsi(ptr);
                NeteaseFreeString(ptr);

                var detail = JsonConvert.DeserializeObject<PlaylistDetail>(json);
                _logger.LogInfo($"[NeteaseBridge] Got playlist detail: {detail?.Name} ({detail?.Songs?.Count ?? 0} songs)");
                return detail;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NeteaseBridge] GetPlaylistDetail exception: {ex}");
                return null;
            }
        }

        #endregion

        #endregion
    }
}

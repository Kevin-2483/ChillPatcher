using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniMixPlayer.Module.Kugou
{
    public sealed class KugouBridge
    {
        private const string DllName = "ChillKugou";
        private readonly ILogger _logger;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern int KugouInit(string dataDir);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouGetSessions();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouRegisterDevice();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouQRCreate();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouQRCheck(string key);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern int KugouRemoveSession(long userId);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouGetUserPlaylistsPage(long userId, int page, int pageSize);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouGetPlaylistSongsPage(long userId, long listId, string globalCollectionId, int page, int pageSize);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouGetSongURLV2(long userId, string requestJson);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouClaimDailyVIP(long userId, string receiveDay);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouUpgradeDailyVIP(long userId);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouGetSongPrivilege(long userId, string requestJson);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr KugouGetLyricV2(long userId, string requestJson);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern int KugouSetFavorite(long userId, string requestJson, int favorite);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr KugouGetLastError();
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)] private static extern void KugouFreeString(IntPtr ptr);

        public sealed class UserInfo
        {
            [JsonProperty("userid")] public long UserId { get; set; }
            [JsonProperty("nickname")] public string Nickname { get; set; }
            [JsonProperty("avatar")] public string AvatarUrl { get; set; }
        }

        private sealed class SessionState
        {
            [JsonProperty("activeUserId")] public long ActiveUserId { get; set; }
            [JsonProperty("accounts")] public List<UserInfo> Accounts { get; set; }
        }

        private sealed class QRCodeResult
        {
            [JsonProperty("key")] public string Key { get; set; }
            [JsonProperty("url")] public string Url { get; set; }
        }

        public sealed class QRState
        {
            [JsonProperty("status")] public int Status { get; set; }
            [JsonProperty("session")] public UserInfo Session { get; set; }
            public string Message => Status switch { 0 => "二维码已过期", 1 => "等待扫码", 2 => "已扫码，等待确认", 4 => "登录成功", _ => "等待登录" };
            public bool IsSuccess => Status == 4;
            public bool IsExpired => Status == 0;
        }

        public sealed class PlaylistInfo
        {
            public string Id { get; set; }
            public long ListId { get; set; }
            public string GlobalId { get; set; }
            public string Name { get; set; }
            public string CoverUrl { get; set; }
            public int SongCount { get; set; }
            public bool Favorite { get; set; }
            public bool ReadOnly => true;
        }

        public sealed class SongInfo
        {
            public string Hash { get; set; }
            public long AudioId { get; set; }
            public string Name { get; set; }
            public string Artist { get; set; }
            public string Album { get; set; }
            public long AlbumId { get; set; }
            public long Duration { get; set; }
            public string CoverUrl { get; set; }
            public long FileId { get; set; }
            public long FavoriteListId { get; set; }
            public long Sort { get; set; } = -1;
            public int FetchOrder { get; set; }
        }

        public sealed class SongUrl
        {
            public string Url { get; set; }
            public string Format { get; set; }
            public long Size { get; set; }
        }

        public sealed class BridgeErrorInfo
        {
            [JsonProperty("code")] public int Code { get; set; }
            [JsonProperty("message")] public string Message { get; set; }
            [JsonProperty("operation")] public string Operation { get; set; }
            [JsonProperty("apiCode")] public int ApiCode { get; set; }
            [JsonProperty("ssaCode")] public string SsaCode { get; set; }
            [JsonProperty("verificationUrl")] public string VerificationUrl { get; set; }
            [JsonProperty("cause")] public string Cause { get; set; }
            public bool RequiresVerification => ApiCode == 20028 && !string.IsNullOrWhiteSpace(SsaCode);
        }

        public KugouBridge(ILogger logger) => _logger = logger;

        private string Read(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(ptr); }
            finally { KugouFreeString(ptr); }
        }

        private T Parse<T>(IntPtr ptr) where T : class
        {
            var json = Read(ptr);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); }
            catch (Exception ex) { _logger?.LogError(ex, "[酷狗音乐] 响应解析失败"); return null; }
        }

        public bool Initialize(string dataDir)
        {
            if (KugouInit(dataDir) != 0) return false;
            try { KugouRegisterDevice(); } catch { }
            return true;
        }

        private SessionState Sessions => Parse<SessionState>(KugouGetSessions());
        public bool IsLoggedIn => Sessions?.ActiveUserId > 0;
        public long ActiveUserId => Sessions?.ActiveUserId ?? 0;
        public UserInfo GetUserInfo()
        {
            var state = Sessions;
            return state?.Accounts?.FirstOrDefault(x => x.UserId == state.ActiveUserId);
        }

        public (string key, string url) CreateQR()
        {
            var result = Parse<QRCodeResult>(KugouQRCreate());
            return (result?.Key, result?.Url);
        }

        public QRState CheckQRStatus(string key) => Parse<QRState>(KugouQRCheck(key));
        public bool Logout() => ActiveUserId == 0 || KugouRemoveSession(ActiveUserId) == 0;

        public List<PlaylistInfo> GetUserPlaylists()
        {
            var result = new List<PlaylistInfo>();
            for (var page = 1; page <= 50; page++)
            {
                var root = Parse<JObject>(KugouGetUserPlaylistsPage(ActiveUserId, page, 100));
                var items = FindArray(root?["data"], "info", "list", "lists", "items");
                if (root == null) return null;
                foreach (var token in items.OfType<JObject>())
                {
                    var listId = Long(token, "listid", "list_id", "id");
                    var globalId = Text(token, "global_collection_id", "global_id");
                    var id = !string.IsNullOrWhiteSpace(globalId) ? globalId : listId.ToString();
                    if (string.IsNullOrWhiteSpace(id) || id == "0") continue;
                    var name = Text(token, "name", "listname", "list_name");
                    result.Add(new PlaylistInfo
                    {
                        Id = id,
                        ListId = listId,
                        GlobalId = globalId,
                        Name = name,
                        CoverUrl = Cover(Text(token, "pic", "cover", "img")),
                        SongCount = (int)Long(token, "count", "song_count", "filecount"),
                        Favorite = name?.Contains("喜欢") == true || Long(token, "is_default", "default") == 1
                    });
                }
                if (items.Count < 100) break;
            }
            return result.GroupBy(x => x.Id).Select(x => x.First()).ToList();
        }

        public List<SongInfo> GetPlaylistSongs(PlaylistInfo playlist)
        {
            var result = new List<SongInfo>();
            for (var page = 1; page <= 200; page++)
            {
                var root = Parse<JObject>(KugouGetPlaylistSongsPage(ActiveUserId, playlist.ListId, playlist.GlobalId ?? "", page, 100));
                if (root == null) return null;
                var items = FindArray(root?["data"], "info", "songs", "list", "data", "items");
                foreach (var token in items.OfType<JObject>())
                {
                    var source = token["resource"] as JObject ?? token;
                    var hash = Text(source, "hash", "audio_hash", "file_hash")?.ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(hash)) continue;
                    var name = Text(source, "songname", "song_name", "name", "filename");
                    var artist = Text(source, "singername", "author_name", "singer_name", "singer");
                    if (string.IsNullOrWhiteSpace(artist) && name?.Contains(" - ") == true)
                    {
                        var parts = name.Split(new[] { " - " }, 2, StringSplitOptions.None);
                        artist = parts[0];
                        name = parts[1];
                    }
                    var duration = Long(source, "duration", "timelen", "time_length");
                    if (duration > 10000) duration /= 1000;
                    result.Add(new SongInfo
                    {
                        Hash = hash,
                        AudioId = Long(source, "album_audio_id", "mixsongid", "audio_id"),
                        Name = name,
                        Artist = artist,
                        Album = Text(source, "album_name", "albumname"),
                        AlbumId = Long(source, "album_id", "albumid"),
                        Duration = duration,
                        CoverUrl = Cover(Text(source, "album_sizable_cover", "sizable_cover", "cover", "img", "album_img")),
                        FileId = Long(token, "fileid", "file_id"),
                        FavoriteListId = playlist.Favorite ? playlist.ListId : 0,
                        Sort = OptionalLong(source, "sort", "position", "index"),
                        FetchOrder = result.Count
                    });
                }
                if (items.Count < 100) break;
            }
            return result
                .GroupBy(x => x.Hash, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.Sort >= 0 ? x.Sort : long.MaxValue)
                .ThenBy(x => x.FetchOrder)
                .ToList();
        }

        public bool ClaimDailyVip(string receiveDay) =>
            !string.IsNullOrWhiteSpace(Read(KugouClaimDailyVIP(ActiveUserId, receiveDay)));

        public bool UpgradeDailyVip() =>
            !string.IsNullOrWhiteSpace(Read(KugouUpgradeDailyVIP(ActiveUserId)));

        public string GetQualityHash(SongInfo song, string quality)
        {
            var request = JsonConvert.SerializeObject(new { hash = song.Hash, albumAudioId = song.AudioId, albumId = song.AlbumId });
            var root = Parse<JObject>(KugouGetSongPrivilege(ActiveUserId, request));
            var items = root?["data"] as JArray ?? FindArray(root, "data", "items", "info");
            foreach (var item in items.OfType<JObject>())
            {
                var variants = new List<JObject> { item };
                if (item["relate_goods"] is JArray related)
                    variants.AddRange(related.OfType<JObject>());
                foreach (var variant in variants)
                {
                    var candidateQuality = Text(variant, "quality");
                    var hash = Text(variant, "hash")?.ToUpperInvariant();
                    if (!string.Equals(candidateQuality, quality, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(hash))
                        continue;
                    if (variant["level"] != null && Long(variant, "level") == 0)
                        continue;
                    return hash;
                }
            }
            return null;
        }

        public SongUrl GetSongUrl(SongInfo song, string quality, string playbackHash = null)
        {
            var requestHash = string.IsNullOrWhiteSpace(playbackHash) ? song.Hash : playbackHash;
            var request = JsonConvert.SerializeObject(new { hash = requestHash, albumAudioId = song.AudioId, albumId = song.AlbumId, quality });
            var root = Parse<JObject>(KugouGetSongURLV2(ActiveUserId, request));
            var data = root?["data"] as JObject ?? root;
            var url = Text(data, "play_url", "url");
            if (data?["url"] is JArray urls && urls.Count > 0) url = urls[0]?.ToString();
            return string.IsNullOrWhiteSpace(url) ? null : new SongUrl { Url = url, Format = url.Contains(".flac", StringComparison.OrdinalIgnoreCase) ? "flac" : "mp3", Size = Long(data, "filesize", "file_size") };
        }

        public string GetLyric(SongInfo song)
        {
            var request = JsonConvert.SerializeObject(new { hash = song.Hash, albumAudioId = song.AudioId, keywords = $"{song.Artist} - {song.Name}", format = "lrc" });
            var root = Parse<JObject>(KugouGetLyricV2(ActiveUserId, request));
            return Text(root, "content", "decodeContent");
        }

        public bool SetFavorite(SongInfo song, bool favorite)
        {
            var request = JsonConvert.SerializeObject(new { listId = song.FavoriteListId, fileId = song.FileId, hash = song.Hash, name = song.Name, albumId = song.AlbumId, albumAudioId = song.AudioId });
            return KugouSetFavorite(ActiveUserId, request, favorite ? 1 : 0) == 0;
        }

        public string GetLastError() => Read(KugouGetLastError());

        public BridgeErrorInfo GetLastErrorInfo()
        {
            var json = GetLastError();
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<BridgeErrorInfo>(json); }
            catch { return null; }
        }

        private static JArray FindArray(JToken token, params string[] names)
        {
            if (token == null) return new JArray();
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value is JArray array) return array;
                    var result = FindArray(property.Value, names);
                    if (result.Count > 0) return result;
                }
                return new JArray();
            }
            if (token is JProperty directProperty) return FindArray(directProperty.Value, names);
            foreach (var child in token.Children())
            {
                var result = FindArray(child, names);
                if (result.Count > 0) return result;
            }
            return new JArray();
        }

        private static string Text(JToken token, params string[] names)
        {
            if (token == null) return null;
            foreach (var name in names)
            {
                var value = token[name];
                if (value != null && value.Type != JTokenType.Null && value.Type != JTokenType.Array && value.Type != JTokenType.Object) return value.ToString();
            }
            return null;
        }

        private static long Long(JToken token, params string[] names) => long.TryParse(Text(token, names), out var value) ? value : 0;
        private static long OptionalLong(JToken token, params string[] names) =>
            long.TryParse(Text(token, names), out var value) ? value : -1;
        private static string Cover(string value)
        {
            value = value?.Replace("{size}", "400");
            return value?.StartsWith("//") == true ? "https:" + value : value;
        }
    }
}

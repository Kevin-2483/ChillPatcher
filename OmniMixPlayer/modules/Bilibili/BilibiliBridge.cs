using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniMixPlayer.Module.Bilibili
{
    public class BilibiliBridge
    {
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private BilibiliSession _session;
        private readonly string _sessionPath;

        public int PageDelay { get; set; }

        public bool IsLoggedIn => _session != null && _session.IsValid;
        public string CurrentUserId => _session?.DedeUserID;

        public void ClearSession()
        {
            _session = null;
            try { if (File.Exists(_sessionPath)) File.Delete(_sessionPath); } catch { }
        }

        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0";

        // [修改] 构造函数接收 delay 参数
        public BilibiliBridge(ILogger logger, string dataDir, int pageDelay)
        {
            _logger = logger;
            _sessionPath = Path.Combine(dataDir, "bilibili_session.json");
            PageDelay = pageDelay;

            var handler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler);

            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            _client.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com/");
            _client.DefaultRequestHeaders.Add("Origin", "https://www.bilibili.com");
            _client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            _client.DefaultRequestHeaders.Add("Connection", "keep-alive");

            LoadSession();
        }

        private void LoadSession()
        {
            if (File.Exists(_sessionPath))
            {
                try { _session = JsonConvert.DeserializeObject<BilibiliSession>(File.ReadAllText(_sessionPath)); }
                catch { _session = new BilibiliSession(); }
            }
        }

        private void SaveSession() => File.WriteAllText(_sessionPath, JsonConvert.SerializeObject(_session));

        private void UpdateHeader()
        {
            _client.DefaultRequestHeaders.Remove("Cookie");
            if (IsLoggedIn)
            {
                string cookieStr = _session.ToCookieString().Trim();
                if (!cookieStr.EndsWith(";")) cookieStr += ";";
                _client.DefaultRequestHeaders.Add("Cookie", cookieStr);
            }
        }

        public async Task<BiliQrCodeData> GetLoginUrlAsync()
        {
            var json = await _client.GetStringAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate");
            return JObject.Parse(json)["data"].ToObject<BiliQrCodeData>();
        }

        /// <summary>
        /// 检查二维码登录状态
        /// 返回: 0=成功, 86101=未扫码, 86090=已扫码未确认, 86038=已过期
        /// </summary>
        public async Task<int> CheckLoginStatusAsync(string key)
        {
            var response = await _client.GetAsync($"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={key}");
            var obj = JObject.Parse(await response.Content.ReadAsStringAsync());

            int code = (int)obj["data"]["code"];

            if (code == 0)
            {
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    _session = new BilibiliSession { LoginTime = DateTimeOffset.Now.ToUnixTimeSeconds() };
                    foreach (var c in cookies)
                    {
                        foreach (var p in c.Split(';'))
                        {
                            var kv = p.Trim().Split('=');
                            if (kv.Length < 2) continue;
                            if (kv[0] == "SESSDATA") _session.SESSDATA = kv[1];
                            if (kv[0] == "bili_jct") _session.BiliJct = kv[1];
                            if (kv[0] == "DedeUserID") _session.DedeUserID = kv[1];
                        }
                    }
                    SaveSession();
                }
            }
            return code;
        }

        public async Task<BiliFolderListResult> GetMyFoldersAsync()
        {
            if (!IsLoggedIn) return BiliFolderListResult.Failed("Not logged in");
            UpdateHeader();
            try
            {
                await Task.Delay(100);
                var url = $"https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={_session.DedeUserID}&type=2";
                var data = JObject.Parse(await _client.GetStringAsync(url));
                if (data["code"]?.ToString() != "0")
                {
                    var message = data["message"]?.ToString() ?? "";
                    _logger.LogWarning("[Sync] 获取收藏夹列表失败: code={Code}, message={Message}",
                        data["code"]?.ToString(), message);
                    return BiliFolderListResult.Failed(message);
                }

                var folders = data["data"]?["list"]?.ToObject<List<BiliFolder>>() ?? new List<BiliFolder>();
                _logger.LogInformation("[Sync] 获取收藏夹列表成功: {Count} 个", folders.Count);
                return BiliFolderListResult.Ok(folders);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Sync] 获取收藏夹列表失败: {ex.Message}");
                return BiliFolderListResult.Failed(ex.Message);
            }
        }

        public async Task<BiliFolderVideosResult> GetFolderVideosAsync(long folderId)
        {
            if (!IsLoggedIn) return BiliFolderVideosResult.Failed(message: "Not logged in");
            UpdateHeader();

            var result = new List<BiliVideoInfo>();

            for (int page = 1; page <= 50; page++)
            {
                bool success = false;
                int retryCount = 0;

                while (!success && retryCount < 3)
                {
                    try
                    {
                        if (page % 5 == 0) _logger.LogInformation($"[Sync] 正在加载收藏夹 {folderId} 第 {page}/50 页...");

                        var url = $"https://api.bilibili.com/x/v3/fav/resource/list?media_id={folderId}&ps=20&pn={page}&keyword=&order=mtime&type=0&tid=0&platform=web";

                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

                        var jsonStr = await response.Content.ReadAsStringAsync();
                        var root = JObject.Parse(jsonStr);
                        if (root["code"]?.ToString() != "0")
                        {
                            var message = root["message"]?.ToString() ?? "";
                            _logger.LogWarning("[Sync] 收藏夹 {FolderId} 第 {Page} 页返回错误: code={Code}, message={Message}",
                                folderId, page, root["code"]?.ToString(), message);
                            return BiliFolderVideosResult.Failed(result, message);
                        }

                        var data = root["data"];

                        if (data == null || data["medias"] == null)
                        {
                            if (page == 1) _logger.LogInformation("[Sync] 收藏夹 {FolderId} 为空", folderId);
                            return BiliFolderVideosResult.Ok(result);
                        }

                        foreach (var item in data["medias"])
                        {
                            try
                            {
                                if (item["title"]?.ToString() == "已失效视频") continue;

                                string cover = item["cover"]?.ToString();
                                if (cover != null && cover.StartsWith("http://")) cover = cover.Replace("http://", "https://");

                                var bvid = item["bvid"]?.ToString();
                                if (string.IsNullOrWhiteSpace(bvid)) continue;

                                result.Add(new BiliVideoInfo
                                {
                                    Bvid = bvid,
                                    Title = item["title"]?.ToString(),
                                    Artist = item["upper"]?["name"]?.ToString(),
                                    CoverUrl = cover,
                                    Duration = item["duration"]?.ToObject<float>() ?? 0,
                                });
                            }
                            catch { }
                        }

                        bool hasMore = (bool?)data["has_more"] ?? false;
                        if (!hasMore) return BiliFolderVideosResult.Ok(result);

                        success = true;

                        // [核心修改] 使用配置的延迟
                        await Task.Delay(PageDelay);
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning($"[Sync] 加载第 {page} 页失败 (尝试 {retryCount}/3): {ex.Message}");

                        // 遇到412时延迟增加，普通错误也根据配置延迟增加
                        int waitTime = ex.Message.Contains("412") ? PageDelay * 5 : PageDelay * 3;
                        await Task.Delay(waitTime);

                        if (retryCount >= 3)
                        {
                            _logger.LogError($"[Sync] 放弃加载第 {page} 页。已获取 {result.Count} 首。");
                            return BiliFolderVideosResult.Failed(result, ex.Message);
                        }
                    }
                }
            }

            _logger.LogInformation($"[Sync] 收藏夹 {folderId} 同步完成，共 {result.Count} 首");
            return BiliFolderVideosResult.Ok(result);
        }

        public async Task<string> GetPlayUrlAsync(string bvid)
        {
            UpdateHeader();
            try
            {
                var viewUrl = $"https://api.bilibili.com/x/web-interface/view?bvid={bvid}";
                var viewData = JObject.Parse(await _client.GetStringAsync(viewUrl))["data"];
                var cid = viewData["cid"]?.ToString() ?? viewData["pages"]?[0]?["cid"]?.ToString();

                var playUrlApi = $"https://api.bilibili.com/x/player/playurl?bvid={bvid}&cid={cid}&fnval=16";
                var playData = JObject.Parse(await _client.GetStringAsync(playUrlApi))["data"];

                var audio = playData["dash"]?["audio"]?[0];
                var candidates = new List<string>();
                var primaryUrl = audio?["baseUrl"]?.ToString();
                if (!string.IsNullOrEmpty(primaryUrl)) candidates.Add(primaryUrl);
                var backupUrls = audio?["backupUrl"]?.Select(x => x?.ToString());
                if (backupUrls != null) candidates.AddRange(backupUrls.Where(x => !string.IsNullOrEmpty(x)));
                var url = candidates.FirstOrDefault(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.IsDefaultPort)
                    ?? candidates.FirstOrDefault();
                if (string.IsNullOrEmpty(url)) url = playData["durl"]?[0]?["url"]?.ToString();

                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError($"GetPlayUrl Error: {ex.Message}");
                return null;
            }
        }

        public async Task<byte[]> GenerateQRBytesAsync(string content)
        {
            var url = $"https://api.qrserver.com/v1/create-qr-code/?size=256x256&data={Uri.EscapeDataString(content)}";
            return await _client.GetByteArrayAsync(url);
        }
    }
}

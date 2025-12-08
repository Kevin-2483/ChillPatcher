package main

/*
#include <stdlib.h>
*/
import "C"
import (
	"encoding/json"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"sync"
	"unsafe"

	"github.com/buger/jsonparser"
	"github.com/go-musicfox/go-musicfox/internal/storage"
	"github.com/go-musicfox/go-musicfox/internal/structs"
	"github.com/go-musicfox/go-musicfox/internal/netease"
	"github.com/go-musicfox/netease-music/service"
	"github.com/go-musicfox/netease-music/util"
	"github.com/telanflow/cookiejar"
)

var (
	initialized  bool
	initMutex    sync.Mutex
	currentUser  *structs.User
	lastError    string
	dataDir      string
)

// SongInfo 导出给 C# 的歌曲信息结构
type SongInfo struct {
	ID       int64    `json:"id"`
	Name     string   `json:"name"`
	Duration float64  `json:"duration"` // 秒
	Artists  []string `json:"artists"`
	Album    string   `json:"album"`
	AlbumID  int64    `json:"albumId"`
	CoverUrl string   `json:"coverUrl"` // 封面 URL
}

// UserInfo 用户信息
type UserInfo struct {
	UserID   int64  `json:"userId"`
	Nickname string `json:"nickname"`
	AvatarURL string `json:"avatarUrl"`
}

// SongURL 歌曲播放地址
type SongURL struct {
	ID   int64  `json:"id"`
	URL  string `json:"url"`
	Size int64  `json:"size"`
	Type string `json:"type"` // mp3, flac, etc.
}

//export NeteaseInit
func NeteaseInit(dataDirC *C.char) C.int {
	initMutex.Lock()
	defer initMutex.Unlock()

	if initialized {
		return 1 // 已初始化
	}

	dataDir = C.GoString(dataDirC)
	if dataDir == "" {
		// 使用默认路径
		homeDir, err := os.UserHomeDir()
		if err != nil {
			lastError = "Failed to get home directory: " + err.Error()
			return 0
		}
		dataDir = filepath.Join(homeDir, "AppData", "Local", "go-musicfox")
	}

	// 确保目录存在
	if err := os.MkdirAll(dataDir, 0755); err != nil {
		lastError = "Failed to create data directory: " + err.Error()
		return 0
	}

	// 初始化数据库管理器
	storage.DBManager = &storage.LocalDBManager{}

	// 加载 Cookie
	cookiePath := filepath.Join(dataDir, "cookie")
	jar, err := cookiejar.NewFileJar(cookiePath, nil)
	if err != nil {
		lastError = "Failed to load cookie jar: " + err.Error()
		return 0
	}
	util.SetGlobalCookieJar(jar)

	// 尝试加载用户信息
	table := storage.NewTable()
	if jsonStr, err := table.GetByKVModel(storage.User{}); err == nil {
		if user, err := structs.NewUserFromLocalJson(jsonStr); err == nil {
			currentUser = &user
		}
	}

	initialized = true
	return 1
}

//export NeteaseIsLoggedIn
func NeteaseIsLoggedIn() C.int {
	if currentUser != nil && currentUser.UserId > 0 {
		return 1
	}
	return 0
}

//export NeteaseGetUserInfo
func NeteaseGetUserInfo() *C.char {
	if currentUser == nil {
		lastError = "Not logged in"
		return nil
	}

	info := UserInfo{
		UserID:    currentUser.UserId,
		Nickname:  currentUser.Nickname,
		AvatarURL: currentUser.AvatarUrl,
	}

	jsonBytes, err := json.Marshal(info)
	if err != nil {
		lastError = "Failed to marshal user info: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseRefreshLogin
func NeteaseRefreshLogin() C.int {
	if !initialized {
		lastError = "Not initialized"
		return 0
	}

	// 刷新登录状态
	refreshService := service.LoginRefreshService{}
	refreshService.LoginRefresh()

	// 获取账户信息
	code, resp := (&service.UserAccountService{}).AccountInfo()
	if code != 200 {
		lastError = "Failed to get account info, code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return 0
	}

	user, err := structs.NewUserFromJsonForLogin(resp)
	if err != nil {
		lastError = "Failed to parse user info: " + err.Error()
		return 0
	}
	currentUser = &user

	// 保存用户信息
	table := storage.NewTable()
	_ = table.SetByKVModel(storage.User{}, user)

	return 1
}

//export NeteaseGetLikeSongs
func NeteaseGetLikeSongs(getAll C.int) *C.char {
	if currentUser == nil || currentUser.UserId == 0 {
		lastError = "Not logged in"
		return nil
	}

	songs, err := netease.FetchLikeSongs(currentUser.UserId, getAll != 0)
	if err != nil {
		lastError = "Failed to fetch like songs: " + err.Error()
		return nil
	}

	// 转换为导出格式
	result := make([]SongInfo, len(songs))
	for i, song := range songs {
		artists := make([]string, len(song.Artists))
		for j, artist := range song.Artists {
			artists[j] = artist.Name
		}
		
		// 获取封面 URL（带尺寸参数）
		coverUrl := song.Album.PicUrl
		if coverUrl != "" {
			coverUrl = coverUrl + "?param=300y300" // 300x300 封面
		}
		
		result[i] = SongInfo{
			ID:       song.Id,
			Name:     song.Name,
			Duration: song.Duration.Seconds(),
			Artists:  artists,
			Album:    song.Album.Name,
			AlbumID:  song.Album.Id,
			CoverUrl: coverUrl,
		}
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal songs: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseGetSongURL
func NeteaseGetSongURL(songId C.longlong, quality *C.char) *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	qualityStr := C.GoString(quality)
	if qualityStr == "" {
		qualityStr = "exhigh" // 默认极高音质
	}

	// 首先尝试新 API (SongUrlV1Service) - 支持高品质
	urlService := service.SongUrlV1Service{
		ID:    strconv.FormatInt(int64(songId), 10),
		Level: service.SongQualityLevel(qualityStr),
	}

	code, resp, err := urlService.SongUrl()
	if err != nil {
		lastError = "Failed to get song URL: " + err.Error()
		return nil
	}

	// 解析响应并检查是否需要回退
	var v1Response struct {
		Data []struct {
			ID            int64       `json:"id"`
			URL           string      `json:"url"`
			Size          int64       `json:"size"`
			Type          string      `json:"type"`
			FreeTrialInfo interface{} `json:"freeTrialInfo"`
		} `json:"data"`
	}

	needFallback := false
	var url string
	var size int64
	var musicType string

	if code == 200 && err == nil {
		if err := json.Unmarshal(resp, &v1Response); err == nil {
			if len(v1Response.Data) > 0 {
				url = v1Response.Data[0].URL
				size = v1Response.Data[0].Size
				musicType = v1Response.Data[0].Type
				// 检查是否是试听歌曲（需要会员）
				if url == "" || v1Response.Data[0].FreeTrialInfo != nil {
					needFallback = true
				}
			} else {
				needFallback = true
			}
		} else {
			needFallback = true
		}
	} else {
		needFallback = true
	}

	// 如果需要回退，使用旧 API (SongUrlService) - 最高 320kbps
	if needFallback {
		// 品质到码率的映射
		brMap := map[string]string{
			"standard": "128000",
			"higher":   "320000",
			"exhigh":   "320000",
			"lossless": "999000",
			"hires":    "999000",
		}
		br, ok := brMap[qualityStr]
		if !ok {
			br = "320000" // 默认 320kbps
		}

		fallbackService := service.SongUrlService{
			ID: strconv.FormatInt(int64(songId), 10),
			Br: br,
		}

		code, resp = fallbackService.SongUrl()
		if code != 200 {
			lastError = "Fallback API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
			return nil
		}

		var fallbackResponse struct {
			Data []struct {
				ID   int64  `json:"id"`
				URL  string `json:"url"`
				Size int64  `json:"size"`
				Type string `json:"type"`
			} `json:"data"`
		}

		if err := json.Unmarshal(resp, &fallbackResponse); err != nil {
			lastError = "Failed to parse fallback response: " + err.Error()
			return nil
		}

		if len(fallbackResponse.Data) > 0 {
			url = fallbackResponse.Data[0].URL
			size = fallbackResponse.Data[0].Size
			musicType = fallbackResponse.Data[0].Type
		}
	}

	if url == "" {
		lastError = "No URL available for this song (tried both APIs)"
		return nil
	}

	// 确保类型不为空
	if musicType == "" {
		musicType = "mp3"
	}

	result := SongURL{
		ID:   int64(songId),
		URL:  url,
		Size: size,
		Type: musicType,
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal URL: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseGetLastError
func NeteaseGetLastError() *C.char {
	if lastError == "" {
		return nil
	}
	return C.CString(lastError)
}

//export NeteaseFreeString
func NeteaseFreeString(ptr *C.char) {
	if ptr != nil {
		C.free(unsafe.Pointer(ptr))
	}
}

//export NeteaseSetCookie
func NeteaseSetCookie(cookieStr *C.char) C.int {
	if !initialized {
		lastError = "Not initialized"
		return 0
	}

	cookies, err := http.ParseCookie(C.GoString(cookieStr))
	if err != nil {
		lastError = "Failed to parse cookies: " + err.Error()
		return 0
	}

	jar := util.GetGlobalCookieJar()
	if fileJar, ok := jar.(*cookiejar.Jar); ok {
		u, _ := url.Parse("https://music.163.com")
		fileJar.SetCookies(u, cookies)
	}

	return 1
}

//export NeteaseLikeSong
// NeteaseLikeSong 添加或取消收藏歌曲
// songId: 歌曲 ID
// like: 1 = 收藏, 0 = 取消收藏
// 返回: 1 = 成功, 0 = 失败
func NeteaseLikeSong(songId C.longlong, like C.int) C.int {
	if !initialized {
		lastError = "Not initialized"
		return 0
	}

	likeStr := "true"
	if like == 0 {
		likeStr = "false"
	}

	likeService := service.LikeService{
		ID: strconv.FormatInt(int64(songId), 10),
		L:  likeStr,
	}

	code, resp := likeService.Like()
	if code != 200 {
		lastError = "Like API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return 0
	}

	// 检查响应中的 code 字段
	var response struct {
		Code int64 `json:"code"`
	}
	if err := json.Unmarshal(resp, &response); err != nil {
		lastError = "Failed to parse like response: " + err.Error()
		return 0
	}

	if response.Code != 200 {
		lastError = "Like operation failed with code: " + strconv.FormatInt(response.Code, 10)
		return 0
	}

	return 1
}

//export NeteaseGetLikeList
// NeteaseGetLikeList 获取用户收藏的歌曲 ID 列表
// 返回: JSON 数组字符串，包含收藏歌曲的 ID 列表
func NeteaseGetLikeList() *C.char {
	if currentUser == nil || currentUser.UserId == 0 {
		lastError = "Not logged in"
		return nil
	}

	likeListService := service.LikeListService{
		UID: strconv.FormatInt(currentUser.UserId, 10),
	}

	code, resp := likeListService.LikeList()
	if code != 200 {
		lastError = "LikeList API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return nil
	}

	// 解析响应获取 ids 数组
	var response struct {
		Code int64   `json:"code"`
		Ids  []int64 `json:"ids"`
	}
	if err := json.Unmarshal(resp, &response); err != nil {
		lastError = "Failed to parse like list response: " + err.Error()
		return nil
	}

	if response.Code != 200 {
		lastError = "LikeList operation failed with code: " + strconv.FormatInt(response.Code, 10)
		return nil
	}

	jsonBytes, err := json.Marshal(response.Ids)
	if err != nil {
		lastError = "Failed to marshal like list: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseGetPersonalFM
// NeteaseGetPersonalFM 获取个人 FM 推荐歌曲
// 返回: JSON 数组字符串，包含推荐歌曲信息
func NeteaseGetPersonalFM() *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	fmService := service.PersonalFmService{}
	code, resp := fmService.PersonalFm()

	if code != 200 {
		lastError = "PersonalFM API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return nil
	}

	// 解析响应
	var response struct {
		Code int64 `json:"code"`
		Data []struct {
			Id       int64  `json:"id"`
			Name     string `json:"name"`
			Duration int64  `json:"duration"` // 毫秒
			Artists  []struct {
				Id   int64  `json:"id"`
				Name string `json:"name"`
			} `json:"artists"`
			Album struct {
				Id     int64  `json:"id"`
				Name   string `json:"name"`
				PicUrl string `json:"picUrl"`
			} `json:"album"`
		} `json:"data"`
	}

	if err := json.Unmarshal(resp, &response); err != nil {
		lastError = "Failed to parse FM response: " + err.Error()
		return nil
	}

	if response.Code != 200 {
		lastError = "PersonalFM operation failed with code: " + strconv.FormatInt(response.Code, 10)
		return nil
	}

	// 转换为导出格式
	result := make([]SongInfo, len(response.Data))
	for i, song := range response.Data {
		artists := make([]string, len(song.Artists))
		for j, artist := range song.Artists {
			artists[j] = artist.Name
		}

		coverUrl := song.Album.PicUrl
		if coverUrl != "" {
			coverUrl = coverUrl + "?param=300y300"
		}

		result[i] = SongInfo{
			ID:       song.Id,
			Name:     song.Name,
			Duration: float64(song.Duration) / 1000.0, // 毫秒转秒
			Artists:  artists,
			Album:    song.Album.Name,
			AlbumID:  song.Album.Id,
			CoverUrl: coverUrl,
		}
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal FM songs: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseFMTrash
// NeteaseFMTrash 将歌曲标记为不喜欢（FM 垃圾桶）
// songId: 歌曲 ID
// 返回: 1 = 成功, 0 = 失败
func NeteaseFMTrash(songId C.longlong) C.int {
	if !initialized {
		lastError = "Not initialized"
		return 0
	}

	trashService := service.FmTrashService{
		SongID: strconv.FormatInt(int64(songId), 10),
	}

	code, resp := trashService.FmTrash()
	if code != 200 {
		lastError = "FMTrash API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return 0
	}

	// 检查响应中的 code 字段
	var response struct {
		Code int64 `json:"code"`
	}
	if err := json.Unmarshal(resp, &response); err != nil {
		lastError = "Failed to parse FM trash response: " + err.Error()
		return 0
	}

	if response.Code != 200 {
		lastError = "FMTrash operation failed with code: " + strconv.FormatInt(response.Code, 10)
		return 0
	}

	return 1
}

func main() {
	// 这个 main 函数是必需的，但在编译为 DLL 时不会被调用
	log.Println("This is a shared library, not meant to be run directly")
}

// =============== QR Code Login API ===============

// QRLoginState 二维码登录状态
type QRLoginState struct {
	UniKey     string `json:"uniKey"`     // 用于轮询的 key
	QRCodeURL  string `json:"qrcodeUrl"`  // 二维码内容 URL
	StatusCode int    `json:"statusCode"` // 状态码: 800=失效, 801=等待扫码, 802=等待确认, 803=成功
	StatusMsg  string `json:"statusMsg"`  // 状态消息
}

var (
	currentQRState *QRLoginState
	qrMutex        sync.Mutex
)

//export NeteaseQRGetKey
// NeteaseQRGetKey 获取二维码登录的 key 和 URL
// 返回: JSON 字符串 {"uniKey": "xxx", "qrcodeUrl": "xxx"}
func NeteaseQRGetKey() *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	qrMutex.Lock()
	defer qrMutex.Unlock()

	qrService := service.LoginQRService{}
	code, _, qrcodeUrl, err := qrService.GetKey()
	if err != nil {
		lastError = "Failed to get QR key: " + err.Error()
		return nil
	}
	if code != 200 || qrcodeUrl == "" {
		lastError = "Failed to get QR key, code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return nil
	}

	currentQRState = &QRLoginState{
		UniKey:     qrService.UniKey,
		QRCodeURL:  qrcodeUrl,
		StatusCode: 801, // 等待扫码
		StatusMsg:  "等待扫码",
	}

	jsonBytes, err := json.Marshal(currentQRState)
	if err != nil {
		lastError = "Failed to marshal QR state: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseQRCheckStatus
// NeteaseQRCheckStatus 检查二维码扫码状态
// 返回: JSON 字符串 {"statusCode": xxx, "statusMsg": "xxx"}
// statusCode: 800=失效, 801=等待扫码, 802=待确认, 803=成功
func NeteaseQRCheckStatus() *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	qrMutex.Lock()
	defer qrMutex.Unlock()

	if currentQRState == nil || currentQRState.UniKey == "" {
		lastError = "QR key not initialized"
		return nil
	}

	qrService := service.LoginQRService{UniKey: currentQRState.UniKey}
	code, _, err := qrService.CheckQR()
	if err != nil {
		lastError = "Failed to check QR status: " + err.Error()
		return nil
	}

	statusCode := int(code)
	var statusMsg string
	switch statusCode {
	case 800:
		statusMsg = "二维码已失效"
		currentQRState = nil // 清除状态
	case 801:
		statusMsg = "等待扫码"
	case 802:
		statusMsg = "已扫码，等待确认"
	case 803:
		statusMsg = "登录成功"
		// 登录成功后获取用户信息
		if NeteaseRefreshLogin() == 1 {
			statusMsg = "登录成功"
		} else {
			statusMsg = "登录成功，但获取用户信息失败"
		}
		currentQRState = nil // 清除状态
	default:
		statusMsg = "未知状态: " + strconv.Itoa(statusCode)
	}

	result := QRLoginState{
		StatusCode: statusCode,
		StatusMsg:  statusMsg,
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal QR status: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseQRCancelLogin
// NeteaseQRCancelLogin 取消二维码登录
func NeteaseQRCancelLogin() {
	qrMutex.Lock()
	defer qrMutex.Unlock()
	currentQRState = nil
}

// PlaylistInfo 歌单信息
type PlaylistInfo struct {
	ID        int64  `json:"id"`
	Name      string `json:"name"`
	SongCount int    `json:"songCount"`
	CoverUrl  string `json:"coverUrl"`
	CreatorId int64  `json:"creatorId"`
}

//export NeteaseGetUserPlaylists
// NeteaseGetUserPlaylists 获取用户歌单列表
// limit: 每页数量 (0 使用默认值 30)
// offset: 偏移量
// 返回: JSON 数组字符串，包含歌单信息和是否有更多 {"playlists": [...], "hasMore": bool}
func NeteaseGetUserPlaylists(limit C.int, offset C.int) *C.char {
	if currentUser == nil || currentUser.UserId == 0 {
		lastError = "Not logged in"
		return nil
	}

	limitVal := int(limit)
	if limitVal <= 0 {
		limitVal = 30
	}
	offsetVal := int(offset)

	// 直接使用 service API 获取更完整的歌单信息
	userPlaylists := service.UserPlaylistService{
		Uid:    strconv.FormatInt(currentUser.UserId, 10),
		Limit:  strconv.Itoa(limitVal),
		Offset: strconv.Itoa(offsetVal),
	}
	code, response := userPlaylists.UserPlaylist()
	if code != 200 {
		lastError = "UserPlaylist API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return nil
	}

	// 解析歌单数组
	var playlists []PlaylistInfo
	_, _ = jsonparser.ArrayEach(response, func(value []byte, dataType jsonparser.ValueType, offset int, err error) {
		var p PlaylistInfo
		if id, err := jsonparser.GetInt(value, "id"); err == nil {
			p.ID = id
		}
		if name, err := jsonparser.GetString(value, "name"); err == nil {
			p.Name = name
		}
		if trackCount, err := jsonparser.GetInt(value, "trackCount"); err == nil {
			p.SongCount = int(trackCount)
		}
		if coverUrl, err := jsonparser.GetString(value, "coverImgUrl"); err == nil {
			p.CoverUrl = coverUrl
		}
		if creatorId, err := jsonparser.GetInt(value, "userId"); err == nil {
			p.CreatorId = creatorId
		}
		playlists = append(playlists, p)
	}, "playlist")

	// 获取是否有更多
	hasMore, _ := jsonparser.GetBoolean(response, "more")

	result := struct {
		Playlists []PlaylistInfo `json:"playlists"`
		HasMore   bool           `json:"hasMore"`
	}{
		Playlists: playlists,
		HasMore:   hasMore,
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal playlists: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseGetPlaylistDetail
// NeteaseGetPlaylistDetail 获取歌单详情（包含歌单信息和歌曲）
// playlistId: 歌单 ID
// 返回: JSON 字符串，包含歌单信息 {"id", "name", "songCount", "coverUrl", "creatorId", "songs": [...]}
func NeteaseGetPlaylistDetail(playlistId C.longlong) *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	// 调用歌单详情 API
	playlistDetail := service.PlaylistDetailService{
		Id: strconv.FormatInt(int64(playlistId), 10),
	}
	code, response := playlistDetail.PlaylistDetail()
	if code != 200 {
		lastError = "PlaylistDetail API returned code: " + strconv.FormatFloat(code, 'f', 0, 64)
		return nil
	}

	// 解析歌单基本信息
	var result struct {
		ID        int64      `json:"id"`
		Name      string     `json:"name"`
		SongCount int        `json:"songCount"`
		CoverUrl  string     `json:"coverUrl"`
		CreatorId int64      `json:"creatorId"`
		Songs     []SongInfo `json:"songs"`
	}

	if id, err := jsonparser.GetInt(response, "playlist", "id"); err == nil {
		result.ID = id
	}
	if name, err := jsonparser.GetString(response, "playlist", "name"); err == nil {
		result.Name = name
	}
	if trackCount, err := jsonparser.GetInt(response, "playlist", "trackCount"); err == nil {
		result.SongCount = int(trackCount)
	}
	if coverUrl, err := jsonparser.GetString(response, "playlist", "coverImgUrl"); err == nil {
		result.CoverUrl = coverUrl
	}
	if creatorId, err := jsonparser.GetInt(response, "playlist", "userId"); err == nil {
		result.CreatorId = creatorId
	}

	// 解析歌曲列表
	_, _ = jsonparser.ArrayEach(response, func(value []byte, dataType jsonparser.ValueType, offset int, err error) {
		var song SongInfo
		if id, err := jsonparser.GetInt(value, "id"); err == nil {
			song.ID = id
		}
		if name, err := jsonparser.GetString(value, "name"); err == nil {
			song.Name = name
		}
		if dt, err := jsonparser.GetInt(value, "dt"); err == nil {
			song.Duration = float64(dt) / 1000.0
		}

		// 艺术家
		_, _ = jsonparser.ArrayEach(value, func(ar []byte, dataType jsonparser.ValueType, offset int, err error) {
			if name, err := jsonparser.GetString(ar, "name"); err == nil {
				song.Artists = append(song.Artists, name)
			}
		}, "ar")

		// 专辑
		if albumName, err := jsonparser.GetString(value, "al", "name"); err == nil {
			song.Album = albumName
		}
		if albumId, err := jsonparser.GetInt(value, "al", "id"); err == nil {
			song.AlbumID = albumId
		}
		if coverUrl, err := jsonparser.GetString(value, "al", "picUrl"); err == nil {
			song.CoverUrl = coverUrl
		}

		result.Songs = append(result.Songs, song)
	}, "playlist", "tracks")

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal playlist detail: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseGetPlaylistSongs
// NeteaseGetPlaylistSongs 获取指定歌单的歌曲
// playlistId: 歌单 ID
// getAll: 1 = 获取全部歌曲, 0 = 只获取第一页
// 返回: JSON 数组字符串，包含歌曲信息
func NeteaseGetPlaylistSongs(playlistId C.longlong, getAll C.int) *C.char {
	if !initialized {
		lastError = "Not initialized"
		return nil
	}

	codeType, songs := netease.FetchSongsOfPlaylist(int64(playlistId), getAll == 1)
	if codeType != 0 { // Success = 0
		lastError = "FetchSongsOfPlaylist failed with code: " + strconv.Itoa(int(codeType))
		return nil
	}

	// 转换为 SongInfo 格式
	result := make([]SongInfo, len(songs))
	for i, s := range songs {
		artists := make([]string, len(s.Artists))
		for j, a := range s.Artists {
			artists[j] = a.Name
		}
		
		coverUrl := ""
		if s.Album.PicUrl != "" {
			coverUrl = s.Album.PicUrl
		}

		result[i] = SongInfo{
			ID:       s.Id,
			Name:     s.Name,
			Duration: float64(s.Duration) / 1000.0, // 毫秒转秒
			Artists:  artists,
			Album:    s.Album.Name,
			AlbumID:  s.Album.Id,
			CoverUrl: coverUrl,
		}
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		lastError = "Failed to marshal playlist songs: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

//export NeteaseSearchPlaylistsByKeyword
// NeteaseSearchPlaylistsByKeyword 根据关键词搜索用户歌单
// keyword: 搜索关键词 (支持 | 分隔多个关键词)
// 返回: JSON 数组字符串，包含匹配的歌单信息
func NeteaseSearchPlaylistsByKeyword(keywordC *C.char) *C.char {
	if currentUser == nil || currentUser.UserId == 0 {
		lastError = "Not logged in"
		return nil
	}

	keyword := C.GoString(keywordC)
	if keyword == "" {
		lastError = "Keyword is empty"
		return nil
	}

	// 拆分关键词 (支持 | 分隔)
	var keywords []string
	for _, k := range splitKeywords(keyword) {
		if k != "" {
			keywords = append(keywords, k)
		}
	}

	if len(keywords) == 0 {
		lastError = "No valid keywords"
		return nil
	}

	// 获取所有歌单并匹配 - 直接使用 API
	var matchedPlaylists []PlaylistInfo
	offset := 0
	for {
		userPlaylists := service.UserPlaylistService{
			Uid:    strconv.FormatInt(currentUser.UserId, 10),
			Limit:  "50",
			Offset: strconv.Itoa(offset),
		}
		code, response := userPlaylists.UserPlaylist()
		if code != 200 {
			break
		}

		count := 0
		_, _ = jsonparser.ArrayEach(response, func(value []byte, dataType jsonparser.ValueType, off int, err error) {
			count++
			name, _ := jsonparser.GetString(value, "name")
			for _, kw := range keywords {
				if containsIgnoreCase(name, kw) {
					var p PlaylistInfo
					if id, err := jsonparser.GetInt(value, "id"); err == nil {
						p.ID = id
					}
					p.Name = name
					if trackCount, err := jsonparser.GetInt(value, "trackCount"); err == nil {
						p.SongCount = int(trackCount)
					}
					if coverUrl, err := jsonparser.GetString(value, "coverImgUrl"); err == nil {
						p.CoverUrl = coverUrl
					}
					if creatorId, err := jsonparser.GetInt(value, "userId"); err == nil {
						p.CreatorId = creatorId
					}
					matchedPlaylists = append(matchedPlaylists, p)
					break // 已匹配，跳到下一个歌单
				}
			}
		}, "playlist")

		hasMore, _ := jsonparser.GetBoolean(response, "more")
		if !hasMore || count == 0 {
			break
		}
		offset += count
	}

	jsonBytes, err := json.Marshal(matchedPlaylists)
	if err != nil {
		lastError = "Failed to marshal matched playlists: " + err.Error()
		return nil
	}

	return C.CString(string(jsonBytes))
}

// splitKeywords 分割关键词 (支持 | 分隔)
func splitKeywords(s string) []string {
	var result []string
	current := ""
	for _, c := range s {
		if c == '|' {
			if current != "" {
				result = append(result, current)
				current = ""
			}
		} else {
			current += string(c)
		}
	}
	if current != "" {
		result = append(result, current)
	}
	return result
}

// containsIgnoreCase 不区分大小写的字符串包含检查
func containsIgnoreCase(s, substr string) bool {
	// 简单实现：转换为小写后比较
	sLower := ""
	for _, c := range s {
		if c >= 'A' && c <= 'Z' {
			sLower += string(c + 32)
		} else {
			sLower += string(c)
		}
	}
	substrLower := ""
	for _, c := range substr {
		if c >= 'A' && c <= 'Z' {
			substrLower += string(c + 32)
		} else {
			substrLower += string(c)
		}
	}
	
	// 检查是否包含
	for i := 0; i <= len(sLower)-len(substrLower); i++ {
		if sLower[i:i+len(substrLower)] == substrLower {
			return true
		}
	}
	return false
}

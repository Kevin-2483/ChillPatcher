package api

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"
)

const (
	appID            = 3116
	srcAppID         = 2919
	clientVer        = 11040
	songURLClientVer = 11430
)

type Options struct {
	GatewayURL string
	LoginURL   string
	LyricsURL  string
	HTTPClient *http.Client
}

type Client struct {
	dataDir         string
	device          Device
	sessions        SessionState
	gatewayURL      string
	loginURL        string
	lyricsURL       string
	httpClient      *http.Client
	cookies         *persistentCookieJar
	cache           *responseCache
	verificationURL string
	mu              sync.RWMutex
}

func NewClient(dataDir string) (*Client, error) {
	return NewClientWithOptions(dataDir, Options{})
}

func NewClientWithOptions(dataDir string, options Options) (*Client, error) {
	if strings.TrimSpace(dataDir) == "" {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "data directory is required", Operation: "init"}
	}
	if err := os.MkdirAll(dataDir, 0700); err != nil {
		return nil, storageError("init", err)
	}
	device, err := loadDevice(dataDir)
	if err != nil {
		return nil, err
	}
	sessions, err := loadSessions(dataDir)
	if err != nil {
		return nil, err
	}
	client := options.HTTPClient
	if client == nil {
		client = &http.Client{Timeout: 30 * time.Second}
	}
	cookies, err := newPersistentCookieJar(dataDir, client.Jar)
	if err != nil {
		return nil, err
	}
	client.Jar = cookies
	result := &Client{
		dataDir: dataDir, device: device, sessions: sessions, httpClient: client, cookies: cookies,
		cache:      newResponseCache(dataDir),
		gatewayURL: defaultURL(options.GatewayURL, "https://gateway.kugou.com"),
		loginURL:   defaultURL(options.LoginURL, "https://login-user.kugou.com"),
		lyricsURL:  defaultURL(options.LyricsURL, "https://lyrics.kugou.com"),
	}
	if err := result.startVerificationServer(); err != nil {
		return nil, err
	}
	return result, nil
}

func (c *Client) Device() Device {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.device
}

func (c *Client) Sessions() SessionState {
	c.mu.RLock()
	defer c.mu.RUnlock()
	state := c.sessions
	state.Accounts = append([]Session(nil), c.sessions.Accounts...)
	return state
}

func (c *Client) UpsertSession(session Session, makeActive bool) error {
	if session.UserID <= 0 || strings.TrimSpace(session.Token) == "" {
		return &BridgeError{Code: ErrorInvalidArgument, Message: "userid and token are required", Operation: "setSession"}
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	session.Updated = time.Now().Unix()
	found := false
	for index := range c.sessions.Accounts {
		if c.sessions.Accounts[index].UserID == session.UserID {
			c.sessions.Accounts[index] = session
			found = true
			break
		}
	}
	if !found {
		c.sessions.Accounts = append(c.sessions.Accounts, session)
	}
	if makeActive || c.sessions.ActiveUserID == 0 {
		c.sessions.ActiveUserID = session.UserID
	}
	return saveSessions(c.dataDir, c.sessions)
}

func (c *Client) SwitchSession(userID int64) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	if _, ok := findSession(c.sessions, userID); !ok {
		return &BridgeError{Code: ErrorSessionNotFound, Message: "session not found", Operation: "switchSession"}
	}
	c.sessions.ActiveUserID = userID
	return saveSessions(c.dataDir, c.sessions)
}

func (c *Client) RemoveSession(userID int64) error {
	c.mu.Lock()
	defer c.mu.Unlock()
	accounts := c.sessions.Accounts[:0]
	removed := false
	for _, account := range c.sessions.Accounts {
		if account.UserID == userID {
			removed = true
			continue
		}
		accounts = append(accounts, account)
	}
	if !removed {
		return &BridgeError{Code: ErrorSessionNotFound, Message: "session not found", Operation: "removeSession"}
	}
	c.sessions.Accounts = accounts
	if c.sessions.ActiveUserID == userID {
		c.sessions.ActiveUserID = 0
		if len(accounts) > 0 {
			c.sessions.ActiveUserID = accounts[0].UserID
		}
	}
	return saveSessions(c.dataDir, c.sessions)
}

func (c *Client) CreateQRCode() (QRCode, error) {
	params := map[string]string{"appid": "1001", "type": "1", "plat": "4", "qrcode_txt": "https://h5.kugou.com/apps/loginQRCode/html/index.html?appid=3116&", "srcappid": strconv.Itoa(srcAppID)}
	body, err := c.request("qrCreate", http.MethodGet, c.loginURL, "/v2/qrcode", params, nil, "web", "", false)
	if err != nil {
		return QRCode{}, err
	}
	key := findString(body, "data", "qrcode")
	if key == "" {
		key = findString(body, "data", "key")
	}
	if key == "" {
		return QRCode{}, &BridgeError{Code: ErrorDecode, Message: "QR key missing in response", Operation: "qrCreate"}
	}
	return QRCode{Key: key, URL: "https://h5.kugou.com/apps/loginQRCode/html/index.html?qrcode=" + url.QueryEscape(key)}, nil
}

func (c *Client) RegisterDevice() (Device, error) {
	c.mu.RLock()
	device := c.device
	c.mu.RUnlock()
	if device.Registered && device.RegistrationVersion >= 2 {
		return device, nil
	}
	body, err := c.registerDeviceV2(device)
	if err != nil {
		return Device{}, err
	}
	dfid := findString(body, "data", "dfid")
	if dfid == "" {
		dfid = stringValue(body["dfid"])
	}
	if dfid == "" {
		return Device{}, &BridgeError{Code: ErrorDecode, Message: "dfid missing in response", Operation: "registerDevice"}
	}
	device.DFID = dfid
	device.Registered = true
	device.RegistrationVersion = 2
	if err := writeJSON(filepath.Join(c.dataDir, deviceFileName), device); err != nil {
		return Device{}, err
	}
	c.mu.Lock()
	c.device = device
	c.mu.Unlock()
	return device, nil
}

func (c *Client) CheckQRCode(key string) (QRStatus, error) {
	if strings.TrimSpace(key) == "" {
		return QRStatus{}, &BridgeError{Code: ErrorInvalidArgument, Message: "QR key is required", Operation: "qrCheck"}
	}
	params := map[string]string{"plat": "4", "appid": strconv.Itoa(appID), "srcappid": strconv.Itoa(srcAppID), "qrcode": key}
	body, err := c.request("qrCheck", http.MethodGet, c.loginURL, "/v2/get_userinfo_qrcode", params, nil, "web", "", false)
	if err != nil {
		return QRStatus{}, err
	}
	data := nestedMap(body, "data")
	status := int(numberValue(data["status"]))
	result := QRStatus{Status: status, Data: marshalRaw(data)}
	if status == 4 {
		session := Session{UserID: int64(numberValue(data["userid"])), Token: stringValue(data["token"]), VIPType: int(numberValue(data["vip_type"])), VIPToken: stringValue(data["vip_token"]), Nickname: stringValue(data["nickname"]), Avatar: stringValue(data["pic"])}
		if err := c.UpsertSession(session, true); err != nil {
			return QRStatus{}, err
		}
		result.Session = &session
	}
	return result, nil
}

func (c *Client) UserPlaylists(userID int64, page, pageSize int) (PageResult, error) {
	session, err := c.session(userID)
	if err != nil {
		return PageResult{}, err
	}
	page, pageSize = normalizePage(page, pageSize)
	cacheKey := fmt.Sprintf("user-playlists:%d:%d:%d", session.UserID, page, pageSize)
	if cached, ok := c.cache.GetFresh(cacheKey); ok {
		return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(cached)}, nil
	}
	data := map[string]interface{}{"userid": session.UserID, "token": session.Token, "total_ver": 979, "type": 2, "page": page, "pagesize": pageSize}
	params := map[string]string{"plat": "1", "userid": strconv.FormatInt(session.UserID, 10), "token": session.Token}
	body, err := c.request("userPlaylists", http.MethodPost, c.gatewayURL, "/v7/get_all_list", params, data, "android", "cloudlist.service.kugou.com", false)
	if err != nil {
		if cached, ok := c.cache.GetStale(cacheKey, 24*time.Hour); ok && isTransientError(err) {
			return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(cached)}, nil
		}
		return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID}, err
	}
	c.cache.Put(cacheKey, body, 2*time.Minute)
	return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(body)}, nil
}

func (c *Client) PlaylistSongs(userID, listID int64, globalID string, page, pageSize int) (PageResult, error) {
	page, pageSize = normalizePage(page, pageSize)
	if globalID != "" {
		cacheKey := fmt.Sprintf("playlist-songs:global:%d:%s:%d:%d", userID, globalID, page, pageSize)
		if cached, ok := c.cache.GetFresh(cacheKey); ok {
			return PageResult{Page: page, PageSize: pageSize, UserID: userID, Data: marshalRaw(cached)}, nil
		}
		params := map[string]string{"area_code": "1", "begin_idx": strconv.Itoa((page - 1) * pageSize), "plat": "1", "type": "1", "mode": "1", "personal_switch": "1", "extend_fields": "abtags,hot_cmt,popularization", "pagesize": strconv.Itoa(pageSize), "global_collection_id": globalID}
		body, err := c.request("playlistSongs", http.MethodGet, c.gatewayURL, "/pubsongs/v2/get_other_list_file_nofilt", params, nil, "android", "", false)
		if err != nil {
			if cached, ok := c.cache.GetStale(cacheKey, 24*time.Hour); ok && isTransientError(err) {
				return PageResult{Page: page, PageSize: pageSize, UserID: userID, Data: marshalRaw(cached)}, nil
			}
			return PageResult{Page: page, PageSize: pageSize, UserID: userID}, err
		}
		c.cache.Put(cacheKey, body, 2*time.Minute)
		return PageResult{Page: page, PageSize: pageSize, UserID: userID, Data: marshalRaw(body)}, nil
	}
	if listID <= 0 {
		return PageResult{}, &BridgeError{Code: ErrorInvalidArgument, Message: "listId or globalCollectionId is required", Operation: "playlistSongs"}
	}
	session, err := c.session(userID)
	if err != nil {
		return PageResult{}, err
	}
	cacheKey := fmt.Sprintf("playlist-songs:private:%d:%d:%d:%d", session.UserID, listID, page, pageSize)
	if cached, ok := c.cache.GetFresh(cacheKey); ok {
		return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(cached)}, nil
	}
	data := map[string]interface{}{"listid": listID, "userid": session.UserID, "area_code": 1, "show_relate_goods": 0, "pagesize": pageSize, "allplatform": 1, "show_cover": 1, "type": 0, "token": session.Token, "page": page}
	params := map[string]string{"userid": strconv.FormatInt(session.UserID, 10), "token": session.Token}
	body, err := c.request("playlistSongs", http.MethodPost, c.gatewayURL, "/v4/get_list_all_file", params, data, "android", "cloudlist.service.kugou.com", false)
	if err != nil {
		if cached, ok := c.cache.GetStale(cacheKey, 24*time.Hour); ok && isTransientError(err) {
			return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(cached)}, nil
		}
		return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID}, err
	}
	c.cache.Put(cacheKey, body, 2*time.Minute)
	return PageResult{Page: page, PageSize: pageSize, UserID: session.UserID, Data: marshalRaw(body)}, nil
}

func (c *Client) SongURL(userID int64, input SongURLRequest) (json.RawMessage, error) {
	if strings.TrimSpace(input.Hash) == "" {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "song hash is required", Operation: "songURL"}
	}
	session := Session{}
	if userID != 0 {
		var err error
		session, err = c.session(userID)
		if err != nil {
			return nil, err
		}
	}
	// The module intentionally uses the standard stream for maximum availability.
	// Ignore higher qualities even if a stale caller still sends one.
	quality := "128"
	c.mu.RLock()
	device := c.device
	c.mu.RUnlock()
	freePart := "0"
	if userID == 0 {
		freePart = "1"
	}
	params := map[string]string{
		"album_audio_id": "0", "area_code": "1", "hash": strings.ToLower(input.Hash),
		"behavior": "play", "cmd": "26",
		"version": strconv.Itoa(songURLClientVer), "clientver": strconv.Itoa(songURLClientVer),
		"pidversion": "3001", "IsFreePart": freePart, "album_id": "0",
		"ssa_flag": "is_fromtrack", "page_id": "967177915", "quality": quality, "ppage_id": "356753938",
		"cdnBackup": "1", "module": "", "key": songURLKey(input.Hash, device.MID, session.UserID),
		"userid": strconv.FormatInt(session.UserID, 10), "token": session.Token, "uuid": "-", "pid": "411",
	}
	body, err := c.request("songURL", http.MethodGet, c.gatewayURL, "/v5/url", params, nil, "android", "trackercdn.kugou.com", false)
	if err == nil && numberValue(body["status"]) == 3 && numberValue(body["priv_status"]) == 0 {
		return nil, &BridgeError{Code: ErrorAPI, Message: "当前歌曲没有可用的播放权限", Operation: "songURL", APIErrorCode: 30003}
	}
	return marshalRaw(body), err
}

func (c *Client) ClaimDailyVIP(userID int64, receiveDay string) (json.RawMessage, error) {
	session, err := c.session(userID)
	if err != nil {
		return nil, err
	}
	if _, err := time.Parse("2006-01-02", receiveDay); err != nil {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "receive day must use yyyy-MM-dd", Operation: "claimDailyVIP", Cause: err.Error()}
	}
	params := map[string]string{
		"source_id":   "90139",
		"receive_day": receiveDay,
		"userid":      strconv.FormatInt(session.UserID, 10),
		"token":       session.Token,
	}
	body, err := c.requestWithHeaders(
		"claimDailyVIP",
		http.MethodPost,
		c.gatewayURL,
		"/youth/v1/recharge/receive_vip_listen_song",
		params,
		nil,
		"android",
		"",
		false,
		map[string]string{"Content-Type": "application/x-www-form-urlencoded"},
	)
	return marshalRaw(body), err
}

func (c *Client) UpgradeDailyVIP(userID int64) (json.RawMessage, error) {
	session, err := c.session(userID)
	if err != nil {
		return nil, err
	}
	params := map[string]string{
		"kugouid": strconv.FormatInt(session.UserID, 10),
		"ad_type": "1",
		"userid":  strconv.FormatInt(session.UserID, 10),
		"token":   session.Token,
	}
	body, err := c.request(
		"upgradeDailyVIP",
		http.MethodPost,
		c.gatewayURL,
		"/youth/v1/listen_song/upgrade_vip_reward",
		params,
		nil,
		"android",
		"",
		false,
	)
	return marshalRaw(body), err
}

func (c *Client) SongPrivilege(userID int64, input SongURLRequest) (json.RawMessage, error) {
	if strings.TrimSpace(input.Hash) == "" {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "song hash is required", Operation: "songPrivilege"}
	}
	if userID != 0 {
		if _, err := c.session(userID); err != nil {
			return nil, err
		}
	}
	data := map[string]interface{}{
		"appid":            appID,
		"area_code":        1,
		"behavior":         "play",
		"clientver":        clientVer,
		"need_hash_offset": 1,
		"relate":           1,
		"support_verify":   1,
		"resource": []map[string]interface{}{{
			"type": "audio", "page_id": 0, "hash": input.Hash, "album_id": input.AlbumID,
		}},
		"qualities": []string{"128", "320", "flac", "high", "viper_atmos", "viper_tape", "viper_clear", "super", "multitrack"},
	}
	body, err := c.request(
		"songPrivilege",
		http.MethodPost,
		c.gatewayURL,
		"/v2/get_res_privilege/lite",
		nil,
		data,
		"android",
		"media.store.kugou.com",
		false,
	)
	return marshalRaw(body), err
}

func (c *Client) Lyric(userID int64, input LyricRequest) (json.RawMessage, error) {
	if strings.TrimSpace(input.Hash) == "" && strings.TrimSpace(input.Keywords) == "" {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "song hash or keywords are required", Operation: "lyric"}
	}
	search := map[string]string{"album_audio_id": strconv.FormatInt(input.AlbumAudioID, 10), "appid": strconv.Itoa(appID), "clientver": strconv.Itoa(clientVer), "duration": "0", "hash": input.Hash, "keyword": input.Keywords, "lrctxt": "1", "man": "no"}
	body, err := c.request("lyricSearch", http.MethodGet, c.lyricsURL, "/v1/search", search, nil, "none", "", true)
	if err != nil {
		return nil, err
	}
	candidates, _ := nestedMap(body, "candidates")["0"].(map[string]interface{})
	if candidates == nil {
		if list, ok := body["candidates"].([]interface{}); ok && len(list) > 0 {
			candidates, _ = list[0].(map[string]interface{})
		}
	}
	if candidates == nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "lyric candidate not found", Operation: "lyricSearch"}
	}
	format := input.Format
	if format == "" {
		format = "lrc"
	}
	params := map[string]string{"ver": "1", "client": "android", "id": stringValue(candidates["id"]), "accesskey": stringValue(candidates["accesskey"]), "fmt": format, "charset": "utf8"}
	download, err := c.request("lyricDownload", http.MethodGet, c.lyricsURL, "/download", params, nil, "android", "", false)
	return marshalRaw(download), err
}

func (c *Client) SetFavorite(userID int64, input FavoriteRequest, favorite bool) error {
	if input.ListID <= 0 || strings.TrimSpace(input.Hash) == "" {
		return &BridgeError{Code: ErrorInvalidArgument, Message: "favorite list and song hash are required", Operation: "favorite"}
	}
	session, err := c.session(userID)
	if err != nil {
		return err
	}
	params := map[string]string{"userid": strconv.FormatInt(session.UserID, 10), "token": session.Token}
	var path string
	var data interface{}
	if favorite {
		path = "/cloudlist.service/v6/add_song"
		data = map[string]interface{}{"userid": session.UserID, "token": session.Token, "listid": input.ListID, "list_ver": 0, "type": 0, "slow_upload": 1, "scene": "false;null", "data": []map[string]interface{}{{"number": 1, "name": input.Name, "hash": input.Hash, "size": 0, "sort": 0, "timelen": 0, "bitrate": 0, "album_id": input.AlbumID, "mixsongid": input.AlbumAudioID}}}
	} else {
		if input.FileID <= 0 {
			return &BridgeError{Code: ErrorInvalidArgument, Message: "favorite entry fileId is required", Operation: "favorite"}
		}
		path = "/v4/delete_songs"
		data = map[string]interface{}{"listid": input.ListID, "userid": session.UserID, "data": []map[string]interface{}{{"fileid": input.FileID}}, "type": 0, "token": session.Token, "list_ver": 0}
	}
	_, err = c.request("favorite", http.MethodPost, c.gatewayURL, path, params, data, "android", "cloudlist.service.kugou.com", false)
	if err == nil {
		c.cache.Clear()
	}
	return err
}

func (c *Client) session(userID int64) (Session, error) {
	c.mu.RLock()
	defer c.mu.RUnlock()
	if userID == 0 {
		userID = c.sessions.ActiveUserID
	}
	if userID == 0 {
		return Session{}, &BridgeError{Code: ErrorNotLoggedIn, Message: "no active session", Operation: "session"}
	}
	session, ok := findSession(c.sessions, userID)
	if !ok {
		return Session{}, &BridgeError{Code: ErrorSessionNotFound, Message: "session not found", Operation: "session"}
	}
	return session, nil
}

func (c *Client) request(operation, method, base, path string, requestParams map[string]string, data interface{}, signType, router string, skipSignature bool) (map[string]interface{}, error) {
	return c.requestWithHeaders(operation, method, base, path, requestParams, data, signType, router, skipSignature, nil)
}

func (c *Client) requestWithHeaders(operation, method, base, path string, requestParams map[string]string, data interface{}, signType, router string, skipSignature bool, extraHeaders map[string]string) (map[string]interface{}, error) {
	c.mu.RLock()
	device := c.device
	active, _ := findSession(c.sessions, c.sessions.ActiveUserID)
	c.mu.RUnlock()
	params := map[string]string{"dfid": device.DFID, "mid": device.MID, "uuid": "-", "appid": strconv.Itoa(appID), "clientver": strconv.Itoa(clientVer), "userid": strconv.FormatInt(active.UserID, 10), "clienttime": strconv.FormatInt(time.Now().Unix(), 10)}
	if active.Token != "" {
		params["token"] = active.Token
	}
	for key, value := range requestParams {
		params[key] = value
	}
	bodyBytes := []byte{}
	if data != nil {
		var err error
		bodyBytes, err = json.Marshal(data)
		if err != nil {
			return nil, &BridgeError{Code: ErrorDecode, Message: "request encoding failed", Operation: operation, Cause: err.Error()}
		}
	}
	if !skipSignature {
		secret := androidSecret
		if signType == "web" {
			secret = webSecret
		}
		params["signature"] = signature(params, string(bodyBytes), secret)
	}
	endpoint, err := url.Parse(strings.TrimRight(base, "/") + path)
	if err != nil {
		return nil, &BridgeError{Code: ErrorInvalidArgument, Message: "invalid endpoint", Operation: operation, Cause: err.Error()}
	}
	query := endpoint.Query()
	for key, value := range params {
		query.Set(key, value)
	}
	endpoint.RawQuery = query.Encode()
	request, err := http.NewRequest(method, endpoint.String(), bytes.NewReader(bodyBytes))
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "request creation failed", Operation: operation, Cause: err.Error()}
	}
	request.Header.Set("User-Agent", "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi")
	request.Header.Set("dfid", device.DFID)
	request.Header.Set("mid", device.MID)
	request.Header.Set("clienttime", params["clienttime"])
	setCommonHeaders(request.Header)
	if router != "" {
		request.Header.Set("x-router", router)
	}
	for name, value := range extraHeaders {
		request.Header.Set(name, value)
	}
	if len(bodyBytes) > 0 {
		request.Header.Set("Content-Type", "application/json")
	}
	response, err := c.httpClient.Do(request)
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "request failed", Operation: operation, Cause: err.Error()}
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 16<<20))
	if err != nil {
		return nil, &BridgeError{Code: ErrorNetwork, Message: "response read failed", Operation: operation, Cause: err.Error()}
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, &BridgeError{Code: ErrorHTTP, Message: "unexpected HTTP status", Operation: operation, HTTPStatus: response.StatusCode, Cause: truncate(string(responseBody), 512)}
	}
	var decoded map[string]interface{}
	if err := json.Unmarshal(responseBody, &decoded); err != nil {
		return nil, &BridgeError{Code: ErrorDecode, Message: "invalid JSON response", Operation: operation, Cause: err.Error()}
	}
	status, hasStatus := decoded["status"]
	errorCode, hasErrorCode := decoded["error_code"]
	apiErrorCode := int(numberValue(errorCode))
	if apiErrorCode == 0 {
		apiErrorCode = int(numberValue(decoded["errcode"]))
	}
	if (hasStatus && numberValue(status) == 0) || (hasErrorCode && numberValue(errorCode) != 0) || apiErrorCode != 0 {
		ssaCode := response.Header.Get("ssa-code")
		verificationURL := ""
		if apiErrorCode == 20028 && ssaCode != "" && c.verificationURL != "" {
			verificationURL = c.verificationURL + "/verify?eventid=" + url.QueryEscape(ssaCode)
		}
		return nil, &BridgeError{Code: ErrorAPI, Message: firstString(decoded, "error", "msg", "message"), Operation: operation, APIErrorCode: apiErrorCode, SSACode: ssaCode, VerificationURL: verificationURL, Cause: truncate(string(responseBody), 512)}
	}
	return decoded, nil
}

func setCommonHeaders(headers http.Header) {
	headers.Set("kg-rc", "1")
	headers.Set("kg-thash", "5d816a0")
	headers.Set("kg-rec", "1")
	headers.Set("kg-rf", "B9EDA08A64250DEFFBCADDEE00F8F25F")
}

func isTransientError(err error) bool {
	var bridgeError *BridgeError
	if !errors.As(err, &bridgeError) {
		return false
	}
	return bridgeError.Code == ErrorNetwork || (bridgeError.Code == ErrorHTTP && bridgeError.HTTPStatus >= 500)
}

func findSession(state SessionState, userID int64) (Session, bool) {
	for _, session := range state.Accounts {
		if session.UserID == userID {
			return session, true
		}
	}
	return Session{}, false
}

func normalizePage(page, pageSize int) (int, int) {
	if page < 1 {
		page = 1
	}
	if pageSize < 1 {
		pageSize = 30
	}
	if pageSize > 200 {
		pageSize = 200
	}
	return page, pageSize
}

func nestedMap(value map[string]interface{}, key string) map[string]interface{} {
	nested, _ := value[key].(map[string]interface{})
	if nested == nil {
		return map[string]interface{}{}
	}
	return nested
}

func findString(value map[string]interface{}, keys ...string) string {
	current := value
	for index, key := range keys {
		if index == len(keys)-1 {
			return stringValue(current[key])
		}
		current = nestedMap(current, key)
	}
	return ""
}

func stringValue(value interface{}) string {
	switch typed := value.(type) {
	case string:
		return typed
	case json.Number:
		return typed.String()
	case float64:
		return strconv.FormatInt(int64(typed), 10)
	default:
		return ""
	}
}

func numberValue(value interface{}) float64 {
	switch typed := value.(type) {
	case float64:
		return typed
	case string:
		parsed, _ := strconv.ParseFloat(typed, 64)
		return parsed
	default:
		return 0
	}
}

func marshalRaw(value interface{}) json.RawMessage {
	data, _ := json.Marshal(value)
	return data
}

func firstString(value map[string]interface{}, keys ...string) string {
	for _, key := range keys {
		if result := stringValue(value[key]); result != "" {
			return result
		}
	}
	return "KuGou API request failed"
}

func defaultURL(value, fallback string) string {
	if value == "" {
		return fallback
	}
	return value
}

func truncate(value string, limit int) string {
	if len(value) <= limit {
		return value
	}
	return value[:limit]
}

func SessionFile(dataDir string) string {
	return filepath.Join(dataDir, sessionFileName)
}

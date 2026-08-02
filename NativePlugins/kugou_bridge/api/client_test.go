package api

import (
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestDevicePersists(t *testing.T) {
	dir := t.TempDir()
	first, err := NewClient(dir)
	if err != nil {
		t.Fatal(err)
	}
	second, err := NewClient(dir)
	if err != nil {
		t.Fatal(err)
	}
	if first.Device() != second.Device() {
		t.Fatalf("device changed: %#v != %#v", first.Device(), second.Device())
	}
	if first.Device().UUID == "-" || first.Device().MID != md5Hex(first.Device().UUID) {
		t.Fatalf("registration GUID is not independent and stable: %#v", first.Device())
	}
	info, err := os.Stat(filepath.Join(dir, deviceFileName))
	if err != nil || info.Size() == 0 {
		t.Fatalf("device file missing: %v", err)
	}
}

func TestV2DeviceRegistrationProfile(t *testing.T) {
	var captured bool
	transport := roundTripFunc(func(request *http.Request) (*http.Response, error) {
		captured = true
		if request.URL.Path != "/risk/v2/r_register_dev" || request.URL.Query().Get("uuid") != "-" || request.URL.Query().Get("part") != "1" || request.URL.Query().Get("platid") != "1" {
			t.Fatalf("unexpected registration request: %s", request.URL.String())
		}
		if request.URL.Query().Get("p") == "" || request.URL.Query().Get("signature") == "" || request.Header.Get("kg-rf") == "" {
			t.Fatalf("registration identity is incomplete: %s %#v", request.URL.String(), request.Header)
		}
		body, _ := io.ReadAll(request.Body)
		if len(body) == 0 {
			t.Fatal("encrypted registration body is empty")
		}
		return &http.Response{StatusCode: http.StatusBadGateway, Body: io.NopCloser(strings.NewReader("unavailable")), Header: make(http.Header), Request: request}, nil
	})
	client, err := NewClientWithOptions(t.TempDir(), Options{HTTPClient: &http.Client{Transport: transport}})
	if err != nil {
		t.Fatal(err)
	}
	_, err = client.RegisterDevice()
	assertErrorCode(t, err, ErrorHTTP)
	if !captured {
		t.Fatal("registration request was not sent")
	}
}

func TestRegistrationPayloadRoundTrip(t *testing.T) {
	seed, encoded, err := encryptRegistrationPayload(map[string]interface{}{"uuid": "guid", "batteryLevel": 100})
	if err != nil {
		t.Fatal(err)
	}
	cipherText, err := base64.StdEncoding.DecodeString(encoded)
	if err != nil {
		t.Fatal(err)
	}
	plain, err := decryptRegistrationPayload(cipherText, seed)
	if err != nil || !strings.Contains(string(plain), `"uuid":"guid"`) {
		t.Fatalf("registration payload round trip failed: %s %v", plain, err)
	}
}

func TestSessionsPersistAndSwitchAccounts(t *testing.T) {
	dir := t.TempDir()
	client, err := NewClient(dir)
	if err != nil {
		t.Fatal(err)
	}
	if err := client.UpsertSession(Session{UserID: 11, Token: "first"}, true); err != nil {
		t.Fatal(err)
	}
	if err := client.UpsertSession(Session{UserID: 22, Token: "second"}, false); err != nil {
		t.Fatal(err)
	}
	if err := client.SwitchSession(22); err != nil {
		t.Fatal(err)
	}
	reloaded, err := NewClient(dir)
	if err != nil {
		t.Fatal(err)
	}
	state := reloaded.Sessions()
	if state.ActiveUserID != 22 || len(state.Accounts) != 2 {
		t.Fatalf("unexpected sessions: %#v", state)
	}
	if err := reloaded.RemoveSession(22); err != nil {
		t.Fatal(err)
	}
	if reloaded.Sessions().ActiveUserID != 11 {
		t.Fatalf("unexpected active account: %#v", reloaded.Sessions())
	}
}

func TestQRCodeLoginSavesSession(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		switch request.URL.Path {
		case "/v2/qrcode":
			assertSignedRequest(t, request)
			writeResponse(writer, `{"status":1,"data":{"qrcode":"qr-key"}}`)
		case "/v2/get_userinfo_qrcode":
			assertSignedRequest(t, request)
			writeResponse(writer, `{"status":1,"data":{"status":4,"userid":42,"token":"token-42","nickname":"tester"}}`)
		default:
			http.NotFound(writer, request)
		}
	}))
	defer server.Close()
	client := testClient(t, server.URL)
	qr, err := client.CreateQRCode()
	if err != nil {
		t.Fatal(err)
	}
	if qr.Key != "qr-key" || !strings.Contains(qr.URL, "qr-key") {
		t.Fatalf("unexpected QR result: %#v", qr)
	}
	status, err := client.CheckQRCode(qr.Key)
	if err != nil {
		t.Fatal(err)
	}
	if status.Status != 4 || status.Session == nil || status.Session.UserID != 42 {
		t.Fatalf("unexpected QR status: %#v", status)
	}
	if client.Sessions().ActiveUserID != 42 {
		t.Fatalf("session was not activated: %#v", client.Sessions())
	}
}

func TestCommonIdentityHeadersAndCookiesPersist(t *testing.T) {
	dir := t.TempDir()
	requestCount := 0
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		requestCount++
		assertSignedRequest(t, request)
		if request.URL.Query().Get("uuid") != "-" {
			t.Fatalf("unexpected default uuid: %q", request.URL.Query().Get("uuid"))
		}
		for name, expected := range map[string]string{
			"kg-rc": "1", "kg-thash": "5d816a0", "kg-rec": "1", "kg-rf": "B9EDA08A64250DEFFBCADDEE00F8F25F",
		} {
			if request.Header.Get(name) != expected {
				t.Fatalf("missing common header %s: %q", name, request.Header.Get(name))
			}
		}
		if requestCount == 1 {
			http.SetCookie(writer, &http.Cookie{Name: "kg_risk", Value: "trusted", Path: "/", HttpOnly: true})
		} else if cookie, err := request.Cookie("kg_risk"); err != nil || cookie.Value != "trusted" {
			t.Fatalf("persisted cookie missing: %#v %v", cookie, err)
		}
		if request.URL.Path == "/v2/qrcode" {
			writeResponse(writer, `{"status":1,"data":{"qrcode":"qr-key"}}`)
			return
		}
		writeResponse(writer, `{"status":1,"data":{"status":1}}`)
	}))
	defer server.Close()

	first, err := NewClientWithOptions(dir, Options{GatewayURL: server.URL, LoginURL: server.URL, LyricsURL: server.URL})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := first.CreateQRCode(); err != nil {
		t.Fatal(err)
	}
	second, err := NewClientWithOptions(dir, Options{GatewayURL: server.URL, LoginURL: server.URL, LyricsURL: server.URL})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := second.CheckQRCode("qr-key"); err != nil {
		t.Fatal(err)
	}
	if info, err := os.Stat(filepath.Join(dir, cookieFileName)); err != nil || info.Size() == 0 {
		t.Fatalf("cookie file missing: %v", err)
	}
}

func TestPlaylistResponseCacheAndRiskErrors(t *testing.T) {
	dir := t.TempDir()
	requestCount := 0
	responseStatus := http.StatusOK
	responseBody := `{"status":1,"data":{"info":[{"listid":1}]}}`
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		requestCount++
		writer.WriteHeader(responseStatus)
		_, _ = writer.Write([]byte(responseBody))
	}))
	defer server.Close()
	create := func() *Client {
		client, err := NewClientWithOptions(dir, Options{GatewayURL: server.URL, LoginURL: server.URL, LyricsURL: server.URL})
		if err != nil {
			t.Fatal(err)
		}
		if err := client.UpsertSession(Session{UserID: 7, Token: "token"}, true); err != nil {
			t.Fatal(err)
		}
		return client
	}
	client := create()
	if _, err := client.UserPlaylists(7, 1, 30); err != nil {
		t.Fatal(err)
	}
	if _, err := client.UserPlaylists(7, 1, 30); err != nil {
		t.Fatal(err)
	}
	if requestCount != 1 {
		t.Fatalf("fresh response was not cached: %d requests", requestCount)
	}
	if _, err := create().UserPlaylists(7, 1, 30); err != nil || requestCount != 1 {
		t.Fatalf("disk cache was not reused: requests=%d err=%v", requestCount, err)
	}

	cacheKey := "user-playlists:7:1:30"
	client.cache.Put(cacheKey, map[string]interface{}{"status": 1, "data": map[string]interface{}{"info": []interface{}{}}}, -time.Second)
	responseStatus = http.StatusBadGateway
	if _, err := client.UserPlaylists(7, 1, 30); err != nil {
		t.Fatalf("transient failure did not use stale cache: %v", err)
	}

	client.cache.Put(cacheKey, map[string]interface{}{"status": 1}, -time.Second)
	responseStatus = http.StatusOK
	responseBody = `{"status":0,"errcode":20028,"error":"verification required"}`
	if _, err := client.UserPlaylists(7, 1, 30); err == nil {
		t.Fatal("risk verification error must not be hidden by stale cache")
	}
}

func TestAccountPlaylistPagination(t *testing.T) {
	var requests []map[string]interface{}
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		body, _ := io.ReadAll(request.Body)
		var payload map[string]interface{}
		_ = json.Unmarshal(body, &payload)
		requests = append(requests, payload)
		assertSignedRequest(t, request)
		writeResponse(writer, `{"status":1,"data":{"items":[]}}`)
	}))
	defer server.Close()
	client := testClient(t, server.URL)
	if err := client.UpsertSession(Session{UserID: 11, Token: "first"}, true); err != nil {
		t.Fatal(err)
	}
	if err := client.UpsertSession(Session{UserID: 22, Token: "second"}, false); err != nil {
		t.Fatal(err)
	}
	page, err := client.UserPlaylists(22, 3, 40)
	if err != nil {
		t.Fatal(err)
	}
	if page.UserID != 22 || page.Page != 3 || page.PageSize != 40 || requests[0]["token"] != "second" {
		t.Fatalf("unexpected account page: %#v %#v", page, requests[0])
	}
	page, err = client.PlaylistSongs(22, 99, "", 0, 500)
	if err != nil {
		t.Fatal(err)
	}
	if page.Page != 1 || page.PageSize != 200 || requests[1]["listid"] != float64(99) {
		t.Fatalf("unexpected playlist page: %#v %#v", page, requests[1])
	}
}

func TestSongURLAndLyricRequests(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		switch request.URL.Path {
		case "/v5/url":
			query := request.URL.Query()
			if query.Get("hash") != "abc" || query.Get("quality") != "128" || query.Get("key") == "" || query.Get("signature") == "" ||
				query.Get("album_audio_id") != "0" || query.Get("album_id") != "0" || query.Get("IsFreePart") != "0" ||
				query.Get("version") != "11430" || query.Get("clientver") != "11430" || query.Get("module") != "" ||
				query.Get("ppage_id") != "356753938" || request.Header.Get("User-Agent") != "Android15-1070-11083-46-0-DiscoveryDRADProtocol-wifi" {
				t.Fatalf("unexpected URL query: %s", request.URL.RawQuery)
			}
			writeResponse(writer, `{"status":1,"url":["https://example.invalid/song.mp3"]}`)
		case "/v1/search":
			writeResponse(writer, `{"status":1,"candidates":[{"id":"7","accesskey":"access"}]}`)
		case "/download":
			if request.URL.Query().Get("id") != "7" || request.URL.Query().Get("fmt") != "lrc" {
				t.Fatalf("unexpected lyric query: %s", request.URL.RawQuery)
			}
			writeResponse(writer, `{"status":1,"content":"WzAwOjAwLjAwXQ=="}`)
		default:
			http.NotFound(writer, request)
		}
	}))
	defer server.Close()
	client := testClient(t, server.URL)
	if err := client.UpsertSession(Session{UserID: 7, Token: "token"}, true); err != nil {
		t.Fatal(err)
	}
	result, err := client.SongURL(7, SongURLRequest{Hash: "ABC", AlbumAudioID: 456, AlbumID: 123, Quality: "320"})
	if err != nil || !strings.Contains(string(result), "song.mp3") {
		t.Fatalf("song URL failed: %s %v", result, err)
	}
	result, err = client.Lyric(0, LyricRequest{Hash: "ABC"})
	if err != nil || !strings.Contains(string(result), "content") {
		t.Fatalf("lyric failed: %s %v", result, err)
	}
}

func TestDailyVIPAndPrivilegeRequests(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		query := request.URL.Query()
		switch request.URL.Path {
		case "/youth/v1/recharge/receive_vip_listen_song":
			if request.Method != http.MethodPost || query.Get("source_id") != "90139" || query.Get("receive_day") != "2026-08-01" ||
				query.Get("userid") != "7" || query.Get("token") != "token" || request.Header.Get("Content-Type") != "application/x-www-form-urlencoded" {
				t.Fatalf("unexpected daily VIP request: %s %#v", request.URL.String(), request.Header)
			}
			assertSignedRequest(t, request)
			writeResponse(writer, `{"status":1,"data":{"received":true}}`)
		case "/youth/v1/listen_song/upgrade_vip_reward":
			if request.Method != http.MethodPost || query.Get("kugouid") != "7" || query.Get("ad_type") != "1" ||
				query.Get("userid") != "7" || query.Get("token") != "token" {
				t.Fatalf("unexpected VIP upgrade request: %s", request.URL.String())
			}
			assertSignedRequest(t, request)
			writeResponse(writer, `{"status":1,"data":{"upgraded":true}}`)
		case "/v2/get_res_privilege/lite":
			if request.Method != http.MethodPost || request.Header.Get("x-router") != "media.store.kugou.com" {
				t.Fatalf("unexpected privilege request: %s %#v", request.URL.String(), request.Header)
			}
			var body map[string]interface{}
			if err := json.NewDecoder(request.Body).Decode(&body); err != nil {
				t.Fatal(err)
			}
			resources, _ := body["resource"].([]interface{})
			resource, _ := resources[0].(map[string]interface{})
			if resource["hash"] != "ABC" || resource["album_id"] != float64(123) || body["support_verify"] != float64(1) {
				t.Fatalf("unexpected privilege body: %#v", body)
			}
			assertSignedRequest(t, request)
			writeResponse(writer, `{"status":1,"data":[{"hash":"ABC","quality":"128","level":1}]}`)
		default:
			http.NotFound(writer, request)
		}
	}))
	defer server.Close()

	client := testClient(t, server.URL)
	if err := client.UpsertSession(Session{UserID: 7, Token: "token"}, true); err != nil {
		t.Fatal(err)
	}
	if _, err := client.ClaimDailyVIP(7, "2026-08-01"); err != nil {
		t.Fatal(err)
	}
	if _, err := client.UpgradeDailyVIP(7); err != nil {
		t.Fatal(err)
	}
	result, err := client.SongPrivilege(7, SongURLRequest{Hash: "ABC", AlbumID: 123})
	if err != nil || !strings.Contains(string(result), `"quality":"128"`) {
		t.Fatalf("song privilege failed: %s %v", result, err)
	}
	if _, err := client.ClaimDailyVIP(7, "not-a-date"); err == nil {
		t.Fatal("invalid receive day should fail")
	}
}

func TestStructuredErrors(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		writer.WriteHeader(http.StatusBadGateway)
		_, _ = writer.Write([]byte("upstream unavailable"))
	}))
	defer server.Close()
	client := testClient(t, server.URL)
	_, err := client.SongURL(0, SongURLRequest{})
	assertErrorCode(t, err, ErrorInvalidArgument)
	_, err = client.SongURL(0, SongURLRequest{Hash: "ABC"})
	assertErrorCode(t, err, ErrorHTTP)
	_, err = client.UserPlaylists(0, 1, 30)
	assertErrorCode(t, err, ErrorNotLoggedIn)
}

func TestSignatures(t *testing.T) {
	params := map[string]string{"b": "2", "a": "1"}
	if signature(params, "body", androidSecret) != md5Hex(androidSecret+"a=1b=2body"+androidSecret) {
		t.Fatal("android signature is not deterministic")
	}
	if songURLKey("HASH", "MID", 7) != md5Hex("hash"+urlKeySecret+"3116MID7") {
		t.Fatal("song URL key mismatch")
	}
}

func testClient(t *testing.T, endpoint string) *Client {
	t.Helper()
	client, err := NewClientWithOptions(t.TempDir(), Options{GatewayURL: endpoint, LoginURL: endpoint, LyricsURL: endpoint})
	if err != nil {
		t.Fatal(err)
	}
	return client
}

func assertSignedRequest(t *testing.T, request *http.Request) {
	t.Helper()
	if request.URL.Query().Get("signature") == "" || request.URL.Query().Get("mid") == "" || request.Header.Get("dfid") == "" {
		t.Fatalf("request identity or signature missing: %s", request.URL.String())
	}
}

func assertErrorCode(t *testing.T, err error, code int) {
	t.Helper()
	var bridgeError *BridgeError
	if !errors.As(err, &bridgeError) || bridgeError.Code != code {
		t.Fatalf("unexpected error: %#v", err)
	}
}

func writeResponse(writer http.ResponseWriter, body string) {
	writer.Header().Set("Content-Type", "application/json")
	_, _ = writer.Write([]byte(body))
}

type roundTripFunc func(*http.Request) (*http.Response, error)

func (fn roundTripFunc) RoundTrip(request *http.Request) (*http.Response, error) {
	return fn(request)
}

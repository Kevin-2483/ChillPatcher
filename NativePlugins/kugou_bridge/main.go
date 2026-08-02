package main

/*
#include <stdlib.h>
#include <stdint.h>
*/
import "C"

import (
	"encoding/json"
	"errors"
	"kugou_bridge/api"
	"sync"
	"unsafe"
)

var (
	client    *api.Client
	lastError *api.BridgeError
	stateMu   sync.RWMutex
	errorMu   sync.RWMutex
	qrKey     string
)

func main() {}

func setError(err error) {
	errorMu.Lock()
	defer errorMu.Unlock()
	if err == nil {
		lastError = nil
		return
	}
	var bridgeError *api.BridgeError
	if errors.As(err, &bridgeError) {
		copy := *bridgeError
		lastError = &copy
		return
	}
	lastError = &api.BridgeError{Code: api.ErrorAPI, Message: err.Error()}
}

func currentClient() (*api.Client, error) {
	stateMu.RLock()
	current := client
	stateMu.RUnlock()
	if current == nil {
		return nil, &api.BridgeError{Code: api.ErrorNotInitialized, Message: "bridge is not initialized", Operation: "bridge"}
	}
	return current, nil
}

func jsonString(value interface{}) *C.char {
	data, err := json.Marshal(value)
	if err != nil {
		setError(&api.BridgeError{Code: api.ErrorDecode, Message: "JSON encoding failed", Operation: "bridge", Cause: err.Error()})
		return nil
	}
	setError(nil)
	return C.CString(string(data))
}

//export KugouInit
func KugouInit(dataDir *C.char) C.int {
	if dataDir == nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "data directory is required", Operation: "init"})
		return -1
	}
	created, err := api.NewClient(C.GoString(dataDir))
	if err != nil {
		setError(err)
		return -1
	}
	stateMu.Lock()
	client = created
	stateMu.Unlock()
	setError(nil)
	return 0
}

//export KugouGetDevice
func KugouGetDevice() *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(current.Device())
}

//export KugouRegisterDevice
func KugouRegisterDevice() *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	device, err := current.RegisterDevice()
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(device)
}

//export KugouQRCreate
func KugouQRCreate() *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	result, err := current.CreateQRCode()
	if err != nil {
		setError(err)
		return nil
	}
	stateMu.Lock()
	qrKey = result.Key
	stateMu.Unlock()
	return jsonString(result)
}

//export KugouQRCheck
func KugouQRCheck(key *C.char) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	if key == nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "QR key is required", Operation: "qrCheck"})
		return nil
	}
	result, err := current.CheckQRCode(C.GoString(key))
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(result)
}

//export KugouGetSessions
func KugouGetSessions() *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(current.Sessions())
}

//export KugouSetSession
func KugouSetSession(sessionJSON *C.char, makeActive C.int) C.int {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return -1
	}
	if sessionJSON == nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "session JSON is required", Operation: "setSession"})
		return -1
	}
	var session api.Session
	if err := json.Unmarshal([]byte(C.GoString(sessionJSON)), &session); err != nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "invalid session JSON", Operation: "setSession", Cause: err.Error()})
		return -1
	}
	if err := current.UpsertSession(session, makeActive != 0); err != nil {
		setError(err)
		return -1
	}
	setError(nil)
	return 0
}

//export KugouSwitchSession
func KugouSwitchSession(userID C.longlong) C.int {
	current, err := currentClient()
	if err == nil {
		err = current.SwitchSession(int64(userID))
	}
	if err != nil {
		setError(err)
		return -1
	}
	setError(nil)
	return 0
}

//export KugouRemoveSession
func KugouRemoveSession(userID C.longlong) C.int {
	current, err := currentClient()
	if err == nil {
		err = current.RemoveSession(int64(userID))
	}
	if err != nil {
		setError(err)
		return -1
	}
	setError(nil)
	return 0
}

//export KugouGetUserPlaylistsPage
func KugouGetUserPlaylistsPage(userID C.longlong, page, pageSize C.int) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	result, err := current.UserPlaylists(int64(userID), int(page), int(pageSize))
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(result)
}

//export KugouGetPlaylistSongsPage
func KugouGetPlaylistSongsPage(userID, listID C.longlong, globalCollectionID *C.char, page, pageSize C.int) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	globalID := ""
	if globalCollectionID != nil {
		globalID = C.GoString(globalCollectionID)
	}
	result, err := current.PlaylistSongs(int64(userID), int64(listID), globalID, int(page), int(pageSize))
	if err != nil {
		setError(err)
		return nil
	}
	return jsonString(result)
}

//export KugouGetSongURLV2
func KugouGetSongURLV2(userID C.longlong, requestJSON *C.char) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	var input api.SongURLRequest
	if requestJSON == nil || json.Unmarshal([]byte(C.GoString(requestJSON)), &input) != nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "invalid song URL request JSON", Operation: "songURL"})
		return nil
	}
	result, err := current.SongURL(int64(userID), input)
	if err != nil {
		setError(err)
		return nil
	}
	setError(nil)
	return C.CString(string(result))
}

//export KugouClaimDailyVIP
func KugouClaimDailyVIP(userID C.longlong, receiveDay *C.char) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	if receiveDay == nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "receive day is required", Operation: "claimDailyVIP"})
		return nil
	}
	result, err := current.ClaimDailyVIP(int64(userID), C.GoString(receiveDay))
	if err != nil {
		setError(err)
		return nil
	}
	setError(nil)
	return C.CString(string(result))
}

//export KugouUpgradeDailyVIP
func KugouUpgradeDailyVIP(userID C.longlong) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	result, err := current.UpgradeDailyVIP(int64(userID))
	if err != nil {
		setError(err)
		return nil
	}
	setError(nil)
	return C.CString(string(result))
}

//export KugouGetSongPrivilege
func KugouGetSongPrivilege(userID C.longlong, requestJSON *C.char) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	var input api.SongURLRequest
	if requestJSON == nil || json.Unmarshal([]byte(C.GoString(requestJSON)), &input) != nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "invalid song privilege request JSON", Operation: "songPrivilege"})
		return nil
	}
	result, err := current.SongPrivilege(int64(userID), input)
	if err != nil {
		setError(err)
		return nil
	}
	setError(nil)
	return C.CString(string(result))
}

//export KugouGetLyricV2
func KugouGetLyricV2(userID C.longlong, requestJSON *C.char) *C.char {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return nil
	}
	var input api.LyricRequest
	if requestJSON == nil || json.Unmarshal([]byte(C.GoString(requestJSON)), &input) != nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "invalid lyric request JSON", Operation: "lyric"})
		return nil
	}
	result, err := current.Lyric(int64(userID), input)
	if err != nil {
		setError(err)
		return nil
	}
	setError(nil)
	return C.CString(string(result))
}

//export KugouSetFavorite
func KugouSetFavorite(userID C.longlong, requestJSON *C.char, favorite C.int) C.int {
	current, err := currentClient()
	if err != nil {
		setError(err)
		return -1
	}
	var input api.FavoriteRequest
	if requestJSON == nil || json.Unmarshal([]byte(C.GoString(requestJSON)), &input) != nil {
		setError(&api.BridgeError{Code: api.ErrorInvalidArgument, Message: "invalid favorite request JSON", Operation: "favorite"})
		return -1
	}
	if err := current.SetFavorite(int64(userID), input, favorite != 0); err != nil {
		setError(err)
		return -1
	}
	setError(nil)
	return 0
}

//export KugouGetLastError
func KugouGetLastError() *C.char {
	errorMu.RLock()
	current := lastError
	errorMu.RUnlock()
	if current == nil {
		return nil
	}
	data, _ := json.Marshal(current)
	return C.CString(string(data))
}

//export KugouFreeString
func KugouFreeString(value *C.char) {
	C.free(unsafe.Pointer(value))
}

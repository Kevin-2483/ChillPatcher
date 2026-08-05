package api

import (
	"crypto/rand"
	"encoding/binary"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"time"
)

const (
	deviceFileName  = "kugou_device.json"
	sessionFileName = "kugou_sessions.json"
)

func loadDevice(dataDir string) (Device, error) {
	path := filepath.Join(dataDir, deviceFileName)
	data, err := os.ReadFile(path)
	if err == nil {
		var device Device
		if json.Unmarshal(data, &device) == nil && device.DFID != "" && device.MID != "" && device.UUID != "" {
			if !device.Registered && (len(device.DFID) != 36 || !isHex(device.DFID)) {
				device.Registered = true
			}
			if device.WebGL == "" {
				device.WebGL = randomUint64String()
			}
			_ = writeJSON(path, device)
			return device, nil
		}
	}
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		return Device{}, storageError("loadDevice", err)
	}
	dfid := randomHex(18)
	guid := randomHex(16)
	device := Device{DFID: dfid, MID: md5Hex(guid), UUID: guid, WebGL: randomUint64String()}
	if err := writeJSON(path, device); err != nil {
		return Device{}, err
	}
	return device, nil
}

func loadSessions(dataDir string) (SessionState, error) {
	data, err := os.ReadFile(filepath.Join(dataDir, sessionFileName))
	if errors.Is(err, os.ErrNotExist) {
		return SessionState{Accounts: []Session{}}, nil
	}
	if err != nil {
		return SessionState{}, storageError("loadSessions", err)
	}
	var state SessionState
	if err := json.Unmarshal(data, &state); err != nil {
		return SessionState{}, storageError("loadSessions", err)
	}
	if state.Accounts == nil {
		state.Accounts = []Session{}
	}
	return state, nil
}

func saveSessions(dataDir string, state SessionState) error {
	sort.Slice(state.Accounts, func(i, j int) bool { return state.Accounts[i].Updated > state.Accounts[j].Updated })
	return writeJSON(filepath.Join(dataDir, sessionFileName), state)
}

func writeJSON(path string, value interface{}) error {
	data, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		return storageError("marshal", err)
	}
	temporary := path + ".tmp"
	if err := os.WriteFile(temporary, data, 0600); err != nil {
		return storageError("write", err)
	}
	if err := os.Remove(path); err != nil && !errors.Is(err, os.ErrNotExist) {
		_ = os.Remove(temporary)
		return storageError("replace", err)
	}
	if err := os.Rename(temporary, path); err != nil {
		_ = os.Remove(temporary)
		return storageError("replace", err)
	}
	return nil
}

func readJSON(path string, value interface{}) error {
	data, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	return json.Unmarshal(data, value)
}

func randomHex(size int) string {
	data := make([]byte, size)
	if _, err := rand.Read(data); err != nil {
		return md5Hex(time.Now().String())
	}
	return hex.EncodeToString(data)
}

func isHex(value string) bool {
	_, err := hex.DecodeString(value)
	return err == nil
}

func randomUint64String() string {
	data := make([]byte, 8)
	if _, err := rand.Read(data); err != nil {
		return strconv.FormatInt(time.Now().UnixNano(), 10)
	}
	return strconv.FormatUint(binary.BigEndian.Uint64(data), 10)
}

func storageError(operation string, err error) *BridgeError {
	return &BridgeError{Code: ErrorStorage, Message: "storage operation failed", Operation: operation, Cause: err.Error()}
}

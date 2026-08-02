package api

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sync"
	"time"
)

const cacheDirectoryName = "http_cache"

type cachedResponse struct {
	SavedAt   int64           `json:"savedAt"`
	ExpiresAt int64           `json:"expiresAt"`
	Data      json.RawMessage `json:"data"`
}

type responseCache struct {
	directory string
	mu        sync.RWMutex
	memory    map[string]cachedResponse
}

func newResponseCache(dataDir string) *responseCache {
	return &responseCache{directory: filepath.Join(dataDir, cacheDirectoryName), memory: map[string]cachedResponse{}}
}

func (c *responseCache) GetFresh(key string) (map[string]interface{}, bool) {
	entry, ok := c.get(key)
	if !ok || time.Now().Unix() > entry.ExpiresAt {
		return nil, false
	}
	return decodeCachedResponse(entry)
}

func (c *responseCache) GetStale(key string, maximumAge time.Duration) (map[string]interface{}, bool) {
	entry, ok := c.get(key)
	if !ok || time.Since(time.Unix(entry.SavedAt, 0)) > maximumAge {
		return nil, false
	}
	return decodeCachedResponse(entry)
}

func (c *responseCache) Put(key string, value map[string]interface{}, ttl time.Duration) {
	data, err := json.Marshal(value)
	if err != nil {
		return
	}
	now := time.Now()
	entry := cachedResponse{SavedAt: now.Unix(), ExpiresAt: now.Add(ttl).Unix(), Data: data}
	c.mu.Lock()
	c.memory[key] = entry
	c.mu.Unlock()
	if os.MkdirAll(c.directory, 0700) == nil {
		_ = writeJSON(c.path(key), entry)
	}
}

func (c *responseCache) Clear() {
	c.mu.Lock()
	c.memory = map[string]cachedResponse{}
	c.mu.Unlock()
	_ = os.RemoveAll(c.directory)
}

func (c *responseCache) get(key string) (cachedResponse, bool) {
	c.mu.RLock()
	entry, ok := c.memory[key]
	c.mu.RUnlock()
	if ok {
		return entry, true
	}
	if err := readJSON(c.path(key), &entry); err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			_ = os.Remove(c.path(key))
		}
		return cachedResponse{}, false
	}
	c.mu.Lock()
	c.memory[key] = entry
	c.mu.Unlock()
	return entry, true
}

func (c *responseCache) path(key string) string {
	return filepath.Join(c.directory, md5Hex(key)+".json")
}

func decodeCachedResponse(entry cachedResponse) (map[string]interface{}, bool) {
	var value map[string]interface{}
	if json.Unmarshal(entry.Data, &value) != nil {
		return nil, false
	}
	return value, true
}

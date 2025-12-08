package main

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"sync"
	"time"
)

// AudioCache 管理音频文件的下载缓存
type AudioCache struct {
	url         string
	cacheFile   *os.File
	cachePath   string
	downloaded  int64
	totalSize   int64
	isComplete  bool
	mutex       sync.RWMutex
	ctx         context.Context
	cancel      context.CancelFunc
	onComplete  func() // 下载完成回调
}

// NewAudioCache 创建新的音频缓存
func NewAudioCache(url string, songId int64) (*AudioCache, error) {
	// 创建缓存目录
	cacheDir := filepath.Join(os.TempDir(), "chillpatcher_audio_cache")
	if err := os.MkdirAll(cacheDir, 0755); err != nil {
		return nil, err
	}

	// 缓存文件路径
	cachePath := filepath.Join(cacheDir, formatCacheFileName(songId))

	// 创建或打开缓存文件
	file, err := os.OpenFile(cachePath, os.O_CREATE|os.O_RDWR|os.O_TRUNC, 0644)
	if err != nil {
		return nil, err
	}

	ctx, cancel := context.WithCancel(context.Background())

	return &AudioCache{
		url:       url,
		cacheFile: file,
		cachePath: cachePath,
		ctx:       ctx,
		cancel:    cancel,
	}, nil
}

func formatCacheFileName(songId int64) string {
	return fmt.Sprintf("netease_%d.audio", songId)
}

// StartDownload 开始后台下载
func (c *AudioCache) StartDownload() {
	go c.downloadInBackground()
}

func (c *AudioCache) downloadInBackground() {
	req, err := http.NewRequestWithContext(c.ctx, "GET", c.url, nil)
	if err != nil {
		return
	}
	req.Header.Set("User-Agent", "Mozilla/5.0")

	transport := &http.Transport{
		ResponseHeaderTimeout: 30 * time.Second,
		IdleConnTimeout:       90 * time.Second,
	}
	client := &http.Client{Transport: transport}

	resp, err := client.Do(req)
	if err != nil {
		return
	}
	defer resp.Body.Close()

	c.mutex.Lock()
	c.totalSize = resp.ContentLength
	c.mutex.Unlock()

	buffer := make([]byte, 32*1024) // 32KB buffer
	for {
		select {
		case <-c.ctx.Done():
			return
		default:
		}

		n, err := resp.Body.Read(buffer)
		if n > 0 {
			c.mutex.Lock()
			c.cacheFile.Write(buffer[:n])
			c.downloaded += int64(n)
			c.mutex.Unlock()
		}

		if err == io.EOF {
			c.mutex.Lock()
			c.isComplete = true
			c.mutex.Unlock()
			
			// 调用完成回调
			if c.onComplete != nil {
				c.onComplete()
			}
			return
		}

		if err != nil {
			return
		}
	}
}

// IsComplete 检查下载是否完成
func (c *AudioCache) IsComplete() bool {
	c.mutex.RLock()
	defer c.mutex.RUnlock()
	return c.isComplete
}

// GetCachePath 获取缓存文件路径
func (c *AudioCache) GetCachePath() string {
	return c.cachePath
}

// GetProgress 获取下载进度 (0-100)
func (c *AudioCache) GetProgress() float64 {
	c.mutex.RLock()
	defer c.mutex.RUnlock()
	if c.totalSize <= 0 {
		return 0
	}
	return float64(c.downloaded) / float64(c.totalSize) * 100
}

// SetOnComplete 设置下载完成回调
func (c *AudioCache) SetOnComplete(callback func()) {
	c.onComplete = callback
}

// Close 关闭缓存并删除缓存文件
func (c *AudioCache) Close() {
	c.cancel()
	if c.cacheFile != nil {
		c.cacheFile.Close()
	}
	// 删除缓存文件
	if c.cachePath != "" {
		os.Remove(c.cachePath)
	}
}

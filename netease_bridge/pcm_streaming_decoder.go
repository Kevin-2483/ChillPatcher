package main

import (
	"context"
	"errors"
	"io"
	"net/http"
	"sync"
	"time"

	"github.com/tosone/minimp3"
)

// StreamingDecoder 使用 minimp3 的流式解码器（边下边播）
type StreamingDecoder struct {
	url        string
	decoder    *minimp3.Decoder
	httpResp   *http.Response
	ctx        context.Context
	cancel     context.CancelFunc
	mutex      sync.Mutex
	buffer     []byte // 已解码的 PCM 数据
	sampleRate int
	channels   int
	isReady    bool
	isEOF      bool
	lastError  string
}

// NewStreamingDecoder 创建流式解码器
func NewStreamingDecoder(url string) *StreamingDecoder {
	ctx, cancel := context.WithCancel(context.Background())
	return &StreamingDecoder{
		url:    url,
		ctx:    ctx,
		cancel: cancel,
	}
}

// Start 开始解码
func (d *StreamingDecoder) Start() {
	go d.startDecoding()
}

func (d *StreamingDecoder) startDecoding() {
	req, err := http.NewRequestWithContext(d.ctx, "GET", d.url, nil)
	if err != nil {
		d.setError("Failed to create request: " + err.Error())
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
		d.setError("Failed to fetch audio: " + err.Error())
		return
	}
	d.httpResp = resp

	decoder, err := minimp3.NewDecoder(resp.Body)
	if err != nil {
		d.setError("Failed to create decoder: " + err.Error())
		resp.Body.Close()
		return
	}
	d.decoder = decoder

	// 等待解码器准备好
	for i := 0; i < 100; i++ {
		if decoder.SampleRate > 0 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}

	d.mutex.Lock()
	d.sampleRate = decoder.SampleRate
	d.channels = decoder.Channels
	if d.sampleRate == 0 {
		d.sampleRate = 44100
	}
	if d.channels == 0 {
		d.channels = 2
	}
	d.isReady = true
	d.mutex.Unlock()

	d.decodeLoop()
}

func (d *StreamingDecoder) decodeLoop() {
	defer func() {
		if d.httpResp != nil {
			d.httpResp.Body.Close()
		}
		if d.decoder != nil {
			d.decoder.Close()
		}
	}()

	buffer := make([]byte, 4096)
	for {
		select {
		case <-d.ctx.Done():
			return
		default:
		}

		n, err := d.decoder.Read(buffer)
		if n > 0 {
			d.mutex.Lock()
			d.buffer = append(d.buffer, buffer[:n]...)
			d.mutex.Unlock()
		}

		if err != nil {
			d.mutex.Lock()
			if errors.Is(err, io.EOF) {
				d.isEOF = true
			} else {
				d.lastError = err.Error()
				d.isEOF = true
			}
			d.mutex.Unlock()
			return
		}

		// 控制缓冲区大小
		d.mutex.Lock()
		bufLen := len(d.buffer)
		d.mutex.Unlock()
		if bufLen > 1024*1024 {
			time.Sleep(10 * time.Millisecond)
		}
	}
}

func (d *StreamingDecoder) setError(msg string) {
	d.mutex.Lock()
	d.lastError = msg
	d.isEOF = true
	d.mutex.Unlock()
}

// ReadFrames 读取 PCM 帧
// 返回: 读取的帧数，0=暂无数据，-1=错误，-2=EOF
func (d *StreamingDecoder) ReadFrames(buffer []float32, framesToRead int) int {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if !d.isReady {
		return 0
	}

	bytesPerFrame := d.channels * 2 // int16
	bytesNeeded := framesToRead * bytesPerFrame

	if len(d.buffer) < bytesNeeded {
		if d.isEOF {
			bytesNeeded = len(d.buffer)
			framesToRead = bytesNeeded / bytesPerFrame
		} else {
			return 0
		}
	}

	if framesToRead == 0 {
		if d.isEOF {
			return -2 // EOF
		}
		return 0
	}

	// 读取并转换
	srcData := d.buffer[:bytesNeeded]
	d.buffer = d.buffer[bytesNeeded:]

	for i := 0; i < framesToRead*d.channels; i++ {
		sample := int16(srcData[i*2]) | int16(srcData[i*2+1])<<8
		buffer[i] = float32(sample) / 32768.0
	}

	return framesToRead
}

// GetInfo 获取解码器信息
func (d *StreamingDecoder) GetInfo() (sampleRate, channels int, isReady bool, err string) {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.sampleRate, d.channels, d.isReady, d.lastError
}

// IsReady 是否准备好
func (d *StreamingDecoder) IsReady() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isReady
}

// IsEOF 是否结束
func (d *StreamingDecoder) IsEOF() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isEOF
}

// Close 关闭解码器
func (d *StreamingDecoder) Close() {
	d.cancel()
}

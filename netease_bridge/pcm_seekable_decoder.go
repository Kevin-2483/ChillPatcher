package main

import (
	"io"
	"os"
	"sync"

	gomp3 "github.com/hajimehoshi/go-mp3"
)

// SeekableDecoder 可 Seek 的解码器（需要完整文件）
type SeekableDecoder struct {
	file       *os.File
	decoder    *gomp3.Decoder
	mutex      sync.Mutex
	sampleRate int
	channels   int
	length     int64 // 总采样数
	position   int64 // 当前位置（采样）
	isReady    bool
	isEOF      bool  // 流是否已结束
	lastError  string
}

const (
	seekableChannels      = 2
	seekableBytesPerFrame = seekableChannels * 2 // 2 channels * 2 bytes (int16)
)

// NewSeekableDecoder 从缓存文件创建可 Seek 解码器
func NewSeekableDecoder(cachePath string) (*SeekableDecoder, error) {
	file, err := os.Open(cachePath)
	if err != nil {
		return nil, err
	}

	decoder, err := gomp3.NewDecoder(file)
	if err != nil {
		file.Close()
		return nil, err
	}

	d := &SeekableDecoder{
		file:       file,
		decoder:    decoder,
		sampleRate: decoder.SampleRate(),
		channels:   seekableChannels,
		length:     decoder.Length() / int64(seekableBytesPerFrame),
		isReady:    true,
	}

	return d, nil
}

// Seek 定位到指定帧
func (d *SeekableDecoder) Seek(frameIndex int64) error {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	bytePos := frameIndex * int64(seekableBytesPerFrame)
	_, err := d.decoder.Seek(bytePos, io.SeekStart)
	if err != nil {
		d.lastError = err.Error()
		return err
	}

	d.position = frameIndex
	return nil
}

// ReadFrames 读取 PCM 帧
func (d *SeekableDecoder) ReadFrames(buffer []float32, framesToRead int) int {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if !d.isReady {
		return 0
	}

	// 读取原始 int16 数据
	bytesNeeded := framesToRead * seekableBytesPerFrame
	rawBuffer := make([]byte, bytesNeeded)

	totalRead := 0
	for totalRead < bytesNeeded {
		n, err := d.decoder.Read(rawBuffer[totalRead:])
		totalRead += n

		if err == io.EOF {
			break
		}
		if err != nil {
			d.lastError = err.Error()
			return -1
		}
	}

	if totalRead == 0 {
		d.isEOF = true
		return -2 // EOF
	}

	// 转换为 float32
	framesRead := totalRead / seekableBytesPerFrame
	for i := 0; i < framesRead*d.channels; i++ {
		if i*2+1 < totalRead {
			sample := int16(rawBuffer[i*2]) | int16(rawBuffer[i*2+1])<<8
			buffer[i] = float32(sample) / 32768.0
		}
	}

	d.position += int64(framesRead)
	return framesRead
}

// GetInfo 获取解码器信息
func (d *SeekableDecoder) GetInfo() (sampleRate, channels int, totalFrames int64) {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.sampleRate, d.channels, d.length
}

// GetPosition 获取当前位置（帧）
func (d *SeekableDecoder) GetPosition() int64 {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.position
}

// IsReady 是否准备好
func (d *SeekableDecoder) IsReady() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isReady
}

// IsEOF 是否结束
func (d *SeekableDecoder) IsEOF() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isEOF
}

// Close 关闭解码器
func (d *SeekableDecoder) Close() {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	if d.file != nil {
		d.file.Close()
	}
}

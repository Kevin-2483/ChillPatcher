package main

import (
	"io"
	"os"
	"sync"

	"github.com/mewkiz/flac"
)

// FlacStreamingDecoder FLAC 流式解码器（边下边播）
// 与 MP3 StreamingDecoder 不同，FLAC 需要先读取头部元数据才能开始解码
// 因此我们采用"先缓存再解码"的策略
type FlacStreamingDecoder struct {
	cachePath   string
	stream      *flac.Stream
	mutex       sync.Mutex
	buffer      []float32 // 已解码的 PCM 数据
	sampleRate  int
	channels    int
	totalFrames uint64
	isReady     bool
	isEOF       bool
	lastError   string
	currentPos  uint64 // 当前样本位置
}

// NewFlacStreamingDecoder 创建 FLAC 流式解码器
// cachePath 是本地缓存文件路径
func NewFlacStreamingDecoder(cachePath string) *FlacStreamingDecoder {
	return &FlacStreamingDecoder{
		cachePath: cachePath,
	}
}

// TryOpen 尝试打开 FLAC 文件
// 返回 true 如果成功打开，false 如果文件还不完整
func (d *FlacStreamingDecoder) TryOpen() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if d.stream != nil {
		return true // 已经打开
	}

	// 检查文件是否存在且有足够大小
	info, err := os.Stat(d.cachePath)
	if err != nil || info.Size() < 1024 {
		return false // 文件太小，等待更多数据
	}

	// 尝试打开 FLAC 文件
	stream, err := flac.Open(d.cachePath)
	if err != nil {
		// FLAC 头部可能还没下载完
		return false
	}

	d.stream = stream
	d.sampleRate = int(stream.Info.SampleRate)
	d.channels = int(stream.Info.NChannels)
	d.totalFrames = stream.Info.NSamples
	d.isReady = true

	return true
}

// GetInfo 获取音频信息
func (d *FlacStreamingDecoder) GetInfo() (sampleRate, channels int, isReady bool, errStr string) {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	return d.sampleRate, d.channels, d.isReady, d.lastError
}

// Read 读取 PCM 数据
func (d *FlacStreamingDecoder) Read(buffer []float32, framesToRead int) int {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if d.stream == nil || d.isEOF {
		return 0
	}

	totalSamples := framesToRead * d.channels
	samplesRead := 0
	bitsPerSample := int(d.stream.Info.BitsPerSample)
	scale := 1.0 / float64(int(1)<<(bitsPerSample-1))

	for samplesRead < totalSamples {
		// 首先从缓冲区读取
		if len(d.buffer) > 0 {
			toCopy := len(d.buffer)
			if toCopy > totalSamples-samplesRead {
				toCopy = totalSamples - samplesRead
			}
			copy(buffer[samplesRead:], d.buffer[:toCopy])
			d.buffer = d.buffer[toCopy:]
			samplesRead += toCopy
			continue
		}

		// 解码下一帧
		frame, err := d.stream.ParseNext()
		if err != nil {
			if err == io.EOF {
				d.isEOF = true
			} else {
				d.lastError = err.Error()
			}
			break
		}

		// 将样本转换为 float32 并交错
		nSamples := len(frame.Subframes[0].Samples)
		for i := 0; i < nSamples; i++ {
			for ch := 0; ch < d.channels; ch++ {
				if ch < len(frame.Subframes) {
					sample := float32(float64(frame.Subframes[ch].Samples[i]) * scale)
					if samplesRead < totalSamples {
						buffer[samplesRead] = sample
						samplesRead++
					} else {
						d.buffer = append(d.buffer, sample)
					}
				}
			}
		}
		d.currentPos += uint64(nSamples)
	}

	return samplesRead / d.channels // 返回帧数
}

// IsEOF 是否结束
func (d *FlacStreamingDecoder) IsEOF() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isEOF
}

// Close 关闭解码器
func (d *FlacStreamingDecoder) Close() {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if d.stream != nil {
		d.stream.Close()
		d.stream = nil
	}
}

// FlacSeekableDecoder 可 Seek 的 FLAC 解码器
type FlacSeekableDecoder struct {
	file        *os.File
	stream      *flac.Stream
	mutex       sync.Mutex
	sampleRate  int
	channels    int
	totalFrames uint64
	currentPos  uint64
	isReady     bool
	isEOF       bool   // 流是否已结束
	lastError   string
	buffer      []float32 // 解码缓冲区
	bufferStart uint64    // 缓冲区起始位置（样本）
}

// NewFlacSeekableDecoder 从缓存文件创建可 Seek 的 FLAC 解码器
func NewFlacSeekableDecoder(cachePath string) (*FlacSeekableDecoder, error) {
	file, err := os.Open(cachePath)
	if err != nil {
		return nil, err
	}

	// 使用 NewSeek 创建支持 Seek 的 Stream
	stream, err := flac.NewSeek(file)
	if err != nil {
		file.Close()
		return nil, err
	}

	d := &FlacSeekableDecoder{
		file:        file,
		stream:      stream,
		sampleRate:  int(stream.Info.SampleRate),
		channels:    int(stream.Info.NChannels),
		totalFrames: stream.Info.NSamples,
		isReady:     true,
	}

	return d, nil
}

// GetInfo 获取音频信息
func (d *FlacSeekableDecoder) GetInfo() (sampleRate, channels int, totalFrames uint64) {
	return d.sampleRate, d.channels, d.totalFrames
}

// IsReady 是否准备好
func (d *FlacSeekableDecoder) IsReady() bool {
	return d.isReady
}

// Seek 定位到指定样本
func (d *FlacSeekableDecoder) Seek(sampleIndex uint64) error {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	actualPos, err := d.stream.Seek(sampleIndex)
	if err != nil {
		d.lastError = err.Error()
		return err
	}

	d.currentPos = actualPos
	d.buffer = nil // 清空缓冲区
	d.bufferStart = actualPos

	return nil
}

// ReadFrames 读取 PCM 帧
func (d *FlacSeekableDecoder) ReadFrames(buffer []float32, framesToRead int) int {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if !d.isReady {
		return 0
	}

	totalSamples := framesToRead * d.channels
	samplesRead := 0
	bitsPerSample := int(d.stream.Info.BitsPerSample)
	scale := 1.0 / float64(int(1)<<(bitsPerSample-1))

	for samplesRead < totalSamples {
		// 首先从缓冲区读取
		if len(d.buffer) > 0 {
			toCopy := len(d.buffer)
			if toCopy > totalSamples-samplesRead {
				toCopy = totalSamples - samplesRead
			}
			copy(buffer[samplesRead:], d.buffer[:toCopy])
			d.buffer = d.buffer[toCopy:]
			samplesRead += toCopy
			continue
		}

		// 解码下一帧
		frame, err := d.stream.ParseNext()
		if err != nil {
			if err == io.EOF {
				d.isEOF = true
				return -2 // EOF
			}
			d.lastError = err.Error()
			return -1 // Error
		}

		// 将样本转换为 float32 并交错
		nSamples := len(frame.Subframes[0].Samples)
		for i := 0; i < nSamples; i++ {
			for ch := 0; ch < d.channels; ch++ {
				if ch < len(frame.Subframes) {
					sample := float32(float64(frame.Subframes[ch].Samples[i]) * scale)
					if samplesRead < totalSamples {
						buffer[samplesRead] = sample
						samplesRead++
					} else {
						d.buffer = append(d.buffer, sample)
					}
				}
			}
		}
		d.currentPos += uint64(nSamples)
	}

	return samplesRead / d.channels // 返回帧数
}

// IsEOF 是否结束
func (d *FlacSeekableDecoder) IsEOF() bool {
	d.mutex.Lock()
	defer d.mutex.Unlock()
	return d.isEOF
}

// Close 关闭解码器
func (d *FlacSeekableDecoder) Close() {
	d.mutex.Lock()
	defer d.mutex.Unlock()

	if d.stream != nil {
		d.stream.Close()
		d.stream = nil
	}
	if d.file != nil {
		d.file.Close()
		d.file = nil
	}
}

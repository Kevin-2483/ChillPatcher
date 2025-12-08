package main

/*
#include <stdlib.h>
#include <string.h>
*/
import "C"
import (
	"encoding/json"
	"fmt"
	"strings"
	"sync"
	"time"
	"unsafe"
)

// AudioFormat 音频格式
type AudioFormat int

const (
	FormatMP3 AudioFormat = iota
	FormatFLAC
)

// PcmStream 表示一个 PCM 音频流
// 支持边下边播 + Seek 时切换到完整文件解码
type PcmStream struct {
	id              int64
	songId          int64
	url             string
	format          AudioFormat // 音频格式
	sampleRate      int
	channels        int
	totalFrames     uint64
	
	// MP3 流式解码器（边下边播）
	streamingDec    *StreamingDecoder
	
	// MP3 可 Seek 解码器（下载完成后可用）
	seekableDec     *SeekableDecoder
	
	// FLAC 流式解码器
	flacStreamingDec *FlacStreamingDecoder
	
	// FLAC 可 Seek 解码器
	flacSeekableDec  *FlacSeekableDecoder
	
	// 音频缓存
	cache           *AudioCache
	
	// 状态
	mutex           sync.Mutex
	useSeekable     bool   // 是否使用可 Seek 解码器
	isReady         bool
	isEOF           bool
	lastError       string
	
	// 延迟 Seek 支持
	pendingSeek     int64  // 等待执行的 Seek 位置，-1 表示无
	isPaused        bool   // 是否暂停输出（等待 Seek）
}

var (
	streamsMutex   sync.Mutex
	activeStreams  = make(map[int64]*PcmStream)
	nextStreamId   int64 = 1
)

// PcmStreamInfo 返回给 C# 的流信息
type PcmStreamInfo struct {
	StreamId     int64  `json:"streamId"`
	SampleRate   int    `json:"sampleRate"`
	Channels     int    `json:"channels"`
	TotalFrames  uint64 `json:"totalFrames"`
	IsReady      bool   `json:"isReady"`
	CanSeek      bool   `json:"canSeek"`
	IsEOF        bool   `json:"isEOF"`   // 流是否已结束
	Format       string `json:"format"` // "mp3" or "flac"
	Error        string `json:"error,omitempty"`
}

//export NeteaseCreatePcmStream
func NeteaseCreatePcmStream(songIdC C.longlong, qualityC *C.char) C.longlong {
	if !initialized {
		lastError = "Not initialized"
		return -1
	}

	songId := int64(songIdC)
	quality := C.GoString(qualityC)
	if quality == "" {
		quality = "exhigh"
	}

	// 获取歌曲 URL
	urlResult := NeteaseGetSongURL(C.longlong(songId), C.CString(quality))
	if urlResult == nil {
		return -1
	}
	
	var songUrl SongURL
	urlJson := C.GoString(urlResult)
	NeteaseFreeString(urlResult)
	
	if err := json.Unmarshal([]byte(urlJson), &songUrl); err != nil {
		lastError = "Failed to parse song URL: " + err.Error()
		return -1
	}

	if songUrl.URL == "" {
		lastError = "Empty URL returned"
		return -1
	}

	// 检测音频格式
	audioFormat := FormatMP3 // 默认 MP3
	if strings.ToLower(songUrl.Type) == "flac" {
		audioFormat = FormatFLAC
	}

	// 创建流
	stream := &PcmStream{
		songId:      songId,
		url:         songUrl.URL,
		format:      audioFormat,
		pendingSeek: -1, // 初始化为无待定 Seek
	}

	// 创建缓存（后台下载）
	cache, err := NewAudioCache(songUrl.URL, songId)
	if err == nil {
		stream.cache = cache
		// 设置下载完成回调
		cache.SetOnComplete(func() {
			stream.onCacheComplete()
		})
		cache.StartDownload()
	}

	// 根据格式创建流式解码器
	if audioFormat == FormatFLAC {
		// FLAC: 需要等待足够的数据才能开始解码
		// 创建 FLAC 流式解码器
		if cache != nil {
			stream.flacStreamingDec = NewFlacStreamingDecoder(cache.GetCachePath())
			// FLAC 需要等缓存下载一部分后才能尝试打开
			go stream.tryOpenFlacStream()
		}
	} else {
		// MP3: 可以立即开始流式解码
		stream.streamingDec = NewStreamingDecoder(songUrl.URL)
		stream.streamingDec.Start()
	}

	streamsMutex.Lock()
	stream.id = nextStreamId
	nextStreamId++
	activeStreams[stream.id] = stream
	streamsMutex.Unlock()

	return C.longlong(stream.id)
}

// tryOpenFlacStream 尝试打开 FLAC 流（等待足够的缓存数据）
func (s *PcmStream) tryOpenFlacStream() {
	if s.flacStreamingDec == nil {
		return
	}

	// 每 100ms 尝试一次打开 FLAC
	for i := 0; i < 100; i++ { // 最多等待 10 秒
		s.mutex.Lock()
		if s.flacStreamingDec.TryOpen() {
			sampleRate, channels, isReady, _ := s.flacStreamingDec.GetInfo()
			if isReady {
				s.sampleRate = sampleRate
				s.channels = channels
				s.isReady = true
				s.mutex.Unlock()
				return
			}
		}
		s.mutex.Unlock()
		
		// 检查是否下载完成
		if s.cache != nil && s.cache.IsComplete() {
			// 下载完成，直接使用可 Seek 解码器
			return
		}
		
		// 等待 100ms
		time.Sleep(100 * time.Millisecond)
	}
}

// onCacheComplete 缓存下载完成回调
func (s *PcmStream) onCacheComplete() {
	s.mutex.Lock()
	defer s.mutex.Unlock()

	if s.cache == nil {
		return
	}

	// 根据格式创建可 Seek 解码器
	if s.format == FormatFLAC {
		// FLAC 可 Seek 解码器
		seekable, err := NewFlacSeekableDecoder(s.cache.GetCachePath())
		if err != nil {
			s.lastError = "Failed to create FLAC seekable decoder: " + err.Error()
			return
		}

		s.flacSeekableDec = seekable
		
		// 更新采样率和总帧数
		sampleRate, channels, totalFrames := seekable.GetInfo()
		s.sampleRate = sampleRate
		s.channels = channels
		s.totalFrames = totalFrames

		// 如果有待定的 Seek，执行它
		if s.pendingSeek >= 0 {
			s.useSeekable = true
			// 关闭流式解码器
			if s.flacStreamingDec != nil {
				s.flacStreamingDec.Close()
			}
			// 执行 Seek
			s.flacSeekableDec.Seek(uint64(s.pendingSeek))
			s.pendingSeek = -1
			s.isPaused = false
		}
	} else {
		// MP3 可 Seek 解码器
		seekable, err := NewSeekableDecoder(s.cache.GetCachePath())
		if err != nil {
			s.lastError = "Failed to create seekable decoder: " + err.Error()
			return
		}

		s.seekableDec = seekable
		
		// 更新采样率和总帧数
		sampleRate, channels, totalFrames := seekable.GetInfo()
		s.sampleRate = sampleRate
		s.channels = channels
		s.totalFrames = uint64(totalFrames)

		// 如果有待定的 Seek，执行它
		if s.pendingSeek >= 0 {
			s.useSeekable = true
			// 关闭流式解码器
			if s.streamingDec != nil {
				s.streamingDec.Close()
			}
			// 执行 Seek
			s.seekableDec.Seek(s.pendingSeek)
			s.pendingSeek = -1
			s.isPaused = false
		}
	}
}

//export NeteaseGetPcmStreamInfo
func NeteaseGetPcmStreamInfo(streamIdC C.longlong) *C.char {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		lastError = "Stream not found"
		return nil
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	// 从当前使用的解码器获取信息
	var sampleRate, channels int
	var isReady bool
	var errStr string
	var canSeek bool
	var isEOF bool

	if stream.format == FormatFLAC {
		// FLAC 格式
		if stream.useSeekable && stream.flacSeekableDec != nil {
			sampleRate, channels, _ = stream.flacSeekableDec.GetInfo()
			isReady = stream.flacSeekableDec.IsReady()
			canSeek = true
			isEOF = stream.flacSeekableDec.IsEOF()
		} else if stream.flacStreamingDec != nil {
			sampleRate, channels, isReady, errStr = stream.flacStreamingDec.GetInfo()
			canSeek = false
			isEOF = stream.flacStreamingDec.IsEOF()
		}
	} else {
		// MP3 格式
		if stream.useSeekable && stream.seekableDec != nil {
			sampleRate, channels, _ = stream.seekableDec.GetInfo()
			isReady = stream.seekableDec.IsReady()
			canSeek = true
			isEOF = stream.seekableDec.IsEOF()
		} else if stream.streamingDec != nil {
			sampleRate, channels, isReady, errStr = stream.streamingDec.GetInfo()
			canSeek = false
			isEOF = stream.streamingDec.IsEOF()
		}
	}

	// 格式字符串
	formatStr := "mp3"
	if stream.format == FormatFLAC {
		formatStr = "flac"
	}

	info := PcmStreamInfo{
		StreamId:    stream.id,
		SampleRate:  sampleRate,
		Channels:    channels,
		TotalFrames: stream.totalFrames,
		IsReady:     isReady,
		CanSeek:     canSeek,
		IsEOF:       isEOF || stream.isEOF, // 任一标记为 EOF 即为 EOF
		Format:      formatStr,
		Error:       errStr,
	}

	jsonBytes, _ := json.Marshal(info)
	return C.CString(string(jsonBytes))
}

//export NeteaseReadPcmFrames
func NeteaseReadPcmFrames(streamIdC C.longlong, bufferPtr unsafe.Pointer, framesToReadC C.int) C.int {
	streamId := int64(streamIdC)
	framesToRead := int(framesToReadC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	// 如果暂停中（等待延迟 Seek），返回静音
	if stream.isPaused {
		buffer := (*[1 << 30]float32)(bufferPtr)[:framesToRead*2]
		for i := range buffer {
			buffer[i] = 0
		}
		return C.int(framesToRead) // 返回请求的帧数，但都是静音
	}

	buffer := (*[1 << 30]float32)(bufferPtr)[:framesToRead*2]

	// 根据格式选择解码器
	if stream.format == FormatFLAC {
		// FLAC 格式
		if stream.useSeekable && stream.flacSeekableDec != nil {
			return C.int(stream.flacSeekableDec.ReadFrames(buffer, framesToRead))
		} else if stream.flacStreamingDec != nil {
			return C.int(stream.flacStreamingDec.Read(buffer, framesToRead))
		}
	} else {
		// MP3 格式
		if stream.useSeekable && stream.seekableDec != nil {
			return C.int(stream.seekableDec.ReadFrames(buffer, framesToRead))
		} else if stream.streamingDec != nil {
			return C.int(stream.streamingDec.ReadFrames(buffer, framesToRead))
		}
	}

	return -1
}

//export NeteaseSeekPcmStream
func NeteaseSeekPcmStream(streamIdC C.longlong, frameIndexC C.longlong) C.int {
	streamId := int64(streamIdC)
	frameIndex := int64(frameIndexC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	// 根据格式检查是否有可 Seek 解码器
	if stream.format == FormatFLAC {
		if stream.flacSeekableDec == nil {
			// 缓存还没下载完，设置延迟 Seek
			stream.pendingSeek = frameIndex
			stream.isPaused = true
			return -3 // 延迟 Seek 已设置
		}

		// 切换到可 Seek 解码器
		if !stream.useSeekable {
			stream.useSeekable = true
			// 关闭流式解码器
			if stream.flacStreamingDec != nil {
				stream.flacStreamingDec.Close()
			}
		}

		// 执行 Seek
		err := stream.flacSeekableDec.Seek(uint64(frameIndex))
		if err != nil {
			stream.lastError = err.Error()
			return -1
		}
	} else {
		if stream.seekableDec == nil {
			// 缓存还没下载完，设置延迟 Seek
			stream.pendingSeek = frameIndex
			stream.isPaused = true
			return -3 // 延迟 Seek 已设置
		}

		// 切换到可 Seek 解码器
		if !stream.useSeekable {
			stream.useSeekable = true
			// 关闭流式解码器
			if stream.streamingDec != nil {
				stream.streamingDec.Close()
			}
		}

		// 执行 Seek
		err := stream.seekableDec.Seek(frameIndex)
		if err != nil {
			stream.lastError = err.Error()
			return -1
		}
	}

	return 0 // 成功
}

//export NeteaseCanSeekPcmStream
func NeteaseCanSeekPcmStream(streamIdC C.longlong) C.int {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	if stream.format == FormatFLAC {
		if stream.flacSeekableDec != nil {
			return 1 // 可以 Seek
		}
	} else {
		if stream.seekableDec != nil {
			return 1 // 可以 Seek
		}
	}
	return 0 // 不能 Seek
}

//export NeteaseClosePcmStream
func NeteaseClosePcmStream(streamIdC C.longlong) {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	if exists {
		delete(activeStreams, streamId)
	}
	streamsMutex.Unlock()

	if !exists {
		return
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	// MP3 解码器
	if stream.streamingDec != nil {
		stream.streamingDec.Close()
	}
	if stream.seekableDec != nil {
		stream.seekableDec.Close()
	}
	
	// FLAC 解码器
	if stream.flacStreamingDec != nil {
		stream.flacStreamingDec.Close()
	}
	if stream.flacSeekableDec != nil {
		stream.flacSeekableDec.Close()
	}
	
	// 缓存
	if stream.cache != nil {
		stream.cache.Close()
	}
}

//export NeteaseIsPcmStreamReady
func NeteaseIsPcmStreamReady(streamIdC C.longlong) C.int {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	if stream.format == FormatFLAC {
		// FLAC 格式
		if stream.useSeekable && stream.flacSeekableDec != nil {
			if stream.flacSeekableDec.IsReady() {
				return 1
			}
		} else if stream.flacStreamingDec != nil {
			_, _, isReady, _ := stream.flacStreamingDec.GetInfo()
			if isReady {
				return 1
			}
		}
	} else {
		// MP3 格式
		if stream.useSeekable && stream.seekableDec != nil {
			if stream.seekableDec.IsReady() {
				return 1
			}
		} else if stream.streamingDec != nil {
			if stream.streamingDec.IsReady() {
				return 1
			}
		}
	}
	return 0
}

//export NeteaseGetPcmStreamError
func NeteaseGetPcmStreamError(streamIdC C.longlong) *C.char {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return C.CString("Stream not found")
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	if stream.lastError != "" {
		return C.CString(stream.lastError)
	}

	if stream.format == FormatFLAC {
		if stream.flacStreamingDec != nil {
			_, _, _, err := stream.flacStreamingDec.GetInfo()
			if err != "" {
				return C.CString(err)
			}
		}
	} else {
		if stream.streamingDec != nil {
			_, _, _, err := stream.streamingDec.GetInfo()
			if err != "" {
				return C.CString(err)
			}
		}
	}

	return nil
}

//export NeteaseGetCacheProgress
func NeteaseGetCacheProgress(streamIdC C.longlong) C.double {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists || stream.cache == nil {
		return -1
	}

	return C.double(stream.cache.GetProgress())
}

//export NeteaseHasPendingSeek
func NeteaseHasPendingSeek(streamIdC C.longlong) C.int {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	if stream.pendingSeek >= 0 {
		return 1
	}
	return 0
}

//export NeteaseGetPendingSeekFrame
func NeteaseGetPendingSeekFrame(streamIdC C.longlong) C.longlong {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return -1
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	return C.longlong(stream.pendingSeek)
}

//export NeteaseCancelPendingSeek
func NeteaseCancelPendingSeek(streamIdC C.longlong) {
	streamId := int64(streamIdC)

	streamsMutex.Lock()
	stream, exists := activeStreams[streamId]
	streamsMutex.Unlock()

	if !exists {
		return
	}

	stream.mutex.Lock()
	defer stream.mutex.Unlock()

	stream.pendingSeek = -1
	stream.isPaused = false
}

func init() {
	// 确保 fmt 包被使用
	_ = fmt.Sprint("")
}

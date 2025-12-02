// smtc_bridge.cpp
// System Media Transport Controls Bridge for ChillPatcher
// 使用 C++/WinRT 实现 Windows 媒体传输控制

#define SMTC_BRIDGE_EXPORTS

#include "smtc_bridge.h"

#include <windows.h>
#include <objbase.h>  // CoInitializeEx

// 必须在其他头文件之前包含 WinRT 头文件
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Media.h>
#include <winrt/Windows.Media.Playback.h>
#include <winrt/Windows.Storage.Streams.h>

#include <string>
#include <mutex>

#pragma comment(lib, "windowsapp.lib")
#pragma comment(lib, "ole32.lib")

// 使用完整命名空间避免歧义
namespace wm = winrt::Windows::Media;
namespace wmp = winrt::Windows::Media::Playback;
namespace wss = winrt::Windows::Storage::Streams;
namespace wf = winrt::Windows::Foundation;

// ========== 全局状态 ==========
static std::mutex g_mutex;
static bool g_initialized = false;
static bool g_comInitialized = false;
static wmp::MediaPlayer g_mediaPlayer{ nullptr };
static wm::SystemMediaTransportControls g_smtc{ nullptr };
static wm::SystemMediaTransportControlsDisplayUpdater g_displayUpdater{ nullptr };
static std::string g_lastError;

// 回调函数指针
static SmtcButtonPressedCallback g_buttonCallback = nullptr;
static SmtcPositionChangeRequestedCallback g_positionCallback = nullptr;

// 事件令牌
static winrt::event_token g_buttonPressedToken;

// ========== 辅助函数 ==========

static void SetError(const char* error) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_lastError = error;
}

static void SetError(const std::string& error) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_lastError = error;
}

// ========== 初始化和清理 ==========

SMTC_API int SmtcInitialize(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (g_initialized) {
        return 0; // 已初始化
    }
    
    try {
        // 尝试初始化 COM（如果尚未初始化）
        // 使用 MTA 模式，与 Unity 兼容
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (SUCCEEDED(hr)) {
            g_comInitialized = true;  // 我们初始化了 COM，退出时需要清理
        }
        else if (hr == RPC_E_CHANGED_MODE) {
            // COM 已初始化但模式不同（STA），这在某些情况下是可以的
            g_comInitialized = false;
        }
        else if (hr != S_FALSE) {  // S_FALSE 表示已在此线程上初始化
            SetError("Failed to initialize COM");
            return -1;
        }
        
        // 创建 MediaPlayer 以获取 SMTC
        g_mediaPlayer = wmp::MediaPlayer();
        g_mediaPlayer.CommandManager().IsEnabled(false);
        
        // 获取 SMTC
        g_smtc = g_mediaPlayer.SystemMediaTransportControls();
        if (!g_smtc) {
            SetError("Failed to get SystemMediaTransportControls");
            return -1;
        }
        
        // 启用基本按钮
        g_smtc.IsPlayEnabled(true);
        g_smtc.IsPauseEnabled(true);
        g_smtc.IsNextEnabled(true);
        g_smtc.IsPreviousEnabled(true);
        g_smtc.IsStopEnabled(false);
        
        // 获取显示更新器
        g_displayUpdater = g_smtc.DisplayUpdater();
        g_displayUpdater.Type(wm::MediaPlaybackType::Music);
        
        // 注册按钮事件
        g_buttonPressedToken = g_smtc.ButtonPressed([](
            wm::SystemMediaTransportControls const& sender,
            wm::SystemMediaTransportControlsButtonPressedEventArgs const& args) {
            
            if (g_buttonCallback) {
                SmtcButtonType buttonType;
                switch (args.Button()) {
                    case wm::SystemMediaTransportControlsButton::Play:
                        buttonType = SMTC_BUTTON_PLAY;
                        break;
                    case wm::SystemMediaTransportControlsButton::Pause:
                        buttonType = SMTC_BUTTON_PAUSE;
                        break;
                    case wm::SystemMediaTransportControlsButton::Stop:
                        buttonType = SMTC_BUTTON_STOP;
                        break;
                    case wm::SystemMediaTransportControlsButton::Next:
                        buttonType = SMTC_BUTTON_NEXT;
                        break;
                    case wm::SystemMediaTransportControlsButton::Previous:
                        buttonType = SMTC_BUTTON_PREVIOUS;
                        break;
                    case wm::SystemMediaTransportControlsButton::FastForward:
                        buttonType = SMTC_BUTTON_FAST_FORWARD;
                        break;
                    case wm::SystemMediaTransportControlsButton::Rewind:
                        buttonType = SMTC_BUTTON_REWIND;
                        break;
                    default:
                        return;
                }
                g_buttonCallback(buttonType);
            }
        });
        
        // 启用 SMTC
        g_smtc.IsEnabled(true);
        
        g_initialized = true;
        g_lastError.clear();
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
    catch (const std::exception& ex) {
        SetError(std::string("Exception: ") + ex.what());
        return -3;
    }
}

SMTC_API void SmtcShutdown(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized) {
        return;
    }
    
    try {
        // 取消事件订阅
        if (g_smtc) {
            try {
                g_smtc.ButtonPressed(g_buttonPressedToken);
            } catch (...) {}
            try {
                g_smtc.IsEnabled(false);
            } catch (...) {}
        }
        
        // 清理对象
        try {
            g_displayUpdater = nullptr;
        } catch (...) {}
        
        try {
            g_smtc = nullptr;
        } catch (...) {}
        
        try {
            if (g_mediaPlayer) {
                g_mediaPlayer.Close();
            }
            g_mediaPlayer = nullptr;
        } catch (...) {}
        
        // 清理回调
        g_buttonCallback = nullptr;
        g_positionCallback = nullptr;
        
        g_initialized = false;
        
        // 如果我们初始化了 COM，则清理
        if (g_comInitialized) {
            CoUninitialize();
            g_comInitialized = false;
        }
    }
    catch (...) {
        // 忽略清理错误
    }
}

SMTC_API int SmtcIsInitialized(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_initialized ? 1 : 0;
}

// ========== 媒体信息设置 ==========

SMTC_API int SmtcSetMediaType(SmtcMediaType mediaType) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        wm::MediaPlaybackType type;
        switch (mediaType) {
            case SMTC_MEDIA_MUSIC:
                type = wm::MediaPlaybackType::Music;
                break;
            case SMTC_MEDIA_VIDEO:
                type = wm::MediaPlaybackType::Video;
                break;
            case SMTC_MEDIA_IMAGE:
                type = wm::MediaPlaybackType::Image;
                break;
            default:
                type = wm::MediaPlaybackType::Unknown;
                break;
        }
        g_displayUpdater.Type(type);
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcSetMusicInfo(
    const wchar_t* title,
    const wchar_t* artist,
    const wchar_t* album) {
    
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        auto musicProps = g_displayUpdater.MusicProperties();
        if (title) musicProps.Title(title);
        if (artist) musicProps.Artist(artist);
        if (album) musicProps.AlbumTitle(album);
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcSetThumbnailFromFile(const wchar_t* filePath) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        if (filePath) {
            wf::Uri uri(filePath);
            auto streamRef = wss::RandomAccessStreamReference::CreateFromUri(uri);
            g_displayUpdater.Thumbnail(streamRef);
        } else {
            g_displayUpdater.Thumbnail(nullptr);
        }
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcSetThumbnailFromMemory(
    const unsigned char* data,
    unsigned int dataSize,
    const char* mimeType) {
    
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        if (data && dataSize > 0) {
            // 创建内存流
            wss::InMemoryRandomAccessStream stream;
            wss::DataWriter writer(stream);
            writer.WriteBytes({ data, data + dataSize });
            writer.StoreAsync().get();
            writer.DetachStream();
            
            stream.Seek(0);
            auto streamRef = wss::RandomAccessStreamReference::CreateFromStream(stream);
            g_displayUpdater.Thumbnail(streamRef);
        } else {
            g_displayUpdater.Thumbnail(nullptr);
        }
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcClearThumbnail(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        g_displayUpdater.Thumbnail(nullptr);
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcUpdateDisplay(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_displayUpdater) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        g_displayUpdater.Update();
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

// ========== 播放状态控制 ==========

SMTC_API int SmtcSetPlaybackStatus(SmtcPlaybackStatus status) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_smtc) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        wm::MediaPlaybackStatus mpStatus;
        switch (status) {
            case SMTC_PLAYBACK_CLOSED:
                mpStatus = wm::MediaPlaybackStatus::Closed;
                break;
            case SMTC_PLAYBACK_STOPPED:
                mpStatus = wm::MediaPlaybackStatus::Stopped;
                break;
            case SMTC_PLAYBACK_PLAYING:
                mpStatus = wm::MediaPlaybackStatus::Playing;
                break;
            case SMTC_PLAYBACK_PAUSED:
                mpStatus = wm::MediaPlaybackStatus::Paused;
                break;
            case SMTC_PLAYBACK_CHANGING:
                mpStatus = wm::MediaPlaybackStatus::Changing;
                break;
            default:
                mpStatus = wm::MediaPlaybackStatus::Stopped;
                break;
        }
        g_smtc.PlaybackStatus(mpStatus);
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API SmtcPlaybackStatus SmtcGetPlaybackStatus(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_smtc) {
        return SMTC_PLAYBACK_CLOSED;
    }
    
    try {
        auto status = g_smtc.PlaybackStatus();
        switch (status) {
            case wm::MediaPlaybackStatus::Closed:
                return SMTC_PLAYBACK_CLOSED;
            case wm::MediaPlaybackStatus::Stopped:
                return SMTC_PLAYBACK_STOPPED;
            case wm::MediaPlaybackStatus::Playing:
                return SMTC_PLAYBACK_PLAYING;
            case wm::MediaPlaybackStatus::Paused:
                return SMTC_PLAYBACK_PAUSED;
            case wm::MediaPlaybackStatus::Changing:
                return SMTC_PLAYBACK_CHANGING;
            default:
                return SMTC_PLAYBACK_STOPPED;
        }
    }
    catch (...) {
        return SMTC_PLAYBACK_CLOSED;
    }
}

// ========== 按钮启用控制 ==========

SMTC_API int SmtcSetButtonEnabled(SmtcButtonType buttonType, int enabled) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_smtc) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        bool isEnabled = (enabled != 0);
        switch (buttonType) {
            case SMTC_BUTTON_PLAY:
                g_smtc.IsPlayEnabled(isEnabled);
                break;
            case SMTC_BUTTON_PAUSE:
                g_smtc.IsPauseEnabled(isEnabled);
                break;
            case SMTC_BUTTON_STOP:
                g_smtc.IsStopEnabled(isEnabled);
                break;
            case SMTC_BUTTON_NEXT:
                g_smtc.IsNextEnabled(isEnabled);
                break;
            case SMTC_BUTTON_PREVIOUS:
                g_smtc.IsPreviousEnabled(isEnabled);
                break;
            case SMTC_BUTTON_FAST_FORWARD:
                g_smtc.IsFastForwardEnabled(isEnabled);
                break;
            case SMTC_BUTTON_REWIND:
                g_smtc.IsRewindEnabled(isEnabled);
                break;
            default:
                SetError("Unknown button type");
                return -1;
        }
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

SMTC_API int SmtcIsButtonEnabled(SmtcButtonType buttonType) {
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_smtc) {
        return 0;
    }
    
    try {
        switch (buttonType) {
            case SMTC_BUTTON_PLAY:
                return g_smtc.IsPlayEnabled() ? 1 : 0;
            case SMTC_BUTTON_PAUSE:
                return g_smtc.IsPauseEnabled() ? 1 : 0;
            case SMTC_BUTTON_STOP:
                return g_smtc.IsStopEnabled() ? 1 : 0;
            case SMTC_BUTTON_NEXT:
                return g_smtc.IsNextEnabled() ? 1 : 0;
            case SMTC_BUTTON_PREVIOUS:
                return g_smtc.IsPreviousEnabled() ? 1 : 0;
            case SMTC_BUTTON_FAST_FORWARD:
                return g_smtc.IsFastForwardEnabled() ? 1 : 0;
            case SMTC_BUTTON_REWIND:
                return g_smtc.IsRewindEnabled() ? 1 : 0;
            default:
                return 0;
        }
    }
    catch (...) {
        return 0;
    }
}

// ========== 时间线属性 ==========

SMTC_API int SmtcSetTimelineProperties(
    long long startTimeMs,
    long long endTimeMs,
    long long positionMs) {
    
    std::lock_guard<std::mutex> lock(g_mutex);
    
    if (!g_initialized || !g_smtc) {
        SetError("Not initialized");
        return -1;
    }
    
    try {
        wm::SystemMediaTransportControlsTimelineProperties timeline;
        timeline.StartTime(std::chrono::milliseconds(startTimeMs));
        timeline.EndTime(std::chrono::milliseconds(endTimeMs));
        timeline.Position(std::chrono::milliseconds(positionMs));
        timeline.MinSeekTime(std::chrono::milliseconds(startTimeMs));
        timeline.MaxSeekTime(std::chrono::milliseconds(endTimeMs));
        g_smtc.UpdateTimelineProperties(timeline);
        return 0;
    }
    catch (const winrt::hresult_error& ex) {
        SetError("WinRT error: " + winrt::to_string(ex.message()));
        return -2;
    }
}

// ========== 事件回调 ==========

SMTC_API void SmtcSetButtonPressedCallback(SmtcButtonPressedCallback callback) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_buttonCallback = callback;
}

SMTC_API void SmtcSetPositionChangeRequestedCallback(SmtcPositionChangeRequestedCallback callback) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_positionCallback = callback;
}

// ========== 错误处理 ==========

SMTC_API const char* SmtcGetLastError(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_lastError.c_str();
}

SMTC_API void SmtcClearError(void) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_lastError.clear();
}

#ifndef SMTC_BRIDGE_H
#define SMTC_BRIDGE_H

#ifdef _WIN32
    #ifdef SMTC_BRIDGE_EXPORTS
        #define SMTC_API __declspec(dllexport)
    #else
        #define SMTC_API __declspec(dllimport)
    #endif
#else
    #define SMTC_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ========== 初始化和清理 ==========

/**
 * 初始化 SMTC Bridge
 * @return 0 成功, 负数表示错误代码
 */
SMTC_API int SmtcInitialize(void);

/**
 * 关闭并清理 SMTC Bridge
 */
SMTC_API void SmtcShutdown(void);

/**
 * 检查 SMTC 是否已初始化
 * @return 1 已初始化, 0 未初始化
 */
SMTC_API int SmtcIsInitialized(void);

// ========== 媒体信息设置 ==========

/**
 * 播放状态枚举
 */
typedef enum {
    SMTC_PLAYBACK_CLOSED = 0,   // 已关闭
    SMTC_PLAYBACK_STOPPED = 1,  // 已停止
    SMTC_PLAYBACK_PLAYING = 2,  // 播放中
    SMTC_PLAYBACK_PAUSED = 3,   // 已暂停
    SMTC_PLAYBACK_CHANGING = 4  // 正在切换
} SmtcPlaybackStatus;

/**
 * 按钮类型枚举
 */
typedef enum {
    SMTC_BUTTON_PLAY = 0,
    SMTC_BUTTON_PAUSE = 1,
    SMTC_BUTTON_STOP = 2,
    SMTC_BUTTON_RECORD = 3,
    SMTC_BUTTON_FAST_FORWARD = 4,
    SMTC_BUTTON_REWIND = 5,
    SMTC_BUTTON_NEXT = 6,
    SMTC_BUTTON_PREVIOUS = 7,
    SMTC_BUTTON_CHANNEL_UP = 8,
    SMTC_BUTTON_CHANNEL_DOWN = 9
} SmtcButtonType;

/**
 * 媒体类型枚举
 */
typedef enum {
    SMTC_MEDIA_UNKNOWN = 0,
    SMTC_MEDIA_MUSIC = 1,
    SMTC_MEDIA_VIDEO = 2,
    SMTC_MEDIA_IMAGE = 3
} SmtcMediaType;

/**
 * 设置媒体类型
 * @param mediaType 媒体类型
 * @return 0 成功
 */
SMTC_API int SmtcSetMediaType(SmtcMediaType mediaType);

/**
 * 设置音乐信息
 * @param title 标题 (UTF-16)
 * @param artist 艺术家 (UTF-16)
 * @param album 专辑名 (UTF-16)
 * @return 0 成功
 */
SMTC_API int SmtcSetMusicInfo(
    const wchar_t* title,
    const wchar_t* artist,
    const wchar_t* album);

/**
 * 设置缩略图（从文件路径）
 * @param filePath 图片文件路径 (UTF-16)
 * @return 0 成功
 */
SMTC_API int SmtcSetThumbnailFromFile(const wchar_t* filePath);

/**
 * 设置缩略图（从内存数据）
 * @param data 图片数据
 * @param dataSize 数据大小（字节）
 * @param mimeType MIME类型 如 "image/png" 或 "image/jpeg" (UTF-8)
 * @return 0 成功
 */
SMTC_API int SmtcSetThumbnailFromMemory(
    const unsigned char* data,
    unsigned int dataSize,
    const char* mimeType);

/**
 * 清除缩略图
 * @return 0 成功
 */
SMTC_API int SmtcClearThumbnail(void);

/**
 * 更新显示（在设置完信息后调用）
 * @return 0 成功
 */
SMTC_API int SmtcUpdateDisplay(void);

// ========== 播放状态控制 ==========

/**
 * 设置播放状态
 * @param status 播放状态
 * @return 0 成功
 */
SMTC_API int SmtcSetPlaybackStatus(SmtcPlaybackStatus status);

/**
 * 获取当前播放状态
 * @return 当前播放状态
 */
SMTC_API SmtcPlaybackStatus SmtcGetPlaybackStatus(void);

// ========== 按钮启用控制 ==========

/**
 * 设置按钮是否启用
 * @param buttonType 按钮类型
 * @param enabled 1 启用, 0 禁用
 * @return 0 成功
 */
SMTC_API int SmtcSetButtonEnabled(SmtcButtonType buttonType, int enabled);

/**
 * 检查按钮是否启用
 * @param buttonType 按钮类型
 * @return 1 启用, 0 禁用
 */
SMTC_API int SmtcIsButtonEnabled(SmtcButtonType buttonType);

// ========== 时间线属性 ==========

/**
 * 设置时间线属性
 * @param startTimeMs 开始时间（毫秒）
 * @param endTimeMs 结束时间（毫秒）
 * @param positionMs 当前位置（毫秒）
 * @return 0 成功
 */
SMTC_API int SmtcSetTimelineProperties(
    long long startTimeMs,
    long long endTimeMs,
    long long positionMs);

// ========== 事件回调 ==========

/**
 * 按钮按下回调函数类型
 * @param buttonType 被按下的按钮类型
 */
typedef void (*SmtcButtonPressedCallback)(SmtcButtonType buttonType);

/**
 * 设置按钮按下回调
 * @param callback 回调函数指针，NULL 取消回调
 */
SMTC_API void SmtcSetButtonPressedCallback(SmtcButtonPressedCallback callback);

/**
 * 播放位置改变请求回调函数类型
 * @param positionMs 请求的位置（毫秒）
 */
typedef void (*SmtcPositionChangeRequestedCallback)(long long positionMs);

/**
 * 设置播放位置改变请求回调
 * @param callback 回调函数指针，NULL 取消回调
 */
SMTC_API void SmtcSetPositionChangeRequestedCallback(SmtcPositionChangeRequestedCallback callback);

// ========== 错误处理 ==========

/**
 * 获取最后的错误消息
 * @return 错误消息字符串（UTF-8），返回的指针在下次调用此函数前有效
 */
SMTC_API const char* SmtcGetLastError(void);

/**
 * 清除错误消息
 */
SMTC_API void SmtcClearError(void);

#ifdef __cplusplus
}
#endif

#endif // SMTC_BRIDGE_H

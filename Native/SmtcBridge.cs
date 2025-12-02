using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Logging;

namespace ChillPatcher.Native
{
    /// <summary>
    /// System Media Transport Controls (SMTC) 原生桥接
    /// 通过 P/Invoke 调用 C++ DLL 来控制 Windows 媒体传输控制
    /// </summary>
    public static class SmtcBridge
    {
        private const string DllName = "ChillSmtcBridge";
        private static readonly ManualLogSource _log = Logger.CreateLogSource("SmtcBridge");
        
        // DLL 加载状态
        private static string DllPath;
        private static IntPtr DllHandle = IntPtr.Zero;
        
        // 静态构造函数：手动加载 DLL
        static SmtcBridge()
        {
            try
            {
                // BepInEx 插件目录
                var pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

                // SMTC 只支持 x64 (Windows 10+)
                var arch = IntPtr.Size == 8 ? "x64" : "x86";
                DllPath = Path.Combine(pluginDir, "native", arch, "ChillSmtcBridge.dll");

                if (!File.Exists(DllPath))
                {
                    _log.LogWarning($"DLL not found at: {DllPath}");
                    return;
                }

                // Windows: 使用 LoadLibrary 手动加载 DLL
                DllHandle = LoadLibrary(DllPath);
                if (DllHandle == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    _log.LogError($"Failed to load DLL from: {DllPath}, Error: {error}");
                }
                else
                {
                    _dllLoaded = true;
                    _log.LogInfo($"✅ Loaded Native DLL from: {DllPath}");
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Exception loading DLL: {ex}");
            }
        }
        
        // Windows LoadLibrary
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        #region Enums

        /// <summary>
        /// 播放状态
        /// </summary>
        public enum PlaybackStatus
        {
            Closed = 0,
            Stopped = 1,
            Playing = 2,
            Paused = 3,
            Changing = 4
        }

        /// <summary>
        /// 按钮类型
        /// </summary>
        public enum ButtonType
        {
            Play = 0,
            Pause = 1,
            Stop = 2,
            Record = 3,
            FastForward = 4,
            Rewind = 5,
            Next = 6,
            Previous = 7,
            ChannelUp = 8,
            ChannelDown = 9
        }

        /// <summary>
        /// 媒体类型
        /// </summary>
        public enum MediaType
        {
            Unknown = 0,
            Music = 1,
            Video = 2,
            Image = 3
        }

        #endregion

        #region P/Invoke Declarations

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcInitialize();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmtcShutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcIsInitialized();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcSetMediaType(MediaType mediaType);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmtcSetMusicInfo(
            [MarshalAs(UnmanagedType.LPWStr)] string title,
            [MarshalAs(UnmanagedType.LPWStr)] string artist,
            [MarshalAs(UnmanagedType.LPWStr)] string album);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int SmtcSetThumbnailFromFile(
            [MarshalAs(UnmanagedType.LPWStr)] string filePath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcSetThumbnailFromMemory(
            byte[] data,
            uint dataSize,
            [MarshalAs(UnmanagedType.LPStr)] string mimeType);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcClearThumbnail();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcUpdateDisplay();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcSetPlaybackStatus(PlaybackStatus status);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern PlaybackStatus SmtcGetPlaybackStatus();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcSetButtonEnabled(ButtonType buttonType, int enabled);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcIsButtonEnabled(ButtonType buttonType);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SmtcSetTimelineProperties(
            long startTimeMs,
            long endTimeMs,
            long positionMs);

        // 回调委托
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ButtonPressedCallbackDelegate(ButtonType buttonType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PositionChangeRequestedCallbackDelegate(long positionMs);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmtcSetButtonPressedCallback(ButtonPressedCallbackDelegate callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmtcSetPositionChangeRequestedCallback(PositionChangeRequestedCallbackDelegate callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SmtcGetLastError();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SmtcClearError();

        #endregion

        #region State

        private static bool _dllLoaded = false;
        private static ButtonPressedCallbackDelegate _buttonCallbackDelegate;
        private static PositionChangeRequestedCallbackDelegate _positionCallbackDelegate;

        /// <summary>
        /// 按钮按下事件
        /// </summary>
        public static event Action<ButtonType> OnButtonPressed;

        /// <summary>
        /// 位置改变请求事件
        /// </summary>
        public static event Action<long> OnPositionChangeRequested;

        #endregion

        #region Public Methods

        /// <summary>
        /// 检查 DLL 是否已加载
        /// </summary>
        public static bool IsDllLoaded => _dllLoaded;

        /// <summary>
        /// 初始化 SMTC
        /// </summary>
        public static bool Initialize()
        {
            try
            {
                if (!_dllLoaded)
                {
                    _log.LogWarning("SMTC DLL 未加载，无法初始化");
                    return false;
                }

                int result = SmtcInitialize();
                if (result != 0)
                {
                    _log.LogError($"SMTC 初始化失败: {GetLastError()}");
                    return false;
                }

                // 设置回调
                _buttonCallbackDelegate = OnButtonPressedNative;
                _positionCallbackDelegate = OnPositionChangeRequestedNative;
                SmtcSetButtonPressedCallback(_buttonCallbackDelegate);
                SmtcSetPositionChangeRequestedCallback(_positionCallbackDelegate);

                _log.LogInfo("SMTC 初始化成功");
                return true;
            }
            catch (DllNotFoundException ex)
            {
                _log.LogWarning($"SMTC DLL 加载失败: {ex.Message}");
                _dllLoaded = false;
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError($"SMTC 初始化异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭 SMTC
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                if (!_dllLoaded) return;
                
                SmtcSetButtonPressedCallback(null);
                SmtcSetPositionChangeRequestedCallback(null);
                SmtcShutdown();
                
                _buttonCallbackDelegate = null;
                _positionCallbackDelegate = null;
                
                _log.LogInfo("SMTC 已关闭");
            }
            catch (Exception ex)
            {
                _log.LogError($"SMTC 关闭异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        public static bool IsInitialized()
        {
            try
            {
                return _dllLoaded && SmtcIsInitialized() != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 设置媒体类型
        /// </summary>
        public static bool SetMediaType(MediaType mediaType)
        {
            if (!IsInitialized()) return false;
            return SmtcSetMediaType(mediaType) == 0;
        }

        /// <summary>
        /// 设置音乐信息
        /// </summary>
        public static bool SetMusicInfo(string title, string artist, string album)
        {
            if (!IsInitialized()) return false;
            return SmtcSetMusicInfo(title, artist, album) == 0;
        }

        /// <summary>
        /// 从文件设置缩略图
        /// </summary>
        public static bool SetThumbnailFromFile(string filePath)
        {
            if (!IsInitialized()) return false;
            return SmtcSetThumbnailFromFile(filePath) == 0;
        }

        /// <summary>
        /// 从内存设置缩略图
        /// </summary>
        public static bool SetThumbnailFromMemory(byte[] data, string mimeType)
        {
            if (!IsInitialized() || data == null || data.Length == 0) return false;
            return SmtcSetThumbnailFromMemory(data, (uint)data.Length, mimeType) == 0;
        }

        /// <summary>
        /// 清除缩略图
        /// </summary>
        public static bool ClearThumbnail()
        {
            if (!IsInitialized()) return false;
            return SmtcClearThumbnail() == 0;
        }

        /// <summary>
        /// 更新显示（设置完信息后调用）
        /// </summary>
        public static bool UpdateDisplay()
        {
            if (!IsInitialized()) return false;
            return SmtcUpdateDisplay() == 0;
        }

        /// <summary>
        /// 设置播放状态
        /// </summary>
        public static bool SetPlaybackStatus(PlaybackStatus status)
        {
            if (!IsInitialized()) return false;
            return SmtcSetPlaybackStatus(status) == 0;
        }

        /// <summary>
        /// 获取播放状态
        /// </summary>
        public static PlaybackStatus GetPlaybackStatus()
        {
            if (!IsInitialized()) return PlaybackStatus.Closed;
            return SmtcGetPlaybackStatus();
        }

        /// <summary>
        /// 设置按钮是否启用
        /// </summary>
        public static bool SetButtonEnabled(ButtonType buttonType, bool enabled)
        {
            if (!IsInitialized()) return false;
            return SmtcSetButtonEnabled(buttonType, enabled ? 1 : 0) == 0;
        }

        /// <summary>
        /// 检查按钮是否启用
        /// </summary>
        public static bool IsButtonEnabled(ButtonType buttonType)
        {
            if (!IsInitialized()) return false;
            return SmtcIsButtonEnabled(buttonType) != 0;
        }

        /// <summary>
        /// 设置时间线属性
        /// </summary>
        public static bool SetTimelineProperties(long startTimeMs, long endTimeMs, long positionMs)
        {
            if (!IsInitialized()) return false;
            return SmtcSetTimelineProperties(startTimeMs, endTimeMs, positionMs) == 0;
        }

        /// <summary>
        /// 获取最后的错误消息
        /// </summary>
        public static string GetLastError()
        {
            try
            {
                IntPtr ptr = SmtcGetLastError();
                if (ptr == IntPtr.Zero) return string.Empty;
                return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 清除错误消息
        /// </summary>
        public static void ClearError()
        {
            try
            {
                if (_dllLoaded)
                    SmtcClearError();
            }
            catch { }
        }

        #endregion

        #region Native Callbacks

        private static void OnButtonPressedNative(ButtonType buttonType)
        {
            try
            {
                OnButtonPressed?.Invoke(buttonType);
            }
            catch (Exception ex)
            {
                _log.LogError($"按钮回调异常: {ex.Message}");
            }
        }

        private static void OnPositionChangeRequestedNative(long positionMs)
        {
            try
            {
                OnPositionChangeRequested?.Invoke(positionMs);
            }
            catch (Exception ex)
            {
                _log.LogError($"位置回调异常: {ex.Message}");
            }
        }

        #endregion
    }
}

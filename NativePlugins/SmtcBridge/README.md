# SMTC Bridge

System Media Transport Controls (SMTC) 桥接库，用于将 ChillPatcher 的音乐播放状态同步到 Windows 系统媒体控制。

## 功能

- 在 Windows 10/11 系统媒体浮窗中显示当前播放的歌曲信息
- 支持显示歌曲标题、艺术家、专辑封面
- 响应系统媒体按键（播放/暂停、上一曲、下一曲）
- 支持媒体键盘和蓝牙耳机控制

## 编译要求

- **Visual Studio 2019** 或更高版本
- **Windows SDK 10.0.17763.0** 或更高版本
- **CMake 3.15** 或更高版本
- **C++/WinRT** (随 Windows SDK 自带)

## 编译步骤

### 方法 1：使用 CMake GUI

1. 打开 CMake GUI
2. 设置 Source directory 为 `NativePlugins/SmtcBridge`
3. 设置 Build directory 为 `NativePlugins/SmtcBridge/build`
4. 点击 Configure，选择 Visual Studio 2019/2022
5. 点击 Generate
6. 点击 Open Project 打开 Visual Studio
7. 在 Visual Studio 中选择 Release 配置，编译项目

### 方法 2：使用命令行

```powershell
cd NativePlugins/SmtcBridge
mkdir build
cd build
cmake .. -G "Visual Studio 16 2019" -A x64
cmake --build . --config Release
```

### 方法 3：使用 Developer Command Prompt

```batch
cd NativePlugins\SmtcBridge
mkdir build
cd build
cmake .. -A x64
msbuild ChillPatcher_SmtcBridge.sln /p:Configuration=Release /p:Platform=x64
```

## 部署

编译成功后，将 `build/bin/Release/ChillSmtcBridge.dll` 复制到：
```
BepInEx/plugins/ChillPatcher/ChillSmtcBridge.dll
```

## 配置

在 `BepInEx/config/ChillPatcher.cfg` 中启用此功能：

```ini
[Audio]
EnableSystemMediaTransport = true
```

## API 参考

### 初始化

```c
int SmtcInitialize(void);   // 初始化 SMTC
void SmtcShutdown(void);    // 关闭 SMTC
int SmtcIsInitialized(void); // 检查是否已初始化
```

### 媒体信息

```c
int SmtcSetMusicInfo(const wchar_t* title, const wchar_t* artist, const wchar_t* album);
int SmtcSetThumbnailFromFile(const wchar_t* filePath);
int SmtcSetThumbnailFromMemory(const unsigned char* data, unsigned int dataSize, const char* mimeType);
int SmtcUpdateDisplay(void);
```

### 播放控制

```c
int SmtcSetPlaybackStatus(SmtcPlaybackStatus status);
SmtcPlaybackStatus SmtcGetPlaybackStatus(void);
int SmtcSetTimelineProperties(long long startTimeMs, long long endTimeMs, long long positionMs);
```

### 事件回调

```c
typedef void (*SmtcButtonPressedCallback)(SmtcButtonType buttonType);
void SmtcSetButtonPressedCallback(SmtcButtonPressedCallback callback);
```

## 注意事项

- 此功能仅在 Windows 10 (Build 17763) 及更高版本可用
- 需要 C++17 或更高版本的编译器
- DLL 使用静态链接的运行时库，无需额外的 MSVC 运行时

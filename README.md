# ChillPatcher

这是一个 BepInEx 插件，包括多种新的功能改进
- 为游戏真正的flac支持.
- 为游戏提供歌曲批量导入和歌单管理.
- 使《Chill With You》游戏正确在 Wallpaper Engine 环境下运行.
- 添加了游戏内输入法.

![截屏展示](<img/Screenshot 2025-12-02 235846.png>)

![队列展示](<img/Screenshot 2025-12-02 235858.png>)

## 🐛 遇到问题？

**请提供日志文件以便排查！**

日志文件位置：
```
# 插件日志
<游戏目录>\BepInEx\LogOutput.log
# unity 日志
C:\Users\<你的用户名>\AppData\LocalLow\Nestopi\Chill With You\Player.log
```

提交 Issue 时请附带日志文件，否则可能无法定位问题！

---

## ✨ 主要功能

### 核心功能
- **🎮 壁纸引擎模式运行**：无需 Steam 即可启动游戏
- **💾 存档切换**：支持多个存档槽位，或读取原 Steam 用户的存档
- **⌨️ 桌面输入支持**：在 Wallpaper Engine 中可以直接从桌面输入
- **🇨🇳 中文输入法**：集成 RIME 中州韵输入法引擎，支持拼音、双拼等多种输入方案
- **🌍 语言切换**：自定义默认语言设置
- **🎁 DLC 控制**：可选启用或禁用 DLC 功能

### 🎵 音乐播放器增强
- **📁 文件夹播放列表**：自动扫描音频文件夹，按目录生成播放列表
- **💿 专辑管理**：二级子文件夹自动识别为专辑，支持专辑封面
- **🎵 扩展音频格式**：支持 OGG、FLAC、AIFF、.egg
- **🔢 突破限制**：突破100首歌曲限制，支持12个额外自定义标签
- **⚡ 虚拟滚动**：只渲染可见的音乐列表项，支持 2000+ 首歌曲
- **📋 播放队列管理**：支持手动添加歌曲到队列、清空队列等操作
- **⏮️ 播放历史记录**：支持"上一首"功能，可回溯最近50首播放记录
- **💾 播放状态恢复**：自动保存并恢复播放进度、队列、历史记录

### 🔊 音频控制
- **🎛️ 系统媒体控制 (SMTC)**：在 Windows 系统媒体浮窗中显示歌曲信息和封面，支持媒体键控制
- **🔇 音频回避**：检测到其他应用播放音频时自动降低游戏音量，停止后自动恢复

### 🎨 UI 优化
- **🖼️ 专辑封面显示**：播放列表显示专辑封面，播放时显示当前歌曲封面
- **📐 UI 重排列**：将游戏 UI 调整为更接近音乐播放器的布局
- **📜 专辑分组**：歌曲按专辑分组显示，支持专辑折叠/展开

## 📦 安装方式

### 1. 安装 BepInEx

1. 下载 [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)
2. 解压到 游戏exe 所在目录：
   ```
   # steam 安装
   steamapps\common\Chill with You Lo-Fi Story
   # 壁纸引擎项目
   wallpaper_engine\projects\myprojects\chill_with_you
   ```

### 2. 安装 ChillPatcher 插件

1. 从 [Releases](../../releases) 下载最新的 `ChillPatcher.zip`
2. 将 `ChillPatcher.zip` 中的文件夹`ChillPatcher`解压复制到：
   ```
   # steam 安装
   steamapps\common\Chill with You Lo-Fi Story\BepInEx\plugins\
   # 壁纸引擎项目
   wallpaper_engine\projects\myprojects\chill_with_you\BepInEx\plugins\
   ```

### 3. 完成！

## **FLAC 支持说明**

- **原游戏问题**：原游戏的音频类型识别仅对 `.mp3`/`.wav` 有明确处理，Unity 对运行时 `.flac` 的支持有限。
  - ❌ 采样率可能错误识别（导致播放速度不对）
  - ❌ 某些平台不支持 FLAC
  - ❌ 行为不一致（Windows Editor 可用，Standalone 可能失败）

- **插件如何解决**：本插件包含一个基于 `dr_flac` 的原生解码器（`NativePlugins/FlacDecoder`），并通过 Harmony 补丁拦截游戏的音频加载流程：
  - 当启用扩展格式（`EnableExtendedFormats`）且遇到 `.flac` 文件时，插件会优先使用原生解码器进行流式解码和播放（使用 `AudioClip.Create(..., stream: true)` + PCM 回调）。
  - 原生解码器导出流式 API（`OpenFlacStream` / `ReadFlacFrames` / `SeekFlacStream` / `CloseFlacStream`），托管层通过 `Native/FlacDecoder.cs` 的 `FlacStreamReader` 进行安全封装，保证低内存占用与可 seek 行为。

更多详细信息和构建选项请参见 [FlacDecoder](NativePlugins/FlacDecoder/README.md)。

## 📋 播放队列与历史记录

本插件实现了完整的播放队列管理系统，提供类似专业音乐播放器的体验。

### 核心概念

- **播放队列**：待播放的歌曲列表，队列第一个永远是当前正在播放的歌曲
- **播放历史**：最近播放的歌曲记录（最多50首），支持"上一首"功能
- **状态恢复**：自动保存并恢复播放进度、队列和历史

### 使用方式

**添加到队列**：
- 在播放列表中点击歌曲控件，实现"添加到队列"和"下一首播放"
- 歌曲会被添加到当前播放歌曲之后或当前歌曲之后

**查看队列**：
- 点击播放界面的"队列"按钮，进入队列视图
- 可以看到待播放的歌曲和播放历史
- 可以拖动排序, 移除

**队列操作按钮**（在Tag下拉菜单中）：
- **清空全部队列**：清空所有待播放歌曲，从播放列表继续播放
- **清空未来队列**：只清空待播放部分，保留当前歌曲
- **清空播放历史**：清空历史记录

**上一首/下一首**：
- 下一首：播放队列中的下一首歌曲
- 上一首：回溯到历史记录中的上一首
- 当历史到头时，会继续往前探索播放列表

### 状态自动保存

游戏运行时会自动保存以下状态：
- 当前播放的歌曲
- 播放队列中的歌曲
- 播放历史记录
- 随机/单曲循环模式
- 当前选中的播放列表

下次启动游戏时会自动恢复这些状态。

状态文件位置：
```
C:\Users\<你的用户名>\AppData\LocalLow\Nestopi\Chill With You\ChillPatcher\playback_state.json
```

## ⚙️ 配置选项

配置文件位于：`<游戏目录>\BepInEx\config\com.chillpatcher.plugin.cfg`

### 壁纸引擎模式

```ini
[WallpaperEngine]

## 是否启用壁纸引擎兼容功能
## true = 启用离线模式，屏蔽所有Steam在线功能
## false = 使用游戏原本逻辑（默认）
## 注意：启用后将强制使用配置的存档，成就不会同步到Steam
# Setting type: Boolean
# Default value: false
EnableWallpaperEngineMode = true
```

更改此选项来启动壁纸引擎模式
此模式不需要steam授权,并且可以鼠标点击交互

#### 关于时长和成就
- **无法获取steam游戏时长**
- **缓存的成就**

壁纸引擎模式会缓存成就到本地,当在线启动之后会自动从缓存的成就尝试为steam解锁,但是需要设置OfflineUserId为你的steamid.就是你的steam存档名.在

```
C:\Users\<你的用户名>\AppData\LocalLow\Nestopi\Chill With You\SaveData\Release\v2
```

你的steam安装的游戏也需要安装此插件,并且没有开启壁纸引擎模式,才会尝试从缓存同步成就

### 框架功能

```ini
[Features]

## 无限的歌曲导入(开启文件夹歌单时自动生效)
## Enable unlimited song import (may affect save compatibility)
# Setting type: Boolean
# Default value: false
EnableUnlimitedSongs = false

## 不限歌曲导入格式(不开也可以用文件夹导入,用于破解官方导入限制)
## Enable extended audio formats (OGG, FLAC, AIFF)
# Setting type: Boolean
# Default value: false
EnableExtendedFormats = false

## 虚拟滚动
## Enable virtual scrolling for better performance
# Setting type: Boolean
# Default value: true
EnableVirtualScroll = true

## 文件夹导入功能
## Enable folder-based playlists (runtime only, not saved)
# Setting type: Boolean
# Default value: true
EnableFolderPlaylists = true

## 专辑分隔符（在播放列表中显示专辑头）
## Enable album separators in playlist view
# Setting type: Boolean
# Default value: true
EnableAlbumSeparators = true

## 专辑封面显示（播放时显示当前歌曲封面）
## Enable album art display during playback
# Setting type: Boolean
# Default value: true
EnableAlbumArtDisplay = true

## UI 重排列（将UI调整为更接近音乐播放器的布局）
## Enable UI rearrangement for music player style
# Setting type: Boolean
# Default value: true
EnableUIRearrange = true
```

### 虚拟滚动高级设置

```ini
[Advanced]
## 虚拟滚动缓冲区大小
## 在可见区域前后渲染的额外项目数量
## 较大值：滚动更流畅，内存占用略高
## 较小值：内存占用低，快速滚动可能有延迟
## 推荐值：3-5
VirtualScrollBufferSize = 3
```

### 文件夹播放列表设置

```ini
[Playlist]

## 是否启用文件夹歌单系统
## true = 启用（默认），扫描目录并创建自定义Tag
## false = 禁用，不会扫描文件夹也不会添加自定义Tag
# Setting type: Boolean
# Default value: true
EnableFolderPlaylists = true

## 歌单根目录路径
## 相对路径基于游戏根目录（.dll所在目录）
## 默认：playlist（与ChillPatcher.dll同级的playlist文件夹）
# Setting type: String
# Default value: playlist
RootFolder = playlist

## 是否自动生成playlist.json
## true = 首次扫描目录时自动生成JSON缓存（默认）
## false = 仅使用已存在的JSON文件
# Setting type: Boolean
# Default value: true
AutoGenerateJson = true

## 是否启用歌单缓存
## true = 读取playlist.json缓存，加快启动速度（默认）
## false = 每次启动重新扫描所有音频文件
# Setting type: Boolean
# Default value: true
EnableCache = true
```

**使用示例**：

假设你的音乐文件夹结构如下：
```
playlist/
├── 歌单A/                    ← 一级子文件夹 = 歌单
│   ├── song1.mp3            ← 根目录歌曲归入"其他"专辑
│   ├── song2.ogg
│   ├── 专辑1/               ← 二级子文件夹 = 专辑
│   │   ├── cover.jpg        ← 专辑封面（可选）
│   │   ├── track1.flac
│   │   └── track2.flac
│   └── 专辑2/
│       └── track3.mp3
└── 歌单B/
    └── ...

```

配置 `RootFolder = playlist` 后，将自动生成以下播放列表：
- 📁 歌单A（包含"其他"专辑 + 专辑1 + 专辑2）
- 📁 歌单B

**注意**：
- 根目录（playlist/）中的音频文件会自动移动到 `default` 文件夹
- 一级子文件夹作为歌单，二级子文件夹作为专辑
- 每个专辑可以有独立的封面图片

**支持的音频格式**：
- `.mp3` - MP3 (MPEG Audio)
- `.wav` - WAV (Waveform Audio)
- `.ogg` - Ogg Vorbis
- `.egg` - Ogg Vorbis
- `.flac` - FLAC (Free Lossless Audio Codec)
- `.aiff` / `.aif` - AIFF (Audio Interchange File Format)

**专辑封面**：

系统会按以下优先级查找专辑封面：
1. 专辑目录中的图片文件（按优先级）：
   - `cover.jpg`, `cover.png`, `cover.jpeg`
   - `folder.jpg`, `folder.png`
   - `album.jpg`, `album.png`
   - `front.jpg`, `front.png`
2. 音频文件内嵌的封面（如 MP3 的 ID3 标签、FLAC 的 metadata）

**如何添加新歌曲（增量更新）**：

首次运行后，每个歌单文件夹会生成缓存文件：
```
playlist/
├── 我的收藏/
│   ├── !rescan_playlist    ← 扫描标志文件
│   ├── playlist.json       ← 歌曲缓存
│   ├── song1.mp3
│   └── 专辑1/
│       ├── cover.jpg       ← 专辑封面
│       └── track1.mp3
```

要添加新歌曲：
1. 将新的音频文件放入歌单文件夹或专辑子文件夹
2. 删除该歌单文件夹中的 `!rescan_playlist` 文件
3. 重启游戏

系统会：
- ✅ 保留已有歌曲的 UUID（收藏、排序、排除状态不丢失）
- ✅ 为新歌曲分配新的 UUID
- ✅ 更新 `playlist.json` 缓存
- ✅ 重新创建 `!rescan_playlist` 标志文件

**注意**：
- 每个歌单文件夹独立管理，互不影响
- 只需删除需要更新的文件夹的标志文件
- 不删除标志文件时，使用缓存快速加载

### 🔊 音频控制

```ini
[Audio]
## 是否启用系统音频检测自动静音功能
## 当检测到其他应用播放音频时，自动降低游戏音乐音量
## 使用 Windows WASAPI，仅在 Windows 上有效
# Setting type: Boolean
# Default value: false
EnableAutoMuteOnOtherAudio = false

## 检测到其他音频时的目标音量（0-1）
## 0 = 完全静音
## 0.1 = 降低到10%（默认）
# Setting type: Float
# Default value: 0.1
AutoMuteVolumeLevel = 0.1

## 检测其他音频的间隔（秒）
## 默认：1秒
# Setting type: Float
# Default value: 1.0
AudioDetectionInterval = 1.0

## 恢复音量的淡入时间（秒）
# Setting type: Float
# Default value: 1.0
AudioResumeFadeInDuration = 1.0

## 降低音量的淡出时间（秒）
# Setting type: Float
# Default value: 0.3
AudioMuteFadeOutDuration = 0.3

## 是否启用系统媒体控制功能 (SMTC)
## 在系统媒体浮窗中显示播放信息，支持媒体键控制
## 需要 ChillSmtcBridge.dll，仅在 Windows 10/11 上有效
# Setting type: Boolean
# Default value: false
EnableSystemMediaTransport = false
```

**音频回避功能说明**：
- 当你在看视频、语音通话或使用其他应用播放音频时，游戏音乐会自动降低音量
- 其他音频停止后，游戏音乐会平滑恢复原音量
- 可调整检测间隔和淡入淡出时间

**系统媒体控制 (SMTC) 功能说明**：
- 在 Windows 系统媒体浮窗中显示当前播放的歌曲信息和封面
- 支持使用键盘媒体键控制：播放/暂停、上一首、下一首
- 支持使用系统音量 OSD 控制播放

### 语言设置

不重要,仅无存档时生效

```ini
[Language]
## 默认游戏语言
## 0 = None (无)
## 1 = Japanese (日语)
## 2 = English (英语)
## 3 = ChineseSimplified (简体中文) - 默认
## 4 = ChineseTraditional (繁体中文)
## 5 = Portuguese (葡萄牙语)
DefaultLanguage = 3
```

### 存档设置
```ini
[SaveData]
## 离线模式使用的用户ID
## 修改此值可以使用不同的存档槽位，或读取原Steam用户的存档
## 例如：将其改为你的 Steam ID 可以访问原来的存档
OfflineUserId = OfflineUser

## 是否使用多存档功能
## true = 使用配置的离线用户ID作为存档路径，可以切换不同存档
## false = 使用Steam ID作为存档路径（默认）
## 注意：启用后即使不在壁纸引擎模式下也会使用配置的存档路径
# Setting type: Boolean
# Default value: false
UseMultipleSaveSlots = false
```

**如何使用原 Steam 存档？**

1. 找到你的 Steam ID（17 位数字）
2. 修改配置文件中的 `OfflineUserId = 你的SteamID`
3. 重启游戏即可使用原存档

**如何使用多个存档槽位？**

- 开启 `UseMultipleSaveSlots = true`
- 不同的 `OfflineUserId` 对应不同的存档
- 例如：`OfflineUserId = Save1`、`OfflineUserId = Save2`

### DLC 设置
```ini
[DLC]
## 是否启用DLC功能
EnableDLC = false
```

### 键盘钩子设置
```ini
[KeyboardHook]
## 键盘钩子消息循环检查间隔（毫秒）
## 默认值：1ms（推荐）
## 较小值：响应更快，CPU占用略高
## 较大值：CPU占用低，响应略慢
## 建议范围：1-10ms
MessageLoopInterval = 1
```

**调整建议**：
- `1ms` - 最佳响应速度（默认推荐）
- `5ms` - 平衡性能和响应
- `10ms` - 低 CPU 占用

### 中文输入法设置
```ini
[InputMethod]
## 是否启用RIME中文输入法
EnableRimeInputMethod = true

## Rime共享数据目录路径（Schema配置文件）
## 留空则自动查找，优先级：
## 1. BepInEx/plugins/ChillPatcher/rime-data/shared
## 2. %AppData%/Rime
## 3. 此配置指定的自定义路径
# Setting type: String
# Default value: 
SharedDataPath = 

## Rime用户数据目录路径（词库、用户配置）
## 留空则使用：BepInEx/plugins/ChillPatcher/rime-data/user
# Setting type: String
# Default value: 
UserDataPath = 
```

## 🖥️ Wallpaper Engine 使用说明

### 桌面输入功能

当你点击桌面（而不是游戏窗口）时，仍然可以输入到游戏的输入框中：

1. 在游戏中点击输入框（如搜索框、聊天框等）
2. 此时输入框获得焦点
3. 即使你点击了桌面，在键盘上输入的字符仍会被捕获并输入到游戏中

**支持功能**：
- ✅ 中文输入（RIME 输入法引擎）
- ✅ 英文字母、数字、常用符号
- ✅ Backspace（删除）、Delete、方向键
- ✅ Enter（确认）、上下键选择候选词

### 🇨🇳 RIME 中文输入法

本插件集成了 **RIME/中州韵输入法引擎**，这是一个强大的开源跨平台输入法框架。

#### 什么是 RIME？

RIME（Rime Input Method Engine）是一个开源的输入法引擎，支持：
- 🎯 **多种输入方案**：拼音、双拼、五笔、注音等
- 🔧 **高度可定制**：通过 YAML 配置文件自由定制
- 📚 **智能候选**：支持云输入、用户词库、自动学习
- 🌏 **跨平台**：Windows(小狼毫)、macOS(鼠须管)、Linux(ibus-rime)

更多信息请访问：
- 官方网站：https://rime.im/
- GitHub：https://github.com/rime/home
- 详细文档：https://github.com/rime/home/wiki

#### 快捷键

| 按键 | 功能 |
|------|------|
| **F4** | 打开方案选单（切换输入方案、中英标点、全/半角） |
| **F6** | 重新部署 RIME（重新加载配置，无需重启游戏） |
| **上/下** | 选择候选词 |
| **数字键 1-9** | 直接选择对应候选词 |
| **空格** | 确认第一个候选词 |
| **左/右** | 移动拼音光标 |

#### 默认输入方案

首次运行会自动部署以下输入方案：
- 🌙 **明月拼音** (luna_pinyin) - 全拼，默认方案
- 📌 **小鹤双拼** (double_pinyin_flypy)
- 🎹 **自然码双拼** (double_pinyin)
- 🪟 **微软双拼** (microsoft_shuangpin)
- 等等

#### 配置文件路径

RIME 配置文件位于：
```
BepInEx\plugins\ChillPatcher\rime-data\shared
```

常用配置文件：
- `default.yaml` - 全局配置（方案列表、快捷键等）
- `<方案名>.schema.yaml` - 各输入方案配置
- `<方案名>.custom.yaml` - 用户自定义配置（推荐）
- `<方案名>.userdb.txt` - 用户词库（可导入导出）

#### 自定义配置示例

修改 `user/default.custom.yaml`（如不存在请创建）：

```yaml
# 自定义补丁文件
patch:
  # 修改候选词数量
  "menu/page_size": 7
  
  # 修改快捷键
  "switcher/hotkeys":
    - "Control+grave"  # Ctrl+` 切换输入方案
    - "F4"
  
  # 添加自定义方案
  "schema_list":
    - schema: luna_pinyin         # 明月拼音
    - schema: double_pinyin_flypy # 小鹤双拼
```

修改后按 **F6** 重新部署即可生效。

#### 候选词显示格式

- **下标数字** `₁₂₃` - 未选中的候选词
- **上标数字** `¹²³` - 当前选中的候选词

示例：`nihao [你¹ 呢₂ 尼₃ 倪₄]`（当前选中"你"）

#### 常见问题

**Q: 如何切换输入方案？**  
A: 按 `F4` 打开方案选单，用数字键或方向键选择。

**Q: 修改配置后如何生效？**  
A: 按 `F6` 重新部署 RIME，无需重启游戏。

**Q: 如何导入自己的词库？**  
A: 将 `.userdb.txt` 或 `.dict.yaml` 放入 `rime/user/` 目录，按 `F6` 重新部署。

**Q: RIME 输入法有问题怎么办？**  
A: 
- ✅ 请先查看 RIME 官方文档：https://github.com/rime/home/wiki
- ✅ 检查日志文件：`rime/user/logs/`
- ❌ **不要向 RIME 官方仓库提交 issue**（这是第三方集成）
- ✅ 如果确认是本插件集成问题，请在本项目提交 issue

**Q: 如何完全禁用中文输入法？**  
A: 修改配置文件 `BepInEx\config\com.chillpatcher.plugin.cfg`：
```ini
[InputMethod]
EnableRimeInputMethod = false
```

### 清空输入缓冲

如果不想继续输入，只需：
- 在游戏中点击任意位置（鼠标左键）
- 或者点击其他输入框（会自动清理 RIME 状态）

## 🔧 开发构建

```bash
# 克隆仓库
git clone <repository-url>

# 使用 Visual Studio 或 Rider 打开
ChillPatcher.sln

# 构建项目
dotnet build

# 输出目录
bin/Debug/ChillPatcher.dll
```

## ❓ 常见问题

### Q: 游戏启动白屏/卡住？
A: 检查 `BepInEx\LogOutput.log` 查看错误信息。通常是 BepInEx 版本不兼容。

### Q: 桌面输入不起作用？
A: 确保：
1. 游戏输入框已获得焦点
2. 当前前台窗口是桌面（不是其他应用）
3. 尝试调整 `MessageLoopInterval` 配置

### Q: 游戏关闭后进程无响应？
A: 最新版本已修复此问题。如果仍有问题，请查看日志中的 `[KeyboardHook]` 信息。

### Q: 如何禁用桌面输入功能？
A: 暂不支持配置禁用。如需禁用，请移除 `ChillPatcher` 插件，输入法可以禁用。

## 📜 许可证

本项目采用 **GPL v3** 许可证:

- **librime** ([中州韵输入法引擎](https://github.com/rime/librime)) - BSD 3-Clause License
- **BepInEx** - LGPL 2.1 License
- **HarmonyX** - MIT License

根据开源许可兼容性,ChillPatcher 整体采用 GPL v3 许可。详见 [LICENSE](LICENSE) 文件。

**注意**: 本项目仅供学习研究使用。游戏本体版权归原开发者所有,请支持正版。

## 🙏 致谢

- [RIME/中州韵输入法引擎](https://github.com/rime/librime) - 强大的开源输入法引擎
- [BepInEx](https://github.com/BepInEx/BepInEx) - Unity 游戏模组框架
- [HarmonyX](https://github.com/BepInEx/HarmonyX) - .NET 运行时方法补丁库
- [dr_libs](https://github.com/mackron/dr_libs) - flac解码支持

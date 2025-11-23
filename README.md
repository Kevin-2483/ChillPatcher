# ChillPatcher - Wallpaper Engine 环境运行补丁

这是一个 BepInEx 插件，用于使《Chill With You》游戏正确在 Wallpaper Engine 环境下运行。

## ✨ 主要功能

- **🎮 离线模式运行**：无需 Steam 即可启动游戏
- **💾 存档切换**：支持多个存档槽位，或读取原 Steam 用户的存档
- **⌨️ 桌面输入支持**：在 Wallpaper Engine 中可以直接从桌面输入
- **🇨🇳 中文输入法**：集成 RIME 中州韵输入法引擎，支持拼音、双拼等多种输入方案
- **🌍 语言切换**：自定义默认语言设置
- **🎁 DLC 控制**：可选启用或禁用 DLC 功能

## 📦 安装方式

### 1. 安装 BepInEx

1. 下载 [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (Unity IL2CPP x64 版本)
2. 解压到 Wallpaper Engine 项目目录：
   ```
   wallpaper_engine\projects\myprojects\chill_with_you\
   ```
3. 运行一次游戏以生成 BepInEx 配置文件

### 2. 安装 ChillPatcher 插件

1. 从 [Releases](../../releases) 下载最新的 `ChillPatcher.dll`
2. 将 `ChillPatcher.dll` 复制到：
   ```
   wallpaper_engine\projects\myprojects\chill_with_you\BepInEx\plugins\
   ```

### 3. 完成！

安装完成后，游戏将自动：
- 绕过 Steam 验证
- 使用离线用户 ID 创建存档
- 支持在桌面激活时捕获键盘输入

## ⚙️ 配置选项

配置文件位于：`BepInEx\config\com.chillpatcher.plugin.cfg`

### 语言设置
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
```

**如何使用原 Steam 存档？**

1. 找到你的 Steam ID（17 位数字）
2. 修改配置文件中的 `OfflineUserId = 你的SteamID`
3. 重启游戏即可使用原存档

**如何使用多个存档槽位？**

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
- ✍️ **五笔画** (stroke)

#### 配置文件路径

RIME 配置文件位于：
```
BepInEx\plugins\ChillPatcher\rime\user\
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

## 📝 技术细节

### 核心补丁

1. **SteamAPIPatch**：绕过 Steam 初始化，防止启动死锁
2. **LanguagePatch**：自定义默认语言设置
3. **KeyboardHookPatch**：全局键盘钩子，捕获桌面输入
4. **AchievementsPatch**：禁用 Steam 成就系统
5. **EventBrokerPatch**：防止 Steam 事件代理异常

### 键盘钩子原理

使用 Windows 底层键盘钩子 (WH_KEYBOARD_LL) 捕获全局键盘事件：
- 检测前台窗口是否为桌面 (Progman/WorkerW/SysListView32)
- 捕获键盘输入并加入队列
- 在 TMP_InputField.LateUpdate 时注入字符

### 退出清理机制

- 使用 `PeekMessage` 非阻塞消息循环
- 监听 `OnApplicationQuit()` 事件清理钩子
- 捕获 `ThreadAbortException` 防止退出报错

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
A: 暂不支持配置禁用。如需禁用，请移除 `ChillPatcher.dll` 插件。

## 📜 许可证

本项目采用 **GPL v3** 许可证,因为它使用了以下开源组件:

- **librime** ([中州韵输入法引擎](https://github.com/rime/librime)) - BSD 3-Clause License
- **BepInEx** - LGPL 2.1 License
- **HarmonyX** - MIT License

根据开源许可兼容性,ChillPatcher 整体采用 GPL v3 许可。详见 [LICENSE](LICENSE) 文件。

**注意**: 本项目仅供学习研究使用。游戏本体版权归原开发者所有,请支持正版。

## 🙏 致谢

- [RIME/中州韵输入法引擎](https://github.com/rime/librime) - 强大的开源输入法引擎
- [BepInEx](https://github.com/BepInEx/BepInEx) - Unity 游戏模组框架
- [HarmonyX](https://github.com/BepInEx/HarmonyX) - .NET 运行时方法补丁库

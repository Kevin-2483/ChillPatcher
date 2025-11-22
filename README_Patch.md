# ChillPatcher - BepInEx 插件

这是一个用于 "Chill with You Lo-Fi Story" 游戏的 BepInEx 插件，使用 Harmony 来 patch 游戏代码，实现离线运行。

## 功能特性

- ✅ 完全离线运行，无需 Steam
- ✅ 可配置的默认语言
- ✅ 可配置的用户ID（支持读取原Steam存档）
- ✅ 可选的DLC启用/禁用
- ✅ 禁用Steam成就系统
- ✅ 移除Steam依赖死锁

## 配置文件

首次运行后，会在 `BepInEx/config/com.chillpatcher.plugin.cfg` 生成配置文件：

```ini
[Language]
## 默认游戏语言
## 枚举值说明：
## 0 = None (无)
## 1 = Japanese (日语)
## 2 = English (英语)
## 3 = ChineseSimplified (简体中文)
## 4 = ChineseTraditional (繁体中文)
## 5 = Portuguese (葡萄牙语)
# Setting type: Int32
# Default value: 3
# Acceptable value range: From 0 to 5
DefaultLanguage = 3

[SaveData]
## 离线模式使用的用户ID，用于存档路径
## 修改此值可以使用不同的存档槽位，或读取原Steam用户的存档
## 例如：使用原Steam ID可以访问原来的存档
# Setting type: String
# Default value: OfflineUser
OfflineUserId = OfflineUser

[DLC]
## 是否启用DLC功能
## true = 启用DLC
## false = 禁用DLC（默认）
# Setting type: Boolean
# Default value: false
EnableDLC = false
```

### 配置说明

1. **DefaultLanguage** - 设置游戏默认语言
   - 默认值：`3` (简体中文)
   - 可选值：0=None, 1=日语, 2=英语, 3=简体中文, 4=繁体中文, 5=葡萄牙语

2. **OfflineUserId** - 设置离线用户ID
   - 默认值：`OfflineUser`
   - 如果你之前用Steam玩过游戏，可以将此值改为你的Steam ID来读取原存档
   - Steam存档路径格式：`SaveData/Release/v2/<SteamID>`

3. **EnableDLC** - 是否启用DLC
   - 默认值：`false`
   - 如果拥有DLC，可以设置为 `true`

## 项目结构

```
ChillPatcher/
├── Plugin.cs                  # 主插件入口
├── PluginConfig.cs            # 配置管理
├── MyPluginInfo.cs            # 插件信息
├── Patches/                   # Harmony patch 类
│   ├── ExamplePatch.cs        # SteamManager patches
│   ├── EntryBehaviorPatch.cs  # 启动流程 patch
│   ├── BulbulConstantPatch.cs # 存档路径 patches
│   ├── LanguagePatch.cs       # 语言 patch
│   ├── AchievementsPatch.cs   # 成就系统 patches
│   └── EventBrokerPatch.cs    # 事件代理 patches
└── ChillPatcher.csproj        # 项目文件
```

## 依赖

- BepInEx 5.x
- HarmonyX 2.x
- Unity (游戏版本)
- .NET Framework 4.7.2

## 编译

使用 Visual Studio 2019/2022 或 dotnet CLI:

```powershell
dotnet build ChillPatcher.csproj
```

编译成功后，DLL 会自动复制到游戏的 BepInEx/plugins/ 目录。

## 引用

项目引用以下 DLL:
- `D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\BepInEx\core\0Harmony.dll`
- `D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\BepInEx\core\BepInEx.dll`
- `D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\Chill With You_Data\Managed\Assembly-CSharp.dll`
- `D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\Chill With You_Data\Managed\UnityEngine.dll`
- `D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\Chill With You_Data\Managed\UnityEngine.CoreModule.dll`

## Patch 列表

### 1. SteamManager 核心阻断
- **Initialize** - 强制离线模式，阻止Steam初始化
- **Tick** - 切断Steam心跳
- **IsInstalledDLC** - 根据配置返回DLC状态

### 2. EntryBehavior 解除死锁
- **StartAsync** - 删除等待Steam初始化的代码，防止无限Loading

### 3. BulbulConstant 路径修复
- **CreateSaveDirectoryPath (两个重载)** - 使用配置的用户ID替代SteamID

### 4. 语言修复
- **SteamDefaultLanguageSupplier.GetDefaultLanguage** - 使用配置的语言，防止空指针

### 5. 成就系统禁用
- 禁用所有成就相关功能
- 阻止Steam API调用

### 6. 事件代理安全处理
- 跳过Steam Callback注册
- 安全的Dispose实现

## 使用说明

1. 确保已安装 BepInEx 5.x
2. 将编译好的 `ChillPatcher.dll` 复制到 `BepInEx/plugins/` 目录
3. 运行游戏，插件会自动生成配置文件
4. 根据需要修改配置文件
5. 重启游戏应用新配置

## 注意事项

- 首次使用时会创建新的离线存档
- 如需使用原Steam存档，请在配置中设置正确的Steam ID
- 修改配置后需要重启游戏才能生效

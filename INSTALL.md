# ChillPatcher 安装说明

## 前置要求

1. 游戏：Chill with You Lo-Fi Story
2. BepInEx 5.x (用于Unity IL2CPP或Mono游戏)

## 安装步骤

### 1. 安装 BepInEx

如果还没有安装 BepInEx：

1. 从 [BepInEx GitHub Releases](https://github.com/BepInEx/BepInEx/releases) 下载适合你游戏的版本
   - 如果游戏是 IL2CPP: 下载 `BepInEx_UnityIL2CPP_x64_xxx.zip`
   - 如果游戏是 Mono: 下载 `BepInEx_UnityMono_x64_xxx.zip`
   
2. 解压到游戏根目录（包含游戏exe的文件夹）
   - 例如：`D:\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\`

3. 运行游戏一次，让 BepInEx 初始化并创建文件夹结构

### 2. 安装 ChillPatcher 插件

1. 将编译好的 `ChillPatcher.dll` 复制到：
   ```
   <游戏目录>\BepInEx\plugins\ChillPatcher.dll
   ```

2. 启动游戏，插件会自动加载并生成配置文件

### 3. 配置插件 (可选)

配置文件位置：`<游戏目录>\BepInEx\config\com.chillpatcher.plugin.cfg`

```ini
[Language]
## 设置游戏语言
DefaultLanguage = 3

[SaveData]
## 设置用户ID（用于存档路径）
## 如果要使用原Steam存档，请改为你的Steam ID
OfflineUserId = OfflineUser

[DLC]
## 是否启用DLC
EnableDLC = false
```

### 4. 如何找到原Steam存档

如果你之前用Steam玩过游戏并想继续使用原存档：

1. Steam存档路径格式：
   ```
   <游戏目录>\<用户数据路径>\SaveData\Release\v2\<SteamID>\
   ```

2. 找到你的 Steam ID（一串数字）

3. 在配置文件中设置：
   ```ini
   OfflineUserId = <你的SteamID>
   ```

### 5. 验证安装

启动游戏后，检查：

1. 游戏是否正常启动（没有卡在 Loading 画面）
2. 查看 BepInEx 控制台输出（如果启用）
3. 查看日志文件：`<游戏目录>\BepInEx\LogOutput.log`

应该能看到类似的日志：
```
[Info   : ChillPatcher] Plugin com.chillpatcher.plugin is loaded!
[Info   : ChillPatcher] 配置文件已加载:
[Info   : ChillPatcher]   - 默认语言: 3
[Info   : ChillPatcher]   - 离线用户ID: OfflineUser
[Info   : ChillPatcher]   - 启用DLC: False
[Info   : ChillPatcher] Harmony patches applied!
```

## 卸载

如果需要卸载插件：

1. 删除 `<游戏目录>\BepInEx\plugins\ChillPatcher.dll`
2. 删除配置文件 `<游戏目录>\BepInEx\config\com.chillpatcher.plugin.cfg`
3. (可选) 如果要完全移除 BepInEx，删除整个 `BepInEx` 文件夹

## 故障排除

### 游戏无法启动

1. 确保 BepInEx 正确安装
2. 检查 BepInEx 日志文件是否有错误
3. 尝试删除配置文件让插件重新生成

### 无法加载存档

1. 检查 `OfflineUserId` 设置是否正确
2. 确认存档路径是否存在

### 语言设置无效

1. 检查配置中的 `DefaultLanguage` 值是否在 0-5 范围内
2. 确保修改配置后重启了游戏

## 编译源代码

如果需要自己编译：

```powershell
cd "f:\code\C#\Chill"
dotnet build ChillPatcher.csproj -c Release
```

输出文件：`bin\ChillPatcher.dll`

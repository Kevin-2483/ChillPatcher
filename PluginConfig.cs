using BepInEx.Configuration;

namespace ChillPatcher
{
    public static class PluginConfig
    {
        // 语言设置
        public static ConfigEntry<int> DefaultLanguage { get; private set; }

        // 用户ID设置
        public static ConfigEntry<string> OfflineUserId { get; private set; }

        // DLC设置
        public static ConfigEntry<bool> EnableDLC { get; private set; }

        // Steam补丁设置（统一开关）
        public static ConfigEntry<bool> EnableWallpaperEngineMode { get; private set; }

        // 多存档设置
        public static ConfigEntry<bool> UseMultipleSaveSlots { get; private set; }

        // 成就缓存设置
        public static ConfigEntry<bool> EnableAchievementCache { get; private set; }

        // 键盘钩子设置
        public static ConfigEntry<int> KeyboardHookInterval { get; private set; }

        // Rime输入法设置
        public static ConfigEntry<string> RimeSharedDataPath { get; private set; }
        public static ConfigEntry<string> RimeUserDataPath { get; private set; }
        public static ConfigEntry<bool> EnableRimeInputMethod { get; private set; }

        // 文件夹歌单设置
        public static ConfigEntry<bool> EnableFolderPlaylists { get; private set; }
        public static ConfigEntry<string> PlaylistRootFolder { get; private set; }
        public static ConfigEntry<bool> AutoGeneratePlaylistJson { get; private set; }
        public static ConfigEntry<bool> EnablePlaylistCache { get; private set; }
        public static ConfigEntry<bool> HideEmptyTags { get; private set; }
        public static ConfigEntry<float> TagDropdownHeightMultiplier { get; private set; }
        public static ConfigEntry<float> TagDropdownHeightOffset { get; private set; }
        public static ConfigEntry<int> MaxTagsInTitle { get; private set; }
        public static ConfigEntry<bool> CleanInvalidMusicData { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            // 语言设置 - 使用枚举值
            DefaultLanguage = config.Bind(
                "Language",
                "DefaultLanguage",
                3, // 默认值：ChineseSimplified = 3
                new ConfigDescription(
                    "默认游戏语言\n" +
                    "枚举值说明：\n" +
                    "0 = None (无)\n" +
                    "1 = Japanese (日语)\n" +
                    "2 = English (英语)\n" +
                    "3 = ChineseSimplified (简体中文)\n" +
                    "4 = ChineseTraditional (繁体中文)\n" +
                    "5 = Portuguese (葡萄牙语)",
                    new AcceptableValueRange<int>(0, 5)
                )
            );

            // 离线用户ID设置
            OfflineUserId = config.Bind(
                "SaveData",
                "OfflineUserId",
                "OfflineUser",
                "离线模式使用的用户ID，用于存档路径\n" +
                "修改此值可以使用不同的存档槽位，或读取原Steam用户的存档\n" +
                "例如：使用原Steam ID可以访问原来的存档"
            );

            // DLC设置
            EnableDLC = config.Bind(
                "DLC",
                "EnableDLC",
                false,
                "是否启用DLC功能\n" +
                "true = 启用DLC\n" +
                "false = 禁用DLC（默认）"
            );

            // WallpaperEngine补丁设置
            EnableWallpaperEngineMode = config.Bind(
                "WallpaperEngine",
                "EnableWallpaperEngineMode",
                false,
                "是否启用壁纸引擎兼容功能\n" +
                "true = 启用离线模式，屏蔽所有Steam在线功能\n" +
                "false = 使用游戏原本逻辑（默认）\n" +
                "注意：启用后将强制使用配置的存档，成就不会同步到Steam"
            );

            // 多存档设置
            UseMultipleSaveSlots = config.Bind(
                "SaveData",
                "UseMultipleSaveSlots",
                false,
                "是否使用多存档功能\n" +
                "true = 使用配置的离线用户ID作为存档路径，可以切换不同存档\n" +
                "false = 使用Steam ID作为存档路径（默认）\n" +
                "注意：启用后即使不在壁纸引擎模式下也会使用配置的存档路径"
            );

            // 成就缓存设置
            EnableAchievementCache = config.Bind(
                "Achievement",
                "EnableAchievementCache",
                true,
                "是否启用成就缓存功能\n" +
                "true = 所有成就都会缓存到本地作为备份（默认）\n" +
                "  - 壁纸引擎模式：仅缓存，不推送到Steam\n" +
                "  - 正常模式：缓存后继续推送到Steam\n" +
                "false = 禁用成就缓存（正常模式直接推送Steam，壁纸引擎模式丢弃成就）\n" +
                "缓存位置: C:\\Users\\(user)\\AppData\\LocalLow\\Nestopi\\Chill With You\\ChillPatcherCache\\[UserID]\n" +
                "注意：缓存永久保留，每次启动会自动同步到Steam"
            );

            // 键盘钩子消息循环间隔
            KeyboardHookInterval = config.Bind(
                "KeyboardHook",
                "MessageLoopInterval",
                10,
                new ConfigDescription(
                    "键盘钩子消息循环检查间隔（毫秒）\n" +
                    "默认值：10ms（推荐）\n" +
                    "较小值：响应更快，CPU占用略高\n" +
                    "较大值：CPU占用低，响应略慢\n" +
                    "建议范围：1-10ms",
                    new AcceptableValueRange<int>(1, 100)
                )
            );

            // Rime输入法配置
            EnableRimeInputMethod = config.Bind(
                "Rime",
                "EnableRimeInputMethod",
                true,
                "是否启用Rime输入法引擎\n" +
                "true = 启用Rime（默认）\n" +
                "false = 使用简单队列输入"
            );

            RimeSharedDataPath = config.Bind(
                "Rime",
                "SharedDataPath",
                "",
                "Rime共享数据目录路径（Schema配置文件）\n" +
                "留空则自动查找，优先级：\n" +
                "1. BepInEx/plugins/ChillPatcher/rime-data/shared\n" +
                "2. %AppData%/Rime\n" +
                "3. 此配置指定的自定义路径"
            );

            RimeUserDataPath = config.Bind(
                "Rime",
                "UserDataPath",
                "",
                "Rime用户数据目录路径（词库、用户配置）\n" +
                "留空则使用：BepInEx/plugins/ChillPatcher/rime-data/user"
            );

            // 文件夹歌单配置
            EnableFolderPlaylists = config.Bind(
                "Playlist",
                "EnableFolderPlaylists",
                true,
                "是否启用文件夹歌单系统\n" +
                "true = 启用（默认），扫描目录并创建自定义Tag\n" +
                "false = 禁用，不会扫描文件夹也不会添加自定义Tag"
            );

            PlaylistRootFolder = config.Bind(
                "Playlist",
                "RootFolder",
                "playlist",
                "歌单根目录路径\n" +
                "相对路径基于游戏根目录（.dll所在目录）\n" +
                "默认：playlist（与ChillPatcher.dll同级的playlist文件夹）\n" +
                "目录结构：\n" +
                "  playlist/\n" +
                "    ├─ default/        ← 默认歌单\n" +
                "    ├─ 歌单A/          ← 一级子文件夹作为歌单\n" +
                "    │   ├─ 专辑1/     ← 二级子文件夹作为专辑\n" +
                "    │   └─ 专辑2/\n" +
                "    └─ 歌单B/\n" +
                "注意：根目录的音频文件会自动移动到default文件夹"
            );

            AutoGeneratePlaylistJson = config.Bind(
                "Playlist",
                "AutoGenerateJson",
                true,
                "是否自动生成playlist.json\n" +
                "true = 首次扫描目录时自动生成JSON缓存（默认）\n" +
                "false = 仅使用已存在的JSON文件"
            );

            EnablePlaylistCache = config.Bind(
                "Playlist",
                "EnableCache",
                true,
                "是否启用歌单缓存\n" +
                "true = 读取playlist.json缓存，加快启动速度（默认）\n" +
                "false = 每次启动重新扫描所有音频文件"
            );

            HideEmptyTags = config.Bind(
                "UI",
                "HideEmptyTags",
                false,
                "是否在Tag下拉框中隐藏空标签\n" +
                "true = 隐藏没有歌曲的Tag\n" +
                "false = 显示所有Tag（默认）"
            );

            TagDropdownHeightMultiplier = config.Bind(
                "UI",
                "TagDropdownHeightMultiplier",
                1f,
                new ConfigDescription(
                    "Tag下拉框高度线性系数（斜率a）\n" +
                    "计算公式：最终高度 = a × 内容实际高度 + b\n" +
                    "默认：1",
                    new AcceptableValueRange<float>(0.1f, 3.0f)
                )
            );

            TagDropdownHeightOffset = config.Bind(
                "UI",
                "TagDropdownHeightOffset",
                50f,
                new ConfigDescription(
                    "Tag下拉框高度偏移量（常数b，单位：像素）\n" +
                    "计算公式：最终高度 = a × 内容实际高度 + b\n" +
                    "默认：0（无偏移）\n" +
                    "示例：50 = 增加50像素, -50 = 减少50像素",
                    new AcceptableValueRange<float>(-500f, 500f)
                )
            );

            MaxTagsInTitle = config.Bind(
                "UI",
                "MaxTagsInTitle",
                2,
                new ConfigDescription(
                    "标签下拉框标题最多显示的标签数量\n" +
                    "超过此数量将显示'等其他'\n" +
                    "默认：3",
                    new AcceptableValueRange<int>(1, 10)
                )
            );

            CleanInvalidMusicData = config.Bind(
                "Maintenance",
                "CleanInvalidMusicData",
                false,
                "清理无效的音乐数据（启动时执行一次）\n" +
                "删除收藏列表和本地音乐列表中不存在的文件\n" +
                "执行后会自动关闭此选项\n" +
                "默认：false"
            );

            Plugin.Logger.LogInfo("配置文件已加载:");
            Plugin.Logger.LogInfo($"  - 默认语言: {DefaultLanguage.Value}");
            Plugin.Logger.LogInfo($"  - 离线用户ID: {OfflineUserId.Value}");
            Plugin.Logger.LogInfo($"  - 启用DLC: {EnableDLC.Value}");
            Plugin.Logger.LogInfo($"  - 壁纸引擎模式: {EnableWallpaperEngineMode.Value}");
            Plugin.Logger.LogInfo($"  - 使用多存档: {UseMultipleSaveSlots.Value}");
            Plugin.Logger.LogInfo($"  - 启用成就缓存: {EnableAchievementCache.Value}");
            Plugin.Logger.LogInfo($"  - 键盘钩子间隔: {KeyboardHookInterval.Value}ms");
            Plugin.Logger.LogInfo($"  - 启用Rime: {EnableRimeInputMethod.Value}");
            if (!string.IsNullOrEmpty(RimeSharedDataPath.Value))
                Plugin.Logger.LogInfo($"  - Rime共享目录: {RimeSharedDataPath.Value}");
            if (!string.IsNullOrEmpty(RimeUserDataPath.Value))
                Plugin.Logger.LogInfo($"  - Rime用户目录: {RimeUserDataPath.Value}");
            Plugin.Logger.LogInfo($"  - 歌单根目录: {PlaylistRootFolder.Value}");
            Plugin.Logger.LogInfo($"  - 自动生成JSON: {AutoGeneratePlaylistJson.Value}");
            Plugin.Logger.LogInfo($"  - 启用缓存: {EnablePlaylistCache.Value}");
            Plugin.Logger.LogInfo($"  - 隐藏空Tag: {HideEmptyTags.Value}");
            Plugin.Logger.LogInfo($"  - Tag下拉框高度: a={TagDropdownHeightMultiplier.Value}, b={TagDropdownHeightOffset.Value}");
        }
    }
}

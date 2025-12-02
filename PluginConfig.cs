using BepInEx.Configuration;
using System.Collections.Generic;

namespace ChillPatcher
{
    public static class PluginConfig
    {
        // 配置分区版本号 - 用于自动重置过期配置
        // 当分区默认值发生变化时，增加对应版本号，旧配置会被重置为新默认值
        private static readonly Dictionary<string, int> SectionVersions = new Dictionary<string, int>
        {
            { "Language", 1 },
            { "SaveData", 1 },
            { "DLC", 1 },
            { "Steam", 1 },
            { "SaveSlot", 1 },
            { "Achievement", 1 },
            { "Keyboard", 1 },
            { "Rime", 1 },
            { "Playlist", 1 },
            { "UI", 2 },        // v2: TagDropdownHeightOffset 默认值从50改为80
            { "Maintenance", 1 },
            { "Audio", 1 }      // 音频自动静音功能
        };
        
        // 配置版本存储
        private static ConfigEntry<int> _languageVersion;
        private static ConfigEntry<int> _saveDataVersion;
        private static ConfigEntry<int> _dlcVersion;
        private static ConfigEntry<int> _steamVersion;
        private static ConfigEntry<int> _saveSlotVersion;
        private static ConfigEntry<int> _achievementVersion;
        private static ConfigEntry<int> _keyboardVersion;
        private static ConfigEntry<int> _rimeVersion;
        private static ConfigEntry<int> _playlistVersion;
        private static ConfigEntry<int> _uiVersion;
        private static ConfigEntry<int> _maintenanceVersion;
        private static ConfigEntry<int> _audioVersion;

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

        // 音频自动静音设置
        public static ConfigEntry<bool> EnableAutoMuteOnOtherAudio { get; private set; }
        public static ConfigEntry<float> AutoMuteVolumeLevel { get; private set; }
        public static ConfigEntry<float> AudioDetectionInterval { get; private set; }
        public static ConfigEntry<float> AudioResumeFadeInDuration { get; private set; }
        public static ConfigEntry<float> AudioMuteFadeOutDuration { get; private set; }

        // 系统媒体控制设置
        public static ConfigEntry<bool> EnableSystemMediaTransport { get; private set; }
        
        // 配置文件引用（用于版本重置）
        private static ConfigFile _configFile;

        public static void Initialize(ConfigFile config)
        {
            _configFile = config;
            
            // 先加载所有分区版本号
            LoadSectionVersions(config);
            
            // 检查并重置过期分区
            CheckAndResetOutdatedSections(config);
            
            // 加载实际配置
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
                80f,
                new ConfigDescription(
                    "Tag下拉框高度偏移量（常数b，单位：像素）\n" +
                    "计算公式：最终高度 = a × (按钮数 × 45) + b\n" +
                    "默认：80\n" +
                    "示例：100 = 增加偏移, 50 = 减少偏移",
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

            // 音频自动静音配置
            EnableAutoMuteOnOtherAudio = config.Bind(
                "Audio",
                "EnableAutoMuteOnOtherAudio",
                false,
                "是否启用系统音频检测自动静音功能\n" +
                "true = 当检测到其他应用播放音频时，自动降低游戏音乐音量\n" +
                "false = 禁用此功能（默认）\n" +
                "注意：此功能使用 Windows WASAPI，仅在 Windows 上有效"
            );

            AutoMuteVolumeLevel = config.Bind(
                "Audio",
                "AutoMuteVolumeLevel",
                0.1f,
                new ConfigDescription(
                    "检测到其他音频时的目标音量（0-1）\n" +
                    "0 = 完全静音\n" +
                    "0.1 = 降低到10%（默认）\n" +
                    "1 = 不降低\n" +
                    "建议：0.05-0.2",
                    new AcceptableValueRange<float>(0f, 1f)
                )
            );

            AudioDetectionInterval = config.Bind(
                "Audio",
                "AudioDetectionInterval",
                1.0f,
                new ConfigDescription(
                    "检测其他音频的间隔（秒）\n" +
                    "默认：1秒\n" +
                    "较小值：响应更快，CPU占用略高\n" +
                    "较大值：CPU占用低，响应略慢\n" +
                    "建议范围：0.5-3秒",
                    new AcceptableValueRange<float>(0.1f, 10f)
                )
            );

            AudioResumeFadeInDuration = config.Bind(
                "Audio",
                "AudioResumeFadeInDuration",
                1.0f,
                new ConfigDescription(
                    "恢复音量的淡入时间（秒）\n" +
                    "当其他音频停止时，游戏音乐会在此时间内逐渐恢复音量\n" +
                    "默认：1秒",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            AudioMuteFadeOutDuration = config.Bind(
                "Audio",
                "AudioMuteFadeOutDuration",
                0.3f,
                new ConfigDescription(
                    "降低音量的淡出时间（秒）\n" +
                    "当检测到其他音频时，游戏音乐会在此时间内逐渐降低音量\n" +
                    "默认：0.3秒（快速响应）",
                    new AcceptableValueRange<float>(0f, 3f)
                )
            );

            // 系统媒体控制配置
            EnableSystemMediaTransport = config.Bind(
                "Audio",
                "EnableSystemMediaTransport",
                false,
                "是否启用系统媒体控制功能 (SMTC)\n" +
                "true = 启用，在系统媒体浮窗中显示播放信息，支持媒体键控制\n" +
                "false = 禁用（默认）\n" +
                "注意：此功能需要 ChillSmtcBridge.dll，仅在 Windows 10/11 上有效"
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
            Plugin.Logger.LogInfo($"  - 自动静音功能: {EnableAutoMuteOnOtherAudio.Value}");
            if (EnableAutoMuteOnOtherAudio.Value)
            {
                Plugin.Logger.LogInfo($"    - 目标音量: {AutoMuteVolumeLevel.Value}");
                Plugin.Logger.LogInfo($"    - 检测间隔: {AudioDetectionInterval.Value}秒");
            }
            Plugin.Logger.LogInfo($"  - 系统媒体控制: {EnableSystemMediaTransport.Value}");
        }
        
        /// <summary>
        /// 加载所有分区版本号
        /// </summary>
        private static void LoadSectionVersions(ConfigFile config)
        {
            // 先检查哪些版本条目在配置文件中不存在（表示是旧配置）
            // 对于这些不存在的版本条目，如果当前版本不是1，就需要重置
            
            var versionDefinitions = new Dictionary<string, ConfigDefinition>
            {
                { "Language", new ConfigDefinition("_Version", "Language") },
                { "SaveData", new ConfigDefinition("_Version", "SaveData") },
                { "DLC", new ConfigDefinition("_Version", "DLC") },
                { "Steam", new ConfigDefinition("_Version", "Steam") },
                { "SaveSlot", new ConfigDefinition("_Version", "SaveSlot") },
                { "Achievement", new ConfigDefinition("_Version", "Achievement") },
                { "Keyboard", new ConfigDefinition("_Version", "Keyboard") },
                { "Rime", new ConfigDefinition("_Version", "Rime") },
                { "Playlist", new ConfigDefinition("_Version", "Playlist") },
                { "UI", new ConfigDefinition("_Version", "UI") },
                { "Maintenance", new ConfigDefinition("_Version", "Maintenance") },
                { "Audio", new ConfigDefinition("_Version", "Audio") }
            };
            
            // 记录缺失的版本条目（需要按当前版本是否>1来决定是否重置）
            _missingSectionVersions.Clear();
            foreach (var kvp in versionDefinitions)
            {
                bool exists = false;
                foreach (var key in config.Keys)
                {
                    if (key.Section == kvp.Value.Section && key.Key == kvp.Value.Key)
                    {
                        exists = true;
                        break;
                    }
                }
                
                if (!exists && SectionVersions[kvp.Key] > 1)
                {
                    _missingSectionVersions.Add(kvp.Key);
                    Plugin.Logger.LogInfo($"[Config] 检测到缺失版本条目且当前版本>1: {kvp.Key} (v{SectionVersions[kvp.Key]})");
                }
            }
            
            // 现在绑定版本号（会为缺失的创建默认值）
            _languageVersion = config.Bind("_Version", "Language", SectionVersions["Language"], "配置分区版本号（请勿手动修改）");
            _saveDataVersion = config.Bind("_Version", "SaveData", SectionVersions["SaveData"], "配置分区版本号（请勿手动修改）");
            _dlcVersion = config.Bind("_Version", "DLC", SectionVersions["DLC"], "配置分区版本号（请勿手动修改）");
            _steamVersion = config.Bind("_Version", "Steam", SectionVersions["Steam"], "配置分区版本号（请勿手动修改）");
            _saveSlotVersion = config.Bind("_Version", "SaveSlot", SectionVersions["SaveSlot"], "配置分区版本号（请勿手动修改）");
            _achievementVersion = config.Bind("_Version", "Achievement", SectionVersions["Achievement"], "配置分区版本号（请勿手动修改）");
            _keyboardVersion = config.Bind("_Version", "Keyboard", SectionVersions["Keyboard"], "配置分区版本号（请勿手动修改）");
            _rimeVersion = config.Bind("_Version", "Rime", SectionVersions["Rime"], "配置分区版本号（请勿手动修改）");
            _playlistVersion = config.Bind("_Version", "Playlist", SectionVersions["Playlist"], "配置分区版本号（请勿手动修改）");
            _uiVersion = config.Bind("_Version", "UI", SectionVersions["UI"], "配置分区版本号（请勿手动修改）");
            _maintenanceVersion = config.Bind("_Version", "Maintenance", SectionVersions["Maintenance"], "配置分区版本号（请勿手动修改）");
            _audioVersion = config.Bind("_Version", "Audio", SectionVersions["Audio"], "配置分区版本号（请勿手动修改）");
        }
        
        // 记录缺失的版本条目（版本>1需要重置）
        private static List<string> _missingSectionVersions = new List<string>();
        
        /// <summary>
        /// 检查并重置过期分区配置
        /// </summary>
        private static void CheckAndResetOutdatedSections(ConfigFile config)
        {
            var sectionsToReset = new List<string>();
            
            // 添加版本条目缺失且当前版本>1的分区（旧配置需要重置）
            sectionsToReset.AddRange(_missingSectionVersions);
            
            // 检查每个分区的版本（版本存在但小于当前版本）
            if (_languageVersion.Value < SectionVersions["Language"] && !sectionsToReset.Contains("Language")) sectionsToReset.Add("Language");
            if (_saveDataVersion.Value < SectionVersions["SaveData"] && !sectionsToReset.Contains("SaveData")) sectionsToReset.Add("SaveData");
            if (_dlcVersion.Value < SectionVersions["DLC"] && !sectionsToReset.Contains("DLC")) sectionsToReset.Add("DLC");
            if (_steamVersion.Value < SectionVersions["Steam"] && !sectionsToReset.Contains("Steam")) sectionsToReset.Add("Steam");
            if (_saveSlotVersion.Value < SectionVersions["SaveSlot"] && !sectionsToReset.Contains("SaveSlot")) sectionsToReset.Add("SaveSlot");
            if (_achievementVersion.Value < SectionVersions["Achievement"] && !sectionsToReset.Contains("Achievement")) sectionsToReset.Add("Achievement");
            if (_keyboardVersion.Value < SectionVersions["Keyboard"] && !sectionsToReset.Contains("Keyboard")) sectionsToReset.Add("Keyboard");
            if (_rimeVersion.Value < SectionVersions["Rime"] && !sectionsToReset.Contains("Rime")) sectionsToReset.Add("Rime");
            if (_playlistVersion.Value < SectionVersions["Playlist"] && !sectionsToReset.Contains("Playlist")) sectionsToReset.Add("Playlist");
            if (_uiVersion.Value < SectionVersions["UI"] && !sectionsToReset.Contains("UI")) sectionsToReset.Add("UI");
            if (_maintenanceVersion.Value < SectionVersions["Maintenance"] && !sectionsToReset.Contains("Maintenance")) sectionsToReset.Add("Maintenance");
            if (_audioVersion.Value < SectionVersions["Audio"] && !sectionsToReset.Contains("Audio")) sectionsToReset.Add("Audio");
            
            if (sectionsToReset.Count > 0)
            {
                Plugin.Logger.LogInfo($"[Config] 检测到 {sectionsToReset.Count} 个配置分区需要重置: {string.Join(", ", sectionsToReset)}");
                
                // 收集需要删除的键
                var keysToRemove = new List<ConfigDefinition>();
                foreach (var key in config.Keys)
                {
                    if (sectionsToReset.Contains(key.Section))
                    {
                        keysToRemove.Add(key);
                    }
                }
                
                // 删除过期的配置项（触发重新绑定时使用默认值）
                foreach (var key in keysToRemove)
                {
                    config.Remove(key);
                    Plugin.Logger.LogDebug($"[Config] 移除过期配置: [{key.Section}] {key.Key}");
                }
                
                // 更新版本号
                foreach (var section in sectionsToReset)
                {
                    UpdateSectionVersion(section);
                }
                
                // 保存配置文件
                config.Save();
                Plugin.Logger.LogInfo("[Config] 配置分区已重置为默认值");
            }
        }
        
        /// <summary>
        /// 更新指定分区的版本号
        /// </summary>
        private static void UpdateSectionVersion(string section)
        {
            switch (section)
            {
                case "Language": _languageVersion.Value = SectionVersions["Language"]; break;
                case "SaveData": _saveDataVersion.Value = SectionVersions["SaveData"]; break;
                case "DLC": _dlcVersion.Value = SectionVersions["DLC"]; break;
                case "Steam": _steamVersion.Value = SectionVersions["Steam"]; break;
                case "SaveSlot": _saveSlotVersion.Value = SectionVersions["SaveSlot"]; break;
                case "Achievement": _achievementVersion.Value = SectionVersions["Achievement"]; break;
                case "Keyboard": _keyboardVersion.Value = SectionVersions["Keyboard"]; break;
                case "Rime": _rimeVersion.Value = SectionVersions["Rime"]; break;
                case "Playlist": _playlistVersion.Value = SectionVersions["Playlist"]; break;
                case "UI": _uiVersion.Value = SectionVersions["UI"]; break;
                case "Maintenance": _maintenanceVersion.Value = SectionVersions["Maintenance"]; break;
            }
        }
    }
}

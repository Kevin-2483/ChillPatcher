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

            Plugin.Logger.LogInfo("配置文件已加载:");
            Plugin.Logger.LogInfo($"  - 默认语言: {DefaultLanguage.Value}");
            Plugin.Logger.LogInfo($"  - 离线用户ID: {OfflineUserId.Value}");
            Plugin.Logger.LogInfo($"  - 启用DLC: {EnableDLC.Value}");
        }
    }
}

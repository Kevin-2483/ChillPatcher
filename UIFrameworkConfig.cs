using BepInEx.Configuration;

namespace ChillPatcher
{
    public static class UIFrameworkConfig
    {
        // ========== 功能开关配置 ==========
        
        /// <summary>
        /// 是否启用100首限制破除（默认：关闭）
        /// 警告：开启可能影响存档兼容性
        /// </summary>
        public static ConfigEntry<bool> EnableUnlimitedSongs { get; private set; }
        
        /// <summary>
        /// 是否扩展音频格式支持（默认：关闭）
        /// 扩展格式：OGG, FLAC, AIFF
        /// </summary>
        public static ConfigEntry<bool> EnableExtendedFormats { get; private set; }
        
        /// <summary>
        /// 是否启用虚拟滚动（默认：开启）
        /// 虚拟滚动不影响存档，仅优化性能
        /// </summary>
        public static ConfigEntry<bool> EnableVirtualScroll { get; private set; }
        
        /// <summary>
        /// 是否显示音乐封面（默认：开启）
        /// 将播放列表按钮的图标替换为当前播放音乐的封面
        /// </summary>
        public static ConfigEntry<bool> EnableAlbumArtDisplay { get; private set; }

        /// <summary>
        /// 是否在播放列表中显示专辑分隔（默认：开启）
        /// 仅在虚拟滚动启用时有效
        /// </summary>
        public static ConfigEntry<bool> EnableAlbumSeparators { get; private set; }
        
        /// <summary>
        /// 是否启用UI重排列（默认：关闭）
        /// 重新排列主界面按钮布局
        /// </summary>
        public static ConfigEntry<bool> EnableUIRearrange { get; private set; }
        
        // ========== 高级配置 ==========
        
        /// <summary>
        /// 虚拟滚动缓冲区大小（默认：3）
        /// </summary>
        public static ConfigEntry<int> VirtualScrollBufferSize { get; private set; }
        
        public static void Initialize(ConfigFile config)
        {
            // 功能开关
            EnableUnlimitedSongs = config.Bind(
                "Features",
                "EnableUnlimitedSongs",
                false,  // 默认关闭
                "Enable unlimited song import (may affect save compatibility)"
            );
            
            EnableExtendedFormats = config.Bind(
                "Features",
                "EnableExtendedFormats",
                false,  // 默认关闭
                "Enable extended audio formats (OGG, FLAC, AIFF)"
            );
            
            EnableVirtualScroll = config.Bind(
                "Features",
                "EnableVirtualScroll",
                true,  // 默认开启，不影响存档
                "Enable virtual scrolling for better performance"
            );
            
            EnableAlbumArtDisplay = config.Bind(
                "Features",
                "EnableAlbumArtDisplay",
                true,  // 默认开启
                "Display album art on playlist toggle button"
            );

            EnableAlbumSeparators = config.Bind(
                "Features",
                "EnableAlbumSeparators",
                true,  // 默认开启
                "Display album separators in playlist (requires virtual scroll)"
            );
            
            EnableUIRearrange = config.Bind(
                "Features",
                "EnableUIRearrange",
                true,  // 默认开启
                "Rearrange main UI buttons layout"
            );
            
            // 高级配置
            VirtualScrollBufferSize = config.Bind(
                "Advanced",
                "VirtualScrollBufferSize",
                3,
                "Virtual scroll buffer size"
            );
        }
    }
}

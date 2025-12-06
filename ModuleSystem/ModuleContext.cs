using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;
using UnityEngine;

namespace ChillPatcher.ModuleSystem
{
    /// <summary>
    /// 模块上下文实现
    /// 提供给模块使用的各种服务
    /// </summary>
    public class ModuleContext : IModuleContext
    {
        private readonly string _pluginPath;
        private readonly ConfigFile _config;
        private readonly ManualLogSource _logger;
        private readonly string _moduleId;

        public ITagRegistry TagRegistry { get; }
        public IAlbumRegistry AlbumRegistry { get; }
        public IMusicRegistry MusicRegistry { get; }
        public IModuleConfigManager ConfigManager { get; }
        public IEventBus EventBus { get; }
        public ManualLogSource Logger => _logger;
        public IDefaultCoverProvider DefaultCover { get; }
        public IAudioLoader AudioLoader { get; }
        public IDependencyLoader DependencyLoader { get; }

        public ModuleContext(
            string pluginPath,
            ConfigFile config,
            ManualLogSource logger,
            string moduleId,
            ITagRegistry tagRegistry,
            IAlbumRegistry albumRegistry,
            IMusicRegistry musicRegistry,
            IEventBus eventBus,
            IDefaultCoverProvider defaultCover,
            IAudioLoader audioLoader,
            IDependencyLoader dependencyLoader)
        {
            _pluginPath = pluginPath;
            _config = config;
            _logger = logger;
            _moduleId = moduleId;

            TagRegistry = tagRegistry;
            AlbumRegistry = albumRegistry;
            MusicRegistry = musicRegistry;
            EventBus = eventBus;
            DefaultCover = defaultCover;
            AudioLoader = audioLoader;
            DependencyLoader = dependencyLoader;

            ConfigManager = new ModuleConfigManager(config, moduleId);
        }

        public string GetModuleDataPath(string moduleId)
        {
            var path = Path.Combine(_pluginPath, "Modules", moduleId, "data");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        public string GetModuleNativePath(string moduleId)
        {
            var arch = IntPtr.Size == 8 ? "x64" : "x86";
            var path = Path.Combine(_pluginPath, "Modules", moduleId, "native", arch);
            return path;
        }
    }

    /// <summary>
    /// 模块配置管理器实现
    /// 自动为模块配置添加 ID 前缀，实现配置隔离
    /// 
    /// 配置格式：
    /// - 默认 section: [Module:com.example.mymodule]
    /// - 自定义 section: [Module:com.example.mymodule:CustomSection]
    /// </summary>
    public class ModuleConfigManager : IModuleConfigManager
    {
        private const string MODULE_SECTION_PREFIX = "Module";
        
        private readonly ConfigFile _config;
        private readonly string _moduleId;
        private readonly string _defaultSection;
        private readonly Dictionary<string, object> _overrides = new Dictionary<string, object>();

        public ConfigFile Config => _config;
        public string ModuleId => _moduleId;

        public ModuleConfigManager(ConfigFile config, string moduleId)
        {
            _config = config;
            _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
            _defaultSection = GetFullSectionName(null);
        }

        /// <summary>
        /// 获取完整的 section 名称
        /// </summary>
        /// <param name="section">相对 section 名称（null 或空表示使用默认 section）</param>
        /// <returns>完整的 section 名称，格式：Module:moduleId 或 Module:moduleId:section</returns>
        public string GetFullSectionName(string section)
        {
            if (string.IsNullOrEmpty(section))
            {
                // 默认 section：使用模块 ID
                return $"{MODULE_SECTION_PREFIX}:{_moduleId}";
            }
            else
            {
                // 自定义 section：添加模块 ID 前缀
                return $"{MODULE_SECTION_PREFIX}:{_moduleId}:{section}";
            }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            var fullSection = GetFullSectionName(section);
            return _config.Bind(fullSection, key, defaultValue, description);
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            var fullSection = GetFullSectionName(section);
            return _config.Bind(fullSection, key, defaultValue, description);
        }

        public ConfigEntry<T> BindDefault<T>(string key, T defaultValue, string description)
        {
            return _config.Bind(_defaultSection, key, defaultValue, description);
        }

        public bool Override<T>(string section, string key, T value)
        {
            // Override 不添加模块前缀，用于覆盖主程序配置
            var configKey = $"{section}.{key}";
            _overrides[configKey] = value;

            // 尝试直接设置配置值
            if (_config.TryGetEntry<T>(section, key, out var entry))
            {
                entry.Value = value;
                return true;
            }

            return false;
        }

        public T GetValue<T>(string section, string key, T defaultValue = default)
        {
            var fullSection = GetFullSectionName(section);
            var configKey = $"{fullSection}.{key}";
            
            // 先检查覆盖值
            if (_overrides.TryGetValue(configKey, out var overrideValue))
            {
                return (T)overrideValue;
            }

            // 从配置文件获取
            if (_config.TryGetEntry<T>(fullSection, key, out var entry))
            {
                return entry.Value;
            }

            return defaultValue;
        }

        public T GetDefaultValue<T>(string key, T defaultValue = default)
        {
            return GetValue<T>(null, key, defaultValue);
        }

        public void SetValue<T>(string section, string key, T value)
        {
            var fullSection = GetFullSectionName(section);
            if (_config.TryGetEntry<T>(fullSection, key, out var entry))
            {
                entry.Value = value;
            }
        }

        public bool HasKey(string section, string key)
        {
            var fullSection = GetFullSectionName(section);
            return _config.TryGetEntry<object>(fullSection, key, out _);
        }

        public void Save()
        {
            _config.Save();
        }
    }
}

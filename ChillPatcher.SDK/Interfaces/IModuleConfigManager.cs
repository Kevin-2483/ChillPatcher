using BepInEx.Configuration;

namespace ChillPatcher.SDK.Interfaces
{
    /// <summary>
    /// 模块配置管理器接口
    /// 
    /// 配置隔离机制：
    /// - 每个模块的配置会自动使用模块 ID 作为 section 前缀
    /// - 例如模块 "com.chillpatcher.localfolder" 的 "RootFolder" 配置
    ///   会存储在 [Module:com.chillpatcher.localfolder] section 下
    /// - 模块调用时只需指定相对 section 名，前缀会自动添加
    /// </summary>
    public interface IModuleConfigManager
    {
        /// <summary>
        /// 模块 ID（用于配置隔离）
        /// </summary>
        string ModuleId { get; }

        /// <summary>
        /// BepInEx 配置文件实例
        /// 注意：直接使用此属性绑定的配置不会自动添加模块前缀
        /// </summary>
        ConfigFile Config { get; }

        /// <summary>
        /// 绑定配置项（自动添加模块 ID 前缀）
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="section">配置分区（相对名称，会自动添加模块前缀）</param>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="description">描述</param>
        /// <returns>配置条目</returns>
        ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description);

        /// <summary>
        /// 绑定配置项（带配置描述，自动添加模块 ID 前缀）
        /// </summary>
        ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description);

        /// <summary>
        /// 绑定配置项到默认 section（使用模块 ID 作为 section）
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <param name="description">描述</param>
        /// <returns>配置条目</returns>
        ConfigEntry<T> BindDefault<T>(string key, T defaultValue, string description);

        /// <summary>
        /// 覆盖主程序的配置项（不添加模块前缀）
        /// 注意：只有在模块有更高优先级时才会生效
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="section">配置分区</param>
        /// <param name="key">配置键</param>
        /// <param name="value">要覆盖的值</param>
        /// <returns>是否覆盖成功</returns>
        bool Override<T>(string section, string key, T value);

        /// <summary>
        /// 获取配置项的当前值（自动添加模块 ID 前缀）
        /// </summary>
        /// <typeparam name="T">配置值类型</typeparam>
        /// <param name="section">配置分区（相对名称）</param>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">如果不存在时的默认值</param>
        /// <returns>配置值</returns>
        T GetValue<T>(string section, string key, T defaultValue = default);

        /// <summary>
        /// 获取默认 section 的配置项值
        /// </summary>
        T GetDefaultValue<T>(string key, T defaultValue = default);

        /// <summary>
        /// 设置配置项的值（自动添加模块 ID 前缀）
        /// </summary>
        void SetValue<T>(string section, string key, T value);

        /// <summary>
        /// 检查配置项是否存在（自动添加模块 ID 前缀）
        /// </summary>
        bool HasKey(string section, string key);

        /// <summary>
        /// 获取完整的 section 名称（添加模块前缀后的）
        /// </summary>
        /// <param name="section">相对 section 名称</param>
        /// <returns>完整的 section 名称</returns>
        string GetFullSectionName(string section);

        /// <summary>
        /// 保存配置
        /// </summary>
        void Save();
    }
}

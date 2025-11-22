using HarmonyLib;
using NestopiSystem.Steam;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 1: 核心阻断 - SteamManager.Initialize
    /// 强制离线模式，跳过Steam初始化
    /// </summary>
    [HarmonyPatch(typeof(SteamManager), "Initialize")]
    public class SteamManager_Initialize_Patch
    {
        static bool Prefix()
        {
            Plugin.Logger.LogInfo("[ChillPatcher] SteamManager.Initialize - 跳过Steam初始化（离线模式）");
            // 什么都不做，阻止Steam初始化
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 2: 核心阻断 - SteamManager.Tick
    /// 切断心跳
    /// </summary>
    [HarmonyPatch(typeof(SteamManager), "VContainer.Unity.ITickable.Tick")]
    public class SteamManager_Tick_Patch
    {
        static bool Prefix()
        {
            // 什么都不做，阻止原方法执行
            return false;
        }
    }

    /// <summary>
    /// Patch 3: 核心阻断 - SteamManager.IsInstalledDLC
    /// 根据配置返回是否启用DLC
    /// </summary>
    [HarmonyPatch(typeof(SteamManager), "IsInstalledDLC")]
    public class SteamManager_IsInstalledDLC_Patch
    {
        static bool Prefix(ref bool __result)
        {
            __result = PluginConfig.EnableDLC.Value;
            if (__result)
            {
                Plugin.Logger.LogInfo("[ChillPatcher] IsInstalledDLC - DLC已启用");
            }
            return false; // 阻止原方法执行
        }
    }
}

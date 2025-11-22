using HarmonyLib;
using Steamworks;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch: SteamUtils.GetAppID - 防止离线模式下调用失败
    /// </summary>
    [HarmonyPatch(typeof(SteamUtils), nameof(SteamUtils.GetAppID))]
    public class SteamUtils_GetAppID_Patch
    {
        static bool Prefix(ref AppId_t __result)
        {
            __result = new AppId_t(3548580); // 使用游戏的实际 AppID
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch: SteamFriends.GetPersonaName - 防止离线模式下调用失败
    /// </summary>
    [HarmonyPatch(typeof(SteamFriends), nameof(SteamFriends.GetPersonaName))]
    public class SteamFriends_GetPersonaName_Patch
    {
        static bool Prefix(ref string __result)
        {
            __result = PluginConfig.OfflineUserId.Value;
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch: SteamUser.GetSteamID - 防止离线模式下调用失败
    /// 这个在存档路径中使用，但我们已经patch了 BulbulConstant，所以这里也防御一下
    /// </summary>
    [HarmonyPatch(typeof(SteamUser), nameof(SteamUser.GetSteamID))]
    public class SteamUser_GetSteamID_Patch
    {
        static bool Prefix(ref CSteamID __result)
        {
            // 尝试解析用户ID为数字，如果失败则使用固定值
            ulong steamId;
            if (ulong.TryParse(PluginConfig.OfflineUserId.Value, out steamId))
            {
                __result = new CSteamID(steamId);
            }
            else
            {
                // 如果不是数字ID，使用一个固定的离线ID
                __result = new CSteamID(76561198000000000UL); // 一个有效的Steam ID格式
            }
            Plugin.Logger.LogInfo($"[ChillPatcher] SteamUser.GetSteamID - 返回: {__result}");
            return false; // 阻止原方法执行
        }
    }
}

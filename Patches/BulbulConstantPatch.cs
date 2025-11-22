using HarmonyLib;
using Bulbul;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 5: 修复路径 - BulbulConstant.CreateSaveDirectoryPath (重载1)
    /// 使用配置文件中的用户ID替换 SteamUser.GetSteamID().ToString()
    /// </summary>
    [HarmonyPatch(typeof(BulbulConstant), "CreateSaveDirectoryPath", new System.Type[] { typeof(bool), typeof(string) })]
    public class BulbulConstant_CreateSaveDirectoryPath1_Patch
    {
        static bool Prefix(bool isDemo, string version, ref string __result)
        {
            string userID = PluginConfig.OfflineUserId.Value;
            __result = System.IO.Path.Combine("SaveData", isDemo ? "Demo" : "Release", version, userID);
            Plugin.Logger.LogInfo($"[ChillPatcher] CreateSaveDirectoryPath(bool, string) - 使用用户ID: {userID}, 路径: {__result}");
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 6: 修复路径 - BulbulConstant.CreateSaveDirectoryPath (重载2)
    /// 使用配置文件中的用户ID替换 SteamUser.GetSteamID().ToString()
    /// </summary>
    [HarmonyPatch(typeof(BulbulConstant), "CreateSaveDirectoryPath", new System.Type[] { typeof(string) })]
    public class BulbulConstant_CreateSaveDirectoryPath2_Patch
    {
        static bool Prefix(string versionDirectory, ref string __result)
        {
            string userID = PluginConfig.OfflineUserId.Value;
            __result = System.IO.Path.Combine("SaveData", versionDirectory, userID);
            Plugin.Logger.LogInfo($"[ChillPatcher] CreateSaveDirectoryPath(string) - 使用用户ID: {userID}, 路径: {__result}");
            return false; // 阻止原方法执行
        }
    }
}

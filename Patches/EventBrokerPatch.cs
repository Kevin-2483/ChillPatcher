using HarmonyLib;
using NestopiSystem.Steam;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 14: 移除炸弹 - SteamAchievementEventBroker 构造函数
    /// 清空异常抛出和 Steam Callback 注册
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievementEventBroker), MethodType.Constructor, new System.Type[] { typeof(SteamManager) })]
    public class SteamAchievementEventBroker_Constructor_Patch
    {
        static bool Prefix()
        {
            Plugin.Logger.LogInfo("[ChillPatcher] SteamAchievementEventBroker - 跳过构造函数，防止异常");
            // 阻止原构造函数执行，避免抛出异常和注册 Steam Callback
            return false;
        }
    }

    /// <summary>
    /// Patch 15: 移除炸弹 - SteamAchievementEventBroker.Dispose
    /// 只保留 Subject 的 Dispose，删除 Steam Callback 的 Dispose
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievementEventBroker), "Dispose")]
    public class SteamAchievementEventBroker_Dispose_Patch
    {
        static bool Prefix(SteamAchievementEventBroker __instance)
        {
            Plugin.Logger.LogInfo("[ChillPatcher] SteamAchievementEventBroker.Dispose - 安全处置");
            
            // 获取 Subject 字段并调用 Dispose
            try
            {
                var onUserStatsReceivedField = AccessTools.Field(typeof(SteamAchievementEventBroker), "onUserStatsReceived");
                var onUserStatsStoredField = AccessTools.Field(typeof(SteamAchievementEventBroker), "onUserStatsStored");
                var onAchievementStoredField = AccessTools.Field(typeof(SteamAchievementEventBroker), "onAchievementStored");

                var onUserStatsReceived = onUserStatsReceivedField?.GetValue(__instance);
                var onUserStatsStored = onUserStatsStoredField?.GetValue(__instance);
                var onAchievementStored = onAchievementStoredField?.GetValue(__instance);

                (onUserStatsReceived as System.IDisposable)?.Dispose();
                (onUserStatsStored as System.IDisposable)?.Dispose();
                (onAchievementStored as System.IDisposable)?.Dispose();
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"[ChillPatcher] Dispose Subject 时出错: {ex.Message}");
            }

            return false; // 阻止原方法执行
        }
    }
}

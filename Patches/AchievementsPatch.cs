using HarmonyLib;
using Bulbul.Achievements;
using NestopiSystem.Steam;
using System.Reflection;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 8: 哑巴成就 - SteamAchievements 构造函数
    /// 删除 achievementEventBroker 的初始化
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), MethodType.Constructor, new System.Type[] { typeof(SteamManager) })]
    public class SteamAchievements_Constructor_Patch
    {
        static void Postfix(SteamAchievements __instance)
        {
            // 将 achievementEventBroker 设置为 null
            FieldInfo field = AccessTools.Field(typeof(SteamAchievements), "achievementEventBroker");
            field.SetValue(__instance, null);
            Plugin.Logger.LogInfo("[ChillPatcher] SteamAchievements - 禁用成就事件代理");
        }
    }

    /// <summary>
    /// Patch 9: 哑巴成就 - SteamAchievements.SetProgress
    /// 直接返回，不做任何事
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), "SetProgress")]
    public class SteamAchievements_SetProgress_Patch
    {
        static bool Prefix(ref int __result)
        {
            __result = -1;
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 10: 哑巴成就 - SteamAchievements.ProgressIncrement
    /// 直接返回，不做任何事
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), "ProgressIncrement")]
    public class SteamAchievements_ProgressIncrement_Patch
    {
        static bool Prefix(ref int __result)
        {
            __result = -1;
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 11: 哑巴成就 - SteamAchievements.TryGetStat
    /// 直接返回 false
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), "TryGetStat")]
    public class SteamAchievements_TryGetStat_Patch
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 12: 哑巴成就 - SteamAchievements.TrySetStat
    /// 直接返回 false
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), "TrySetStat")]
    public class SteamAchievements_TrySetStat_Patch
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false; // 阻止原方法执行
        }
    }

    /// <summary>
    /// Patch 13: 哑巴成就 - SteamAchievements.GetAchievement
    /// 只保留创建本地缓存的代码，删除联网获取部分
    /// </summary>
    [HarmonyPatch(typeof(SteamAchievements), "GetAchievement")]
    public class SteamAchievements_GetAchievement_Patch
    {
        static bool Prefix(SteamAchievements __instance, AchievementCategory category, ref AchievementStats __result)
        {
            // 直接创建本地缓存，不从 Steam 获取
            __result = AchievementStats.Create(category, 0);
            Plugin.Logger.LogInfo($"[ChillPatcher] GetAchievement - 返回本地缓存: {category}");
            return false; // 阻止原方法执行
        }
    }
}

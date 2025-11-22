using HarmonyLib;
using Bulbul;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 4: 解除死锁 - EntryBehavior.StartAsync
    /// 通过修改 SteamManager.IsInitialized 属性绕过等待
    /// 注意：实际的 StartAsync 仍会执行，但由于 SteamManager.Initialize 被patch了，
    /// 它会直接认为Steam已初始化并继续执行
    /// </summary>
    [HarmonyPatch(typeof(EntryBehavior))]
    [HarmonyPatch("VContainer.Unity.IAsyncStartable.StartAsync")]
    public class EntryBehavior_StartAsync_Patch
    {
        static void Prefix()
        {
            Plugin.Logger.LogInfo("[ChillPatcher] EntryBehavior.StartAsync - 已通过 SteamManager patch 绕过死锁");
            // 不需要修改任何东西，因为 SteamManager.Initialize 已经被patch
            // 它会直接设置 isInitialized = false，但 IsInitialized 属性也会被patch
        }
    }

    /// <summary>
    /// Patch: 修复 SteamManager.IsInitialized 属性
    /// 让它在离线模式下返回 true，以绕过 EntryBehavior 中的等待
    /// </summary>
    [HarmonyPatch(typeof(NestopiSystem.Steam.SteamManager), "IsInitialized", MethodType.Getter)]
    public class SteamManager_IsInitialized_Patch
    {
        static bool Prefix(ref bool __result)
        {
            __result = true; // 始终返回 true，表示"已初始化"
            return false; // 阻止原方法执行
        }
    }
}

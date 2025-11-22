using HarmonyLib;
using Bulbul;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// Patch 7: 修复语言 - SteamDefaultLanguageSupplier.GetDefaultLanguage
    /// 返回配置文件中指定的语言，防止空指针
    /// </summary>
    [HarmonyPatch(typeof(SteamDefaultLanguageSupplier), "GetDefaultLanguage")]
    public class SteamDefaultLanguageSupplier_GetDefaultLanguage_Patch
    {
        static bool Prefix(ref GameLanguageType __result)
        {
            // 从配置读取语言设置
            int languageValue = PluginConfig.DefaultLanguage.Value;
            __result = (GameLanguageType)languageValue;
            
            Plugin.Logger.LogInfo($"[ChillPatcher] GetDefaultLanguage - 返回配置的语言: {__result} ({languageValue})");
            return false; // 阻止原方法执行
        }
    }
}

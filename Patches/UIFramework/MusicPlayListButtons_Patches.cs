using Bulbul;
using HarmonyLib;
using ChillPatcher.ModuleSystem.Registry;
using ChillPatcher.ModuleSystem;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// MusicPlayListButtons补丁: 根据歌曲的可删除设置控制删除按钮显示
    /// </summary>
    [HarmonyPatch(typeof(MusicPlayListButtons))]
    public class MusicPlayListButtons_Patches
    {
        /// <summary>
        /// Patch Setup方法 - 根据歌曲/模块设置控制删除按钮
        /// </summary>
        [HarmonyPatch("Setup")]
        [HarmonyPostfix]
        static void Setup_Postfix(MusicPlayListButtons __instance)
        {
            try
            {
                // 获取当前歌曲信息
                var audioInfo = __instance.AudioInfo;
                if (audioInfo == null)
                    return;
                
                // 检查是否是自定义歌曲 (来自模块)
                var musicInfo = MusicRegistry.Instance?.GetByUUID(audioInfo.UUID);
                if (musicInfo == null)
                    return;  // 不是自定义歌曲,保持原样
                
                // 判断是否可删除
                bool canDelete = false;
                
                // 1. 首先检查歌曲级别的设置
                if (musicInfo.IsDeletable.HasValue)
                {
                    canDelete = musicInfo.IsDeletable.Value;
                }
                else
                {
                    // 2. 如果歌曲没有设置，使用模块级别的设置
                    var module = ModuleLoader.Instance?.GetModule(musicInfo.ModuleId);
                    if (module != null)
                    {
                        canDelete = module.Capabilities?.CanDelete ?? false;
                    }
                }
                
                // 根据是否可删除设置按钮显示
                var removeInteractableUI = Traverse.Create(__instance)
                    .Field("removeInteractableUI")
                    .GetValue<InteractableUI>();
                
                if (removeInteractableUI != null)
                {
                    removeInteractableUI.gameObject.SetActive(canDelete);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[DeleteButton] Error: {ex}");
            }
        }
    }
}

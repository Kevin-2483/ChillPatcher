using Bulbul;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using System;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 在 MusicService.Load 之后通过 IPC 从 OmniMixPlayer 导入歌曲
    /// </summary>
    [HarmonyPatch(typeof(MusicService), "Load")]
    public static class MusicService_Load_Patch
    {
        private static bool _songsImported = false;
        private static bool _startupPending;
        private static bool _startupStarted;

        [HarmonyPostfix]
        static void Postfix(MusicService __instance)
        {
            MusicService_RemoveLimit_Patch.CurrentInstance = __instance;
            _startupPending = true;
        }

        internal static void TryStartDeferredStartup()
        {
            if (!_startupPending || _startupStarted) return;

            _startupStarted = true;
            RunDeferredStartupAsync().Forget();
        }

        private static async UniTask RunDeferredStartupAsync()
        {
            try
            {
                // MusicService.Load runs too early for the complete OmniMix
                // startup state machine. Wait for MusicUI.Setup and one Unity
                // Update so all target game services are initialized.
                await UniTask.Yield(PlayerLoopTiming.Update);

                var integration = OmniMixIntegration.Instance;
                if (!integration.IsConnected)
                {
                    var ok = await integration.ConnectAsync();
                    if (!ok)
                    {
                        Plugin.Log?.LogWarning("[OmniMix] Backend is unavailable; automatic reconnect will continue in the background");
                        return;
                    }
                }

                var replaceOnlyFirstTime = !_songsImported;
                _songsImported = true;

                Plugin.Log?.LogInfo("[OmniMix] Importing songs from backend");
                var count = await integration.ImportSongsToGame(replace: replaceOnlyFirstTime);

                Plugin.Log?.LogInfo($"[OmniMix] Imported {count} songs to game MusicService");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[OmniMix] Deferred startup failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(MusicUI), "Bulbul.IMusicListUI.Setup")]
    public static class MusicUI_OmniMixStartup_Patch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            MusicService_Load_Patch.TryStartDeferredStartup();
        }
    }
}

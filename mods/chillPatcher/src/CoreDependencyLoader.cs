using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;

namespace ChillPatcher
{
    public static class CoreDependencyLoader
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libFilename);

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string libFilename, IntPtr file, uint flags);

        private const uint LoadLibrarySearchSystem32 = 0x00000800;

        public static void WarmUpSteamOverlayLoader(BepInEx.Logging.ManualLogSource log)
        {
            // Force the API-set loader path to initialize before Steam Overlay
            // lazily enters it from Mono startup. This avoids an early native
            // loader crash observed when gameoverlayrenderer64.dll is present.
            var handle = LoadLibraryEx(
                "api-ms-win-core-processthreads-l1-1-2",
                IntPtr.Zero,
                LoadLibrarySearchSystem32);

            if (handle == IntPtr.Zero)
            {
                log.LogWarning($"[Core] System loader warm-up failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            log.LogDebug("[Core] System loader warm-up completed");
        }

        public static void EnsureDependencies(BepInEx.Logging.ManualLogSource log)
        {
            try
            {
                // 1. 获取路径: BepInEx/plugins/ChillPatcher/native/x64
                var pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                var arch = IntPtr.Size == 8 ? "x64" : "x86"; // 基本上都是 x64
                var nativeDir = Path.Combine(pluginDir, "native", arch);

                // 2. 必须按顺序加载的依赖文件（VC++ 运行时 + 核心原生插件）
                // 注意: SQLite.Interop.dll 由 System.Data.SQLite 自行加载
                string[] libs = {
                    "vcruntime140.dll",
                    "vcruntime140_1.dll",
                    "msvcp140.dll",
                    "concrt140.dll",
                    "OmniAudioDecoder.dll",  // Rust Symphonia, 替代 ChillFlacDecoder + ChillAudioDecoder
                    "ChillSmtcBridge.dll",
                    "ChillEsbuildBridge.dll",
                    "puerts.dll"
                };

                foreach (var lib in libs)
                {
                    string path = Path.Combine(nativeDir, lib);
                    if (File.Exists(path))
                    {
                        var handle = LoadLibrary(path);
                        if (handle != IntPtr.Zero)
                            log.LogInfo($"[Core] 已预加载依赖: {lib}");
                        else
                            log.LogWarning($"[Core] 加载 {lib} 返回 0 (可能系统已存在，非致命错误)");
                    }
                    else
                    {
                        // 这是一个严重警告，提醒您发版时漏文件了
                        log.LogError($"[Core] 缺失依赖文件: {path}");
                    }
                }

                string optionalOmniPcm = Path.Combine(nativeDir, "OmniPcmShared.dll");
                if (File.Exists(optionalOmniPcm))
                {
                    var handle = LoadLibrary(optionalOmniPcm);
                    if (handle != IntPtr.Zero)
                        log.LogInfo("[Core] Preloaded optional native SDK: OmniPcmShared.dll");
                    else
                        log.LogWarning("[Core] Optional native SDK load returned 0: OmniPcmShared.dll");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"[Core] 依赖加载器异常: {ex}");
            }
        }
    }
}

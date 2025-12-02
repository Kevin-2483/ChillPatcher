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

        public static void EnsureDependencies(BepInEx.Logging.ManualLogSource log)
        {
            try
            {
                // 1. 获取路径: BepInEx/plugins/ChillPatcher/native/x64
                var pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                var arch = IntPtr.Size == 8 ? "x64" : "x86"; // 基本上都是 x64
                var nativeDir = Path.Combine(pluginDir, "native", arch);

                // 2. 必须按顺序加载的 4 个“救命”文件
                string[] libs = {
                    "vcruntime140.dll",
                    "vcruntime140_1.dll", // <-- 重点文件
                    "msvcp140.dll",
                    "concrt140.dll",
                    "SQLite.Interop.dll",
                    "ChillFlacDecoder.dll",
                    "ChillSmtcBridge.dll"
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
            }
            catch (Exception ex)
            {
                log.LogError($"[Core] 依赖加载器异常: {ex}");
            }
        }
    }
}
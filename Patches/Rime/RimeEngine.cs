using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;  // 添加此行用于 Marshal

namespace ChillPatcher.Rime
{
    /// <summary>
    /// Rime引擎封装类
    /// </summary>
    public class RimeEngine : IDisposable
    {
        private ulong _sessionId;
        private bool _initialized;
        private bool _disposed;

        public ulong SessionId => _sessionId;
        public bool IsInitialized => _initialized;

        public void Initialize(string sharedDataDir, string userDataDir, string appName = "rime.chill")
        {
            if (_initialized)
                throw new InvalidOperationException("Rime引擎已经初始化");

            Directory.CreateDirectory(sharedDataDir);
            Directory.CreateDirectory(userDataDir);

            // 创建日志目录
            string logDir = Path.Combine(userDataDir, "logs");
            Directory.CreateDirectory(logDir);
            
            // 创建 OpenCC 数据目录（在 shared 目录下）
            string openccDir = Path.Combine(sharedDataDir, "opencc");
            Directory.CreateDirectory(openccDir);

            var traits = RimeApi.RimeTraits.Create();
            traits.SharedDataDir = sharedDataDir;
            traits.UserDataDir = userDataDir;
            traits.AppName = appName;
            traits.DistributionName = "ChillPatcher";
            traits.DistributionCodeName = "chill";
            traits.DistributionVersion = "1.0.0";
            traits.LogDir = logDir;           // 设置日志目录
            traits.MinLogLevel = 2;           // 0=INFO, 1=WARNING, 2=ERROR, 3=FATAL
            traits.PrebuiltDataDir = sharedDataDir;  // OpenCC 从 shared 目录下查找

            RimeApi.RimeSetup(ref traits);
            
            // 初始化部署器,加载部署模块
            Plugin.Logger.LogInfo("[Rime] 初始化部署器...");
            RimeApi.RimeDeployerInitialize(ref traits);
            
            // 执行部署,构建用户数据
            Plugin.Logger.LogInfo("[Rime] 开始部署工作空间...");
            if (RimeApi.RimeDeployWorkspace())
            {
                Plugin.Logger.LogInfo("[Rime] 工作空间部署成功");
            }
            else
            {
                Plugin.Logger.LogWarning("[Rime] 工作空间部署失败或已是最新");
            }
            
            // 初始化Rime引擎
            Plugin.Logger.LogInfo("[Rime] 初始化引擎...");
            RimeApi.RimeInitialize(ref traits);
            RimeApi.RimeJoinMaintenanceThread();

            _sessionId = RimeApi.RimeCreateSession();
            if (_sessionId == 0)
                throw new Exception("创建Rime Session失败");

            // 检查状态
            var status = RimeApi.RimeStatus.Create();
            if (RimeApi.RimeGetStatus(_sessionId, ref status))
            {
                try
                {
                    string schemaId = status.SchemaId != IntPtr.Zero ? Marshal.PtrToStringAnsi(status.SchemaId) : "null";
                    string schemaName = status.SchemaName != IntPtr.Zero ? Marshal.PtrToStringAnsi(status.SchemaName) : "null";
                    Plugin.Logger.LogInfo($"[Rime] 初始化状态检查成功 - Schema: {schemaId}/{schemaName}, ASCII: {status.IsAsciiMode}, Composing: {status.IsComposing}");
                }
                finally
                {
                    if (status.SchemaId != IntPtr.Zero || status.SchemaName != IntPtr.Zero)
                    {
                        RimeApi.RimeFreeStatus(ref status);
                    }
                }
            }
            else
            {
                Plugin.Logger.LogWarning("[Rime] 初始化状态检查失败");
            }

            _initialized = true;
            Plugin.Logger.LogInfo($"[Rime] 引擎初始化成功, SessionID={_sessionId}");
            Plugin.Logger.LogInfo($"[Rime] 日志目录: {logDir}");
        }

        public bool ProcessKey(int keyCode, int modifiers = 0)
        {
            if (!_initialized) return false;
            return RimeApi.RimeProcessKey(_sessionId, keyCode, modifiers);
        }

        public RimeContextInfo GetContext()
        {
            if (!_initialized) return null;

            // 初始化 context 结构体 (RIME_STRUCT_INIT)
            var context = RimeApi.RimeContext.Create();
            
            if (!RimeApi.RimeGetContext(_sessionId, ref context))
            {
                return null; // 静默失败,避免每帧日志刷屏
            }

            try
            {
                var info = new RimeContextInfo
                {
                    // 手动 marshal IntPtr → string
                    Preedit = context.Composition.Preedit != IntPtr.Zero 
                        ? Marshal.PtrToStringAnsi(context.Composition.Preedit) ?? string.Empty 
                        : string.Empty,
                    CursorPos = context.Composition.CursorPos,
                    HighlightedIndex = context.Menu.HighlightedCandidateIndex,
                    Candidates = new List<CandidateInfo>()
                };

                var candidates = RimeApi.GetCandidates(
                    context.Menu.Candidates,
                    context.Menu.NumCandidates);

                foreach (var candidate in candidates)
                {
                    info.Candidates.Add(new CandidateInfo
                    {
                        Text = candidate.Text ?? string.Empty,
                        Comment = candidate.Comment ?? string.Empty
                    });
                }

                return info;
            }
            finally
            {
                // 正确释放 context 内存
                RimeApi.RimeFreeContext(ref context);
            }
        }

        public void SetOption(string option, bool value)
        {
            if (!_initialized) return;
            RimeApi.RimeSetOption(_sessionId, option, value);
        }

        public bool GetOption(string option)
        {
            if (!_initialized) return false;
            return RimeApi.RimeGetOption(_sessionId, option);
        }

        public void ToggleAsciiMode()
        {
            if (!_initialized) return;
            bool current = GetOption("ascii_mode");
            SetOption("ascii_mode", !current);
            Plugin.Logger.LogInfo($"[Rime] ASCII 模式: {!current}");
        }

        public string GetCommit()
        {
            if (!_initialized) return null;

            var commit = RimeApi.RimeCommit.Create();
            if (!RimeApi.RimeGetCommit(_sessionId, ref commit))
                return null;

            try
            {
                // 手动 marshal IntPtr → string
                if (commit.Text == IntPtr.Zero)
                    return null;
                
                return Marshal.PtrToStringAnsi(commit.Text);
            }
            finally
            {
                // 正确释放内存 (librime 用 new char[] 分配,用 delete[] 释放)
                RimeApi.RimeFreeCommit(ref commit);
            }
        }

        public void ClearComposition()
        {
            if (!_initialized) return;
            RimeApi.RimeClearComposition(_sessionId);
        }

        public bool SelectCandidate(int index)
        {
            if (!_initialized) return false;
            return RimeApi.RimeSelectCandidateOnCurrentPage(_sessionId, (nuint)index);
        }

        /// <summary>
        /// 重新部署并重启会话(F6热重载配置)
        /// </summary>
        public void Redeploy()
        {
            if (!_initialized)
            {
                Plugin.Logger.LogWarning("[Rime] 引擎未初始化,无法重新部署");
                return;
            }

            Plugin.Logger.LogInfo("[Rime] 开始重新部署...");

            // 1. 销毁当前会话
            if (_sessionId != 0)
            {
                RimeApi.RimeDestroySession(_sessionId);
                _sessionId = 0;
            }

            // 2. 重新部署(重新编译schema)
            if (RimeApi.RimeDeployWorkspace())
            {
                Plugin.Logger.LogInfo("[Rime] 重新部署成功");
            }
            else
            {
                Plugin.Logger.LogWarning("[Rime] 重新部署失败");
            }

            // 3. 重新创建会话
            _sessionId = RimeApi.RimeCreateSession();
            if (_sessionId == 0)
            {
                Plugin.Logger.LogError("[Rime] 重新创建Session失败");
                return;
            }

            // 4. 重新选择schema
            if (!RimeApi.RimeSelectSchema(_sessionId, "luna_pinyin"))
            {
                Plugin.Logger.LogWarning("[Rime] 重新选择schema失败");
            }

            // 5. 重新设置选项
            RimeApi.RimeSetOption(_sessionId, "ascii_mode", false);

            Plugin.Logger.LogInfo("[Rime] 重新部署完成,会话已重启");
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_initialized)
            {
                if (_sessionId != 0)
                {
                    RimeApi.RimeDestroySession(_sessionId);
                    _sessionId = 0;
                }

                RimeApi.RimeFinalize();
                _initialized = false;
                Plugin.Logger.LogInfo("[Rime] 引擎已释放");
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// 上下文信息
    /// </summary>
    public class RimeContextInfo
    {
        public string Preedit { get; set; }
        public int CursorPos { get; set; }
        public int HighlightedIndex { get; set; }
        public List<CandidateInfo> Candidates { get; set; }

        /// <summary>
        /// 生成在preedit区域显示的文本
        /// 格式: "preedit [候选词1₁ 候选词2₂ 候选词3₃]"
        /// 选中的候选用上标,未选中用下标
        /// </summary>
        public string GetPreeditWithCandidates()
        {
            if (string.IsNullOrEmpty(Preedit))
                return string.Empty;

            if (Candidates == null || Candidates.Count == 0)
                return Preedit;

            var result = Preedit + " [";

            for (int i = 0; i < Math.Min(Candidates.Count, 9); i++)
            {
                if (i > 0) result += " "; // 候选词之间添加空格

                var candidate = Candidates[i];
                result += candidate.Text;
                
                // 选中的用上标,未选中用下标
                bool isHighlighted = (i == HighlightedIndex);
                result += isHighlighted ? GetSuperscriptNumber(i + 1) : GetSubscriptNumber(i + 1);

                if (!string.IsNullOrEmpty(candidate.Comment))
                    result += $"({candidate.Comment})";
            }

            result += "]";
            return result;
        }

        private static string GetSuperscriptNumber(int num)
        {
            return num switch
            {
                1 => "¹",
                2 => "²",
                3 => "³",
                4 => "⁴",
                5 => "⁵",
                6 => "⁶",
                7 => "⁷",
                8 => "⁸",
                9 => "⁹",
                _ => num.ToString()
            };
        }

        private static string GetSubscriptNumber(int num)
        {
            return num switch
            {
                1 => "₁",
                2 => "₂",
                3 => "₃",
                4 => "₄",
                5 => "₅",
                6 => "₆",
                7 => "₇",
                8 => "₈",
                9 => "₉",
                _ => num.ToString()
            };
        }
    }

    /// <summary>
    /// 候选词信息
    /// </summary>
    public class CandidateInfo
    {
        public string Text { get; set; }
        public string Comment { get; set; }
    }
}

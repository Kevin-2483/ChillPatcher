using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using Bulbul;
using Cysharp.Threading.Tasks;
using NestopiSystem.DIContainers;
using R3;
using UnityEngine;

namespace ChillPatcher.Integration
{
    /// <summary>
    /// 游戏核心 API 服务：封装番茄钟、经验值与等级读写，以及事件转发。
    /// </summary>
    public sealed class GameApiService : IDisposable
    {
        private readonly ManualLogSource _logger;
        private CompositeDisposable _subscriptions = new CompositeDisposable();
        private bool _eventsSubscribed;

        public static GameApiService Instance { get; private set; }

        public event Action<string, object> OnGameEvent;

        public GameApiService(ManualLogSource logger)
        {
            _logger = logger;
            Instance = this;
        }

        public bool ensureEventBridge()
        {
            if (_eventsSubscribed) return true;

            var pomodoro = ResolvePomodoroService();
            if (pomodoro == null)
            {
                return false;
            }

            try
            {
                pomodoro.OnStartPomodoro.Subscribe(_ => Emit("pomodoroStart", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnPlayPomodoro.Subscribe(type => Emit("pomodoroPlay", new Dictionary<string, object>
                {
                    ["type"] = type.ToString(),
                    ["state"] = getPomodoroStateObject()
                })).AddTo(_subscriptions);
                pomodoro.OnPause.Subscribe(_ => Emit("pomodoroPause", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnUnpause.Subscribe(type => Emit("pomodoroUnpause", new Dictionary<string, object>
                {
                    ["type"] = type.ToString(),
                    ["state"] = getPomodoroStateObject()
                })).AddTo(_subscriptions);
                pomodoro.OnUpdatePomodoro.Subscribe(info => Emit("pomodoroProgress", new Dictionary<string, object>
                {
                    ["type"] = info.Item1.ToString(),
                    ["remainingSeconds"] = info.Item2.TotalSeconds,
                    ["totalSeconds"] = info.Item3.TotalSeconds,
                    ["state"] = getPomodoroStateObject()
                })).AddTo(_subscriptions);
                pomodoro.OnStartWork.Subscribe(_ => Emit("pomodoroWorkStart", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnEndWork.Subscribe(_ => Emit("pomodoroWorkEnd", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnStartBreak.Subscribe(_ => Emit("pomodoroBreakStart", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnEndBreak.Subscribe(_ => Emit("pomodoroBreakEnd", getPomodoroStateObject())).AddTo(_subscriptions);
                pomodoro.OnCompletePomodoro.Subscribe(type => Emit("pomodoroComplete", new Dictionary<string, object>
                {
                    ["type"] = type.ToString(),
                    ["state"] = getPomodoroStateObject()
                })).AddTo(_subscriptions);
                // The current game version removed the legacy work-hour and
                // pre-reward observables, so those legacy events are no longer emitted.

                var levelService = ResolvePlayerLevelService();
                if (levelService != null)
                {
                    levelService.OnAddExp.Subscribe(exp => Emit("levelAddExp", new Dictionary<string, object>
                    {
                        ["exp"] = exp
                    })).AddTo(_subscriptions);
                    levelService.OnAddedExp.Subscribe(_ => Emit("levelAddedExp", getPlayerProgressObject())).AddTo(_subscriptions);
                }

                var dateService = ResolveDateService();
                if (dateService != null)
                {
                    dateService.OnChangeTime.Subscribe(_ => Emit("gameClockTick", getGameClockObject())).AddTo(_subscriptions);
                    dateService.OnChangeDate.Subscribe(_ => Emit("gameDateChanged", getGameClockObject())).AddTo(_subscriptions);
                }

                _eventsSubscribed = true;
                EmitPomodoroStateChanged();
                EmitPlayerProgressChanged();
                Emit("gameClockTick", getGameClockObject());
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GameApiService] Failed to subscribe events: {ex.Message}");
                return false;
            }
        }

        public bool startPomodoro()
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.StartPomodoro();
            EmitPomodoroStateChanged();
            return true;
        }

        public bool togglePomodoroPause()
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.PlayOrPausePomodoroTimer();
            EmitPomodoroStateChanged();
            return true;
        }

        public bool skipPomodoroPhase()
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.SkipTimer();
            EmitPomodoroStateChanged();
            return true;
        }

        public bool resetPomodoro()
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.ResetTimer();
            EmitPomodoroStateChanged();
            return true;
        }

        public bool completePomodoroNow()
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.CompletePomodoroTimer().Forget();
            EmitPomodoroStateChanged();
            return true;
        }

        public bool moveAheadPomodoro(float seconds)
        {
            if (seconds <= 0f) return false;
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.MoveAheadTimer(seconds);
            EmitPomodoroStateChanged();
            return true;
        }

        public bool setWorkMinutes(int minutes)
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.SetWorkMinutes(minutes);
            EmitPomodoroStateChanged();
            return true;
        }

        public bool setBreakMinutes(int minutes)
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.SetBreakMinutes(minutes);
            EmitPomodoroStateChanged();
            return true;
        }

        public bool setLoopCount(int loopCount)
        {
            var svc = ResolvePomodoroService();
            if (svc == null) return false;
            svc.SetLoopCount(loopCount);
            EmitPomodoroStateChanged();
            return true;
        }

        public object getPomodoroStateObject()
        {
            var save = SaveDataManager.Instance;
            if (save == null)
            {
                return new Dictionary<string, object>
                {
                    ["available"] = false
                };
            }

            var svc = ResolvePomodoroService();
            var loopCurrent = svc?.CurrentLoopCount?.CurrentValue ?? 0;
            var isPaused = IsPomodoroPaused(svc);
            var isTimerActive = svc?.IsTimerRunning() ?? false;

            return new Dictionary<string, object>
            {
                ["available"] = svc != null,
                ["type"] = svc?.CurrentPomodoroType.ToString() ?? "Unknown",
                ["isRunning"] = isTimerActive && !isPaused,
                ["isPaused"] = isPaused,
                ["isTimerActive"] = isTimerActive,
                ["isWorking"] = svc?.IsCurrentWorking() ?? false,
                ["isResting"] = svc?.IsCurrentResting() ?? false,
                ["loopCurrent"] = loopCurrent,
                ["loopTotal"] = save.PomodoroData?.LoopCount?.Value ?? 0,
                ["workMinutes"] = save.PomodoroData?.WorkMinutes?.Value ?? 0,
                ["breakMinutes"] = save.PomodoroData?.BreakMinutes?.Value ?? 0,
                ["currentWorkSeconds"] = save.PlayerData?.CurrentWorkSeconds ?? 0d,
                ["totalWorkSeconds"] = save.PlayerData?.PomodoroTotalWorkSeconds ?? 0d,
                ["lastWorkStartTimeSeconds"] = svc?.LastWorkStartTimeSeconds ?? float.MinValue,
                ["lastWorkEndTimeSeconds"] = svc?.LastWorkEndTimeSeconds ?? float.MinValue,
                ["lastPomodoroTotalWorkHours"] = 0f,
                ["isLastFinishedMidway"] = svc?.IsLastPomodoroFinishedMidway ?? false
            };
        }

        public object getPlayerProgressObject()
        {
            var save = SaveDataManager.Instance;
            if (save == null)
            {
                return new Dictionary<string, object>
                {
                    ["available"] = false
                };
            }

            var level = LevelData.GetCurrentLevelData();
            return new Dictionary<string, object>
            {
                ["available"] = level != null,
                ["level"] = level?.CurrentLevel ?? 0,
                ["exp"] = level?.CurrentExp ?? 0f,
                ["nextLevelExp"] = level?.NextLevelNecessaryExp ?? 0f,
                ["currentWorkSeconds"] = save.PlayerData?.CurrentWorkSeconds ?? 0d,
                ["totalWorkSeconds"] = save.PlayerData?.PomodoroTotalWorkSeconds ?? 0d,
                ["lastStoryUnlockWorkSeconds"] = save.PlayerData?.LastStoryUnlockWorkSeconds ?? 0d
            };
        }

        public object getGameClockObject()
        {
            var now = DateTime.Now;
            var dateText = BuildDateText(now);
            var timeText = BuildTimeText(now);
            var amPmText = BuildAmPmText(now);

            return new Dictionary<string, object>
            {
                ["available"] = true,
                ["unixMs"] = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
                ["date"] = dateText,
                ["time"] = timeText,
                ["amPm"] = amPmText,
                ["dateTime"] = string.IsNullOrEmpty(amPmText)
                    ? $"{dateText} {timeText}"
                    : $"{dateText} {timeText} {amPmText}"
            };
        }

        public bool addExp(float exp)
        {
            if (exp <= 0f) return false;

            var facility = UnityEngine.Object.FindObjectOfType<FacilityPlayerLevel>();
            if (facility != null)
            {
                facility.AddExp(exp);
                return true;
            }

            var save = SaveDataManager.Instance;
            var level = LevelData.GetCurrentLevelData();
            if (save == null || level == null) return false;

            level.AddExp(exp);
            var master = ResolveMasterDataLoader();
            if (master != null)
            {
                while (level.NextLevelNecessaryExp > 0f && level.CurrentExp >= level.NextLevelNecessaryExp)
                {
                    level.LevelUp(master);
                    save.ScenarioProgressData.UpdateNextMainEpisode(master);
                }
            }

            SaveCurrentLevelData();
            EmitPlayerProgressChanged();
            return true;
        }

        public bool setLevel(int level)
        {
            if (level < 1) level = 1;
            var levelData = LevelData.GetCurrentLevelData();
            if (levelData == null) return false;

            levelData.SetLevel(level);
            var master = ResolveMasterDataLoader();
            if (master != null)
            {
                levelData.SetupNextLevelNecessaryExp(master);
            }

            SaveCurrentLevelData();
            Emit("levelChanged", getPlayerProgressObject());
            EmitPlayerProgressChanged();
            return true;
        }

        public bool setCurrentExp(float exp)
        {
            if (exp < 0f) exp = 0f;
            var levelData = LevelData.GetCurrentLevelData();
            if (levelData == null) return false;

            levelData.AddExp(-levelData.CurrentExp);
            levelData.AddExp(exp);
            SaveCurrentLevelData();
            Emit("expChanged", getPlayerProgressObject());
            EmitPlayerProgressChanged();
            return true;
        }

        public bool setCurrentWorkSeconds(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            var save = SaveDataManager.Instance;
            if (save == null || save.PlayerData == null) return false;

            save.PlayerData.CurrentWorkSeconds = seconds;
            save.SavePlayerData();
            Emit("workSecondsChanged", getPlayerProgressObject());
            EmitPlayerProgressChanged();
            return true;
        }

        public bool setTotalWorkSeconds(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            var save = SaveDataManager.Instance;
            if (save == null || save.PlayerData == null) return false;

            save.PlayerData.PomodoroTotalWorkSeconds = seconds;
            save.SavePlayerData();
            Emit("totalWorkSecondsChanged", getPlayerProgressObject());
            EmitPlayerProgressChanged();
            return true;
        }

        public bool savePomodoroData()
        {
            var save = SaveDataManager.Instance;
            if (save == null) return false;
            save.SavePomodoroData();
            return true;
        }

        public bool savePlayerData()
        {
            var save = SaveDataManager.Instance;
            if (save == null) return false;
            SaveCurrentLevelData();
            return true;
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _eventsSubscribed = false;
        }

        /// <summary>
        /// 清理旧的事件订阅，允许下次 ensureEventBridge() 重新绑定。
        /// 用于场景重载（存档切换）后重置状态。
        /// </summary>
        public void ResetEventBridge()
        {
            _subscriptions.Dispose();
            _subscriptions = new CompositeDisposable();
            _eventsSubscribed = false;
        }

        private void SaveCurrentLevelData()
        {
            var save = SaveDataManager.Instance;
            if (save == null) return;

            if (save.CollaborationSaveData.CurrentType.Value == SpecialService.CollaborationType.None)
            {
                save.SavePlayerData();
            }
            else
            {
                save.SaveCollaborationData();
            }
        }

        private void Emit(string eventName, object data)
        {
            try
            {
                OnGameEvent?.Invoke(eventName, data);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GameApiService] Emit failed ({eventName}): {ex.Message}");
            }
        }

        private static PomodoroService ResolvePomodoroService()
        {
            try
            {
                return RoomLifetimeScope.Resolve<PomodoroService>();
            }
            catch
            {
                return null;
            }
        }

        private static PlayerLevelService ResolvePlayerLevelService()
        {
            try
            {
                return RoomLifetimeScope.Resolve<PlayerLevelService>();
            }
            catch
            {
                return null;
            }
        }

        private static MasterDataLoader ResolveMasterDataLoader()
        {
            try
            {
                return RoomLifetimeScope.Resolve<MasterDataLoader>();
            }
            catch
            {
                return null;
            }
        }

        private static DateService ResolveDateService()
        {
            try
            {
                return RoomLifetimeScope.Resolve<DateService>();
            }
            catch
            {
                return null;
            }
        }

        private void EmitPomodoroStateChanged()
        {
            Emit("pomodoroStateChanged", getPomodoroStateObject());
        }

        private void EmitPlayerProgressChanged()
        {
            Emit("playerProgressChanged", getPlayerProgressObject());
        }

        private static LanguageSupplier ResolveLanguageSupplier()
        {
            try
            {
                return RoomLifetimeScope.Resolve<LanguageSupplier>();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildDateText(DateTime now)
        {
            var language = ResolveLanguageSupplier()?.Get() ?? GameLanguageType.English;
            switch (language)
            {
                case GameLanguageType.Japanese:
                    return now.ToString("yyyy/MM/dd(ddd)", new CultureInfo("ja-JP"));
                case GameLanguageType.ChineseSimplified:
                    return now.ToString("yyyy/MM/dd(ddd)", new CultureInfo("zh-CN"));
                case GameLanguageType.ChineseTraditional:
                    return now.ToString("yyyy/MM/dd(ddd)", new CultureInfo("zh-TW"));
                case GameLanguageType.Portuguese:
                    return now.ToString("dd/MM/yyyy(ddd)", new CultureInfo("pt-BR"));
                case GameLanguageType.Korean:
                    return now.ToString("yyyy/MM/dd(ddd)", new CultureInfo("ko-KR"));
                case GameLanguageType.Russian:
                    return now.ToString("dd/MM/yyyy(ddd)", new CultureInfo("ru-RU"));
                default:
                    return now.ToString("ddd, MMM dd, yyyy", CultureInfo.CreateSpecificCulture("en-US"));
            }
        }

        private static string BuildTimeText(DateTime now)
        {
            var save = SaveDataManager.Instance;
            var format = save?.SettingData?.TimeFormat?.Value ?? TimeFormatType.All;
            return format == TimeFormatType.AMPM
                ? now.ToString("hh:mm")
                : now.ToString("HH:mm");
        }

        private static string BuildAmPmText(DateTime now)
        {
            var save = SaveDataManager.Instance;
            var format = save?.SettingData?.TimeFormat?.Value ?? TimeFormatType.All;
            if (format != TimeFormatType.AMPM)
            {
                return string.Empty;
            }

            return now.ToString("tt", CultureInfo.CreateSpecificCulture("en-US"));
        }

        private static readonly FieldInfo PomodoroMainStateField =
            typeof(PomodoroService).GetField("_mainState", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool IsPomodoroPaused(PomodoroService service)
        {
            if (service == null || PomodoroMainStateField == null)
            {
                return false;
            }

            try
            {
                var mainState = PomodoroMainStateField.GetValue(service);
                return string.Equals(mainState?.ToString(), "Pause", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}

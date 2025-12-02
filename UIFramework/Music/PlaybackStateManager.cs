using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Bulbul;
using HarmonyLib;
using R3;
using ChillPatcher.Patches.UIFramework;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 播放状态数据
    /// </summary>
    [Serializable]
    public class PlaybackState
    {
        /// <summary>
        /// 当前播放歌曲的UUID
        /// </summary>
        public string CurrentSongUUID;

        /// <summary>
        /// 当前选中的AudioTag值
        /// </summary>
        public int CurrentAudioTagValue;

        /// <summary>
        /// 是否随机播放
        /// </summary>
        public bool IsShuffle;

        /// <summary>
        /// 是否单曲循环
        /// </summary>
        public bool IsRepeatOne;

        /// <summary>
        /// 保存时间
        /// </summary>
        public string SavedAt;

        /// <summary>
        /// 播放队列（UUID列表）
        /// </summary>
        public List<string> QueueUUIDs;

        /// <summary>
        /// 播放历史（UUID列表，最近播放的在前）
        /// </summary>
        public List<string> HistoryUUIDs;

        /// <summary>
        /// 播放列表位置
        /// </summary>
        public int PlaylistPosition;

        /// <summary>
        /// 历史位置指针
        /// </summary>
        public int HistoryPosition;

        /// <summary>
        /// 扩展步数
        /// </summary>
        public int ExtendedSteps;
    }

    /// <summary>
    /// 播放状态管理器 - 保存和恢复播放状态
    /// </summary>
    public class PlaybackStateManager
    {
        private static readonly BepInEx.Logging.ManualLogSource Logger = 
            BepInEx.Logging.Logger.CreateLogSource("PlaybackStateManager");

        private static PlaybackStateManager _instance;
        public static PlaybackStateManager Instance => _instance ??= new PlaybackStateManager();

        // 状态文件路径
        private readonly string _stateFilePath;

        // 当前状态
        private PlaybackState _currentState;

        // 是否已加载
        private bool _loaded = false;

        // 事件订阅
        private IDisposable _audioTagSubscription;
        private IDisposable _musicChangeSubscription;

        private PlaybackStateManager()
        {
            // 状态文件保存在游戏存档目录的 ChillPatcher 子目录
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace("Local", "LocalLow"),
                "Nestopi",
                "Chill With You",
                "ChillPatcher"
            );

            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            _stateFilePath = Path.Combine(baseDir, "playback_state.json");
            Logger.LogInfo($"Playback state file: {_stateFilePath}");
        }

        /// <summary>
        /// 初始化状态管理器
        /// </summary>
        public void Initialize()
        {
            if (_loaded) return;

            // 加载保存的状态
            LoadState();
            _loaded = true;

            Logger.LogInfo("PlaybackStateManager initialized");
        }

        /// <summary>
        /// 订阅事件以监听状态变化
        /// </summary>
        public void SubscribeToEvents(MusicService musicService)
        {
            if (musicService == null)
            {
                Logger.LogWarning("MusicService is null, cannot subscribe to events");
                return;
            }

            // 订阅 AudioTag 变化
            _audioTagSubscription?.Dispose();
            _audioTagSubscription = SaveDataManager.Instance.MusicSetting.CurrentAudioTag
                .Subscribe(OnAudioTagChanged);

            // 订阅音乐变化
            _musicChangeSubscription?.Dispose();
            _musicChangeSubscription = musicService.OnChangeMusic
                .Subscribe(_ => OnMusicChanged(musicService));

            Logger.LogInfo("Subscribed to music events");
        }

        /// <summary>
        /// AudioTag变化时保存状态
        /// </summary>
        private void OnAudioTagChanged(AudioTag tag)
        {
            if (_currentState == null)
            {
                _currentState = new PlaybackState();
            }

            _currentState.CurrentAudioTagValue = (int)tag;
            SaveState();
        }

        /// <summary>
        /// 音乐变化时保存状态
        /// </summary>
        private void OnMusicChanged(MusicService musicService)
        {
            if (_currentState == null)
            {
                _currentState = new PlaybackState();
            }

            if (musicService.PlayingMusic != null)
            {
                _currentState.CurrentSongUUID = musicService.PlayingMusic.UUID;
            }

            _currentState.IsShuffle = musicService.IsShuffle;
            _currentState.IsRepeatOne = musicService.IsRepeatOneMusic;

            // 保存队列和历史
            SaveQueueAndHistory();

            SaveState();
        }

        /// <summary>
        /// 保存队列和历史到当前状态
        /// </summary>
        private void SaveQueueAndHistory()
        {
            var queueManager = PlayQueueManager.Instance;
            if (queueManager == null) return;

            _currentState.QueueUUIDs = queueManager.Queue.Select(a => a.UUID).ToList();
            _currentState.HistoryUUIDs = queueManager.History.Select(a => a.UUID).ToList();
            _currentState.PlaylistPosition = queueManager.PlaylistPosition;
            _currentState.HistoryPosition = queueManager.HistoryPosition;
            _currentState.ExtendedSteps = queueManager.ExtendedSteps;
        }

        /// <summary>
        /// 加载保存的状态
        /// </summary>
        private void LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath);
                    _currentState = DeserializeState(json);
                    Logger.LogInfo($"Loaded playback state: Tag={_currentState.CurrentAudioTagValue}, Song={_currentState.CurrentSongUUID}");
                }
                else
                {
                    _currentState = new PlaybackState();
                    Logger.LogInfo("No saved playback state found, using defaults");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load playback state: {ex.Message}");
                _currentState = new PlaybackState();
            }
        }

        /// <summary>
        /// 保存当前状态
        /// </summary>
        private void SaveState()
        {
            try
            {
                _currentState.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var json = SerializeState(_currentState);
                File.WriteAllText(_stateFilePath, json);
                Logger.LogDebug($"Saved playback state: Tag={_currentState.CurrentAudioTagValue}, Song={_currentState.CurrentSongUUID}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to save playback state: {ex.Message}");
            }
        }

        /// <summary>
        /// 简单的 JSON 序列化
        /// </summary>
        private string SerializeState(PlaybackState state)
        {
            var queueJson = SerializeStringList(state.QueueUUIDs);
            var historyJson = SerializeStringList(state.HistoryUUIDs);
            
            return "{\n" +
                $"  \"CurrentSongUUID\": \"{EscapeJson(state.CurrentSongUUID ?? "")}\",\n" +
                $"  \"CurrentAudioTagValue\": {state.CurrentAudioTagValue},\n" +
                $"  \"IsShuffle\": {state.IsShuffle.ToString().ToLower()},\n" +
                $"  \"IsRepeatOne\": {state.IsRepeatOne.ToString().ToLower()},\n" +
                $"  \"SavedAt\": \"{EscapeJson(state.SavedAt ?? "")}\",\n" +
                $"  \"QueueUUIDs\": {queueJson},\n" +
                $"  \"HistoryUUIDs\": {historyJson},\n" +
                $"  \"PlaylistPosition\": {state.PlaylistPosition},\n" +
                $"  \"HistoryPosition\": {state.HistoryPosition},\n" +
                $"  \"ExtendedSteps\": {state.ExtendedSteps}\n" +
                "}";
        }

        /// <summary>
        /// 序列化字符串列表为 JSON 数组
        /// </summary>
        private string SerializeStringList(List<string> list)
        {
            if (list == null || list.Count == 0)
                return "[]";
            
            var items = list.Select(s => $"\"{EscapeJson(s ?? "")}\"");
            return "[" + string.Join(", ", items) + "]";
        }

        /// <summary>
        /// 简单的 JSON 反序列化
        /// </summary>
        private PlaybackState DeserializeState(string json)
        {
            var state = new PlaybackState();
            
            state.CurrentSongUUID = ExtractStringValue(json, "CurrentSongUUID");
            state.CurrentAudioTagValue = ExtractIntValue(json, "CurrentAudioTagValue");
            state.IsShuffle = ExtractBoolValue(json, "IsShuffle");
            state.IsRepeatOne = ExtractBoolValue(json, "IsRepeatOne");
            state.SavedAt = ExtractStringValue(json, "SavedAt");
            state.QueueUUIDs = ExtractStringListValue(json, "QueueUUIDs");
            state.HistoryUUIDs = ExtractStringListValue(json, "HistoryUUIDs");
            state.PlaylistPosition = ExtractIntValue(json, "PlaylistPosition");
            state.HistoryPosition = ExtractIntValue(json, "HistoryPosition");
            state.ExtendedSteps = ExtractIntValue(json, "ExtendedSteps");

            return state;
        }

        private string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string ExtractStringValue(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"([^\"]*)\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : "";
        }

        private int ExtractIntValue(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*(\\d+)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
        }

        private bool ExtractBoolValue(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && match.Groups[1].Value.ToLower() == "true";
        }

        /// <summary>
        /// 从 JSON 提取字符串数组
        /// </summary>
        private List<string> ExtractStringListValue(string json, string key)
        {
            var result = new List<string>();
            try
            {
                // 匹配 "key": [...]
                var pattern = $"\"{key}\"\\s*:\\s*\\[([^\\]]*)\\]";
                var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
                if (match.Success)
                {
                    var arrayContent = match.Groups[1].Value;
                    // 匹配所有引号中的字符串
                    var itemPattern = "\"([^\"]*)\"";
                    var itemMatches = System.Text.RegularExpressions.Regex.Matches(arrayContent, itemPattern);
                    foreach (System.Text.RegularExpressions.Match itemMatch in itemMatches)
                    {
                        result.Add(UnescapeJson(itemMatch.Groups[1].Value));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to extract string list for key '{key}': {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// 反转义 JSON 字符串
        /// </summary>
        private string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// 获取保存的AudioTag
        /// </summary>
        public AudioTag? GetSavedAudioTag()
        {
            if (_currentState != null && _currentState.CurrentAudioTagValue != 0)
            {
                return (AudioTag)_currentState.CurrentAudioTagValue;
            }
            return null;
        }

        /// <summary>
        /// 获取保存的歌曲UUID
        /// </summary>
        public string GetSavedSongUUID()
        {
            return _currentState?.CurrentSongUUID;
        }

        /// <summary>
        /// 获取保存的随机播放状态
        /// </summary>
        public bool? GetSavedShuffleState()
        {
            return _currentState?.IsShuffle;
        }

        /// <summary>
        /// 获取保存的单曲循环状态
        /// </summary>
        public bool? GetSavedRepeatOneState()
        {
            return _currentState?.IsRepeatOne;
        }

        /// <summary>
        /// 应用保存的Tag选择状态
        /// 如果保存的Tag无效（没有歌曲匹配），则返回false，让游戏使用默认值
        /// </summary>
        public bool ApplySavedAudioTag()
        {
            var savedTag = GetSavedAudioTag();
            if (!savedTag.HasValue || savedTag.Value == 0)
            {
                Logger.LogInfo("No saved AudioTag or tag is 0, using game default");
                return false;
            }

            // 验证保存的Tag是否有效（是否有歌曲匹配）
            var musicService = MusicService_RemoveLimit_Patch.CurrentInstance;
            if (musicService != null)
            {
                var allMusic = musicService.AllMusicList;
                bool hasMatchingSong = false;
                
                foreach (var song in allMusic)
                {
                    if (savedTag.Value.HasFlagFast(song.Tag))
                    {
                        hasMatchingSong = true;
                        break;
                    }
                }

                if (!hasMatchingSong)
                {
                    Logger.LogWarning($"Saved AudioTag {savedTag.Value} has no matching songs, resetting to default");
                    // 重置状态文件
                    ResetState();
                    return false;
                }
            }

            Logger.LogInfo($"Applying saved AudioTag: {savedTag.Value}");
            SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value = savedTag.Value;
            return true;
        }

        /// <summary>
        /// 重置状态文件（当状态无效时调用）
        /// </summary>
        public void ResetState()
        {
            try
            {
                _currentState = new PlaybackState();
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                    Logger.LogInfo("Deleted invalid playback state file");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to reset state: {ex.Message}");
            }
        }

        /// <summary>
        /// 在播放列表中查找并播放保存的歌曲
        /// 如果歌曲不存在，返回false，游戏会正常从头播放
        /// </summary>
        public bool TryPlaySavedSong(MusicService musicService)
        {
            var savedUUID = GetSavedSongUUID();
            if (string.IsNullOrEmpty(savedUUID))
            {
                Logger.LogInfo("No saved song UUID found, playing from beginning");
                return false;
            }

            var playlist = musicService.CurrentPlayList;
            if (playlist == null || playlist.Count == 0)
            {
                Logger.LogWarning("Current playlist is empty, playing from beginning");
                return false;
            }

            // 查找歌曲在播放列表中的位置
            int index = -1;
            for (int i = 0; i < playlist.Count; i++)
            {
                if (playlist[i].UUID == savedUUID)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                Logger.LogInfo($"Found saved song at index {index}: {savedUUID}");
                
                // 使用 MusicService.PlayMusicInPlaylist 播放
                return musicService.PlayMusicInPlaylist(index);
            }
            else
            {
                Logger.LogInfo($"Saved song not in current playlist: {savedUUID}, playing from beginning");
                // 不重置状态，因为歌曲可能只是暂时被过滤掉了
                return false;
            }
        }

        /// <summary>
        /// 恢复队列和历史
        /// </summary>
        /// <param name="allMusic">所有音乐列表</param>
        /// <returns>是否成功恢复</returns>
        public bool TryRestoreQueueAndHistory(IReadOnlyList<GameAudioInfo> allMusic)
        {
            if (_currentState == null || allMusic == null || allMusic.Count == 0)
            {
                Logger.LogInfo("Cannot restore queue/history: no saved state or no music available");
                return false;
            }

            var queueManager = PlayQueueManager.Instance;
            if (queueManager == null)
            {
                Logger.LogWarning("PlayQueueManager not available");
                return false;
            }

            // 检查是否有保存的队列或历史
            bool hasQueue = _currentState.QueueUUIDs != null && _currentState.QueueUUIDs.Count > 0;
            bool hasHistory = _currentState.HistoryUUIDs != null && _currentState.HistoryUUIDs.Count > 0;

            if (!hasQueue && !hasHistory)
            {
                Logger.LogInfo("No saved queue or history to restore");
                return false;
            }

            try
            {
                queueManager.RestoreFullState(
                    _currentState.QueueUUIDs,
                    _currentState.HistoryUUIDs,
                    _currentState.PlaylistPosition,
                    _currentState.HistoryPosition,
                    _currentState.ExtendedSteps,
                    allMusic
                );

                Logger.LogInfo($"Restored queue ({_currentState.QueueUUIDs?.Count ?? 0} items) and history ({_currentState.HistoryUUIDs?.Count ?? 0} items)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to restore queue/history: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取保存的队列 UUID 列表
        /// </summary>
        public List<string> GetSavedQueueUUIDs()
        {
            return _currentState?.QueueUUIDs;
        }

        /// <summary>
        /// 获取保存的历史 UUID 列表
        /// </summary>
        public List<string> GetSavedHistoryUUIDs()
        {
            return _currentState?.HistoryUUIDs;
        }

        /// <summary>
        /// 强制保存当前状态（用于游戏退出时）
        /// </summary>
        public void ForceSave()
        {
            if (_currentState == null)
            {
                _currentState = new PlaybackState();
            }

            // 保存队列和历史
            SaveQueueAndHistory();

            // 保存到文件
            SaveState();
            Logger.LogInfo("Force saved playback state");
        }

        /// <summary>
        /// 清理订阅
        /// </summary>
        public void Dispose()
        {
            _audioTagSubscription?.Dispose();
            _musicChangeSubscription?.Dispose();
        }
    }
}

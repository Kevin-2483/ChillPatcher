using Bulbul;
using ChillPatcher.Patches.UIFramework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 播放队列管理器
    /// 
    /// 核心概念：
    /// - 队列的第一个永远是正在播放的歌曲
    /// - 播放完成后移除第一个，播放下一个
    /// - 队列为空时从播放列表补充
    /// - 播放指针记录在 CurrentPlayList 中的位置（顺序模式用）
    /// - 播放历史记录支持"上一首"功能
    /// </summary>
    public class PlayQueueManager
    {
        private static PlayQueueManager _instance;
        public static PlayQueueManager Instance => _instance ??= new PlayQueueManager();
        
        /// <summary>
        /// 播放队列（第一个是正在播放的）
        /// </summary>
        private List<GameAudioInfo> _queue = new List<GameAudioInfo>();
        
        /// <summary>
        /// 播放历史记录（最近播放的在前面）
        /// </summary>
        private List<GameAudioInfo> _history = new List<GameAudioInfo>();
        
        /// <summary>
        /// 历史位置指针（-1 表示不在历史回溯模式，>=0 表示当前在历史中的位置）
        /// </summary>
        private int _historyPosition = -1;
        
        /// <summary>
        /// 扩展步数（当历史到头后继续往前探索播放列表的步数）
        /// </summary>
        private int _extendedSteps = 0;
        
        /// <summary>
        /// 最大历史记录数量
        /// </summary>
        public const int MaxHistoryCount = 50;
        
        /// <summary>
        /// 只读的队列访问
        /// </summary>
        public IReadOnlyList<GameAudioInfo> Queue => _queue;
        
        /// <summary>
        /// 只读的历史记录访问
        /// </summary>
        public IReadOnlyList<GameAudioInfo> History => _history;
        
        /// <summary>
        /// 播放指针：在 CurrentPlayList 中的下一个播放位置
        /// </summary>
        public int PlaylistPosition { get; private set; } = 0;
        
        /// <summary>
        /// 当前正在播放的歌曲（队列第一个）
        /// </summary>
        public GameAudioInfo CurrentPlaying => _queue.Count > 0 ? _queue[0] : null;
        
        /// <summary>
        /// 队列是否为空（不包括正在播放的）
        /// </summary>
        public bool IsQueueEmpty => _queue.Count <= 1;
        
        /// <summary>
        /// 队列中待播放的歌曲数量（不包括正在播放的）
        /// </summary>
        public int PendingCount => Math.Max(0, _queue.Count - 1);
        
        /// <summary>
        /// 扩展步数（当历史到头后继续往前探索的步数）
        /// </summary>
        public int ExtendedSteps => _extendedSteps;
        
        /// <summary>
        /// 是否在扩展模式中（历史到头后继续往前探索）
        /// </summary>
        public bool IsInExtendedMode => _extendedSteps > 0;
        
        // 事件
        public event Action OnQueueChanged;
        public event Action<GameAudioInfo> OnCurrentChanged;
        public event Action<int> OnPlaylistPositionChanged;
        
        private PlayQueueManager() 
        {
            // 订阅歌曲排除状态变化事件
            MusicService_Excluded_Patch.OnSongExcludedChanged += HandleSongExcludedChanged;
        }
        
        /// <summary>
        /// 处理歌曲排除状态变化
        /// </summary>
        private void HandleSongExcludedChanged(string uuid, bool isExcluded)
        {
            if (isExcluded)
            {
                // 歌曲被排除，从队列和历史中移除
                OnSongExcluded(uuid);
            }
            // 如果是取消排除，不需要特别处理
        }
        
        #region 基础队列操作
        
        /// <summary>
        /// 清空整个队列
        /// </summary>
        public void Clear()
        {
            _queue.Clear();
            OnQueueChanged?.Invoke();
        }
        
        /// <summary>
        /// 清空队列但保留正在播放的
        /// </summary>
        public void ClearPending()
        {
            if (_queue.Count > 1)
            {
                var current = _queue[0];
                _queue.Clear();
                _queue.Add(current);
                OnQueueChanged?.Invoke();
            }
        }
        
        #endregion
        
        #region 播放历史记录操作
        
        /// <summary>
        /// 添加歌曲到播放历史
        /// 如果在历史回溯模式中，会丢弃当前位置之前的历史（类似浏览器历史行为）
        /// </summary>
        public void AddToHistory(GameAudioInfo audio)
        {
            if (audio == null) return;
            
            // 如果在历史回溯模式中，丢弃当前位置之前的历史
            // 例如：历史是 [5,4,3,2,1]，_historyPosition=2（正在播放3）
            // 添加新歌曲6后，应该变成 [6,3,2,1]（丢弃5和4）
            if (_historyPosition > 0)
            {
                // 移除位置0到_historyPosition-1的项（不包括当前位置）
                _history.RemoveRange(0, _historyPosition);
                Plugin.Log.LogDebug($"[Queue] Removed {_historyPosition} items from history (before current position)");
            }
            
            // 如果新歌曲已经在历史记录中，先移除（避免重复）
            _history.RemoveAll(a => a.UUID == audio.UUID);
            
            // 添加到最前面
            _history.Insert(0, audio);
            
            // 重置历史位置指针（新播放的歌曲，退出回溯模式）
            _historyPosition = -1;
            
            // 重置扩展步数
            _extendedSteps = 0;
            
            // 限制历史记录数量
            while (_history.Count > MaxHistoryCount)
            {
                _history.RemoveAt(_history.Count - 1);
            }
            
            Plugin.Log.LogDebug($"[Queue] Added to history: {audio.AudioClipName}, history size: {_history.Count}");
        }
        
        /// <summary>
        /// 是否可以回退到上一首
        /// </summary>
        public bool CanGoPrevious
        {
            get
            {
                // 需要有至少2条历史记录才能回退（第一条是当前播放的）
                // 或者已经在历史模式中且还有更早的记录
                if (_historyPosition < 0)
                {
                    // 不在历史模式，需要至少2条
                    return _history.Count >= 2;
                }
                else
                {
                    // 在历史模式，检查是否还有更早的
                    return _historyPosition + 1 < _history.Count;
                }
            }
        }
        
        /// <summary>
        /// 是否在历史回溯模式中
        /// </summary>
        public bool IsInHistoryMode => _historyPosition >= 0;
        
        /// <summary>
        /// 回退到上一首（从历史记录）
        /// 注意：此方法不处理扩展模式，扩展模式需要调用 GoPreviousExtended
        /// </summary>
        /// <returns>上一首歌曲，如果历史到头则返回 null</returns>
        public GameAudioInfo GoPrevious()
        {
            int nextPos;
            
            if (_historyPosition < 0)
            {
                // 第一次上一首：从位置 1 开始（跳过当前播放的位置 0）
                nextPos = 1;
            }
            else
            {
                // 已经在历史模式，继续后退
                nextPos = _historyPosition + 1;
            }
            
            if (nextPos >= _history.Count)
            {
                Plugin.Log.LogInfo("[Queue] No more history to go back, need extended mode");
                return null;
            }
            
            _historyPosition = nextPos;
            var audio = _history[_historyPosition];
            
            Plugin.Log.LogInfo($"[Queue] Go previous: {audio.AudioClipName}, history position: {_historyPosition}/{_history.Count - 1}");
            return audio;
        }
        
        /// <summary>
        /// 在扩展模式下回退到播放列表的前一首
        /// 当历史到头后调用此方法继续往前探索
        /// </summary>
        /// <param name="currentPlaylist">当前播放列表</param>
        /// <param name="currentAudio">当前播放的歌曲（用于确定位置）</param>
        /// <param name="isExcludedFunc">检查歌曲是否被排除的函数</param>
        /// <returns>前一首歌曲，如果没有则返回 null</returns>
        public GameAudioInfo GoPreviousExtended(IReadOnlyList<GameAudioInfo> currentPlaylist, GameAudioInfo currentAudio, Func<GameAudioInfo, bool> isExcludedFunc = null)
        {
            if (currentPlaylist == null || currentPlaylist.Count == 0 || currentAudio == null)
            {
                return null;
            }
            
            // 找到当前歌曲在播放列表中的位置
            int currentIndex = -1;
            for (int i = 0; i < currentPlaylist.Count; i++)
            {
                if (currentPlaylist[i].UUID == currentAudio.UUID)
                {
                    currentIndex = i;
                    break;
                }
            }
            
            if (currentIndex < 0)
            {
                Plugin.Log.LogWarning("[Queue] Current audio not found in playlist for extended mode");
                return null;
            }
            
            // 从当前位置往前找一首不被排除的歌曲
            int playlistCount = currentPlaylist.Count;
            for (int i = 1; i <= playlistCount; i++)
            {
                int prevIndex = (currentIndex - i + playlistCount) % playlistCount;
                var candidate = currentPlaylist[prevIndex];
                
                bool isExcluded = isExcludedFunc?.Invoke(candidate) ?? false;
                if (!isExcluded)
                {
                    // 增加扩展步数
                    _extendedSteps++;
                    Plugin.Log.LogInfo($"[Queue] Go previous extended: {candidate.AudioClipName}, extended steps: {_extendedSteps}");
                    return candidate;
                }
            }
            
            Plugin.Log.LogWarning("[Queue] Cannot find previous non-excluded song in playlist");
            return null;
        }
        
        /// <summary>
        /// 在扩展模式下前进到播放列表的下一首（消耗扩展步数）
        /// </summary>
        /// <param name="currentPlaylist">当前播放列表</param>
        /// <param name="currentAudio">当前播放的歌曲（用于确定位置）</param>
        /// <param name="isExcludedFunc">检查歌曲是否被排除的函数</param>
        /// <returns>下一首歌曲，如果扩展步数用完返回 null（需要回到历史最早记录）</returns>
        public GameAudioInfo GoNextExtended(IReadOnlyList<GameAudioInfo> currentPlaylist, GameAudioInfo currentAudio, Func<GameAudioInfo, bool> isExcludedFunc = null)
        {
            if (_extendedSteps <= 0)
            {
                Plugin.Log.LogInfo("[Queue] Extended steps exhausted, return to history");
                return null;
            }
            
            if (currentPlaylist == null || currentPlaylist.Count == 0 || currentAudio == null)
            {
                return null;
            }
            
            // 找到当前歌曲在播放列表中的位置
            int currentIndex = -1;
            for (int i = 0; i < currentPlaylist.Count; i++)
            {
                if (currentPlaylist[i].UUID == currentAudio.UUID)
                {
                    currentIndex = i;
                    break;
                }
            }
            
            if (currentIndex < 0)
            {
                Plugin.Log.LogWarning("[Queue] Current audio not found in playlist for extended mode");
                return null;
            }
            
            // 从当前位置往后找一首不被排除的歌曲
            int playlistCount = currentPlaylist.Count;
            for (int i = 1; i <= playlistCount; i++)
            {
                int nextIndex = (currentIndex + i) % playlistCount;
                var candidate = currentPlaylist[nextIndex];
                
                bool isExcluded = isExcludedFunc?.Invoke(candidate) ?? false;
                if (!isExcluded)
                {
                    // 消耗扩展步数
                    _extendedSteps--;
                    Plugin.Log.LogInfo($"[Queue] Go next extended: {candidate.AudioClipName}, remaining extended steps: {_extendedSteps}");
                    return candidate;
                }
            }
            
            Plugin.Log.LogWarning("[Queue] Cannot find next non-excluded song in playlist");
            return null;
        }
        
        /// <summary>
        /// 前进到下一首（在历史回溯模式中）
        /// 注意：此方法不处理扩展模式，扩展模式需要调用 GoNextExtended
        /// </summary>
        /// <returns>下一首歌曲，如果已到最前面返回 null（应该使用队列）</returns>
        public GameAudioInfo GoNext()
        {
            if (_historyPosition <= 0)
            {
                // 已经在最前面，退出历史模式
                _historyPosition = -1;
                Plugin.Log.LogInfo("[Queue] Exiting history mode, use queue for next");
                return null;
            }
            
            _historyPosition--;
            var audio = _history[_historyPosition];
            
            Plugin.Log.LogInfo($"[Queue] Go next in history: {audio.AudioClipName}, history position: {_historyPosition}/{_history.Count - 1}");
            return audio;
        }
        
        /// <summary>
        /// 重置历史位置（退出回溯模式）
        /// </summary>
        public void ResetHistoryPosition()
        {
            _historyPosition = -1;
        }
        
        /// <summary>
        /// 重置扩展步数
        /// </summary>
        public void ResetExtendedSteps()
        {
            _extendedSteps = 0;
            Plugin.Log.LogDebug("[Queue] Extended steps reset");
        }
        
        /// <summary>
        /// 清空播放历史
        /// 如果不在历史模式：直接清空所有历史
        /// 如果在历史模式：保留当前播放的歌曲，清空前后历史，清空扩展步数，退出历史模式
        /// </summary>
        public void ClearHistory()
        {
            if (!IsInHistoryMode && !IsInExtendedMode)
            {
                // 不在历史模式，直接清空
                _history.Clear();
                Plugin.Log.LogInfo("[Queue] History cleared (not in history mode)");
            }
            else
            {
                // 在历史模式或扩展模式中
                // 保留当前播放的歌曲（队列第一个）
                GameAudioInfo currentPlaying = CurrentPlaying;
                
                // 清空历史
                _history.Clear();
                
                // 如果有当前播放的歌曲，把它放回历史第一位
                if (currentPlaying != null)
                {
                    _history.Add(currentPlaying);
                    Plugin.Log.LogInfo($"[Queue] History cleared, kept current: {currentPlaying.AudioClipName}");
                }
                else
                {
                    Plugin.Log.LogInfo("[Queue] History cleared (was in history/extended mode)");
                }
                
                // 清空扩展步数
                _extendedSteps = 0;
                
                // 退出历史模式
                _historyPosition = -1;
            }
        }
        
        /// <summary>
        /// 历史记录数量
        /// </summary>
        public int HistoryCount => _history.Count;
        
        /// <summary>
        /// 当前历史位置
        /// </summary>
        public int HistoryPosition => _historyPosition;
        
        /// <summary>
        /// 从历史记录中移除指定歌曲（当用户排除歌曲时调用）
        /// </summary>
        public void RemoveFromHistory(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            
            int removedCount = _history.RemoveAll(a => a.UUID == uuid);
            
            if (removedCount > 0)
            {
                // 如果在历史回溯模式中，调整位置
                if (_historyPosition >= _history.Count)
                {
                    _historyPosition = _history.Count - 1;
                }
                
                Plugin.Log.LogDebug($"[Queue] Removed {removedCount} songs from history with UUID: {uuid}");
            }
        }
        
        /// <summary>
        /// 当用户排除歌曲时，从队列和历史中移除
        /// </summary>
        /// <param name="uuid">被排除的歌曲 UUID</param>
        public void OnSongExcluded(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            
            // 从队列移除（第一个是正在播放的，保留）
            bool inQueue = false;
            for (int i = _queue.Count - 1; i >= 1; i--)
            {
                if (_queue[i].UUID == uuid)
                {
                    Plugin.Log.LogInfo($"[Queue] Removing excluded song from queue: {_queue[i].AudioClipName}");
                    _queue.RemoveAt(i);
                    inQueue = true;
                }
            }
            
            if (inQueue)
            {
                OnQueueChanged?.Invoke();
            }
            
            // 从历史记录移除
            RemoveFromHistory(uuid);
        }
        
        #endregion
        
        #region 队列操作
        public void Enqueue(GameAudioInfo audio)
        {
            if (audio == null) return;
            _queue.Add(audio);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Enqueued: {audio.AudioClipName}, queue size: {_queue.Count}");
        }
        
        /// <summary>
        /// 添加多首歌曲到队列末尾
        /// </summary>
        public void EnqueueRange(IEnumerable<GameAudioInfo> audios)
        {
            if (audios == null) return;
            var list = audios.ToList();
            _queue.AddRange(list);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Enqueued {list.Count} songs, queue size: {_queue.Count}");
        }
        
        /// <summary>
        /// 插入歌曲到指定位置
        /// </summary>
        /// <param name="index">位置（0=替换正在播放，1=下一首播放）</param>
        /// <param name="audio">歌曲</param>
        public void Insert(int index, GameAudioInfo audio)
        {
            if (audio == null) return;
            index = Math.Max(0, Math.Min(index, _queue.Count));
            _queue.Insert(index, audio);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Inserted at {index}: {audio.AudioClipName}");
        }
        
        /// <summary>
        /// 插入为"下一首播放"（队列第二个位置）
        /// 会清空历史模式和扩展步数
        /// </summary>
        public void InsertNext(GameAudioInfo audio)
        {
            // 清空历史模式和扩展步数
            if (IsInHistoryMode || IsInExtendedMode)
            {
                Plugin.Log.LogInfo("[Queue] InsertNext: Clearing history mode and extended steps");
                _historyPosition = -1;
                _extendedSteps = 0;
            }
            
            Insert(1, audio);
        }
        
        /// <summary>
        /// 从队列移除指定歌曲
        /// </summary>
        public bool Remove(GameAudioInfo audio)
        {
            if (audio == null) return false;
            bool removed = _queue.Remove(audio);
            if (removed)
            {
                OnQueueChanged?.Invoke();
                Plugin.Log.LogInfo($"[Queue] Removed: {audio.AudioClipName}");
            }
            return removed;
        }
        
        /// <summary>
        /// 从队列移除指定索引的歌曲
        /// </summary>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _queue.Count) return false;
            var audio = _queue[index];
            _queue.RemoveAt(index);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Removed at {index}: {audio?.AudioClipName}");
            return true;
        }
        
        /// <summary>
        /// 移动队列中的歌曲位置
        /// </summary>
        public void Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _queue.Count) return;
            if (toIndex < 0 || toIndex >= _queue.Count) return;
            if (fromIndex == toIndex) return;
            
            var item = _queue[fromIndex];
            _queue.RemoveAt(fromIndex);
            _queue.Insert(toIndex, item);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Moved from {fromIndex} to {toIndex}: {item.AudioClipName}");
        }
        
        /// <summary>
        /// 获取队列中歌曲的索引
        /// </summary>
        public int IndexOf(GameAudioInfo audio)
        {
            return _queue.IndexOf(audio);
        }
        
        /// <summary>
        /// 检查歌曲是否在队列中（通过UUID比较）
        /// </summary>
        public bool Contains(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return false;
            return _queue.Any(a => a.UUID == uuid);
        }
        
        /// <summary>
        /// 通过UUID从队列移除歌曲
        /// </summary>
        public bool RemoveByUUID(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return false;
            var audio = _queue.FirstOrDefault(a => a.UUID == uuid);
            if (audio != null)
            {
                return Remove(audio);
            }
            return false;
        }
        
        /// <summary>
        /// 检查队列中是否包含指定歌曲
        /// </summary>
        public bool Contains(GameAudioInfo audio)
        {
            return _queue.Contains(audio);
        }
        
        /// <summary>
        /// 获取队列的副本
        /// </summary>
        public List<GameAudioInfo> ToList()
        {
            return new List<GameAudioInfo>(_queue);
        }
        
        #endregion
        
        #region 播放控制
        
        /// <summary>
        /// 设置当前播放（替换队列第一个）
        /// 用于：点击播放列表中的歌曲
        /// </summary>
        /// <param name="audio">要播放的歌曲</param>
        /// <param name="updatePosition">是否更新播放指针</param>
        /// <param name="newPosition">新的播放指针位置（如果 updatePosition 为 true）</param>
        /// <param name="addToHistory">是否添加到播放历史（默认 true）</param>
        public void SetCurrentPlaying(GameAudioInfo audio, bool updatePosition = false, int newPosition = 0, bool addToHistory = true)
        {
            if (audio == null) return;
            
            if (_queue.Count > 0)
            {
                _queue[0] = audio;
            }
            else
            {
                _queue.Add(audio);
            }
            
            if (updatePosition)
            {
                PlaylistPosition = newPosition;
                OnPlaylistPositionChanged?.Invoke(PlaylistPosition);
            }
            
            // 添加到历史记录
            if (addToHistory)
            {
                AddToHistory(audio);
            }
            
            OnCurrentChanged?.Invoke(audio);
            OnQueueChanged?.Invoke();
            Plugin.Log.LogInfo($"[Queue] Set current playing: {audio.AudioClipName}, position: {PlaylistPosition}");
        }
        
        /// <summary>
        /// 播放完成，前进到下一首
        /// </summary>
        /// <param name="currentPlaylist">当前播放列表（原始列表，不要提前过滤）</param>
        /// <param name="isShuffle">是否随机模式</param>
        /// <param name="isExcludedFunc">检查歌曲是否被排除的函数（可选，如果为 null 则不检查排除）</param>
        /// <returns>下一首要播放的歌曲，如果没有则返回 null</returns>
        public GameAudioInfo AdvanceToNext(IReadOnlyList<GameAudioInfo> currentPlaylist, bool isShuffle, Func<GameAudioInfo, bool> isExcludedFunc = null)
        {
            // 移除当前播放的（第一个）
            if (_queue.Count > 0)
            {
                _queue.RemoveAt(0);
            }
            
            // 如果队列还有歌曲，播放下一个（队列中的歌曲不检查排除，因为是用户手动添加的）
            if (_queue.Count > 0)
            {
                var next = _queue[0];
                // 添加到历史记录
                AddToHistory(next);
                OnCurrentChanged?.Invoke(next);
                OnQueueChanged?.Invoke();
                Plugin.Log.LogInfo($"[Queue] Advanced to next in queue: {next.AudioClipName}");
                return next;
            }
            
            // 队列为空，从播放列表补充
            return FillFromPlaylist(currentPlaylist, isShuffle, isExcludedFunc);
        }
        
        /// <summary>
        /// 从播放列表补充一首到队列（会跳过排除的歌曲）
        /// </summary>
        /// <param name="currentPlaylist">原始播放列表</param>
        /// <param name="isShuffle">是否随机模式</param>
        /// <param name="isExcludedFunc">检查歌曲是否被排除的函数</param>
        private GameAudioInfo FillFromPlaylist(IReadOnlyList<GameAudioInfo> currentPlaylist, bool isShuffle, Func<GameAudioInfo, bool> isExcludedFunc = null)
        {
            if (currentPlaylist == null || currentPlaylist.Count == 0)
            {
                Plugin.Log.LogWarning("[Queue] Cannot fill: playlist is empty");
                return null;
            }
            
            GameAudioInfo next = null;
            int playlistCount = currentPlaylist.Count;
            
            if (isShuffle)
            {
                // 随机模式：随机选一首，如果被排除则重试
                var random = new System.Random();
                
                // 遍历整个列表尝试找到一首不被排除的歌曲
                for (int attempt = 0; attempt < playlistCount; attempt++)
                {
                    int randomIndex = random.Next(playlistCount);
                    var candidate = currentPlaylist[randomIndex];
                    
                    bool isExcluded = isExcludedFunc?.Invoke(candidate) ?? false;
                    if (!isExcluded)
                    {
                        next = candidate;
                        // 更新播放指针到随机选中的位置 + 1
                        PlaylistPosition = (randomIndex + 1) % playlistCount;
                        OnPlaylistPositionChanged?.Invoke(PlaylistPosition);
                        Plugin.Log.LogInfo($"[Queue] Filled from playlist (shuffle): {next.AudioClipName}, new position: {PlaylistPosition}");
                        break;
                    }
                    Plugin.Log.LogDebug($"[Queue] Shuffle: skipping excluded song: {candidate.AudioClipName}");
                }
                
                if (next == null)
                {
                    Plugin.Log.LogWarning("[Queue] Cannot fill: all songs are excluded");
                    return null;
                }
            }
            else
            {
                // 顺序模式：从播放指针位置开始遍历，跳过排除的歌曲
                // 参考游戏原版 MusicService.GetNextGameAudio 的逻辑
                for (int i = 0; i < playlistCount; i++)
                {
                    int index = (PlaylistPosition + i) % playlistCount;
                    var candidate = currentPlaylist[index];
                    
                    bool isExcluded = isExcludedFunc?.Invoke(candidate) ?? false;
                    if (!isExcluded)
                    {
                        next = candidate;
                        // 播放指针移到下一个位置
                        PlaylistPosition = (index + 1) % playlistCount;
                        OnPlaylistPositionChanged?.Invoke(PlaylistPosition);
                        Plugin.Log.LogInfo($"[Queue] Filled from playlist (sequential): {next.AudioClipName}, new position: {PlaylistPosition}");
                        break;
                    }
                    Plugin.Log.LogDebug($"[Queue] Sequential: skipping excluded song at index {index}: {candidate.AudioClipName}");
                }
                
                if (next == null)
                {
                    Plugin.Log.LogWarning("[Queue] Cannot fill: all songs are excluded");
                    return null;
                }
            }
            
            // 添加到队列
            _queue.Add(next);
            // 添加到历史记录
            AddToHistory(next);
            OnCurrentChanged?.Invoke(next);
            OnQueueChanged?.Invoke();
            
            return next;
        }
        
        /// <summary>
        /// 更新播放指针位置
        /// </summary>
        public void SetPlaylistPosition(int position)
        {
            PlaylistPosition = position;
            OnPlaylistPositionChanged?.Invoke(PlaylistPosition);
            Plugin.Log.LogInfo($"[Queue] Playlist position set to: {position}");
        }
        
        /// <summary>
        /// 根据歌曲在播放列表中的位置更新播放指针
        /// </summary>
        public void SetPlaylistPositionByAudio(GameAudioInfo audio, IReadOnlyList<GameAudioInfo> currentPlaylist)
        {
            if (audio == null || currentPlaylist == null) return;
            
            int index = -1;
            for (int i = 0; i < currentPlaylist.Count; i++)
            {
                if (currentPlaylist[i].UUID == audio.UUID)
                {
                    index = i;
                    break;
                }
            }
            
            if (index >= 0)
            {
                // 设置为下一个位置
                PlaylistPosition = (index + 1) % currentPlaylist.Count;
                OnPlaylistPositionChanged?.Invoke(PlaylistPosition);
                Plugin.Log.LogInfo($"[Queue] Position set by audio: {audio.AudioClipName}, new position: {PlaylistPosition}");
            }
        }
        
        #endregion
        
        #region 序列化/持久化支持
        
        /// <summary>
        /// 获取队列中所有歌曲的 UUID 列表（用于保存）
        /// </summary>
        public List<string> GetQueueUUIDs()
        {
            return _queue.Select(a => a.UUID).ToList();
        }
        
        /// <summary>
        /// 获取历史中所有歌曲的 UUID 列表（用于保存）
        /// </summary>
        public List<string> GetHistoryUUIDs()
        {
            return _history.Select(a => a.UUID).ToList();
        }
        
        /// <summary>
        /// 从 UUID 列表恢复队列
        /// </summary>
        public void RestoreFromUUIDs(IEnumerable<string> uuids, IReadOnlyList<GameAudioInfo> allMusic)
        {
            if (uuids == null || allMusic == null) return;
            
            _queue.Clear();
            
            foreach (var uuid in uuids)
            {
                var audio = allMusic.FirstOrDefault(a => a.UUID == uuid);
                if (audio != null)
                {
                    _queue.Add(audio);
                }
            }
            
            OnQueueChanged?.Invoke();
            if (_queue.Count > 0)
            {
                OnCurrentChanged?.Invoke(_queue[0]);
            }
            
            Plugin.Log.LogInfo($"[Queue] Restored {_queue.Count} songs from UUIDs");
        }
        
        /// <summary>
        /// 从 UUID 列表恢复历史
        /// </summary>
        public void RestoreHistoryFromUUIDs(IEnumerable<string> uuids, IReadOnlyList<GameAudioInfo> allMusic)
        {
            if (uuids == null || allMusic == null) return;
            
            _history.Clear();
            
            foreach (var uuid in uuids)
            {
                var audio = allMusic.FirstOrDefault(a => a.UUID == uuid);
                if (audio != null)
                {
                    _history.Add(audio);
                }
            }
            
            Plugin.Log.LogInfo($"[Queue] Restored {_history.Count} history entries from UUIDs");
        }
        
        /// <summary>
        /// 恢复播放状态（队列、历史、位置等）
        /// </summary>
        public void RestoreFullState(
            IEnumerable<string> queueUUIDs,
            IEnumerable<string> historyUUIDs,
            int playlistPosition,
            int historyPosition,
            int extendedSteps,
            IReadOnlyList<GameAudioInfo> allMusic)
        {
            if (allMusic == null)
            {
                Plugin.Log.LogWarning("[Queue] Cannot restore state: allMusic is null");
                return;
            }
            
            // 恢复队列
            if (queueUUIDs != null)
            {
                RestoreFromUUIDs(queueUUIDs, allMusic);
            }
            
            // 恢复历史
            if (historyUUIDs != null)
            {
                RestoreHistoryFromUUIDs(historyUUIDs, allMusic);
            }
            
            // 恢复位置
            PlaylistPosition = playlistPosition;
            _historyPosition = historyPosition;
            _extendedSteps = extendedSteps;
            
            Plugin.Log.LogInfo($"[Queue] Full state restored: Queue={_queue.Count}, History={_history.Count}, Position={playlistPosition}, HistoryPos={historyPosition}, ExtSteps={extendedSteps}");
        }
        
        #endregion
    }
}

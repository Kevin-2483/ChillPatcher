using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 个人 FM 播放列表管理器
    /// 用于管理 FM 歌曲列表的初始化和增长
    /// </summary>
    public class PersonalFMManager
    {
        private readonly NeteaseBridge _bridge;
        private readonly List<NeteaseBridge.SongInfo> _songs = new List<NeteaseBridge.SongInfo>();
        private int _currentIndex = 0;

        public PersonalFMManager(NeteaseBridge bridge)
        {
            _bridge = bridge;
        }

        /// <summary>
        /// 当前播放列表中的所有歌曲
        /// </summary>
        public IReadOnlyList<NeteaseBridge.SongInfo> Songs => _songs.AsReadOnly();

        /// <summary>
        /// 当前歌曲索引
        /// </summary>
        public int CurrentIndex
        {
            get => _currentIndex;
            set => _currentIndex = Math.Max(0, Math.Min(value, _songs.Count - 1));
        }

        /// <summary>
        /// 当前歌曲
        /// </summary>
        public NeteaseBridge.SongInfo CurrentSong => _songs.Count > 0 && _currentIndex < _songs.Count
            ? _songs[_currentIndex]
            : null;

        /// <summary>
        /// 歌曲总数
        /// </summary>
        public int Count => _songs.Count;

        /// <summary>
        /// 是否已初始化（有歌曲）
        /// </summary>
        public bool IsInitialized => _songs.Count > 0;

        /// <summary>
        /// 初始化 FM 播放列表（首次加载）
        /// </summary>
        /// <returns>是否成功</returns>
        public bool Initialize()
        {
            var songs = _bridge.GetPersonalFM();
            if (songs == null || songs.Count == 0)
                return false;

            _songs.Clear();
            _songs.AddRange(songs);
            _currentIndex = 0;
            return true;
        }

        /// <summary>
        /// 异步初始化 FM 播放列表（首次加载）
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> InitializeAsync()
        {
            var songs = await Task.Run(() => _bridge.GetPersonalFM());
            if (songs == null || songs.Count == 0)
                return false;

            _songs.Clear();
            _songs.AddRange(songs);
            _currentIndex = 0;
            return true;
        }

        /// <summary>
        /// 加载更多歌曲（列表增长）- 同步版本
        /// </summary>
        /// <returns>新加载的歌曲数量，失败返回 -1</returns>
        public int LoadMore()
        {
            var songs = _bridge.GetPersonalFM();
            if (songs == null)
                return -1;

            _songs.AddRange(songs);
            return songs.Count;
        }

        /// <summary>
        /// 加载更多歌曲（列表增长）- 异步版本
        /// 使用此方法避免阻塞主线程
        /// </summary>
        /// <returns>新加载的歌曲数量，失败返回 -1</returns>
        public async Task<int> LoadMoreAsync()
        {
            var songs = await Task.Run(() => _bridge.GetPersonalFM());
            if (songs == null)
                return -1;

            _songs.AddRange(songs);
            return songs.Count;
        }

        /// <summary>
        /// 移动到下一首歌曲
        /// </summary>
        /// <param name="autoLoadMore">如果接近列表末尾是否自动加载更多</param>
        /// <returns>是否成功移动到下一首</returns>
        public bool MoveNext(bool autoLoadMore = true)
        {
            if (_currentIndex < _songs.Count - 1)
            {
                _currentIndex++;

                // 如果快到末尾了，自动加载更多（后台触发，不等待）
                if (autoLoadMore && _currentIndex >= _songs.Count - 2)
                {
                    _ = LoadMoreAsync(); // Fire and forget
                }
                return true;
            }

            // 已经在最后，尝试加载更多（同步等待，因为必须等待）
            if (autoLoadMore)
            {
                var loaded = LoadMore();
                if (loaded > 0)
                {
                    _currentIndex++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 异步移动到下一首歌曲
        /// </summary>
        /// <param name="autoLoadMore">如果接近列表末尾是否自动加载更多</param>
        /// <returns>是否成功移动到下一首</returns>
        public async Task<bool> MoveNextAsync(bool autoLoadMore = true)
        {
            if (_currentIndex < _songs.Count - 1)
            {
                _currentIndex++;

                // 如果快到末尾了，自动加载更多
                if (autoLoadMore && _currentIndex >= _songs.Count - 2)
                {
                    await LoadMoreAsync();
                }
                return true;
            }

            // 已经在最后，尝试加载更多
            if (autoLoadMore)
            {
                var loaded = await LoadMoreAsync();
                if (loaded > 0)
                {
                    _currentIndex++;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 移动到上一首歌曲
        /// </summary>
        /// <returns>是否成功移动到上一首</returns>
        public bool MovePrevious()
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 标记当前歌曲为不喜欢并跳到下一首
        /// </summary>
        /// <returns>是否成功</returns>
        public bool TrashCurrentAndNext()
        {
            var current = CurrentSong;
            if (current == null)
                return false;

            // 先标记为不喜欢
            _bridge.FMTrash(current.Id);

            // 从列表中移除当前歌曲
            _songs.RemoveAt(_currentIndex);

            // 如果列表为空或接近末尾，加载更多（后台触发）
            if (_songs.Count == 0 || _currentIndex >= _songs.Count - 1)
            {
                _ = LoadMoreAsync(); // Fire and forget
            }

            // 调整索引
            if (_currentIndex >= _songs.Count && _songs.Count > 0)
            {
                _currentIndex = _songs.Count - 1;
            }

            return _songs.Count > 0;
        }

        /// <summary>
        /// 异步标记当前歌曲为不喜欢并跳到下一首
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> TrashCurrentAndNextAsync()
        {
            var current = CurrentSong;
            if (current == null)
                return false;

            // 先标记为不喜欢（在后台线程执行）
            await Task.Run(() => _bridge.FMTrash(current.Id));

            // 从列表中移除当前歌曲
            _songs.RemoveAt(_currentIndex);

            // 如果列表为空或接近末尾，加载更多
            if (_songs.Count == 0 || _currentIndex >= _songs.Count - 1)
            {
                await LoadMoreAsync();
            }

            // 调整索引
            if (_currentIndex >= _songs.Count && _songs.Count > 0)
            {
                _currentIndex = _songs.Count - 1;
            }

            return _songs.Count > 0;
        }

        /// <summary>
        /// 清空播放列表
        /// </summary>
        public void Clear()
        {
            _songs.Clear();
            _currentIndex = 0;
        }

        /// <summary>
        /// 检查是否需要加载更多歌曲
        /// </summary>
        /// <param name="threshold">距离末尾多少首时需要加载，默认 2</param>
        /// <returns>是否需要加载更多</returns>
        public bool NeedsMoreSongs(int threshold = 2)
        {
            return _songs.Count - _currentIndex <= threshold;
        }

        /// <summary>
        /// 确保有足够的歌曲可播放（同步版本）
        /// </summary>
        /// <param name="minSongs">最少保持的歌曲数量</param>
        /// <returns>当前列表中的歌曲数量</returns>
        public int EnsureSongs(int minSongs = 5)
        {
            while (_songs.Count - _currentIndex < minSongs)
            {
                var loaded = LoadMore();
                if (loaded <= 0)
                    break;
            }
            return _songs.Count;
        }

        /// <summary>
        /// 确保有足够的歌曲可播放（异步版本）
        /// </summary>
        /// <param name="minSongs">最少保持的歌曲数量</param>
        /// <returns>当前列表中的歌曲数量</returns>
        public async Task<int> EnsureSongsAsync(int minSongs = 5)
        {
            while (_songs.Count - _currentIndex < minSongs)
            {
                var loaded = await LoadMoreAsync();
                if (loaded <= 0)
                    break;
            }
            return _songs.Count;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using ChillPatcher.SDK.Events;
using ChillPatcher.SDK.Interfaces;
using ChillPatcher.SDK.Models;

namespace ChillPatcher.Module.Netease
{
    /// <summary>
    /// 网易云收藏管理器
    /// 处理收藏状态同步和 IFavoriteExcludeHandler 实现
    /// </summary>
    public class NeteaseFavoriteManager : IFavoriteExcludeHandler
    {
        private readonly NeteaseBridge _bridge;
        private readonly ManualLogSource _logger;
        private readonly Dictionary<string, NeteaseBridge.SongInfo> _songInfoMap;
        private readonly HashSet<long> _likedSongIds = new HashSet<long>();
        private readonly HashSet<string> _excludedUuids = new HashSet<string>();  // 内存中的排除列表

        public NeteaseFavoriteManager(
            NeteaseBridge bridge, 
            ManualLogSource logger,
            Dictionary<string, NeteaseBridge.SongInfo> songInfoMap)
        {
            _bridge = bridge;
            _logger = logger;
            _songInfoMap = songInfoMap;
        }

        /// <summary>
        /// 已收藏的歌曲 ID 集合（只读）
        /// </summary>
        public IReadOnlyCollection<long> LikedSongIds => _likedSongIds;

        /// <summary>
        /// 加载收藏列表
        /// </summary>
        public async Task LoadLikeListAsync()
        {
            var likeIds = _bridge.GetLikeList();
            if (likeIds != null)
            {
                _likedSongIds.Clear();
                foreach (var id in likeIds)
                {
                    _likedSongIds.Add(id);
                }
                _logger.LogInfo($"[NeteaseFavoriteManager] 已缓存 {_likedSongIds.Count} 首收藏歌曲 ID");
            }
        }

        /// <summary>
        /// 检查歌曲是否已收藏（通过网易云ID）
        /// </summary>
        public bool IsSongLiked(long songId)
        {
            return _likedSongIds.Contains(songId);
        }

        #region IFavoriteExcludeHandler

        public bool IsFavorite(string uuid)
        {
            if (!_songInfoMap.TryGetValue(uuid, out var songInfo))
                return false;
            return _likedSongIds.Contains(songInfo.Id);
        }

        public void SetFavorite(string uuid, bool isFavorite)
        {
            if (!_songInfoMap.TryGetValue(uuid, out var songInfo))
                return;

            if (isFavorite)
            {
                if (_bridge.LikeSong(songInfo.Id))
                {
                    _likedSongIds.Add(songInfo.Id);
                    _logger.LogInfo($"[NeteaseFavoriteManager] ✅ 已收藏: {songInfo.Name}");
                }
            }
            else
            {
                if (_bridge.UnlikeSong(songInfo.Id))
                {
                    _likedSongIds.Remove(songInfo.Id);
                    _logger.LogInfo($"[NeteaseFavoriteManager] ✅ 已取消收藏: {songInfo.Name}");
                }
            }
        }

        public bool IsExcluded(string uuid)
        {
            return _excludedUuids.Contains(uuid);
        }

        public void SetExcluded(string uuid, bool isExcluded)
        {
            if (isExcluded)
            {
                _excludedUuids.Add(uuid);
                _logger.LogInfo($"[NeteaseFavoriteManager] 已排除: {uuid}");
            }
            else
            {
                _excludedUuids.Remove(uuid);
                _logger.LogInfo($"[NeteaseFavoriteManager] 已取消排除: {uuid}");
            }
        }

        public IReadOnlyList<string> GetFavorites()
        {
            return _songInfoMap
                .Where(kvp => _likedSongIds.Contains(kvp.Value.Id))
                .Select(kvp => kvp.Key)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<string> GetExcluded()
        {
            return _excludedUuids.ToList().AsReadOnly();
        }

        #endregion

        /// <summary>
        /// 处理收藏变化事件
        /// </summary>
        public void HandleFavoriteChanged(FavoriteChangedEvent evt, string moduleId, Action<string, bool> updateCallback)
        {
            // 只处理本模块的歌曲
            if (evt.ModuleId != moduleId) return;
            if (evt.Music == null) return;

            // 从 UUID 获取歌曲 ID
            if (!_songInfoMap.TryGetValue(evt.Music.UUID, out var songInfo)) return;

            if (evt.IsFavorite)
            {
                if (_bridge.LikeSong(songInfo.Id))
                {
                    _likedSongIds.Add(songInfo.Id);
                    updateCallback?.Invoke(evt.Music.UUID, true);
                    _logger.LogInfo($"[NeteaseFavoriteManager] ✅ 已收藏: {songInfo.Name}");
                }
            }
            else
            {
                if (_bridge.UnlikeSong(songInfo.Id))
                {
                    _likedSongIds.Remove(songInfo.Id);
                    updateCallback?.Invoke(evt.Music.UUID, false);
                    _logger.LogInfo($"[NeteaseFavoriteManager] ✅ 已取消收藏: {songInfo.Name}");
                }
            }
        }
    }
}

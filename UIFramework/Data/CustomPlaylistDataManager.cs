using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bulbul;

namespace ChillPatcher.UIFramework.Data
{
    /// <summary>
    /// 自定义歌单数据管理器 - 管理自定义Tag的收藏和排序
    /// </summary>
    public class CustomPlaylistDataManager : IDisposable
    {
        private static CustomPlaylistDataManager _instance;
        public static CustomPlaylistDataManager Instance => _instance;

        // 需要清理的旧文件模式
        private static readonly string[] LEGACY_FILE_PATTERNS = new[]
        {
            "playlist.json",
            "album.json", 
            "!rescan",           // 旧的重扫描标记
            "!rescan_playlist",  // 当前版本的重扫描标记
            ".playlist_cache.json",
            "playlist_cache.json"
        };

        private PlaylistDatabase _database;
        private readonly string _databasePath;
        private readonly string _rootDirectory;

        /// <summary>
        /// 是否需要完整重扫描（数据库升级或恢复后）
        /// </summary>
        public bool NeedsFullRescan { get; private set; }

        /// <summary>
        /// 初始化数据管理器
        /// </summary>
        public static void Initialize(string rootDirectory)
        {
            if (_instance != null)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData").LogWarning("数据管理器已初始化");
                return;
            }

            var dbPath = Path.Combine(rootDirectory, ".playlist_data.db");
            _instance = new CustomPlaylistDataManager(rootDirectory, dbPath);
        }

        private CustomPlaylistDataManager(string rootDirectory, string databasePath)
        {
            _rootDirectory = rootDirectory;
            _databasePath = databasePath;
            
            InitializeDatabase();
        }

        /// <summary>
        /// 初始化数据库（带损坏恢复）
        /// </summary>
        private void InitializeDatabase()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData");
            
            try
            {
                _database = new PlaylistDatabase(_databasePath);
                
                // 检查数据库是否需要完整重扫描（版本升级）
                if (_database.NeedsFullRescan)
                {
                    logger.LogWarning("检测到旧版本数据库，需要清理旧文件并重新扫描");
                    CleanupLegacyFiles(_rootDirectory, 3);
                    NeedsFullRescan = true;
                }
                
                logger.LogInfo($"数据管理器初始化成功: {_databasePath}");
            }
            catch (Exception ex)
            {
                logger.LogError($"数据库初始化失败: {ex.Message}");
                logger.LogWarning("尝试删除损坏的数据库并重建...");
                
                // 尝试恢复
                if (TryRecoverDatabase())
                {
                    logger.LogInfo("数据库恢复成功，需要重新扫描所有歌单");
                }
                else
                {
                    logger.LogError("数据库恢复失败");
                    throw;
                }
            }
        }

        /// <summary>
        /// 尝试恢复损坏的数据库
        /// </summary>
        private bool TryRecoverDatabase()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData");
            
            try
            {
                // 1. 关闭现有连接（如果有）
                _database?.Dispose();
                _database = null;
                
                // 2. 删除损坏的数据库文件
                if (File.Exists(_databasePath))
                {
                    // SQLite 可能有 -wal 和 -shm 文件
                    var walPath = _databasePath + "-wal";
                    var shmPath = _databasePath + "-shm";
                    
                    File.Delete(_databasePath);
                    if (File.Exists(walPath)) File.Delete(walPath);
                    if (File.Exists(shmPath)) File.Delete(shmPath);
                    
                    logger.LogInfo("已删除损坏的数据库文件");
                }
                
                // 3. 清理所有旧文件
                CleanupLegacyFiles(_rootDirectory, 3);
                
                // 4. 重新创建数据库
                _database = new PlaylistDatabase(_databasePath);
                NeedsFullRescan = true;
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"恢复数据库失败: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 递归清理旧文件（JSON、重扫描标记等）
        /// </summary>
        /// <param name="directory">起始目录</param>
        /// <param name="maxDepth">最大递归深度</param>
        public void CleanupLegacyFiles(string directory, int maxDepth)
        {
            if (maxDepth < 0 || !Directory.Exists(directory))
                return;

            var logger = BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData");
            int deletedCount = 0;

            try
            {
                // 删除当前目录的旧文件
                foreach (var pattern in LEGACY_FILE_PATTERNS)
                {
                    var filePath = Path.Combine(directory, pattern);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning($"删除旧文件失败 '{filePath}': {ex.Message}");
                        }
                    }
                }

                // 递归处理子目录
                if (maxDepth > 0)
                {
                    foreach (var subDir in Directory.GetDirectories(directory))
                    {
                        CleanupLegacyFiles(subDir, maxDepth - 1);
                    }
                }

                if (deletedCount > 0)
                {
                    logger.LogInfo($"清理了 {deletedCount} 个旧文件: {directory}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"清理目录失败 '{directory}': {ex.Message}");
            }
        }

        /// <summary>
        /// 获取底层数据库实例
        /// </summary>
        public PlaylistDatabase GetDatabase() => _database;

        /// <summary>
        /// 清理不存在的歌单数据
        /// 在加载歌单列表后调用，传入当前有效的歌单ID列表
        /// </summary>
        public void CleanupStalePlaylistData(IEnumerable<string> validPlaylistIds)
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData");
            var validSet = new HashSet<string>(validPlaylistIds);
            
            // 获取数据库中的所有歌单ID
            var dbPlaylistIds = _database.GetAllPlaylistIds();
            
            int cleanedCount = 0;
            foreach (var playlistId in dbPlaylistIds)
            {
                if (!validSet.Contains(playlistId))
                {
                    logger.LogInfo($"清理不存在的歌单数据: {playlistId}");
                    _database.DeletePlaylist(playlistId);
                    cleanedCount++;
                }
            }
            
            if (cleanedCount > 0)
            {
                logger.LogInfo($"共清理 {cleanedCount} 个不存在的歌单");
            }
        }

        #region 收藏管理

        /// <summary>
        /// 添加收藏
        /// </summary>
        public bool AddFavorite(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            var result = _database.AddFavorite(tagId, songUuid);
            
            if (result)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"添加收藏: Tag={tagId}, UUID={songUuid}");
            }
            
            return result;
        }

        /// <summary>
        /// 移除收藏
        /// </summary>
        public bool RemoveFavorite(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            var result = _database.RemoveFavorite(tagId, songUuid);
            
            if (result)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"移除收藏: Tag={tagId}, UUID={songUuid}");
            }
            
            return result;
        }

        /// <summary>
        /// 检查是否收藏
        /// </summary>
        public bool IsFavorite(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            return _database.IsFavorite(tagId, songUuid);
        }

        /// <summary>
        /// 获取指定Tag的所有收藏
        /// </summary>
        public List<string> GetFavorites(string tagId)
        {
            if (string.IsNullOrEmpty(tagId))
                return new List<string>();

            return _database.GetFavorites(tagId);
        }

        /// <summary>
        /// 切换收藏状态
        /// </summary>
        public bool ToggleFavorite(string tagId, string songUuid)
        {
            if (IsFavorite(tagId, songUuid))
            {
                return RemoveFavorite(tagId, songUuid);
            }
            else
            {
                return AddFavorite(tagId, songUuid);
            }
        }

        #endregion

        #region 播放顺序管理

        /// <summary>
        /// 设置完整播放顺序
        /// </summary>
        public bool SetPlaylistOrder(string tagId, List<string> songUuids)
        {
            if (string.IsNullOrEmpty(tagId) || songUuids == null)
                return false;

            var result = _database.SetPlaylistOrder(tagId, songUuids);
            
            if (result)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"设置播放顺序: Tag={tagId}, Count={songUuids.Count}");
            }
            
            return result;
        }

        /// <summary>
        /// 获取播放顺序
        /// </summary>
        public List<string> GetPlaylistOrder(string tagId)
        {
            if (string.IsNullOrEmpty(tagId))
                return new List<string>();

            return _database.GetPlaylistOrder(tagId);
        }

        /// <summary>
        /// 添加歌曲到播放顺序
        /// </summary>
        public bool AddToPlaylistOrder(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            return _database.AppendToOrder(tagId, songUuid);
        }

        /// <summary>
        /// 从播放顺序中移除歌曲
        /// </summary>
        public bool RemoveFromPlaylistOrder(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            return _database.RemoveFromOrder(tagId, songUuid);
        }

        #endregion

        #region 排除列表管理

        /// <summary>
        /// 添加到排除列表
        /// </summary>
        public bool AddExcluded(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            var result = _database.AddExcluded(tagId, songUuid);
            
            if (result)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"添加到排除列表: Tag={tagId}, UUID={songUuid}");
            }
            
            return result;
        }

        /// <summary>
        /// 从排除列表移除
        /// </summary>
        public bool RemoveExcluded(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            var result = _database.RemoveExcluded(tagId, songUuid);
            
            if (result)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"从排除列表移除: Tag={tagId}, UUID={songUuid}");
            }
            
            return result;
        }

        /// <summary>
        /// 检查是否在排除列表中
        /// </summary>
        public bool IsExcluded(string tagId, string songUuid)
        {
            if (string.IsNullOrEmpty(tagId) || string.IsNullOrEmpty(songUuid))
                return false;

            return _database.IsExcluded(tagId, songUuid);
        }

        /// <summary>
        /// 获取指定Tag的所有排除歌曲
        /// </summary>
        public List<string> GetExcludedSongs(string tagId)
        {
            if (string.IsNullOrEmpty(tagId))
                return new List<string>();

            return _database.GetExcludedSongs(tagId);
        }

        /// <summary>
        /// 切换排除状态
        /// </summary>
        public bool ToggleExcluded(string tagId, string songUuid)
        {
            if (IsExcluded(tagId, songUuid))
            {
                return RemoveExcluded(tagId, songUuid);
            }
            else
            {
                return AddExcluded(tagId, songUuid);
            }
        }

        /// <summary>
        /// 批量添加到排除列表
        /// </summary>
        public int AddExcludedBatch(string tagId, IEnumerable<string> songUuids)
        {
            if (string.IsNullOrEmpty(tagId) || songUuids == null)
                return 0;

            var result = _database.AddExcludedBatch(tagId, songUuids);

            if (result > 0)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"批量添加到排除列表: Tag={tagId}, Count={result}");
            }

            return result;
        }

        /// <summary>
        /// 批量从排除列表移除
        /// </summary>
        public int RemoveExcludedBatch(string tagId, IEnumerable<string> songUuids)
        {
            if (string.IsNullOrEmpty(tagId) || songUuids == null)
                return 0;

            var result = _database.RemoveExcludedBatch(tagId, songUuids);

            if (result > 0)
            {
                BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                    .LogInfo($"批量从排除列表移除: Tag={tagId}, Count={result}");
            }

            return result;
        }

        #endregion

        #region Tag管理

        /// <summary>
        /// 清理指定Tag的所有数据（取消注册Tag时调用）
        /// </summary>
        public void ClearTag(string tagId)
        {
            if (string.IsNullOrEmpty(tagId))
                return;

            _database.ClearTag(tagId);
            
            BepInEx.Logging.Logger.CreateLogSource("CustomPlaylistData")
                .LogInfo($"清理Tag数据: {tagId}");
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 检查是否是自定义Tag（位5-15）
        /// </summary>
        public static bool IsCustomTag(AudioTag tag)
        {
            // 移除Local和Favorite标记
            var cleanTag = tag & ~AudioTag.Local & ~AudioTag.Favorite;
            
            // 检查是否在自定义Tag范围内（位5-15）
            int value = (int)cleanTag;
            
            // 如果值为0，不是自定义Tag
            if (value == 0)
                return false;
            
            // 检查最高位是否在5-15范围内
            for (int bit = 5; bit <= 15; bit++)
            {
                if ((value & (1 << bit)) != 0)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// 从GameAudioInfo中提取Tag ID
        /// </summary>
        public static string GetTagIdFromAudio(GameAudioInfo audio)
        {
            if (audio == null)
                return null;

            // 从CustomTagManager中查找对应的Tag ID
            var customTags = Music.CustomTagManager.Instance.GetAllTags();
            
            foreach (var kvp in customTags)
            {
                if (audio.Tag.HasFlagFast(kvp.Value.BitValue))
                {
                    return kvp.Key;
                }
            }
            
            return null;
        }

        #endregion

        public void Dispose()
        {
            _database?.Dispose();
            _database = null;
        }
    }
}

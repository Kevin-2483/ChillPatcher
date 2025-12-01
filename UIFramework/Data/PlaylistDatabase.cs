using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace ChillPatcher.UIFramework.Data
{
    /// <summary>
    /// 歌曲数据
    /// </summary>
    public class SongData
    {
        public string UUID { get; set; }
        public string PlaylistId { get; set; }
        public string AlbumId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public DateTime? FileModifiedAt { get; set; }
    }

    /// <summary>
    /// 数据库版本枚举
    /// </summary>
    public enum DatabaseVersion
    {
        Unknown = 0,
        V1_Legacy = 1,              // 旧版本，只有收藏和排序
        V2_WithAlbumsAndSongs = 2,  // 包含专辑和歌曲表
        V3_WithCoverCache = 3       // 包含封面缓存表
    }

    /// <summary>
    /// SQLite数据库管理类 - 存储自定义歌单的收藏和排序
    /// </summary>
    public class PlaylistDatabase : IDisposable
    {
        /// <summary>
        /// 当前数据库版本
        /// </summary>
        public const int CURRENT_DB_VERSION = (int)DatabaseVersion.V3_WithCoverCache;

        private readonly string _dbPath;
        private SQLiteConnection _connection;
        private readonly object _lock = new object();

        /// <summary>
        /// 是否需要完整重扫描（数据库版本升级时）
        /// </summary>
        public bool NeedsFullRescan { get; private set; }

        public PlaylistDatabase(string databasePath)
        {
            if (string.IsNullOrEmpty(databasePath))
                throw new ArgumentException("Database path cannot be null or empty");

            _dbPath = databasePath;
            Initialize();
        }

        /// <summary>
        /// 初始化数据库（创建表和索引）
        /// </summary>
        private void Initialize()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("PlaylistDB");
            
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 创建或打开数据库
                _connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                _connection.Open();

                // 检查数据库版本
                var existingVersion = GetDatabaseVersion();
                
                if (existingVersion == DatabaseVersion.Unknown)
                {
                    // 新数据库，创建所有表
                    CreateTables();
                    CreateIndexes();
                    SetDatabaseVersion(CURRENT_DB_VERSION);
                    logger.LogInfo($"数据库初始化成功 (新建): {_dbPath}");
                }
                else if ((int)existingVersion < CURRENT_DB_VERSION)
                {
                    // 旧版本，需要升级
                    logger.LogWarning($"检测到旧版本数据库 (v{(int)existingVersion})，需要升级到 v{CURRENT_DB_VERSION}");
                    NeedsFullRescan = true;
                    
                    // 创建新表（如果不存在）
                    CreateTables();
                    CreateIndexes();
                    SetDatabaseVersion(CURRENT_DB_VERSION);
                    logger.LogInfo($"数据库升级完成: v{(int)existingVersion} -> v{CURRENT_DB_VERSION}");
                }
                else
                {
                    logger.LogInfo($"数据库初始化成功 (已存在 v{(int)existingVersion}): {_dbPath}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"数据库初始化失败: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 获取数据库版本
        /// </summary>
        private DatabaseVersion GetDatabaseVersion()
        {
            lock (_lock)
            {
                try
                {
                    // 检查 DatabaseMeta 表是否存在
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DatabaseMeta'";
                        var result = cmd.ExecuteScalar();
                        
                        if (result == null)
                        {
                            // 检查是否是旧版数据库（有 CustomFavorites 表但没有 DatabaseMeta 表）
                            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='CustomFavorites'";
                            var hasFavorites = cmd.ExecuteScalar() != null;
                            
                            return hasFavorites ? DatabaseVersion.V1_Legacy : DatabaseVersion.Unknown;
                        }

                        // 读取版本号
                        cmd.CommandText = "SELECT value FROM DatabaseMeta WHERE key='version'";
                        var versionStr = cmd.ExecuteScalar()?.ToString();
                        
                        if (int.TryParse(versionStr, out int version))
                        {
                            return (DatabaseVersion)version;
                        }
                        
                        return DatabaseVersion.Unknown;
                    }
                }
                catch
                {
                    return DatabaseVersion.Unknown;
                }
            }
        }

        /// <summary>
        /// 设置数据库版本
        /// </summary>
        private void SetDatabaseVersion(int version)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    // 确保 DatabaseMeta 表存在
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS DatabaseMeta (
                            key TEXT PRIMARY KEY,
                            value TEXT NOT NULL,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    cmd.ExecuteNonQuery();

                    // 更新版本号
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO DatabaseMeta (key, value, updated_at)
                        VALUES ('version', @version, CURRENT_TIMESTAMP)";
                    cmd.Parameters.AddWithValue("@version", version.ToString());
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 创建数据表
        /// </summary>
        private void CreateTables()
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    // 收藏表
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CustomFavorites (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            tag_id TEXT NOT NULL,
                            song_uuid TEXT NOT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(tag_id, song_uuid)
                        )";
                    cmd.ExecuteNonQuery();

                    // 播放顺序表
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CustomPlaylistOrder (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            tag_id TEXT NOT NULL,
                            song_uuid TEXT NOT NULL,
                            order_index INTEGER NOT NULL,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(tag_id, song_uuid)
                        )";
                    cmd.ExecuteNonQuery();

                    // 排除列表表
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CustomExcludedSongs (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            tag_id TEXT NOT NULL,
                            song_uuid TEXT NOT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(tag_id, song_uuid)
                        )";
                    cmd.ExecuteNonQuery();

                    // ✅ 专辑表 - 存储专辑信息
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Albums (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            album_id TEXT NOT NULL UNIQUE,
                            playlist_id TEXT NOT NULL,
                            directory_path TEXT NOT NULL,
                            display_name TEXT NOT NULL,
                            is_other_album INTEGER DEFAULT 0,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    cmd.ExecuteNonQuery();

                    // ✅ 歌曲表 - 存储歌曲信息
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Songs (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            uuid TEXT NOT NULL UNIQUE,
                            playlist_id TEXT NOT NULL,
                            album_id TEXT,
                            file_name TEXT NOT NULL,
                            file_path TEXT NOT NULL,
                            title TEXT,
                            artist TEXT,
                            duration REAL DEFAULT 0,
                            file_modified_at DATETIME,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    cmd.ExecuteNonQuery();

                    // ✅ 专辑封面缓存表 - 存储封面路径
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS AlbumCoverCache (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            album_id TEXT NOT NULL UNIQUE,
                            cover_path TEXT NOT NULL,
                            source_type INTEGER NOT NULL DEFAULT 1,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// 创建索引
        /// </summary>
        private void CreateIndexes()
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_favorites_tag ON CustomFavorites(tag_id)";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_order_tag ON CustomPlaylistOrder(tag_id, order_index)";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_excluded_tag ON CustomExcludedSongs(tag_id)";
                    cmd.ExecuteNonQuery();

                    // ✅ 专辑表索引
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_albums_playlist ON Albums(playlist_id)";
                    cmd.ExecuteNonQuery();

                    // ✅ 歌曲表索引
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_songs_playlist ON Songs(playlist_id)";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_songs_album ON Songs(album_id)";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_songs_file ON Songs(file_path)";
                    cmd.ExecuteNonQuery();

                    // ✅ 封面缓存索引
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_cover_album ON AlbumCoverCache(album_id)";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        #region 收藏操作

        /// <summary>
        /// 添加收藏
        /// </summary>
        public bool AddFavorite(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO CustomFavorites (tag_id, song_uuid)
                            VALUES (@tagId, @songUuid)";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"添加收藏失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 移除收藏
        /// </summary>
        public bool RemoveFavorite(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            DELETE FROM CustomFavorites
                            WHERE tag_id = @tagId AND song_uuid = @songUuid";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"移除收藏失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 检查是否收藏
        /// </summary>
        public bool IsFavorite(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT COUNT(*) FROM CustomFavorites
                            WHERE tag_id = @tagId AND song_uuid = @songUuid";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"检查收藏失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取指定Tag的所有收藏UUID
        /// </summary>
        public List<string> GetFavorites(string tagId)
        {
            lock (_lock)
            {
                var favorites = new List<string>();
                
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT song_uuid FROM CustomFavorites
                            WHERE tag_id = @tagId
                            ORDER BY created_at DESC";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                favorites.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取收藏列表失败: {ex.Message}");
                }
                
                return favorites;
            }
        }

        #endregion

        #region 播放顺序操作

        /// <summary>
        /// 设置播放顺序（完整替换）
        /// </summary>
        public bool SetPlaylistOrder(string tagId, List<string> songUuids)
        {
            lock (_lock)
            {
                try
                {
                    using (var transaction = _connection.BeginTransaction())
                    {
                        try
                        {
                            // 删除旧顺序
                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM CustomPlaylistOrder WHERE tag_id = @tagId";
                                cmd.Parameters.AddWithValue("@tagId", tagId);
                                cmd.ExecuteNonQuery();
                            }

                            // 插入新顺序
                            for (int i = 0; i < songUuids.Count; i++)
                            {
                                using (var cmd = _connection.CreateCommand())
                                {
                                    cmd.CommandText = @"
                                        INSERT INTO CustomPlaylistOrder (tag_id, song_uuid, order_index)
                                        VALUES (@tagId, @songUuid, @orderIndex)";
                                    cmd.Parameters.AddWithValue("@tagId", tagId);
                                    cmd.Parameters.AddWithValue("@songUuid", songUuids[i]);
                                    cmd.Parameters.AddWithValue("@orderIndex", i);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                            return true;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"设置播放顺序失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取播放顺序
        /// </summary>
        public List<string> GetPlaylistOrder(string tagId)
        {
            lock (_lock)
            {
                var order = new List<string>();
                
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT song_uuid FROM CustomPlaylistOrder
                            WHERE tag_id = @tagId
                            ORDER BY order_index ASC";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                order.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取播放顺序失败: {ex.Message}");
                }
                
                return order;
            }
        }

        /// <summary>
        /// 添加歌曲到顺序末尾
        /// </summary>
        public bool AppendToOrder(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    // 获取当前最大索引
                    int maxIndex = -1;
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT COALESCE(MAX(order_index), -1) FROM CustomPlaylistOrder
                            WHERE tag_id = @tagId";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        maxIndex = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    // 插入新记录
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO CustomPlaylistOrder (tag_id, song_uuid, order_index)
                            VALUES (@tagId, @songUuid, @orderIndex)";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        cmd.Parameters.AddWithValue("@orderIndex", maxIndex + 1);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"添加到播放顺序失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 从播放顺序中移除歌曲
        /// </summary>
        public bool RemoveFromOrder(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            DELETE FROM CustomPlaylistOrder
                            WHERE tag_id = @tagId AND song_uuid = @songUuid";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"从播放顺序移除失败: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        #region 排除列表操作

        /// <summary>
        /// 添加到排除列表
        /// </summary>
        public bool AddExcluded(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO CustomExcludedSongs (tag_id, song_uuid)
                            VALUES (@tagId, @songUuid)";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"添加排除失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 从排除列表移除
        /// </summary>
        public bool RemoveExcluded(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            DELETE FROM CustomExcludedSongs
                            WHERE tag_id = @tagId AND song_uuid = @songUuid";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"移除排除失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 检查是否在排除列表中
        /// </summary>
        public bool IsExcluded(string tagId, string songUuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT COUNT(*) FROM CustomExcludedSongs
                            WHERE tag_id = @tagId AND song_uuid = @songUuid";
                        cmd.Parameters.AddWithValue("@tagId", tagId);
                        cmd.Parameters.AddWithValue("@songUuid", songUuid);
                        
                        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"检查排除失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取指定Tag的所有排除UUID
        /// </summary>
        public List<string> GetExcludedSongs(string tagId)
        {
            lock (_lock)
            {
                var result = new List<string>();

                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT song_uuid FROM CustomExcludedSongs
                            WHERE tag_id = @tagId
                            ORDER BY created_at";
                        cmd.Parameters.AddWithValue("@tagId", tagId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取排除列表失败: {ex.Message}");
                }

                return result;
            }
        }

        /// <summary>
        /// 批量添加到排除列表
        /// </summary>
        public int AddExcludedBatch(string tagId, IEnumerable<string> songUuids)
        {
            lock (_lock)
            {
                int count = 0;
                try
                {
                    using (var transaction = _connection.BeginTransaction())
                    {
                        try
                        {
                            foreach (var songUuid in songUuids)
                            {
                                using (var cmd = _connection.CreateCommand())
                                {
                                    cmd.CommandText = @"
                                        INSERT OR IGNORE INTO CustomExcludedSongs (tag_id, song_uuid)
                                        VALUES (@tagId, @songUuid)";
                                    cmd.Parameters.AddWithValue("@tagId", tagId);
                                    cmd.Parameters.AddWithValue("@songUuid", songUuid);
                                    count += cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"批量添加排除失败: {ex.Message}");
                    return 0;
                }
                return count;
            }
        }

        /// <summary>
        /// 批量从排除列表移除
        /// </summary>
        public int RemoveExcludedBatch(string tagId, IEnumerable<string> songUuids)
        {
            lock (_lock)
            {
                int count = 0;
                try
                {
                    using (var transaction = _connection.BeginTransaction())
                    {
                        try
                        {
                            foreach (var songUuid in songUuids)
                            {
                                using (var cmd = _connection.CreateCommand())
                                {
                                    cmd.CommandText = @"
                                        DELETE FROM CustomExcludedSongs
                                        WHERE tag_id = @tagId AND song_uuid = @songUuid";
                                    cmd.Parameters.AddWithValue("@tagId", tagId);
                                    cmd.Parameters.AddWithValue("@songUuid", songUuid);
                                    count += cmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"批量移除排除失败: {ex.Message}");
                    return 0;
                }
                return count;
            }
        }

        #endregion

        /// <summary>
        /// 清理指定Tag的所有数据
        /// </summary>
        public void ClearTag(string tagId)
        {
            lock (_lock)
            {
                try
                {
                    using (var transaction = _connection.BeginTransaction())
                    {
                        try
                        {
                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM CustomFavorites WHERE tag_id = @tagId";
                                cmd.Parameters.AddWithValue("@tagId", tagId);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM CustomPlaylistOrder WHERE tag_id = @tagId";
                                cmd.Parameters.AddWithValue("@tagId", tagId);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM CustomExcludedSongs WHERE tag_id = @tagId";
                                cmd.Parameters.AddWithValue("@tagId", tagId);
                                cmd.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"清理Tag数据失败: {ex.Message}");
                }
            }
        }

        #region 专辑操作

        /// <summary>
        /// 保存或更新专辑
        /// </summary>
        public bool SaveAlbum(string albumId, string playlistId, string directoryPath, string displayName, bool isOtherAlbum = false)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO Albums (album_id, playlist_id, directory_path, display_name, is_other_album, updated_at)
                            VALUES (@albumId, @playlistId, @directoryPath, @displayName, @isOther, CURRENT_TIMESTAMP)";
                        cmd.Parameters.AddWithValue("@albumId", albumId);
                        cmd.Parameters.AddWithValue("@playlistId", playlistId);
                        cmd.Parameters.AddWithValue("@directoryPath", directoryPath);
                        cmd.Parameters.AddWithValue("@displayName", displayName);
                        cmd.Parameters.AddWithValue("@isOther", isOtherAlbum ? 1 : 0);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"保存专辑失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取专辑信息
        /// </summary>
        public (string playlistId, string directoryPath, string displayName, bool isOtherAlbum)? GetAlbum(string albumId)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT playlist_id, directory_path, display_name, is_other_album FROM Albums WHERE album_id = @albumId";
                        cmd.Parameters.AddWithValue("@albumId", albumId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return (
                                    reader.GetString(0),
                                    reader.GetString(1),
                                    reader.GetString(2),
                                    reader.GetInt32(3) == 1
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取专辑失败: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// 获取歌单下的所有专辑
        /// </summary>
        public List<(string albumId, string displayName, bool isOtherAlbum)> GetAlbumsByPlaylist(string playlistId)
        {
            lock (_lock)
            {
                var result = new List<(string, string, bool)>();
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT album_id, display_name, is_other_album FROM Albums WHERE playlist_id = @playlistId";
                        cmd.Parameters.AddWithValue("@playlistId", playlistId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取歌单专辑失败: {ex.Message}");
                }
                return result;
            }
        }

        /// <summary>
        /// 删除专辑
        /// </summary>
        public bool DeleteAlbum(string albumId)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM Albums WHERE album_id = @albumId";
                        cmd.Parameters.AddWithValue("@albumId", albumId);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"删除专辑失败: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        #region 歌曲操作

        /// <summary>
        /// 保存或更新歌曲
        /// </summary>
        public bool SaveSong(string uuid, string playlistId, string albumId, string fileName, string filePath, string title, string artist, DateTime? fileModifiedAt)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO Songs (uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at, updated_at)
                            VALUES (@uuid, @playlistId, @albumId, @fileName, @filePath, @title, @artist, @fileModifiedAt, CURRENT_TIMESTAMP)";
                        cmd.Parameters.AddWithValue("@uuid", uuid);
                        cmd.Parameters.AddWithValue("@playlistId", playlistId);
                        cmd.Parameters.AddWithValue("@albumId", (object)albumId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fileName", fileName);
                        cmd.Parameters.AddWithValue("@filePath", filePath);
                        cmd.Parameters.AddWithValue("@title", (object)title ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@artist", (object)artist ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fileModifiedAt", (object)fileModifiedAt ?? DBNull.Value);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"保存歌曲失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 通过UUID获取歌曲
        /// </summary>
        public SongData GetSong(string uuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at FROM Songs WHERE uuid = @uuid";
                        cmd.Parameters.AddWithValue("@uuid", uuid);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return ReadSongData(reader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取歌曲失败: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// 获取专辑的所有歌曲
        /// </summary>
        public List<SongData> GetSongsByAlbum(string albumId)
        {
            lock (_lock)
            {
                var result = new List<SongData>();
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at FROM Songs WHERE album_id = @albumId";
                        cmd.Parameters.AddWithValue("@albumId", albumId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(ReadSongData(reader));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取专辑歌曲失败: {ex.Message}");
                }
                return result;
            }
        }

        /// <summary>
        /// 获取歌单的所有歌曲
        /// </summary>
        public List<SongData> GetSongsByPlaylist(string playlistId)
        {
            lock (_lock)
            {
                var result = new List<SongData>();
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at FROM Songs WHERE playlist_id = @playlistId";
                        cmd.Parameters.AddWithValue("@playlistId", playlistId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(ReadSongData(reader));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取歌单歌曲失败: {ex.Message}");
                }
                return result;
            }
        }

        /// <summary>
        /// 通过文件路径查找歌曲
        /// </summary>
        public SongData GetSongByFilePath(string filePath)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at FROM Songs WHERE file_path = @filePath";
                        cmd.Parameters.AddWithValue("@filePath", filePath);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return ReadSongData(reader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"通过路径获取歌曲失败: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// 通过UUID查找歌曲
        /// </summary>
        public SongData GetSongByUUID(string uuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT uuid, playlist_id, album_id, file_name, file_path, title, artist, file_modified_at FROM Songs WHERE uuid = @uuid";
                        cmd.Parameters.AddWithValue("@uuid", uuid);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return ReadSongData(reader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"通过UUID获取歌曲失败: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// 删除歌曲
        /// </summary>
        public bool DeleteSong(string uuid)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM Songs WHERE uuid = @uuid";
                        cmd.Parameters.AddWithValue("@uuid", uuid);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"删除歌曲失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 删除歌单的所有歌曲
        /// </summary>
        public int DeleteSongsByPlaylist(string playlistId)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM Songs WHERE playlist_id = @playlistId";
                        cmd.Parameters.AddWithValue("@playlistId", playlistId);
                        return cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"删除歌单歌曲失败: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// 辅助方法：从Reader读取SongData
        /// </summary>
        private SongData ReadSongData(System.Data.SQLite.SQLiteDataReader reader)
        {
            return new SongData
            {
                UUID = reader.GetString(0),
                PlaylistId = reader.GetString(1),
                AlbumId = reader.IsDBNull(2) ? null : reader.GetString(2),
                FileName = reader.GetString(3),
                FilePath = reader.GetString(4),
                Title = reader.IsDBNull(5) ? null : reader.GetString(5),
                Artist = reader.IsDBNull(6) ? null : reader.GetString(6),
                FileModifiedAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
            };
        }

        #endregion

        #region 封面缓存操作

        /// <summary>
        /// 获取专辑封面缓存
        /// </summary>
        public Music.CoverCacheData GetAlbumCoverCache(string albumId)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT album_id, cover_path, source_type 
                            FROM AlbumCoverCache 
                            WHERE album_id = @albumId";
                        cmd.Parameters.AddWithValue("@albumId", albumId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new Music.CoverCacheData
                                {
                                    AlbumId = reader.GetString(0),
                                    CoverPath = reader.GetString(1),
                                    SourceType = (Music.CoverSourceType)reader.GetInt32(2)
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogWarning($"获取封面缓存失败: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// 保存专辑封面缓存
        /// </summary>
        public bool SaveAlbumCoverCache(string albumId, string coverPath, int sourceType)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR REPLACE INTO AlbumCoverCache 
                            (album_id, cover_path, source_type, updated_at)
                            VALUES (@albumId, @coverPath, @sourceType, CURRENT_TIMESTAMP)";
                        cmd.Parameters.AddWithValue("@albumId", albumId);
                        cmd.Parameters.AddWithValue("@coverPath", coverPath);
                        cmd.Parameters.AddWithValue("@sourceType", sourceType);

                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"保存封面缓存失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 移除专辑封面缓存
        /// </summary>
        public bool RemoveAlbumCoverCache(string albumId)
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM AlbumCoverCache WHERE album_id = @albumId";
                        cmd.Parameters.AddWithValue("@albumId", albumId);

                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"移除封面缓存失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 清除所有封面缓存
        /// </summary>
        public int ClearAllCoverCache()
        {
            lock (_lock)
            {
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM AlbumCoverCache";
                        return cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"清除封面缓存失败: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>
        /// 获取所有歌单ID（从Songs表去重）
        /// </summary>
        public List<string> GetAllPlaylistIds()
        {
            lock (_lock)
            {
                var result = new List<string>();
                try
                {
                    using (var cmd = _connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT DISTINCT playlist_id FROM Songs";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB").LogError($"获取歌单ID列表失败: {ex.Message}");
                }
                return result;
            }
        }

        /// <summary>
        /// 删除歌单及其所有关联数据（专辑、歌曲）
        /// </summary>
        public void DeletePlaylist(string playlistId)
        {
            lock (_lock)
            {
                try
                {
                    using (var transaction = _connection.BeginTransaction())
                    {
                        try
                        {
                            // 删除歌曲
                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM Songs WHERE playlist_id = @playlistId";
                                cmd.Parameters.AddWithValue("@playlistId", playlistId);
                                cmd.ExecuteNonQuery();
                            }

                            // 删除专辑
                            using (var cmd = _connection.CreateCommand())
                            {
                                cmd.CommandText = "DELETE FROM Albums WHERE playlist_id = @playlistId";
                                cmd.Parameters.AddWithValue("@playlistId", playlistId);
                                cmd.ExecuteNonQuery();
                            }

                            transaction.Commit();
                            BepInEx.Logging.Logger.CreateLogSource("PlaylistDB")
                                .LogInfo($"删除歌单数据: {playlistId}");
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("PlaylistDB")
                        .LogError($"删除歌单失败: {ex.Message}");
                }
            }
        }

        #endregion

        public void Dispose()
        {
            lock (_lock)
            {
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
            }
        }
    }
}

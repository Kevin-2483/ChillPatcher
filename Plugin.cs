using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ChillPatcher.Patches;
using ChillPatcher.Patches.UIFramework;
using ChillPatcher.UIFramework;
using ChillPatcher.UIFramework.Audio;
using ChillPatcher.UIFramework.Config;
using ChillPatcher.UIFramework.Music;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bulbul;

namespace ChillPatcher
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;
        internal static ManualLogSource Log; // 别名，用于Patches
        
        private float healthCheckTimer = 0f;
        private const float healthCheckInterval = 5f; // 每5秒检查一次

        private void Awake()
        {
            Logger = base.Logger;
            Log = Logger; // 设置别名
            
            CoreDependencyLoader.EnsureDependencies(Log);
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            // 初始化配置
            PluginConfig.Initialize(Config);
            
            // 初始化UI框架配置
            UIFrameworkConfig.Initialize(Config);

            // Apply Harmony patches
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            Logger.LogInfo("Harmony patches applied!");
            
            // 输出配置状态
            Logger.LogInfo("==== ChillPatcher Configuration ====");
            Logger.LogInfo($"Virtual Scroll: {(UIFrameworkConfig.EnableVirtualScroll.Value ? "ON" : "OFF")} (Performance optimization)");
            Logger.LogInfo($"Album Art Display: {(UIFrameworkConfig.EnableAlbumArtDisplay.Value ? "ON" : "OFF")} (Show cover on button)");
            Logger.LogInfo($"Folder Playlists: {(PluginConfig.EnableFolderPlaylists.Value ? "ON" : "OFF")} (Runtime only)");
            Logger.LogInfo($"Unlimited Songs: {(UIFrameworkConfig.EnableUnlimitedSongs.Value ? "ON" : "OFF")} (May affect save)");
            Logger.LogInfo($"Extended Formats: {(UIFrameworkConfig.EnableExtendedFormats.Value ? "ON" : "OFF")} (OGG/FLAC/AIFF)");
            Logger.LogInfo("====================================");

            // 初始化全局键盘钩子（用于壁纸引擎模式）
            KeyboardHookPatch.Initialize();
            Logger.LogInfo("Keyboard hook initialized!");
            
            // 初始化成就同步管理器
            if (PluginConfig.EnableAchievementCache.Value)
            {
                AchievementSyncManager.Initialize();
                Logger.LogInfo("Achievement sync manager initialized!");
            }
            
            // ========== 初始化UI框架 ==========
            try
            {
                // 初始化UI框架
                ChillUIFramework.Initialize();
                Logger.LogInfo("ChillUIFramework initialized!");
                
                // 仅在启用文件夹歌单时设置扫描器（但不立即加载）
                if (PluginConfig.EnableFolderPlaylists.Value)
                {
                    Logger.LogInfo("==================================================");
                    Logger.LogInfo("ChillPatcher UI Framework initialized!");
                    Logger.LogInfo("Will load playlists after MusicService.Load");
                    Logger.LogInfo("==================================================");
                }
                else
                {
                    Logger.LogInfo("Folder playlists disabled, skipping playlist setup");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize UI Framework: {ex}");
            }
        }
        
        /// <summary>
        /// 设置文件夹歌单（异步延迟加载版本 - 在MusicService.Load后调用）
        /// </summary>
        public static async UniTask SetupFolderPlaylistsAsync()
        {
            try
            {
                if (!PluginConfig.EnableFolderPlaylists.Value)
                {
                    return;
                }
                
                var logger = BepInEx.Logging.Logger.CreateLogSource("ChillPatcher");
                
                // 获取绝对路径
                var rootPath = PlaylistDirectoryScanner.GetAbsolutePath(PluginConfig.PlaylistRootFolder.Value);
                
                logger.LogInfo($"[Playlist] 根目录: {rootPath}");
                
                // ✅ 初始化数据库
                UIFramework.Data.CustomPlaylistDataManager.Initialize(rootPath);
                logger.LogInfo($"[Playlist] 数据库已初始化");
                
                // ✅ 初始化专辑管理器
                AlbumManager.Initialize();
                logger.LogInfo($"[Playlist] 专辑管理器已初始化");
                
                // 创建扫描器（不再需要递归深度参数）
                var playlistScanner = new PlaylistDirectoryScanner(
                    rootPath,
                    ChillUIFramework.Music.AudioLoader
                );
                
                // 扫描所有歌单
                var playlists = playlistScanner.ScanAllPlaylists();
                
                // ✅ 清理不存在的歌单数据（文件夹被重命名或删除的情况）
                var validPlaylistIds = playlists.Select(p => p.Id).ToList();
                UIFramework.Data.CustomPlaylistDataManager.Instance?.CleanupStalePlaylistData(validPlaylistIds);
                
                // 注册到框架
                var registry = ChillUIFramework.Music.PlaylistRegistry;
                int customTagIndex = 0;
                var allSongs = new List<GameAudioInfo>(); // 收集所有歌曲
                var songTagMapping = new Dictionary<string, string>(); // UUID -> CustomTagId映射
                
                foreach (var provider in playlists)
                {
                    // 为每个歌单创建自定义标签
                    var customTag = (AudioTag)(1 << (16 + customTagIndex));
                    provider.Tag = customTag;
                    
                    // 注册歌单
                    registry.RegisterPlaylist(provider.Id, provider);
                    
                    // ✅ 异步构建歌单并获取歌曲列表
                    var songs = await provider.BuildPlaylist();
                    
                    // 记录歌曲和对应的CustomTagId（延迟Tag注册）
                    foreach (var song in songs)
                    {
                        allSongs.Add(song);
                        songTagMapping[song.UUID] = provider.CustomTagId;
                    }
                    
                    // 添加到标签下拉菜单
                    ChillUIFramework.Music.TagDropdown.AddCustomTag(customTag, new UIFramework.Core.TagDropdownItem
                    {
                        Tag = customTag,
                        DisplayName = provider.DisplayName,
                        Priority = 100 + customTagIndex,
                        ShowInDropdown = true
                    });
                    
                    Logger.LogInfo($"[Playlist] 注册成功: {provider.DisplayName} (标签: {customTag})");
                    
                    customTagIndex++;
                }
                
                // ✅ 批量添加所有歌曲到MusicService
                if (allSongs.Count > 0 && MusicService_RemoveLimit_Patch.CurrentInstance != null)
                {
                    var musicService = MusicService_RemoveLimit_Patch.CurrentInstance;
                    
                    // 直接操作内部列表，避免逐个调用AddMusicItem
                    var allMusicList = HarmonyLib.Traverse.Create(musicService)
                        .Field("_allMusicList")
                        .GetValue<List<GameAudioInfo>>();
                    
                    var shuffleList = HarmonyLib.Traverse.Create(musicService)
                        .Field("shuffleList")
                        .GetValue<List<GameAudioInfo>>();
                    
                    if (allMusicList != null)
                    {
                        logger.LogInfo($"[Playlist] 开始批量添加 {allSongs.Count} 首歌曲...");
                        
                        int addedCount = 0;
                        foreach (var song in allSongs)
                        {
                            // 检查重复
                            if (allMusicList.Any(m => m.UUID == song.UUID))
                            {
                                continue;
                            }
                            
                            allMusicList.Add(song);
                            shuffleList?.Add(song);
                            addedCount++;
                        }
                        
                        logger.LogInfo($"[Playlist] 批量添加完成: {addedCount} 首 (总计: {allMusicList.Count})");
                        
                        // ✅ 批量注册Tag并更新audio.Tag字段
                        logger.LogInfo($"[Playlist] 开始批量注册Custom Tag...");
                        foreach (var kvp in songTagMapping)
                        {
                            var songUUID = kvp.Key;
                            var customTagId = kvp.Value;
                            
                            // 注册Tag映射
                            UIFramework.Music.CustomTagManager.Instance.AddTagToSong(songUUID, customTagId);
                            
                            // 立即更新audio.Tag字段
                            var audio = allMusicList.FirstOrDefault(a => a.UUID == songUUID);
                            if (audio != null)
                            {
                                var customTags = UIFramework.Music.CustomTagManager.Instance.GetSongCustomTags(songUUID);
                                if (customTags != 0)
                                {
                                    audio.Tag |= customTags;
                                }
                            }
                        }
                        logger.LogInfo($"[Playlist] Custom Tag注册完成");
                    }
                }
                
                // ✅ 更新CurrentAudioTag包含所有自定义Tag
                if (MusicService_RemoveLimit_Patch.CurrentInstance != null)
                {
                    var allCustomTags = UIFramework.Music.CustomTagManager.Instance.GetAllTags();
                    if (allCustomTags.Count > 0)
                    {
                        var musicService = MusicService_RemoveLimit_Patch.CurrentInstance;
                        
                        AudioTag allBits = AudioTag.All; // 从游戏原生All开始
                        foreach (var tag in allCustomTags.Values)
                        {
                            allBits |= tag.BitValue; // 合并所有自定义Tag位
                        }
                        
                        SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value = allBits;
                        logger.LogInfo($"[Playlist] 已更新CurrentAudioTag包含所有自定义Tag: {allBits}");
                        
                        // ✅ 一次性重建CurrentPlayList
                        var allMusicList = HarmonyLib.Traverse.Create(musicService)
                            .Field("_allMusicList")
                            .GetValue<List<GameAudioInfo>>();
                        
                        var shuffleList = HarmonyLib.Traverse.Create(musicService)
                            .Field("shuffleList")
                            .GetValue<List<GameAudioInfo>>();
                        
                        if (allMusicList != null)
                        {
                            var currentPlayList = musicService.CurrentPlayList;
                            currentPlayList.Clear();
                            
                            var sourceList = musicService.IsShuffle ? shuffleList : allMusicList;
                            var filtered = sourceList.Where(m => allBits.HasFlagFast(m.Tag));
                            
                            foreach (var music in filtered)
                            {
                                currentPlayList.Add(music);
                            }
                            
                            logger.LogInfo($"[Playlist] 已重建CurrentPlayList: {currentPlayList.Count} 首歌曲");
                        }
                    }
                }
                
                logger.LogInfo($"[Playlist] 总共注册 {playlists.Count} 个歌单");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Playlist] 设置失败: {ex}");
            }
        }

        // Unity Update方法 - 每帧调用,用于定期健康检查
        private void Update()
        {
            try
            {
                healthCheckTimer += UnityEngine.Time.deltaTime;
                
                if (healthCheckTimer >= healthCheckInterval)
                {
                    healthCheckTimer = 0f;
                    KeyboardHookPatch.HealthCheck();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Update健康检查异常(已隔离): {ex.Message}");
            }
        }

        // Unity 生命周期方法 - 在应用退出时自动调用
        private void OnApplicationQuit()
        {
            Logger.LogInfo("OnApplicationQuit called - cleaning up...");
            
            // 清理键盘钩子
            KeyboardHookPatch.Cleanup();
            Logger.LogInfo("Keyboard hook cleanup completed!");
            
            // 清理UI框架
            try
            {
                ChillUIFramework.Cleanup();
                Logger.LogInfo("UI Framework cleanup completed!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during UI Framework cleanup: {ex}");
            }
        }
    }
}

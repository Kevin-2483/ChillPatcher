using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ChillPatcher.UIFramework.Audio;
using ChillPatcher.UIFramework.Core;
using UnityEngine;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 歌单目录扫描器 - 扫描playlist根目录下的一级子文件夹作为歌单
    /// </summary>
    public class PlaylistDirectoryScanner
    {
        private readonly string _rootPath;
        private readonly IAudioLoader _audioLoader;
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".egg", ".flac", ".aiff", ".aif" };
        private const string DEFAULT_PLAYLIST_NAME = "default";

        public PlaylistDirectoryScanner(string rootPath, IAudioLoader audioLoader)
        {
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            _audioLoader = audioLoader ?? throw new ArgumentNullException(nameof(audioLoader));
        }

        /// <summary>
        /// 扫描所有歌单目录
        /// </summary>
        public List<FileSystemPlaylistProvider> ScanAllPlaylists()
        {
            var playlists = new List<FileSystemPlaylistProvider>();
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");

            if (!Directory.Exists(_rootPath))
            {
                logger.LogWarning($"[Scanner] 歌单根目录不存在: {_rootPath}");
                Directory.CreateDirectory(_rootPath);
                logger.LogInfo($"[Scanner] 已创建根目录: {_rootPath}");
            }

            logger.LogInfo($"[Scanner] 开始扫描: {_rootPath}");

            // 第一步：处理根目录中的音频文件，移动到default文件夹
            MoveRootAudioFilesToDefault();

            // 第二步：扫描根目录下的一级子文件夹作为歌单
            try
            {
                var subdirectories = Directory.GetDirectories(_rootPath);
                
                foreach (var subdirectory in subdirectories)
                {
                    try
                    {
                        // 检查目录是否有音频文件（包括子目录中的专辑）
                        var audioFiles = GetAudioFilesIncludingSubdirs(subdirectory);

                        if (audioFiles.Any())
                        {
                            var provider = new FileSystemPlaylistProvider(
                                subdirectory,
                                _audioLoader
                            );

                            playlists.Add(provider);

                            logger.LogInfo(
                                $"[Scanner] 发现歌单: {provider.DisplayName} ({audioFiles.Count} 个音频文件)"
                            );
                        }
                        else
                        {
                            logger.LogInfo($"[Scanner] 跳过空目录: {Path.GetFileName(subdirectory)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"[Scanner] 创建歌单失败 '{subdirectory}': {ex.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning($"[Scanner] 访问被拒绝: {_rootPath} - {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.LogError($"[Scanner] 扫描目录失败: {_rootPath} - {ex}");
            }

            logger.LogInfo($"[Scanner] 扫描完成，发现 {playlists.Count} 个歌单");

            return playlists;
        }

        /// <summary>
        /// 将根目录中的音频文件移动到default文件夹
        /// </summary>
        private void MoveRootAudioFilesToDefault()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework");
            
            try
            {
                var rootAudioFiles = GetAudioFiles(_rootPath);
                
                if (!rootAudioFiles.Any())
                {
                    return;
                }

                logger.LogInfo($"[Scanner] 发现根目录有 {rootAudioFiles.Count} 个音频文件，将移动到 {DEFAULT_PLAYLIST_NAME} 文件夹");

                // 创建default文件夹
                var defaultPath = Path.Combine(_rootPath, DEFAULT_PLAYLIST_NAME);
                if (!Directory.Exists(defaultPath))
                {
                    Directory.CreateDirectory(defaultPath);
                    logger.LogInfo($"[Scanner] 已创建 {DEFAULT_PLAYLIST_NAME} 文件夹");
                }

                // 移动音频文件
                int movedCount = 0;
                foreach (var audioFile in rootAudioFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(audioFile);
                        var destPath = Path.Combine(defaultPath, fileName);

                        // 如果目标文件已存在，添加编号
                        if (File.Exists(destPath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            var ext = Path.GetExtension(fileName);
                            int counter = 1;
                            while (File.Exists(destPath))
                            {
                                destPath = Path.Combine(defaultPath, $"{nameWithoutExt}_{counter}{ext}");
                                counter++;
                            }
                        }

                        File.Move(audioFile, destPath);
                        movedCount++;
                        logger.LogInfo($"[Scanner] 已移动: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"[Scanner] 移动文件失败 '{audioFile}': {ex.Message}");
                    }
                }

                logger.LogInfo($"[Scanner] 成功移动 {movedCount} 个文件到 {DEFAULT_PLAYLIST_NAME} 文件夹");

                // 更新default歌单的playlist.json（如果存在）
                UpdateDefaultPlaylistJson(defaultPath, rootAudioFiles);
            }
            catch (Exception ex)
            {
                logger.LogError($"[Scanner] 处理根目录音频文件失败: {ex}");
            }
        }

        /// <summary>
        /// 更新default歌单的playlist.json，添加新移动的文件
        /// </summary>
        private void UpdateDefaultPlaylistJson(string defaultPath, List<string> movedFiles)
        {
            // 删除rescan标志文件，让FileSystemPlaylistProvider在下次加载时重新扫描
            var rescanFlagPath = Path.Combine(defaultPath, "!rescan_playlist");
            if (File.Exists(rescanFlagPath))
            {
                try
                {
                    File.Delete(rescanFlagPath);
                    BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework").LogInfo(
                        $"[Scanner] 已删除 {DEFAULT_PLAYLIST_NAME} 的重扫描标志，将在加载时更新缓存");
                }
                catch (Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework").LogWarning(
                        $"[Scanner] 删除重扫描标志失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 获取目录中的音频文件（不递归）
        /// </summary>
        private List<string> GetAudioFiles(string directoryPath)
        {
            var files = new List<string>();

            try
            {
                files = Directory.GetFiles(directoryPath)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework").LogError($"[Scanner] 读取文件列表失败: {directoryPath} - {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// 获取目录中的音频文件（包括子目录，用于检测歌单是否有内容）
        /// </summary>
        private List<string> GetAudioFilesIncludingSubdirs(string directoryPath)
        {
            var files = new List<string>();

            try
            {
                files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("ChillUIFramework").LogError($"[Scanner] 读取文件列表失败: {directoryPath} - {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// 获取绝对路径
        /// </summary>
        public static string GetAbsolutePath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            // 相对于游戏根目录（.dll所在目录）
            var gameRoot = Path.GetDirectoryName(Application.dataPath); // 通常是 Game_Data 的父目录
            if (string.IsNullOrEmpty(gameRoot))
                gameRoot = Directory.GetCurrentDirectory();

            return Path.GetFullPath(Path.Combine(gameRoot, relativePath));
        }
    }
}


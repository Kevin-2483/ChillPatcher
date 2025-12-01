using System;
using System.Collections.Generic;
using System.IO;
using Bulbul;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using R3;
using ChillPatcher.UIFramework.Audio;
using ChillPatcher.UIFramework.Music;
using ChillPatcher.UIFramework.Data;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// 将播放列表切换按钮的图标替换为当前播放音乐的封面
    /// 保持按钮的所有功能不变
    /// 
    /// ✅ 使用 Publicizer 直接访问 private 字段（消除反射开销）
    /// </summary>
    [HarmonyPatch(typeof(MusicUI))]
    public static class MusicUI_AlbumArt_Patch
    {
        
        // 存储原始图标引用
        private static Sprite _originalDeactiveIcon;
        private static Sprite _originalActiveIcon;
        
        // 存储当前的封面 Sprite
        private static Sprite _currentAlbumArtSprite;
        private static string _currentAudioPath;
        
        // 存储 Image 组件引用
        private static Image _iconDeactiveImage;
        private static Image _iconActiveImage;
        
        // 存储按钮引用
        private static InteractableUI _facilityOpenButton;
        
        // 存储原始 Mask 引用
        private static Mask _originalMask;
        private static Image _originalMaskImage;
        
        // 订阅管理
        private static IDisposable _musicPlaySubscription;

        /// <summary>
        /// 检查是否已经有封面被设置（用于 UI 重排列补丁判断）
        /// </summary>
        public static bool HasAlbumArtSet => _currentAlbumArtSprite != null;

        /// <summary>
        /// 在 Setup 方法执行后初始化封面显示
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("Setup")]
        public static void Setup_Postfix(MusicUI __instance)
        {
            // 检查配置是否启用
            if (!UIFrameworkConfig.EnableAlbumArtDisplay.Value)
            {
                Plugin.Logger.LogDebug("[MusicUI_AlbumArt_Patch] Album art display is disabled");
                return;
            }

            try
            {
                // ✅ 直接访问 _facilityOpenButton（Publicizer 消除反射）
                var facilityOpenButton = __instance._facilityOpenButton;
                if (facilityOpenButton == null)
                {
                    Plugin.Logger.LogWarning("[MusicUI_AlbumArt_Patch] Failed to get _facilityOpenButton");
                    return;
                }
                _facilityOpenButton = facilityOpenButton;

                // ✅ 直接访问 _facilityMusic（Publicizer 消除反射）
                var facilityMusic = __instance._facilityMusic;
                if (facilityMusic == null)
                {
                    Plugin.Logger.LogWarning("[MusicUI_AlbumArt_Patch] Failed to get _facilityMusic");
                    return;
                }

                // 查找 IconDeactivemage 和 IconActiveImage
                var buttonTransform = facilityOpenButton.transform;
                var deactiveImageTransform = buttonTransform.Find("IconDeactivemage");
                var activeImageTransform = buttonTransform.Find("IconActiveImage");

                if (deactiveImageTransform == null || activeImageTransform == null)
                {
                    Plugin.Logger.LogWarning("[MusicUI_AlbumArt_Patch] Failed to find icon images");
                    return;
                }

                _iconDeactiveImage = deactiveImageTransform.GetComponent<Image>();
                _iconActiveImage = activeImageTransform.GetComponent<Image>();

                if (_iconDeactiveImage == null || _iconActiveImage == null)
                {
                    Plugin.Logger.LogWarning("[MusicUI_AlbumArt_Patch] Failed to get Image components");
                    return;
                }

                // 保存原始图标
                if (_originalDeactiveIcon == null)
                {
                    _originalDeactiveIcon = _iconDeactiveImage.sprite;
                    Plugin.Logger.LogInfo($"[MusicUI_AlbumArt_Patch] Saved original deactive icon: {_originalDeactiveIcon?.name}");
                }
                
                if (_originalActiveIcon == null)
                {
                    _originalActiveIcon = _iconActiveImage.sprite;
                    Plugin.Logger.LogInfo($"[MusicUI_AlbumArt_Patch] Saved original active icon: {_originalActiveIcon?.name}");
                }
                
                // 如果启用了 UI 重排列，设置方形模式
                if (UIFrameworkConfig.EnableUIRearrange.Value)
                {
                    SetupSquareMode(buttonTransform);
                }

                // 监听音乐播放事件 - 使用静态字段保存订阅，避免 AddTo 参数问题
                if (_musicPlaySubscription != null)
                {
                    _musicPlaySubscription.Dispose();
                }
                
                _musicPlaySubscription = facilityMusic.MusicService.OnPlayMusic.Subscribe(music =>
                {
                    UpdateAlbumArt(music);
                });

                // 立即更新当前播放的音乐封面
                if (facilityMusic.PlayingMusic != null)
                {
                    UpdateAlbumArt(facilityMusic.PlayingMusic);
                }

                Plugin.Logger.LogInfo("[MusicUI_AlbumArt_Patch] Album art display initialized");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[MusicUI_AlbumArt_Patch] Error in Setup_Postfix: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 设置方形模式（移除圆形遮罩，添加阴影）
        /// </summary>
        private static void SetupSquareMode(Transform buttonTransform)
        {
            try
            {
                // 查找并禁用 Mask 组件
                var maskObj = buttonTransform.Find("Mask");
                if (maskObj != null)
                {
                    _originalMask = maskObj.GetComponent<Mask>();
                    if (_originalMask != null)
                    {
                        _originalMask.enabled = false;
                        Plugin.Logger.LogInfo("[MusicUI_AlbumArt_Patch] Disabled circular mask for square mode");
                    }
                    
                    // 同时隐藏 Mask 的 Image（圆形边框）
                    _originalMaskImage = maskObj.GetComponent<Image>();
                    if (_originalMaskImage != null)
                    {
                        _originalMaskImage.enabled = false;
                    }
                }
                
                // 方形模式保留原来的悬浮缩放效果，不需要额外添加阴影
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MusicUI_AlbumArt_Patch] Error setting up square mode: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新封面图标
        /// </summary>
        private static void UpdateAlbumArt(GameAudioInfo audioInfo)
        {
            // 检查配置是否启用
            if (!UIFrameworkConfig.EnableAlbumArtDisplay.Value)
                return;

            if (_iconDeactiveImage == null || _iconActiveImage == null)
                return;

            bool useSquareMode = UIFrameworkConfig.EnableUIRearrange.Value;

            try
            {
                // 如果是本地文件且路径存在
                if (audioInfo.PathType == AudioMode.LocalPc && !string.IsNullOrEmpty(audioInfo.LocalPath))
                {
                    // 如果已经加载过相同的文件，不需要重新加载
                    if (_currentAudioPath == audioInfo.LocalPath && _currentAlbumArtSprite != null)
                    {
                        Plugin.Logger.LogDebug($"[MusicUI_AlbumArt_Patch] Using cached album art for: {audioInfo.Title}");
                        return;
                    }

                    // 1. 先尝试读取歌曲嵌入封面
                    var albumArtTexture = AlbumArtReader.GetAlbumArt(audioInfo.LocalPath);
                    
                    // 2. 如果没有嵌入封面，尝试使用专辑封面
                    if (albumArtTexture == null)
                    {
                        albumArtTexture = TryLoadAlbumCover(audioInfo.LocalPath);
                    }
                    
                    if (albumArtTexture != null)
                    {
                        // 根据模式创建 Sprite
                        // 如果启用 UI 重排列，使用高分辨率以支持缩放
                        int resolution = useSquareMode ? UIRearrangePatch.AlbumArtResolution : 88;
                        
                        Sprite albumArtSprite;
                        if (useSquareMode)
                        {
                            albumArtSprite = AlbumArtReader.CreateSquareSprite(albumArtTexture, resolution);
                        }
                        else
                        {
                            albumArtSprite = AlbumArtReader.CreateCircularSprite(albumArtTexture, resolution);
                        }
                        
                        if (albumArtSprite != null)
                        {
                            // 销毁旧的封面 Sprite
                            if (_currentAlbumArtSprite != null)
                            {
                                UnityEngine.Object.Destroy(_currentAlbumArtSprite.texture);
                                UnityEngine.Object.Destroy(_currentAlbumArtSprite);
                            }

                            // 应用新的封面
                            _currentAlbumArtSprite = albumArtSprite;
                            _currentAudioPath = audioInfo.LocalPath;
                            
                            _iconDeactiveImage.sprite = albumArtSprite;
                            _iconActiveImage.sprite = albumArtSprite;
                            
                            Plugin.Logger.LogInfo($"[MusicUI_AlbumArt_Patch] Updated album art for: {audioInfo.Title}");
                            return;
                        }
                        else
                        {
                            // 创建 Sprite 失败，销毁纹理
                            UnityEngine.Object.Destroy(albumArtTexture);
                        }
                    }
                }

                // 如果没有封面或不是本地文件，尝试使用默认封面
                TryUseDefaultCover(useSquareMode);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[MusicUI_AlbumArt_Patch] Error updating album art: {ex.Message}");
                RestoreOriginalIcon();
            }
        }

        /// <summary>
        /// 尝试加载专辑封面（从文件夹中查找）
        /// </summary>
        private static Texture2D TryLoadAlbumCover(string audioFilePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(audioFilePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return null;

                // 查找常见的封面文件名
                string[] coverNames = { "cover", "folder", "front", "album", "artwork" };
                string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

                foreach (var name in coverNames)
                {
                    foreach (var ext in extensions)
                    {
                        var coverPath = Path.Combine(directory, name + ext);
                        if (File.Exists(coverPath))
                        {
                            var bytes = File.ReadAllBytes(coverPath);
                            var texture = new Texture2D(2, 2);
                            if (UnityEngine.ImageConversion.LoadImage(texture, bytes))
                            {
                                Plugin.Logger.LogInfo($"[MusicUI_AlbumArt_Patch] Loaded album cover from: {coverPath}");
                                return texture;
                            }
                            UnityEngine.Object.Destroy(texture);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MusicUI_AlbumArt_Patch] Error loading album cover: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 尝试使用默认封面
        /// </summary>
        private static void TryUseDefaultCover(bool useSquareMode)
        {
            try
            {
                // 加载嵌入的默认封面
                var texture = AlbumCoverLoader.LoadEmbeddedCoverTexture("ChillPatcher.Resources.defaultcover.png");
                if (texture != null)
                {
                    // 如果启用 UI 重排列，使用高分辨率以支持缩放
                    int resolution = useSquareMode ? UIRearrangePatch.AlbumArtResolution : 88;
                    
                    Sprite sprite;
                    if (useSquareMode)
                    {
                        sprite = AlbumArtReader.CreateSquareSprite(texture, resolution);
                    }
                    else
                    {
                        sprite = AlbumArtReader.CreateCircularSprite(texture, resolution);
                    }
                    
                    if (sprite != null)
                    {
                        if (_currentAlbumArtSprite != null)
                        {
                            UnityEngine.Object.Destroy(_currentAlbumArtSprite.texture);
                            UnityEngine.Object.Destroy(_currentAlbumArtSprite);
                        }
                        
                        _currentAlbumArtSprite = sprite;
                        _currentAudioPath = null;
                        
                        _iconDeactiveImage.sprite = sprite;
                        _iconActiveImage.sprite = sprite;
                        
                        Plugin.Logger.LogInfo($"[MusicUI_AlbumArt_Patch] Using default cover, resolution: {resolution}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[MusicUI_AlbumArt_Patch] Error loading default cover: {ex.Message}");
            }

            // 最后的回退：使用原始图标
            RestoreOriginalIcon();
        }

        /// <summary>
        /// 恢复原始图标
        /// </summary>
        private static void RestoreOriginalIcon()
        {
            if (_iconDeactiveImage != null && _originalDeactiveIcon != null)
            {
                _iconDeactiveImage.sprite = _originalDeactiveIcon;
            }
            
            if (_iconActiveImage != null && _originalActiveIcon != null)
            {
                _iconActiveImage.sprite = _originalActiveIcon;
            }
            
            // 清理当前封面
            if (_currentAlbumArtSprite != null)
            {
                UnityEngine.Object.Destroy(_currentAlbumArtSprite.texture);
                UnityEngine.Object.Destroy(_currentAlbumArtSprite);
                _currentAlbumArtSprite = null;
            }
            
            _currentAudioPath = null;
        }
    }
}

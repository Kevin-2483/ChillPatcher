using System;
using System.IO;
using UnityEngine;

namespace ChillPatcher.UIFramework.Audio
{
    /// <summary>
    /// 音乐封面读取器 - 使用 TagLibSharp 读取音频文件的封面
    /// </summary>
    public static class AlbumArtReader
    {
        /// <summary>
        /// 从音频文件路径读取封面并转换为 Unity Texture2D
        /// </summary>
        /// <param name="audioFilePath">音频文件的完整路径</param>
        /// <returns>封面的 Texture2D，如果没有封面或出错则返回 null</returns>
        public static Texture2D GetAlbumArt(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath))
            {
                Plugin.Logger.LogWarning("[AlbumArtReader] Audio file path is null or empty");
                return null;
            }

            if (!File.Exists(audioFilePath))
            {
                Plugin.Logger.LogWarning($"[AlbumArtReader] Audio file not found: {audioFilePath}");
                return null;
            }

            try
            {
                // 使用 TagLibSharp 读取音频文件
                using (var file = TagLib.File.Create(audioFilePath))
                {
                    // 获取封面数据
                    var pictures = file.Tag.Pictures;
                    
                    if (pictures == null || pictures.Length == 0)
                    {
                        Plugin.Logger.LogDebug($"[AlbumArtReader] No album art found in: {Path.GetFileName(audioFilePath)}");
                        return null;
                    }

                    // 获取第一张图片（通常是封面）
                    var picture = pictures[0];
                    byte[] imageData = picture.Data.Data;

                    if (imageData == null || imageData.Length == 0)
                    {
                        Plugin.Logger.LogWarning($"[AlbumArtReader] Album art data is empty: {Path.GetFileName(audioFilePath)}");
                        return null;
                    }

                    // 将字节数组转换为 Texture2D
                    Texture2D texture = new Texture2D(2, 2); // 大小会在 LoadImage 时自动调整
                    
                    if (UnityEngine.ImageConversion.LoadImage(texture, imageData))
                    {
                        Plugin.Logger.LogInfo($"[AlbumArtReader] Successfully loaded album art: {Path.GetFileName(audioFilePath)} ({texture.width}x{texture.height})");
                        return texture;
                    }
                    else
                    {
                        Plugin.Logger.LogError($"[AlbumArtReader] Failed to load image data: {Path.GetFileName(audioFilePath)}");
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }
                }
            }
            catch (TagLib.UnsupportedFormatException ex)
            {
                Plugin.Logger.LogWarning($"[AlbumArtReader] Unsupported audio format: {Path.GetFileName(audioFilePath)} - {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[AlbumArtReader] Error reading album art from {Path.GetFileName(audioFilePath)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 Texture2D 创建圆形遮罩的 Sprite
        /// </summary>
        /// <param name="texture">源纹理</param>
        /// <param name="size">Sprite 的大小（像素）</param>
        /// <returns>圆形的 Sprite</returns>
        public static Sprite CreateCircularSprite(Texture2D texture, int size = 88)
        {
            if (texture == null)
                return null;

            try
            {
                // 创建一个新的正方形纹理
                Texture2D squareTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                squareTexture.filterMode = FilterMode.Bilinear;
                
                // 计算源纹理的缩放
                float sourceAspect = (float)texture.width / texture.height;
                int sourceX, sourceY, sourceSize;
                
                if (sourceAspect > 1f)
                {
                    // 宽度大于高度，使用高度作为基准
                    sourceSize = texture.height;
                    sourceX = (texture.width - sourceSize) / 2;
                    sourceY = 0;
                }
                else
                {
                    // 高度大于宽度，使用宽度作为基准
                    sourceSize = texture.width;
                    sourceX = 0;
                    sourceY = (texture.height - sourceSize) / 2;
                }

                // 获取源纹理的中心正方形区域
                Color[] sourcePixels = texture.GetPixels(sourceX, sourceY, sourceSize, sourceSize);
                
                // 创建圆形遮罩
                Color[] pixels = new Color[size * size];
                float radius = size / 2f;
                float center = size / 2f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int index = y * size + x;
                        
                        // 计算距离中心的距离
                        float dx = x - center;
                        float dy = y - center;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);

                        // 如果在圆内，则采样源纹理
                        if (distance <= radius)
                        {
                            // 双线性插值采样源纹理
                            float u = (float)x / size;
                            float v = (float)y / size;
                            int srcX = Mathf.FloorToInt(u * sourceSize);
                            int srcY = Mathf.FloorToInt(v * sourceSize);
                            int srcIndex = srcY * sourceSize + srcX;
                            
                            if (srcIndex >= 0 && srcIndex < sourcePixels.Length)
                            {
                                pixels[index] = sourcePixels[srcIndex];
                                
                                // 边缘抗锯齿
                                float edgeDistance = radius - distance;
                                if (edgeDistance < 1f)
                                {
                                    pixels[index].a *= edgeDistance;
                                }
                            }
                            else
                            {
                                pixels[index] = Color.clear;
                            }
                        }
                        else
                        {
                            pixels[index] = Color.clear;
                        }
                    }
                }

                squareTexture.SetPixels(pixels);
                squareTexture.Apply();

                // 创建 Sprite
                Sprite sprite = Sprite.Create(
                    squareTexture,
                    new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[AlbumArtReader] Error creating circular sprite: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 Texture2D 创建方形 Sprite（无遮罩）
        /// </summary>
        /// <param name="texture">源纹理</param>
        /// <param name="size">Sprite 的大小（像素）</param>
        /// <returns>方形的 Sprite</returns>
        public static Sprite CreateSquareSprite(Texture2D texture, int size = 88)
        {
            if (texture == null)
                return null;

            try
            {
                // 创建一个新的正方形纹理
                Texture2D squareTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                squareTexture.filterMode = FilterMode.Bilinear;
                
                // 计算源纹理的缩放，裁剪成正方形
                float sourceAspect = (float)texture.width / texture.height;
                int sourceX, sourceY, sourceSize;
                
                if (sourceAspect > 1f)
                {
                    // 宽度大于高度，使用高度作为基准
                    sourceSize = texture.height;
                    sourceX = (texture.width - sourceSize) / 2;
                    sourceY = 0;
                }
                else
                {
                    // 高度大于宽度，使用宽度作为基准
                    sourceSize = texture.width;
                    sourceX = 0;
                    sourceY = (texture.height - sourceSize) / 2;
                }

                // 获取源纹理的中心正方形区域
                Color[] sourcePixels = texture.GetPixels(sourceX, sourceY, sourceSize, sourceSize);
                
                // 缩放到目标大小
                Color[] pixels = new Color[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int index = y * size + x;
                        
                        // 双线性插值采样源纹理
                        float u = (float)x / size;
                        float v = (float)y / size;
                        int srcX = Mathf.FloorToInt(u * sourceSize);
                        int srcY = Mathf.FloorToInt(v * sourceSize);
                        int srcIndex = srcY * sourceSize + srcX;
                        
                        if (srcIndex >= 0 && srcIndex < sourcePixels.Length)
                        {
                            pixels[index] = sourcePixels[srcIndex];
                        }
                        else
                        {
                            pixels[index] = Color.white;
                        }
                    }
                }

                squareTexture.SetPixels(pixels);
                squareTexture.Apply();

                // 创建 Sprite
                Sprite sprite = Sprite.Create(
                    squareTexture,
                    new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f
                );

                return sprite;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[AlbumArtReader] Error creating square sprite: {ex.Message}");
                return null;
            }
        }
    }
}

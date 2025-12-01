using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using UnityEngine.UI;
using ChillPatcher.UIFramework.Audio;
using ChillPatcher.UIFramework.Music;
using ChillPatcher.Patches.UIFramework;

namespace ChillPatcher.Patches
{
    /// <summary>
    /// UI 重排列补丁
    /// 将指定的按钮从原位置移动到目标位置
    /// 配置: UIFrameworkConfig.EnableUIRearrange
    /// </summary>
    [HarmonyPatch]
    public static class UIRearrangePatch
    {
        private static ManualLogSource _logger;
        private static bool _hasRearranged = false;

        private static ManualLogSource Logger => _logger ??= BepInEx.Logging.Logger.CreateLogSource("UIRearrange");
        
        /// <summary>
        /// TopIcons 按钮间距
        /// </summary>
        public const float TopIconsSpacing = 10f;
        
        /// <summary>
        /// TopIcons 整体缩放比例
        /// 调整整个 TopIcons 容器的大小
        /// </summary>
        public const float TopIconsContainerScale = 0.7f;
        
        /// <summary>
        /// RightIcons 整体缩放比例
        /// 调整整个 RightIcons 容器的大小
        /// </summary>
        public const float RightIconsContainerScale = 0.7f;
        
        /// <summary>
        /// RightIcons 垂直偏移量（正值向下，负值向上）
        /// </summary>
        public const float RightIconsVerticalOffset = 50f;
        
        /// <summary>
        /// UI_FacilityMusic 整体缩放比例
        /// </summary>
        public const float FacilityMusicScale = 1.0f;
        
        /// <summary>
        /// UI_FacilityMusic 水平偏移（正值向右）
        /// </summary>
        public const float FacilityMusicHorizontalOffset = -80f;
        
        /// <summary>
        /// UI_FacilityMusic 垂直偏移（正值向下）
        /// </summary>
        public const float FacilityMusicVerticalOffset = 0f;
        
        /// <summary>
        /// IconMusicPlaylist_Button 缩放比例（相对于 UI_FacilityMusic）
        /// </summary>
        public const float MusicPlaylistButtonScale = 2.0f;
        
        /// <summary>
        /// IconMusicPlaylist_Button 相对于 UI_FacilityMusic 的水平偏移（正值向右）
        /// </summary>
        public const float MusicPlaylistHorizontalOffset = 40f;
        
        /// <summary>
        /// IconMusicPlaylist_Button 相对于 UI_FacilityMusic 的垂直偏移（正值向上，离条更远）
        /// </summary>
        public const float MusicPlaylistVerticalOffset = 100f;

        /// <summary>
        /// 封面图像分辨率（像素）
        /// 建议设置为 88 * MusicPlaylistButtonScale 或更高
        /// 值越大越清晰，但内存占用也越大
        /// </summary>
        public const int AlbumArtResolution = 256;

        /// <summary>
        /// 按钮路径常量
        /// </summary>
        private static class ButtonPaths
        {
            // 注意：游戏中根节点名字是 "Paremt"（拼写错误但这就是实际名字）
            public const string RootPath = "Paremt/Canvas/UI/MostFrontArea";
            public const string BottomBackImage = "Paremt/Canvas/UI/BottomBackImage";
            public const string UIFacilityMusic = RootPath + "/UI_FacilityMusic";
            
            // 原始位置
            public const string TopIcons = RootPath + "/TopIcons";
            public const string LeftIcons = RootPath + "/LeftIcons";
            public const string CenterIcons = RootPath + "/CenterIcons";
            public const string RightIcons = RootPath + "/RightIcons";
            
            // 按钮名称
            public const string IconExit = "IconExit_Button";
            public const string IconStory = "IconStory_Button";
            public const string IconCollaboToAlterEgo = "IconCollaboToAlterEgo_Button";
            public const string IconDecoration = "IconDecoration_Button";
            public const string IconEnviroment = "IconEnviroment_Button";
            public const string IconMusicPlaylist = "IconMusicPlaylist_Button";
        }

        /// <summary>
        /// Hook 到 ChangeShowUIService.Setup
        /// </summary>
        [HarmonyPatch(typeof(ChangeShowUIService), "Setup")]
        [HarmonyPostfix]
        public static void ChangeShowUIService_Setup_Postfix()
        {
            if (!UIFrameworkConfig.EnableUIRearrange.Value)
            {
                Logger.LogInfo("UI 重排列已禁用");
                return;
            }

            // 延迟执行，确保 UI 已完全加载
            CoroutineRunner.Instance.RunDelayed(0.5f, RearrangeUI);
        }

        /// <summary>
        /// 执行 UI 重排列
        /// </summary>
        private static void RearrangeUI()
        {
            if (_hasRearranged)
            {
                Logger.LogInfo("UI 已重排列，跳过");
                return;
            }

            try
            {
                Logger.LogInfo("开始 UI 重排列...");

                // 1. 查找所有容器
                var topIcons = GameObject.Find(ButtonPaths.TopIcons);
                var leftIcons = GameObject.Find(ButtonPaths.LeftIcons);
                var centerIcons = GameObject.Find(ButtonPaths.CenterIcons);
                var rightIcons = GameObject.Find(ButtonPaths.RightIcons);

                if (topIcons == null)
                {
                    Logger.LogError("找不到 TopIcons 容器");
                    return;
                }

                // 2. 从 LeftIcons 移动按钮到 TopIcons
                MoveButtonToParent(leftIcons, ButtonPaths.IconStory, topIcons);
                MoveButtonToParent(leftIcons, ButtonPaths.IconCollaboToAlterEgo, topIcons);

                // 3. 从 CenterIcons 移动按钮到 TopIcons
                MoveButtonToParent(centerIcons, ButtonPaths.IconDecoration, topIcons);
                MoveButtonToParent(centerIcons, ButtonPaths.IconEnviroment, topIcons);

                // 4. 将 UI_FacilityMusic 移动到 LeftIcons 的位置
                MoveFacilityMusicToLeftIcons(leftIcons);

                // 5. 调整 TopIcons 的布局
                AdjustTopIconsLayout(topIcons);
                
                // 6. 调整 RightIcons 的缩放和偏移
                if (rightIcons != null)
                {
                    // 应用缩放
                    rightIcons.transform.localScale = new Vector3(RightIconsContainerScale, RightIconsContainerScale, 1f);
                    Logger.LogInfo($"已设置 RightIcons 整体缩放为 {RightIconsContainerScale}");
                    
                    // 应用垂直偏移（正值向下）
                    var rightIconsRect = rightIcons.GetComponent<RectTransform>();
                    if (rightIconsRect != null)
                    {
                        var pos = rightIconsRect.anchoredPosition;
                        pos.y -= RightIconsVerticalOffset;  // 减去是因为 UI 坐标系 Y 向上为正
                        rightIconsRect.anchoredPosition = pos;
                        Logger.LogInfo($"已设置 RightIcons 垂直偏移为 {RightIconsVerticalOffset}");
                    }
                }
                
                // 7. 隐藏 BottomBackImage
                var bottomBackImage = GameObject.Find(ButtonPaths.BottomBackImage);
                if (bottomBackImage != null)
                {
                    bottomBackImage.SetActive(false);
                    Logger.LogInfo("已隐藏 BottomBackImage");
                }
                
                // 8. 移动 IconMusicPlaylist_Button 到 UI_FacilityMusic 上方
                MoveMusicPlaylistButton(centerIcons);

                _hasRearranged = true;
                Logger.LogInfo("UI 重排列完成");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"UI 重排列失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 将 UI_FacilityMusic 移动到 LeftIcons 的位置
        /// </summary>
        private static void MoveFacilityMusicToLeftIcons(GameObject leftIcons)
        {
            if (leftIcons == null)
            {
                Logger.LogWarning("LeftIcons 为空，无法移动 UI_FacilityMusic");
                return;
            }

            // 查找 UI_FacilityMusic
            var facilityMusic = GameObject.Find(ButtonPaths.UIFacilityMusic);
            if (facilityMusic == null)
            {
                Logger.LogWarning("找不到 UI_FacilityMusic");
                return;
            }

            // 获取 LeftIcons 的 RectTransform
            var leftIconsRect = leftIcons.GetComponent<RectTransform>();
            var facilityMusicRect = facilityMusic.GetComponent<RectTransform>();

            if (leftIconsRect == null || facilityMusicRect == null)
            {
                Logger.LogError("无法获取 RectTransform");
                return;
            }

            // 记录 LeftIcons 的位置信息
            var leftIconsPosition = leftIconsRect.anchoredPosition;
            var leftIconsAnchorMin = leftIconsRect.anchorMin;
            var leftIconsAnchorMax = leftIconsRect.anchorMax;
            
            // 获取 LeftIcons 的世界坐标位置（左边缘）
            var leftIconsWorldCorners = new Vector3[4];
            leftIconsRect.GetWorldCorners(leftIconsWorldCorners);
            // corners: 0=左下, 1=左上, 2=右上, 3=右下
            var leftEdgeWorldPos = (leftIconsWorldCorners[0] + leftIconsWorldCorners[1]) / 2f;

            Logger.LogInfo($"LeftIcons 位置: {leftIconsPosition}, 锚点: ({leftIconsAnchorMin} - {leftIconsAnchorMax}), 左边缘世界坐标: {leftEdgeWorldPos}");

            // 将 UI_FacilityMusic 移动到 LeftIcons 的父节点下（同级）
            var leftIconsParent = leftIcons.transform.parent;
            facilityMusic.transform.SetParent(leftIconsParent, false);

            // 设置锚点为左边（左对齐）
            facilityMusicRect.anchorMin = new Vector2(0, leftIconsAnchorMin.y);
            facilityMusicRect.anchorMax = new Vector2(0, leftIconsAnchorMax.y);
            facilityMusicRect.pivot = new Vector2(0, 0.5f);  // 左中对齐

            // 计算位置使 UI_FacilityMusic 的左边缘与 LeftIcons 的左边缘对齐
            // 使用 LeftIcons 的 anchoredPosition.x 作为基准（因为 LeftIcons 也是左对齐的）
            facilityMusicRect.anchoredPosition = new Vector2(
                leftIconsPosition.x + FacilityMusicHorizontalOffset,
                leftIconsPosition.y - FacilityMusicVerticalOffset  // 正值向下
            );

            // 应用缩放
            facilityMusic.transform.localScale = new Vector3(FacilityMusicScale, FacilityMusicScale, 1f);

            // 隐藏原来的 LeftIcons（内容已移走）
            leftIcons.SetActive(false);

            Logger.LogInfo($"已将 UI_FacilityMusic 移动到 LeftIcons 的位置（左对齐），缩放: {FacilityMusicScale}，偏移: ({FacilityMusicHorizontalOffset}, {FacilityMusicVerticalOffset})");
        }

        /// <summary>
        /// 将 IconMusicPlaylist_Button 移动到 UI_FacilityMusic 上方
        /// </summary>
        private static void MoveMusicPlaylistButton(GameObject centerIcons)
        {
            if (centerIcons == null)
            {
                Logger.LogWarning("CenterIcons 为空，无法移动 IconMusicPlaylist_Button");
                return;
            }

            // 查找 CenterIcons 中的 IconMusicPlaylist_Button
            var musicPlaylistButton = centerIcons.transform.Find(ButtonPaths.IconMusicPlaylist);
            if (musicPlaylistButton == null)
            {
                Logger.LogWarning($"在 CenterIcons 中找不到按钮: {ButtonPaths.IconMusicPlaylist}");
                return;
            }

            Logger.LogInfo($"找到 IconMusicPlaylist_Button，原始父节点: {musicPlaylistButton.parent.name}");

            // 查找 UI_FacilityMusic
            var facilityMusic = GameObject.Find(ButtonPaths.UIFacilityMusic);
            if (facilityMusic == null)
            {
                Logger.LogWarning("找不到 UI_FacilityMusic");
                return;
            }

            // 保存原始大小
            var buttonRect = musicPlaylistButton.GetComponent<RectTransform>();
            var originalSize = buttonRect != null ? buttonRect.sizeDelta : Vector2.zero;

            Logger.LogInfo($"原始大小: {originalSize}");

            // 获取 UI_FacilityMusic 的 RectTransform
            var facilityMusicRect = facilityMusic.GetComponent<RectTransform>();
            if (buttonRect == null || facilityMusicRect == null)
            {
                Logger.LogError("无法获取 RectTransform");
                return;
            }

            // 创建一个包裹容器来应用缩放（这样悬浮动画不会覆盖缩放）
            var wrapperGo = new GameObject("MusicPlaylistButtonWrapper");
            var wrapperRect = wrapperGo.AddComponent<RectTransform>();
            
            // 设置包裹容器为 UI_FacilityMusic 的子节点
            wrapperRect.SetParent(facilityMusic.transform, false);
            
            // 设置包裹容器的锚点到左上角
            wrapperRect.anchorMin = new Vector2(0f, 1f);
            wrapperRect.anchorMax = new Vector2(0f, 1f);
            wrapperRect.pivot = new Vector2(0.5f, 0.5f);
            
            // 设置包裹容器的大小（考虑缩放后的大小）
            wrapperRect.sizeDelta = originalSize * MusicPlaylistButtonScale;
            
            // 应用缩放到包裹容器
            wrapperGo.transform.localScale = new Vector3(MusicPlaylistButtonScale, MusicPlaylistButtonScale, 1f);
            
            // 设置包裹容器的位置
            float buttonHalfWidth = (originalSize.x * MusicPlaylistButtonScale) / 2f;
            wrapperRect.anchoredPosition = new Vector2(
                MusicPlaylistHorizontalOffset + buttonHalfWidth,
                MusicPlaylistVerticalOffset
            );

            // 将按钮移动到包裹容器内部
            musicPlaylistButton.SetParent(wrapperGo.transform, false);
            
            // 重置按钮的位置和缩放（相对于包裹容器）
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = originalSize;  // 保持原始大小
            musicPlaylistButton.localScale = Vector3.one;  // 保持原始缩放，让悬浮动画正常工作

            Logger.LogInfo($"已移动 IconMusicPlaylist_Button 到 UI_FacilityMusic 内部（通过包裹容器），缩放: {MusicPlaylistButtonScale}，位置: ({wrapperRect.anchoredPosition.x}, {wrapperRect.anchoredPosition.y})");

            // 设置方形默认封面
            SetupSquareDefaultCover(musicPlaylistButton);
        }

        /// <summary>
        /// 设置方形默认封面（仅在 UI 重排列启用时调用）
        /// </summary>
        private static void SetupSquareDefaultCover(Transform buttonTransform)
        {
            try
            {
                // 禁用圆形遮罩（无论是否有封面，都需要禁用遮罩以实现方形效果）
                var maskObj = buttonTransform.Find("Mask");
                if (maskObj != null)
                {
                    var mask = maskObj.GetComponent<Mask>();
                    if (mask != null)
                    {
                        mask.enabled = false;
                        Logger.LogInfo("已禁用圆形遮罩");
                    }
                    
                    var maskImage = maskObj.GetComponent<Image>();
                    if (maskImage != null)
                    {
                        maskImage.enabled = false;
                    }
                }

                // 检查 MusicUI_AlbumArt_Patch 是否已经设置了封面
                // 如果已有封面（恢复播放状态时），不需要再设置默认封面
                if (MusicUI_AlbumArt_Patch.HasAlbumArtSet)
                {
                    Logger.LogInfo("已检测到封面设置，跳过默认封面设置");
                    return;
                }

                // 查找 IconDeactivemage 和 IconActiveImage
                var deactiveImageTransform = buttonTransform.Find("IconDeactivemage");
                var activeImageTransform = buttonTransform.Find("IconActiveImage");

                if (deactiveImageTransform == null || activeImageTransform == null)
                {
                    Logger.LogWarning("找不到按钮图标 Image 组件");
                    return;
                }

                var deactiveImage = deactiveImageTransform.GetComponent<Image>();
                var activeImage = activeImageTransform.GetComponent<Image>();

                if (deactiveImage == null || activeImage == null)
                {
                    Logger.LogWarning("找不到 Image 组件");
                    return;
                }

                // 加载默认封面并创建方形 Sprite（使用高分辨率以支持缩放）
                var defaultTexture = AlbumCoverLoader.LoadEmbeddedCoverTexture("ChillPatcher.Resources.defaultcover.png");
                if (defaultTexture != null)
                {
                    var squareSprite = AlbumArtReader.CreateSquareSprite(defaultTexture, AlbumArtResolution);
                    if (squareSprite != null)
                    {
                        deactiveImage.sprite = squareSprite;
                        activeImage.sprite = squareSprite;
                        Logger.LogInfo($"已设置方形默认封面，分辨率: {AlbumArtResolution}x{AlbumArtResolution}");
                    }
                }
                else
                {
                    Logger.LogWarning("无法加载默认封面");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"设置方形默认封面失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将按钮从源容器移动到目标容器
        /// </summary>
        private static void MoveButtonToParent(GameObject sourceContainer, string buttonName, GameObject targetContainer)
        {
            if (sourceContainer == null || targetContainer == null)
            {
                Logger.LogWarning($"容器为空，无法移动按钮 {buttonName}");
                return;
            }

            var button = sourceContainer.transform.Find(buttonName);
            if (button == null)
            {
                Logger.LogWarning($"找不到按钮: {buttonName}");
                return;
            }

            // 保存原始大小
            var rectTransform = button.GetComponent<RectTransform>();
            var originalSize = rectTransform != null ? rectTransform.sizeDelta : Vector2.zero;

            // 移动到新父节点
            button.SetParent(targetContainer.transform, false);

            // 恢复大小
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = originalSize;
            }

            Logger.LogInfo($"已移动按钮 {buttonName} 到 {targetContainer.name}");
        }

        /// <summary>
        /// 将 Transform 移动到目标容器
        /// </summary>
        /// <param name="child">要移动的子对象</param>
        /// <param name="targetContainer">目标容器</param>
        /// <param name="scale">缩放比例，1.0 表示保持原始大小</param>
        private static void MoveTransformToParent(Transform child, GameObject targetContainer, float scale = 1.0f)
        {
            if (child == null || targetContainer == null)
            {
                return;
            }

            // 保存原始大小
            var rectTransform = child.GetComponent<RectTransform>();
            var originalSize = rectTransform != null ? rectTransform.sizeDelta : Vector2.zero;

            // 移动到新父节点
            child.SetParent(targetContainer.transform, false);

            // 设置 RectTransform
            if (rectTransform != null)
            {
                // 应用缩放后的大小
                rectTransform.sizeDelta = originalSize * scale;
                
                // 重置锚点到中心，确保与其他按钮对齐
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                
                // 重置局部位置，让 LayoutGroup 来控制位置
                rectTransform.anchoredPosition = Vector2.zero;
            }

            Logger.LogInfo($"已移动 {child.name} 到 {targetContainer.name}，sizeDelta 缩放: {scale}");
        }

        /// <summary>
        /// 调整 TopIcons 的布局
        /// </summary>
        private static void AdjustTopIconsLayout(GameObject topIcons)
        {
            var rectTransform = topIcons.GetComponent<RectTransform>();
            if (rectTransform == null) return;

            // 获取现有的 VerticalLayoutGroup（如果有）
            var layoutGroup = topIcons.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                // 使用现有的 LayoutGroup，只修改间距
                layoutGroup.spacing = TopIconsSpacing;
                Logger.LogInfo($"已设置 TopIcons 间距为 {TopIconsSpacing}");
            }
            else
            {
                // 如果没有 LayoutGroup，添加一个新的
                layoutGroup = topIcons.AddComponent<VerticalLayoutGroup>();
                layoutGroup.childAlignment = TextAnchor.UpperCenter;
                layoutGroup.childControlWidth = false;
                layoutGroup.childControlHeight = false;
                layoutGroup.childForceExpandWidth = false;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.spacing = TopIconsSpacing;
                
                // 添加 ContentSizeFitter
                var sizeFitter = topIcons.GetComponent<ContentSizeFitter>();
                if (sizeFitter == null)
                {
                    sizeFitter = topIcons.AddComponent<ContentSizeFitter>();
                }
                sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                
                Logger.LogInfo($"已添加 TopIcons 布局组，间距: {TopIconsSpacing}");
            }

            // 应用整体缩放
            topIcons.transform.localScale = new Vector3(TopIconsContainerScale, TopIconsContainerScale, 1f);
            Logger.LogInfo($"已设置 TopIcons 整体缩放为 {TopIconsContainerScale}");

            // 强制刷新布局
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

            Logger.LogInfo("已调整 TopIcons 布局");
        }

        /// <summary>
        /// 重置状态（场景切换时）
        /// </summary>
        public static void Reset()
        {
            _hasRearranged = false;
        }
    }

    /// <summary>
    /// 协程运行器（单例）
    /// </summary>
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ChillPatcher_CoroutineRunner");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineRunner>();
                }
                return _instance;
            }
        }

        public void RunDelayed(float seconds, System.Action action)
        {
            StartCoroutine(DelayedAction(seconds, action));
        }

        private System.Collections.IEnumerator DelayedAction(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action?.Invoke();
        }
    }
}

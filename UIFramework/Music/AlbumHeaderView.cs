using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace ChillPatcher.UIFramework.Music
{
    /// <summary>
    /// 专辑头视图 - 在播放列表中显示专辑分隔
    /// </summary>
    public class AlbumHeaderView : MonoBehaviour
    {
        // UI 组件引用
        private RectTransform _rectTransform;
        private Image _coverImage;
        private TextMeshProUGUI _albumNameText;
        private TextMeshProUGUI _artistText;
        private TextMeshProUGUI _statsText;
        private Image _separatorLine;
        private GameObject _coverContainer;
        private Button _coverButton;

        // 数据
        private AlbumHeaderInfo _currentHeader;
        private bool _hasCover;

        // 事件
        public event Action<string> OnAlbumToggle;  // 专辑ID

        // 布局常量 - 可调整这些值来改变布局
        private const float HEADER_HEIGHT_WITH_COVER = 70f;  // 带封面时的内容区高度
        private const float HEADER_HEIGHT_NO_COVER = 45f;    // 无封面时的内容区高度（保留但不再使用）
        private const float COVER_SIZE = 50f;                 // 封面尺寸
        private const float CONTENT_LEFT_MARGIN = 10f;        // 内容区左边距
        private const float CONTENT_RIGHT_MARGIN = 35f;       // 内容区右边距（调整此值避开滚动条）
        private const float TOP_MARGIN = 15f;                 // 顶部边距
        private const float BOTTOM_MARGIN = 5f;               // 底部边距
        private const float SEPARATOR_HEIGHT = 1f;            // 分隔线高度
        private const float SEPARATOR_LEFT_MARGIN = 10f;      // 分隔线左边距
        private const float SEPARATOR_RIGHT_MARGIN = 35f;     // 分隔线右边距（调整此值避开滚动条）
        private const float TEXT_COVER_GAP = 10f;             // 文本区与封面的间距

        // 默认占位图
        private static Sprite _defaultPlaceholder;
        private static bool _placeholderLoadAttempted = false;

        /// <summary>
        /// 获取或创建默认封面占位图
        /// 优先从嵌入资源加载 defaultcover.png，失败则使用白色纹理
        /// </summary>
        private static Sprite GetDefaultPlaceholder()
        {
            if (_defaultPlaceholder == null && !_placeholderLoadAttempted)
            {
                _placeholderLoadAttempted = true;
                
                // 尝试从嵌入资源加载
                try
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream("ChillPatcher.Resources.defaultcover.png"))
                    {
                        if (stream != null)
                        {
                            var bytes = new byte[stream.Length];
                            stream.Read(bytes, 0, bytes.Length);
                            
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (texture.LoadImage(bytes))
                            {
                                _defaultPlaceholder = Sprite.Create(
                                    texture,
                                    new Rect(0, 0, texture.width, texture.height),
                                    new Vector2(0.5f, 0.5f)
                                );
                                BepInEx.Logging.Logger.CreateLogSource("AlbumHeaderView")
                                    .LogInfo("Loaded embedded default cover");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    BepInEx.Logging.Logger.CreateLogSource("AlbumHeaderView")
                        .LogWarning($"Failed to load embedded cover: {ex.Message}");
                }
            }
            
            // 如果嵌入资源加载失败，使用白色纹理作为后备
            if (_defaultPlaceholder == null)
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var whiteColor = new Color(0.95f, 0.95f, 0.95f, 1f);
                var pixels = new Color[16];
                for (int i = 0; i < 16; i++)
                    pixels[i] = whiteColor;
                texture.SetPixels(pixels);
                texture.Apply();
                
                _defaultPlaceholder = Sprite.Create(
                    texture,
                    new Rect(0, 0, 4, 4),
                    new Vector2(0.5f, 0.5f)
                );
            }
            
            return _defaultPlaceholder;
        }

        /// <summary>
        /// 带封面的头部高度
        /// </summary>
        public static float HeaderHeightWithCover => HEADER_HEIGHT_WITH_COVER + TOP_MARGIN + BOTTOM_MARGIN;

        /// <summary>
        /// 无封面的头部高度
        /// </summary>
        public static float HeaderHeightNoCover => HEADER_HEIGHT_NO_COVER + TOP_MARGIN + BOTTOM_MARGIN;

        /// <summary>
        /// 当前是否有封面
        /// </summary>
        public bool HasCover => _hasCover;

        /// <summary>
        /// 初始化组件
        /// </summary>
        public void Initialize()
        {
            _rectTransform = GetComponent<RectTransform>();
            if (_rectTransform == null)
            {
                _rectTransform = gameObject.AddComponent<RectTransform>();
            }

            // 创建UI结构
            CreateUIStructure();
        }

        /// <summary>
        /// 创建UI结构
        /// </summary>
        private void CreateUIStructure()
        {
            // 默认使用带封面的高度
            _rectTransform.sizeDelta = new Vector2(0, HeaderHeightWithCover);

            // 创建主容器（用于放置专辑信息）
            var contentArea = CreateContentArea();

            // 创建封面容器
            _coverContainer = CreateCoverContainer(contentArea);

            // 创建文本区域
            CreateTextArea(contentArea);

            // 创建底部分隔线
            CreateSeparatorLine();
        }

        /// <summary>
        /// 创建内容区域
        /// </summary>
        private RectTransform CreateContentArea()
        {
            var contentGO = new GameObject("ContentArea");
            contentGO.transform.SetParent(transform, false);

            var rt = contentGO.AddComponent<RectTransform>();
            
            // 使用拉伸锚点填充父容器，然后用 offset 控制边距
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            // offsetMin = (左边距, 底部边距)
            rt.offsetMin = new Vector2(CONTENT_LEFT_MARGIN, SEPARATOR_HEIGHT + BOTTOM_MARGIN);
            // offsetMax = (-右边距, -顶部边距)  负值表示向内收缩
            rt.offsetMax = new Vector2(-CONTENT_RIGHT_MARGIN, -TOP_MARGIN);

            return rt;
        }

        /// <summary>
        /// 创建封面容器
        /// </summary>
        private GameObject CreateCoverContainer(RectTransform parent)
        {
            var coverGO = new GameObject("CoverContainer");
            coverGO.transform.SetParent(parent, false);

            var rt = coverGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(COVER_SIZE, COVER_SIZE);
            rt.anchoredPosition = new Vector2(0, 0);

            // 创建封面图片
            var imageGO = new GameObject("CoverImage");
            imageGO.transform.SetParent(coverGO.transform, false);

            var imageRT = imageGO.AddComponent<RectTransform>();
            imageRT.anchorMin = Vector2.zero;
            imageRT.anchorMax = Vector2.one;
            imageRT.offsetMin = Vector2.zero;
            imageRT.offsetMax = Vector2.zero;

            _coverImage = imageGO.AddComponent<Image>();
            _coverImage.color = new Color(0.3f, 0.3f, 0.3f, 0.5f); // 默认深灰色背景

            // 添加按钮组件使封面可点击
            _coverButton = coverGO.AddComponent<Button>();
            _coverButton.targetGraphic = _coverImage;
            _coverButton.onClick.AddListener(OnCoverClicked);

            // 添加颜色过渡效果
            var colors = _coverButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            _coverButton.colors = colors;

            return coverGO;
        }

        /// <summary>
        /// 封面点击事件
        /// </summary>
        private void OnCoverClicked()
        {
            if (_currentHeader != null && !string.IsNullOrEmpty(_currentHeader.AlbumId))
            {
                OnAlbumToggle?.Invoke(_currentHeader.AlbumId);
            }
        }

        /// <summary>
        /// 创建文本区域
        /// </summary>
        private void CreateTextArea(RectTransform parent)
        {
            var textAreaGO = new GameObject("TextArea");
            textAreaGO.transform.SetParent(parent, false);

            var rt = textAreaGO.AddComponent<RectTransform>();
            // 文本区域填充父容器（ContentArea已经有边距了）
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            // 左边距 = 封面尺寸 + 间距（有封面时），否则为0
            rt.offsetMin = new Vector2(COVER_SIZE + TEXT_COVER_GAP, 0);
            rt.offsetMax = new Vector2(0, 0);

            // 专辑名
            _albumNameText = CreateText(rt, "AlbumName", 16, FontStyles.Bold);
            var albumRT = _albumNameText.GetComponent<RectTransform>();
            albumRT.anchorMin = new Vector2(0, 0.55f);
            albumRT.anchorMax = new Vector2(0.75f, 1);
            albumRT.offsetMin = Vector2.zero;
            albumRT.offsetMax = Vector2.zero;

            // 艺术家
            _artistText = CreateText(rt, "Artist", 12, FontStyles.Normal);
            _artistText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            var artistRT = _artistText.GetComponent<RectTransform>();
            artistRT.anchorMin = new Vector2(0, 0.1f);
            artistRT.anchorMax = new Vector2(0.75f, 0.55f);
            artistRT.offsetMin = Vector2.zero;
            artistRT.offsetMax = Vector2.zero;

            // 统计信息（右对齐，留出滚动条空间）
            _statsText = CreateText(rt, "Stats", 12, FontStyles.Normal);
            _statsText.alignment = TextAlignmentOptions.Right;
            _statsText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            var statsRT = _statsText.GetComponent<RectTransform>();
            statsRT.anchorMin = new Vector2(0.75f, 0.3f);
            statsRT.anchorMax = new Vector2(1, 0.7f);
            statsRT.offsetMin = Vector2.zero;
            statsRT.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 创建文本组件
        /// </summary>
        private TextMeshProUGUI CreateText(RectTransform parent, string name, int fontSize, FontStyles style)
        {
            var textGO = new GameObject(name);
            textGO.transform.SetParent(parent, false);

            var rt = textGO.AddComponent<RectTransform>();
            var text = textGO.AddComponent<TextMeshProUGUI>();
            
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = Color.white;

            return text;
        }

        /// <summary>
        /// 创建分隔线
        /// </summary>
        private void CreateSeparatorLine()
        {
            var lineGO = new GameObject("Separator");
            lineGO.transform.SetParent(transform, false);

            var rt = lineGO.AddComponent<RectTransform>();
            // 分隔线锚定到底部，水平拉伸
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            // offsetMin = (左边距, 0)
            rt.offsetMin = new Vector2(SEPARATOR_LEFT_MARGIN, 0);
            // offsetMax = (-右边距, 高度)
            rt.offsetMax = new Vector2(-SEPARATOR_RIGHT_MARGIN, SEPARATOR_HEIGHT);

            _separatorLine = lineGO.AddComponent<Image>();
            _separatorLine.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        }

        /// <summary>
        /// 设置数据
        /// </summary>
        public void Setup(AlbumHeaderInfo header)
        {
            _currentHeader = header;

            if (header == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            // 判断是否有真实封面
            bool hasRealCover = header.CoverImage != null;
            
            // 所有专辑头都使用带封面的布局
            _hasCover = true;
            
            // 判断专辑是否禁用（所有歌曲都被排除）
            bool isDisabled = header.EnabledSongCount == 0 && header.TotalSongCount > 0;

            // 调整高度和布局（始终使用带封面的布局）
            AdjustLayout();

            // 设置文本
            if (_albumNameText != null)
            {
                _albumNameText.text = header.DisplayName;
                // 禁用时显示灰色
                _albumNameText.color = isDisabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Color.white;
            }

            if (_artistText != null)
            {
                if (!string.IsNullOrEmpty(header.Artist))
                {
                    _artistText.gameObject.SetActive(true);
                    _artistText.text = header.Artist;
                    // 禁用时显示灰色
                    _artistText.color = isDisabled ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : new Color(0.8f, 0.8f, 0.8f, 1f);
                }
                else
                {
                    _artistText.gameObject.SetActive(false);
                }
            }

            if (_statsText != null)
            {
                _statsText.text = header.StatsText;
                // 禁用时显示红色，否则正常颜色
                _statsText.color = isDisabled ? new Color(0.7f, 0.3f, 0.3f, 0.8f) : new Color(0.6f, 0.6f, 0.6f, 1f);
            }

            // 设置封面（始终显示，无封面时使用白色占位图）
            if (_coverContainer != null)
            {
                _coverContainer.SetActive(true);
                
                if (hasRealCover)
                {
                    _coverImage.sprite = header.CoverImage;
                    // 禁用时显示灰色半透明
                    _coverImage.color = isDisabled ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : Color.white;
                }
                else
                {
                    // 使用白色占位图
                    _coverImage.sprite = GetDefaultPlaceholder();
                    // 禁用时显示更暗的灰色
                    _coverImage.color = isDisabled ? new Color(0.4f, 0.4f, 0.4f, 0.5f) : new Color(0.85f, 0.85f, 0.85f, 1f);
                }
            }
        }

        /// <summary>
        /// 调整布局（根据是否有封面）
        /// </summary>
        private void AdjustLayout()
        {
            // 调整高度
            float height = _hasCover ? HeaderHeightWithCover : HeaderHeightNoCover;
            _rectTransform.sizeDelta = new Vector2(_rectTransform.sizeDelta.x, height);

            // 调整文本区域位置
            var textAreaGO = transform.Find("ContentArea/TextArea");
            if (textAreaGO != null)
            {
                var textRT = textAreaGO.GetComponent<RectTransform>();
                if (_hasCover)
                {
                    textRT.offsetMin = new Vector2(COVER_SIZE + TEXT_COVER_GAP, 0);
                }
                else
                {
                    textRT.offsetMin = new Vector2(0, 0);
                }
            }
        }

        /// <summary>
        /// 清理
        /// </summary>
        public void Clear()
        {
            _currentHeader = null;
            _hasCover = false;
            
            if (_albumNameText != null) _albumNameText.text = "";
            if (_artistText != null) _artistText.text = "";
            if (_statsText != null) _statsText.text = "";
            if (_coverImage != null)
            {
                _coverImage.sprite = null;
                _coverImage.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            }
        }
    }
}

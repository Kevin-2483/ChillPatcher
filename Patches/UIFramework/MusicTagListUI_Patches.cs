using System;
using System.Collections.Generic;
using System.Linq;
using Bulbul;
using ChillPatcher.UIFramework.Music;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ChillPatcher.Patches.UIFramework
{
    /// <summary>
    /// MusicTagListUI补丁：隐藏空Tag + 自定义Tag + 队列操作按钮
    /// </summary>
    [HarmonyPatch(typeof(MusicTagListUI))]
    public class MusicTagListUI_Patches
    {
        private static List<GameObject> _customTagButtons = new List<GameObject>();
        
        // 队列操作按钮
        private static GameObject _clearAllQueueButton;
        private static GameObject _clearFutureQueueButton;
        private static GameObject _clearHistoryButton;
        
        // TodoSwitchFinishButton 缓存
        private static GameObject _todoSwitchFinishButton;
        private static bool _todoSwitchFinishButtonWasActive = false;
        
        // 缓存的原始状态
        private static MusicTagListUI _cachedTagListUI;
        private static bool _isQueueMode = false;

        /// <summary>
        /// Setup后处理：隐藏空Tag + 添加自定义Tag按钮
        /// </summary>
        [HarmonyPatch("Setup")]
        [HarmonyPostfix]
        static void Setup_Postfix(MusicTagListUI __instance)
        {
            try
            {
                // 1. 隐藏空Tag功能
                if (PluginConfig.HideEmptyTags.Value)
                {
                    HideEmptyTags(__instance);
                }

                // 2. 添加自定义Tag按钮
                if (PluginConfig.EnableFolderPlaylists.Value)
                {
                    AddCustomTagButtons(__instance);
                }

                // 3. 更新下拉框高度
                if (PluginConfig.HideEmptyTags.Value || PluginConfig.EnableFolderPlaylists.Value)
                {
                    UpdateDropdownHeight(__instance);
                }
                
                // 4. 打印按钮高度调试信息
                DebugPrintButtonHeights(__instance);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Error in MusicTagListUI_Patches.Setup_Postfix: {ex}");
            }
        }
        
        /// <summary>
        /// 打印按钮高度调试信息（延迟执行以确保布局已更新）
        /// </summary>
        private static void DebugPrintButtonHeights(MusicTagListUI tagListUI)
        {
            // 延迟执行
            DebugPrintButtonHeightsAsync(tagListUI).Forget();
        }
        
        private static async UniTaskVoid DebugPrintButtonHeightsAsync(MusicTagListUI tagListUI)
        {
            // 等待2帧让布局更新
            await UniTask.DelayFrame(2);
            
            try
            {
                var pulldown = Traverse.Create(tagListUI).Field("_pulldown").GetValue<PulldownListUI>();
                if (pulldown == null) return;
                
                var pullDownParentRect = Traverse.Create(pulldown).Field("_pullDownParentRect").GetValue<RectTransform>();
                if (pullDownParentRect == null) return;
                
                var tagListContainer = pullDownParentRect.Find("TagList");
                if (tagListContainer == null) return;
                
                // 强制刷新布局
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(tagListContainer as RectTransform);
                
                var layout = tagListContainer.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    Plugin.Log.LogInfo($"[DebugLayout] VerticalLayoutGroup: spacing={layout.spacing}, padding=(T:{layout.padding.top}, B:{layout.padding.bottom}, L:{layout.padding.left}, R:{layout.padding.right})");
                }
                
                // 使用ContentSizeFitter的preferredSize
                var contentSizeFitter = tagListContainer.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    var layoutElement = tagListContainer.GetComponent<LayoutElement>();
                    Plugin.Log.LogInfo($"[DebugLayout] ContentSizeFitter found, mode: H={contentSizeFitter.horizontalFit}, V={contentSizeFitter.verticalFit}");
                }
                
                var buttons = Traverse.Create(tagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
                if (buttons != null && buttons.Length >= 2)
                {
                    // 统计可见按钮
                    var visibleButtons = buttons.Where(b => b != null && b.gameObject.activeSelf).ToArray();
                    Plugin.Log.LogInfo($"[DebugLayout] Total buttons: {buttons.Length}, Visible: {visibleButtons.Length}");
                    
                    if (visibleButtons.Length >= 2)
                    {
                        var btn1 = visibleButtons[0];
                        var btn2 = visibleButtons[1];
                        
                        var rect1 = btn1.GetComponent<RectTransform>();
                        var rect2 = btn2.GetComponent<RectTransform>();
                        
                        if (rect1 != null && rect2 != null)
                        {
                            Plugin.Log.LogInfo($"[DebugLayout] Button1 '{btn1.name}': position={rect1.anchoredPosition}, size={rect1.rect.size}, localPos={rect1.localPosition}");
                            Plugin.Log.LogInfo($"[DebugLayout] Button2 '{btn2.name}': position={rect2.anchoredPosition}, size={rect2.rect.size}, localPos={rect2.localPosition}");
                            
                            // 计算实际高度差（Y坐标差值的绝对值）
                            float heightDiff = Mathf.Abs(rect1.localPosition.y - rect2.localPosition.y);
                            Plugin.Log.LogInfo($"[DebugLayout] Height difference between buttons: {heightDiff}");
                            
                            // 计算真实按钮高度（包含spacing）
                            float buttonHeight = rect1.rect.height;
                            Plugin.Log.LogInfo($"[DebugLayout] Single button height: {buttonHeight}");
                            Plugin.Log.LogInfo($"[DebugLayout] Effective row height (button + spacing): {heightDiff}");
                        }
                    }
                }
                
                // 打印TagList容器的实际大小
                var tagListRect = tagListContainer as RectTransform;
                if (tagListRect != null)
                {
                    Plugin.Log.LogInfo($"[DebugLayout] TagList container size: {tagListRect.rect.size}, sizeDelta: {tagListRect.sizeDelta}");
                }
                
                // 打印原始下拉框高度设置
                float openHeight = Traverse.Create(pulldown).Field("_openPullDownSizeDeltaY").GetValue<float>();
                Plugin.Log.LogInfo($"[DebugLayout] Original _openPullDownSizeDeltaY: {openHeight}");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[DebugLayout] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 隐藏没有歌曲的Tag按钮
        /// </summary>
        private static void HideEmptyTags(MusicTagListUI tagListUI)
        {
            // 获取MusicService引用
            var musicService = Traverse.Create(tagListUI)
                .Field("musicService")
                .GetValue<MusicService>();

            if (musicService == null)
                return;

            // 获取所有Tag按钮
            var buttons = Traverse.Create(tagListUI)
                .Field("buttons")
                .GetValue<MusicTagListButton[]>();

            if (buttons == null || buttons.Length == 0)
                return;

            // 获取所有歌曲列表
            var allMusicList = musicService.AllMusicList;
            if (allMusicList == null)
                return;

            // 检查每个Tag按钮
            foreach (var button in buttons)
            {
                var tag = button.Tag;

                // 跳过All（总是显示）
                if (tag == AudioTag.All)
                    continue;

                // 检查是否有歌曲属于这个Tag
                bool hasMusic = allMusicList.Any(audio => audio.Tag.HasFlagFast(tag));

                // 如果没有歌曲，隐藏这个按钮
                if (!hasMusic)
                {
                    button.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 添加自定义Tag按钮
        /// </summary>
        private static void AddCustomTagButtons(MusicTagListUI tagListUI)
        {
            // 清除旧的自定义按钮
            foreach (var btn in _customTagButtons)
            {
                if (btn != null)
                    UnityEngine.Object.Destroy(btn);
            }
            _customTagButtons.Clear();

            // 获取按钮容器
            var buttons = Traverse.Create(tagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
            if (buttons == null || buttons.Length == 0)
                return;

            // ✅ 获取MusicService以便同步按钮状态
            var musicService = Traverse.Create(tagListUI).Field("musicService").GetValue<MusicService>();
            if (musicService == null)
            {
                Plugin.Log.LogWarning("[AddCustomTagButtons] MusicService is null, cannot sync button states");
                return;
            }

            // 获取TagList容器（父物体）
            var firstButton = buttons[0];
            var container = firstButton.transform.parent;

            // 获取按钮预制体（克隆第一个按钮）
            var buttonPrefab = firstButton.gameObject;

            // 添加自定义Tag按钮
            var customTags = CustomTagManager.Instance.GetAllTags();
            foreach (var tagKvp in customTags)
            {
                var customTag = tagKvp.Value;

                // 克隆按钮
                var newButtonObj = UnityEngine.Object.Instantiate(buttonPrefab, container);
                newButtonObj.name = $"CustomTag_{customTag.Id}";

                var newButton = newButtonObj.GetComponent<MusicTagListButton>();
                if (newButton != null)
                {
                    // ✅ 设置按钮Tag为实际的位值
                    Traverse.Create(newButton).Field("Tag").SetValue(customTag.BitValue);

                    // ✅ 找到Buttons/TagName子物体并替换为纯Text
                    var buttonsContainer = newButtonObj.transform.Find("Buttons");
                    if (buttonsContainer != null)
                    {
                        // 找到原来的TagName
                        var oldTagName = buttonsContainer.Find("TagName");
                        if (oldTagName != null)
                        {
                            // 保存布局和样式信息
                            var oldRect = oldTagName.GetComponent<RectTransform>();
                            var oldText = oldTagName.GetComponent<TMPro.TMP_Text>();
                            
                            // 记录位置信息
                            Vector2 anchorMin = oldRect.anchorMin;
                            Vector2 anchorMax = oldRect.anchorMax;
                            Vector2 anchoredPosition = oldRect.anchoredPosition;
                            Vector2 sizeDelta = oldRect.sizeDelta;
                            Vector2 pivot = oldRect.pivot;
                            Vector3 localScale = oldRect.localScale;
                            
                            // 记录文本样式
                            TMPro.TMP_FontAsset font = oldText.font;
                            float fontSize = oldText.fontSize;
                            Color color = oldText.color;
                            TMPro.TextAlignmentOptions alignment = oldText.alignment;
                            bool enableAutoSizing = oldText.enableAutoSizing;
                            float fontSizeMin = oldText.fontSizeMin;
                            float fontSizeMax = oldText.fontSizeMax;
                            bool raycastTarget = oldText.raycastTarget;
                            
                        // 销毁旧的TagName（带本地化组件）
                        UnityEngine.Object.Destroy(oldTagName.gameObject);                            // 创建新的TagName（不带本地化组件）
                            var newTagName = new GameObject("TagName");
                            newTagName.transform.SetParent(buttonsContainer, false);
                            
                            // 复制RectTransform
                            var newRect = newTagName.AddComponent<RectTransform>();
                            newRect.anchorMin = anchorMin;
                            newRect.anchorMax = anchorMax;
                            newRect.anchoredPosition = anchoredPosition;
                            newRect.sizeDelta = sizeDelta;
                            newRect.pivot = pivot;
                            newRect.localScale = localScale;
                            
                            // 添加TMP_Text（复制样式但不添加本地化组件）
                            var newText = newTagName.AddComponent<TMPro.TextMeshProUGUI>();
                            newText.text = customTag.DisplayName;  // ← 设置自定义文本
                            newText.font = font;
                            newText.fontSize = fontSize;
                            newText.color = color;
                            newText.alignment = alignment;
                            newText.enableAutoSizing = enableAutoSizing;
                            newText.fontSizeMin = fontSizeMin;
                            newText.fontSizeMax = fontSizeMax;
                            newText.raycastTarget = raycastTarget;
                            
                            // 保存到MusicTagListButton的_text字段
                            Traverse.Create(newButton).Field("_text").SetValue(newText);
                            
                            Plugin.Log.LogInfo($"[CustomTag] Created pure text button: {customTag.DisplayName}");
                        }
                    }

                    // ✅ 设置点击事件（直接操作MusicService.CurrentAudioTag）
                    SetupCustomTagButton(newButton, customTag, tagListUI);
                    
                    // ✅ 同步按钮初始状态 (根据CurrentAudioTag是否包含该位)
                    var currentTag = SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value;
                    bool isActive = currentTag.HasFlagFast(customTag.BitValue);
                    newButton.SetCheck(isActive);
                    Plugin.Log.LogDebug($"[CustomTag] Button '{customTag.DisplayName}' initial state: {(isActive ? "Checked" : "Unchecked")} (CurrentTag: {currentTag})");

                    _customTagButtons.Add(newButtonObj);
                }
            }

            // ✅ 添加完所有自定义Tag后，强制刷新容器布局
            if (_customTagButtons.Count > 0 && container != null)
            {
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(container as UnityEngine.RectTransform);
                Plugin.Log.LogInfo($"[AddCustomTagButtons] Added {_customTagButtons.Count} custom tag buttons, forced layout rebuild");
            }
        }

        /// <summary>
        /// 设置自定义Tag按钮点击事件
        /// ✅ 直接操作MusicService.CurrentAudioTag，完全复用游戏筛选逻辑
        /// </summary>
        private static void SetupCustomTagButton(MusicTagListButton button, CustomTag customTag, MusicTagListUI tagListUI)
        {
            var musicService = Traverse.Create(tagListUI).Field("musicService").GetValue<MusicService>();
            if (musicService == null)
                return;

            // 订阅按钮点击
            button.GetComponent<UnityEngine.UI.Button>()?.onClick.AddListener(() =>
            {
                // 获取当前Tag状态
                var currentTag = SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value;
                bool hasTag = currentTag.HasFlagFast(customTag.BitValue);

                // ✅ 使用位运算切换
                if (hasTag)
                {
                    SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value = currentTag.RemoveFlag(customTag.BitValue);
                    Plugin.Log.LogInfo($"[CustomTag] Removed: {customTag.DisplayName} ({customTag.BitValue})");
                }
                else
                {
                    SaveDataManager.Instance.MusicSetting.CurrentAudioTag.Value = currentTag.AddFlag(customTag.BitValue);
                    Plugin.Log.LogInfo($"[CustomTag] Added: {customTag.DisplayName} ({customTag.BitValue})");
                }

                // 更新按钮UI
                button.SetCheck(!hasTag);
                
                // ✅ 直接调用 SetTitle 更新标题显示（Publicizer 消除反射）
                tagListUI.SetTitle();
                
                // ✅ CurrentAudioTag变化会自动触发游戏的筛选逻辑！
                // 不需要手动调用ApplyFilter，游戏已经订阅了ReactiveProperty
            });
        }

        /// <summary>
        /// 更新下拉框高度
        /// </summary>
        private static void UpdateDropdownHeight(MusicTagListUI tagListUI)
        {
            var pulldown = Traverse.Create(tagListUI)
                .Field("_pulldown")
                .GetValue<PulldownListUI>();

            if (pulldown == null)
                return;

            // 获取下拉列表的Content
            var pullDownParentRect = Traverse.Create(pulldown)
                .Field("_pullDownParentRect")
                .GetValue<UnityEngine.RectTransform>();

            if (pullDownParentRect == null)
                return;

            // 重新计算打开时的高度（根据内容实际高度）
            var contentTransform = pullDownParentRect.Find("TagList");
            if (contentTransform == null)
                return;

            // ✅ 统计实际显示的按钮数量（排除被HideEmptyTags隐藏的）
            int visibleNativeButtonCount = 0;
            var nativeButtons = Traverse.Create(tagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
            if (nativeButtons != null)
            {
                foreach (var btn in nativeButtons)
                {
                    if (btn != null && btn.gameObject.activeSelf)
                        visibleNativeButtonCount++;
                }
            }
            
            int customButtonCount = _customTagButtons.Count;
            int totalVisibleButtonCount = visibleNativeButtonCount + customButtonCount;
            
            Plugin.Log.LogInfo($"[UpdateDropdownHeight] Native (visible): {visibleNativeButtonCount}, Custom: {customButtonCount}, Total: {totalVisibleButtonCount}");

            // 强制刷新布局（确保自定义按钮也被计算）
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(pullDownParentRect);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentTransform as UnityEngine.RectTransform);

            // ✅ 直接根据按钮数量计算内容高度
            // 实测数据: 按钮高度=45
            const float buttonHeight = 45f;  // 实测按钮高度
            
            // 公式：finalHeight = a × (按钮数 × 高度) + b
            // a = 系数, b = 用户偏移
            float a = PluginConfig.TagDropdownHeightMultiplier.Value;
            float b = PluginConfig.TagDropdownHeightOffset.Value;
            float finalHeight = a * (totalVisibleButtonCount * buttonHeight) + b;

            // 更新下拉框打开时的目标高度
            Traverse.Create(pulldown).Field("_openPullDownSizeDeltaY").SetValue(finalHeight);

            Plugin.Log.LogInfo($"Tag dropdown: {a} × ({totalVisibleButtonCount} × {buttonHeight}) + {b} = {finalHeight:F1}");
        }
        
        #region 队列模式切换
        
        /// <summary>
        /// 切换到队列模式 - 隐藏Tag显示队列操作按钮
        /// </summary>
        public static void SwitchToQueueMode()
        {
            if (_isQueueMode) return;
            _isQueueMode = true;
            
            var tagListUI = UnityEngine.Object.FindObjectOfType<MusicTagListUI>();
            if (tagListUI == null)
            {
                Plugin.Log.LogWarning("[TagListUI] Cannot find MusicTagListUI");
                return;
            }
            
            _cachedTagListUI = tagListUI;
            
            // 获取下拉框
            var pulldown = Traverse.Create(tagListUI).Field("_pulldown").GetValue<PulldownListUI>();
            if (pulldown == null) return;
            
            // 关闭下拉框（如果打开的话）
            pulldown.ClosePullDown(true);
            
            // 更改标题为"队列动作"
            pulldown.ChangeSelectContentText("队列动作");
            
            // 获取Tag按钮容器
            var pullDownParentRect = Traverse.Create(pulldown).Field("_pullDownParentRect").GetValue<RectTransform>();
            if (pullDownParentRect == null) return;
            
            var tagListContainer = pullDownParentRect.Find("TagList");
            if (tagListContainer == null) return;
            
            // 隐藏 TodoSwitchFinishButton
            HideTodoSwitchFinishButton(tagListContainer);
            
            // 隐藏所有原生Tag按钮
            var buttons = Traverse.Create(tagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
            if (buttons != null)
            {
                foreach (var btn in buttons)
                {
                    if (btn != null)
                        btn.gameObject.SetActive(false);
                }
            }
            
            // 隐藏自定义Tag按钮
            foreach (var btn in _customTagButtons)
            {
                if (btn != null)
                    btn.SetActive(false);
            }
            
            // 创建队列操作按钮（直接添加到容器）并获取按钮数量
            int queueButtonCount = CreateQueueButtons(tagListContainer);
            
            // 更新下拉框高度（根据实际创建的按钮数量）
            UpdateDropdownHeightForQueueMode(pulldown, queueButtonCount);
            
            Plugin.Log.LogInfo($"[TagListUI] Switched to queue mode with {queueButtonCount} buttons");
        }
        
        /// <summary>
        /// 切换回正常模式 - 恢复Tag显示
        /// </summary>
        public static void SwitchToNormalMode()
        {
            if (!_isQueueMode) return;
            _isQueueMode = false;
            
            var tagListUI = _cachedTagListUI ?? UnityEngine.Object.FindObjectOfType<MusicTagListUI>();
            if (tagListUI == null) return;
            
            // 获取下拉框
            var pulldown = Traverse.Create(tagListUI).Field("_pulldown").GetValue<PulldownListUI>();
            if (pulldown == null) return;
            
            // 关闭下拉框
            pulldown.ClosePullDown(true);
            
            // 销毁队列操作按钮
            if (_clearAllQueueButton != null)
            {
                UnityEngine.Object.Destroy(_clearAllQueueButton);
                _clearAllQueueButton = null;
            }
            if (_clearFutureQueueButton != null)
            {
                UnityEngine.Object.Destroy(_clearFutureQueueButton);
                _clearFutureQueueButton = null;
            }
            if (_clearHistoryButton != null)
            {
                UnityEngine.Object.Destroy(_clearHistoryButton);
                _clearHistoryButton = null;
            }
            
            // 恢复 TodoSwitchFinishButton
            ShowTodoSwitchFinishButton();
            
            // 恢复原生Tag按钮
            var buttons = Traverse.Create(tagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
            if (buttons != null)
            {
                foreach (var btn in buttons)
                {
                    if (btn != null)
                        btn.gameObject.SetActive(true);
                }
            }
            
            // 恢复自定义Tag按钮
            foreach (var btn in _customTagButtons)
            {
                if (btn != null)
                    btn.SetActive(true);
            }
            
            // 重新应用隐藏空Tag
            if (PluginConfig.HideEmptyTags.Value)
            {
                HideEmptyTags(tagListUI);
            }
            
            // 恢复标题
            tagListUI.SetTitle();
            
            // 更新下拉框高度
            UpdateDropdownHeight(tagListUI);
            
            Plugin.Log.LogInfo("[TagListUI] Switched to normal mode");
        }
        
        /// <summary>
        /// 创建队列操作按钮（克隆原生Tag按钮样式）
        /// </summary>
        /// <summary>
        /// 创建队列操作按钮
        /// </summary>
        /// <returns>创建的按钮数量</returns>
        private static int CreateQueueButtons(Transform container)
        {
            int buttonCount = 0;
            
            // 如果已存在，先销毁
            if (_clearAllQueueButton != null)
            {
                UnityEngine.Object.Destroy(_clearAllQueueButton);
                _clearAllQueueButton = null;
            }
            if (_clearFutureQueueButton != null)
            {
                UnityEngine.Object.Destroy(_clearFutureQueueButton);
                _clearFutureQueueButton = null;
            }
            if (_clearHistoryButton != null)
            {
                UnityEngine.Object.Destroy(_clearHistoryButton);
                _clearHistoryButton = null;
            }
            
            // 获取原生按钮作为模板
            var buttons = Traverse.Create(_cachedTagListUI).Field("buttons").GetValue<MusicTagListButton[]>();
            if (buttons == null || buttons.Length == 0)
            {
                Plugin.Log.LogError("[CreateQueueButtons] No template button found");
                return buttonCount;
            }
            
            var templateButton = buttons[0];
            
            // 创建"清空全部队列"按钮
            _clearAllQueueButton = CreateQueueButtonFromTemplate(
                container,
                templateButton,
                "ClearAllQueue",
                "清空全部队列",
                OnClearAllQueueClicked
            );
            if (_clearAllQueueButton != null) buttonCount++;
            
            // 创建"清空未来队列"按钮
            _clearFutureQueueButton = CreateQueueButtonFromTemplate(
                container,
                templateButton,
                "ClearFutureQueue",
                "清空未来队列",
                OnClearFutureQueueClicked
            );
            if (_clearFutureQueueButton != null) buttonCount++;
            
            // 创建"清空播放历史"按钮
            _clearHistoryButton = CreateQueueButtonFromTemplate(
                container,
                templateButton,
                "ClearHistory",
                "清空播放历史",
                OnClearHistoryClicked
            );
            if (_clearHistoryButton != null) buttonCount++;
            
            // 强制刷新布局
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(container as RectTransform);
            
            return buttonCount;
        }
        
        /// <summary>
        /// 从模板创建队列操作按钮（保持原生样式）
        /// </summary>
        private static GameObject CreateQueueButtonFromTemplate(
            Transform parent, 
            MusicTagListButton template, 
            string name,
            string displayText, 
            UnityEngine.Events.UnityAction onClick)
        {
            // 克隆模板按钮
            var buttonObj = UnityEngine.Object.Instantiate(template.gameObject, parent);
            buttonObj.name = name;
            buttonObj.SetActive(true);
            
            // 移除原有的MusicTagListButton行为（我们不需要Tag逻辑）
            var tagButton = buttonObj.GetComponent<MusicTagListButton>();
            if (tagButton != null)
            {
                UnityEngine.Object.Destroy(tagButton);
            }
            
            // 隐藏复选框（队列操作不需要）
            var buttonsContainer = buttonObj.transform.Find("Buttons");
            if (buttonsContainer != null)
            {
                var checkBox = buttonsContainer.Find("CheckBox");
                if (checkBox != null)
                {
                    checkBox.gameObject.SetActive(false);
                }
                
                // 查找并修改TagName文本
                var tagName = buttonsContainer.Find("TagName");
                if (tagName != null)
                {
                    // 获取TMP_Text组件
                    var tmpText = tagName.GetComponent<TMP_Text>();
                    if (tmpText != null)
                    {
                        // 移除本地化组件（如果有）
                        var localization = tagName.GetComponent<TextLocalizationBehaviour>();
                        if (localization != null)
                        {
                            UnityEngine.Object.Destroy(localization);
                        }
                        
                        // 设置文本
                        tmpText.text = displayText;
                    }
                }
            }
            
            // 设置点击事件
            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                // 清除原有监听器
                button.onClick.RemoveAllListeners();
                // 添加新的监听器
                button.onClick.AddListener(onClick);
            }
            else
            {
                // 如果没有Button组件，添加一个
                button = buttonObj.AddComponent<Button>();
                button.onClick.AddListener(onClick);
            }
            
            Plugin.Log.LogInfo($"[CreateQueueButton] Created queue button: {displayText}");
            return buttonObj;
        }
        
        /// <summary>
        /// 隐藏 TodoSwitchFinishButton
        /// </summary>
        private static void HideTodoSwitchFinishButton(Transform tagListContainer)
        {
            // 在 TagList 下搜索所有的 TagCell，找到包含 TodoSwitchFinishButton 的那个
            foreach (Transform child in tagListContainer)
            {
                if (child.name.StartsWith("TagCell"))
                {
                    var buttons = child.Find("Buttons");
                    if (buttons != null)
                    {
                        var todoButton = buttons.Find("TodoSwitchFinishButton");
                        if (todoButton != null)
                        {
                            _todoSwitchFinishButton = todoButton.gameObject;
                            _todoSwitchFinishButtonWasActive = todoButton.gameObject.activeSelf;
                            todoButton.gameObject.SetActive(false);
                            Plugin.Log.LogInfo("[TagListUI] Hidden TodoSwitchFinishButton");
                            return;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 显示 TodoSwitchFinishButton
        /// </summary>
        private static void ShowTodoSwitchFinishButton()
        {
            if (_todoSwitchFinishButton != null)
            {
                _todoSwitchFinishButton.SetActive(_todoSwitchFinishButtonWasActive);
                _todoSwitchFinishButton = null;
                Plugin.Log.LogInfo("[TagListUI] Restored TodoSwitchFinishButton");
            }
        }
        
        /// <summary>
        /// 更新队列模式下的下拉框高度（使用与正常模式相同的计算公式）
        /// </summary>
        private static void UpdateDropdownHeightForQueueMode(PulldownListUI pulldown, int buttonCount)
        {
            // 实测数据: 按钮高度=45
            const float buttonHeight = 45f;
            
            // 公式：finalHeight = a × (按钮数 × 高度) + b
            float a = PluginConfig.TagDropdownHeightMultiplier.Value;
            float b = PluginConfig.TagDropdownHeightOffset.Value;
            float finalHeight = a * (buttonCount * buttonHeight) + b;
            
            Traverse.Create(pulldown).Field("_openPullDownSizeDeltaY").SetValue(finalHeight);
            Plugin.Log.LogInfo($"[QueueMode] Dropdown height: {a} × ({buttonCount} × {buttonHeight}) + {b} = {finalHeight:F1}");
        }
        
        /// <summary>
        /// 清空全部队列按钮点击
        /// </summary>
        private static void OnClearAllQueueClicked()
        {
            Plugin.Log.LogInfo("[TagListUI] Clear all queue clicked");
            
            // 清空整个队列
            PlayQueueManager.Instance.Clear();
            
            // 从播放列表获取下一首开始播放
            // 这会触发 AdvanceToNext，从播放列表补充
            var musicService = Traverse.Create(_cachedTagListUI)
                .Field("musicService")
                .GetValue<MusicService>();
                
            if (musicService != null)
            {
                // 播放下一首（从播放列表位置或随机）
                musicService.SkipCurrentMusic(MusicChangeKind.Manual).Forget();
            }
            
            // 自动切换回播放列表视图
            PlayQueueButton_Patch.SwitchToPlaylist();
        }
        
        /// <summary>
        /// 清空未来队列按钮点击（保留当前播放）
        /// </summary>
        private static void OnClearFutureQueueClicked()
        {
            Plugin.Log.LogInfo("[TagListUI] Clear future queue clicked");
            
            // 只清空待播放的，保留当前播放
            PlayQueueManager.Instance.ClearPending();
            
            // 自动切换回播放列表视图
            PlayQueueButton_Patch.SwitchToPlaylist();
        }
        
        /// <summary>
        /// 清空播放历史按钮点击
        /// </summary>
        private static void OnClearHistoryClicked()
        {
            Plugin.Log.LogInfo("[TagListUI] Clear history clicked");
            
            // 清空播放历史
            PlayQueueManager.Instance.ClearHistory();
            
            Plugin.Log.LogInfo("[TagListUI] Play history cleared");
        }
        
        /// <summary>
        /// 当前是否处于队列模式
        /// </summary>
        public static bool IsQueueMode => _isQueueMode;
        
        #endregion
    }
}

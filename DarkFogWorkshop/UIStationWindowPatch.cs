using System;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFogWorkshop
{
    /// <summary>
    /// 运输塔窗口里的黑雾工坊提示。
    ///
    /// - 一个"?"提示控件（克隆游戏原生的 tip-button，做法参考 UXAssist），放在窗口标题栏
    ///   关闭按钮的正下方；它只负责悬停提示，不响应点击，也不会弹出白色说明文字；
    /// - 悬停提示：标题是 DarkFogWorkshop，正文是本塔的兑换明细（每帧算力、各格单价与每秒产出）；
    /// - 只有满足"黑雾工坊"三项条件时该控件才显示。
    ///
    /// 所有界面操作都只在主线程（窗口 _OnUpdate）里执行。
    /// </summary>
    /// <summary>
    /// 给「通用提示框」设置 1.5 倍行距。
    ///
    /// 为什么补丁挂在这里：提示正文实际由 **`UISimpleGeneralTip.subTextComp`** 渲染
    /// （`UIButton.SetTip` 的分支里 `UISimpleGeneralTip` 优先于 `UIButtonTip`）。
    /// 早期版本挂在 `UIButtonTip.SetTip` 上，**从未被调用**。
    ///
    /// 行距改用 `Text.lineSpacing` 属性，而不再在文本里插 `&lt;size&gt;` 标签 ——
    /// 那个标签会给每行注入一个放大字号的字符，**干扰列宽测量**。
    /// </summary>
    [HarmonyPatch(typeof(UISimpleGeneralTip))]
    public static class UISimpleGeneralTipPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("SetTip")]
        public static void SetTip_Postfix(UISimpleGeneralTip __instance)
        {
            UIStationWindowPatch.ApplyTipLineSpacing(__instance);
        }
    }

    [HarmonyPatch(typeof(UIStationWindow))]
    public static class UIStationWindowPatch
    {
        /// <summary>提示控件相对窗口右上角的水平位置（向左为正）。</summary>
        private const float HelpRight = 48f;
        /// <summary>提示控件相对窗口顶部的纵向位置（向下为正），落在关闭按钮下方。</summary>
        private const float HelpTop = 49f;
        /// <summary>提示控件尺寸（与 UXAssist 的提示按钮一致：24×24）。</summary>
        private const float HelpSize = 24f;
        /// <summary>提示正文的刷新间隔（帧）。60 帧 ≈ 1 秒。</summary>
        private const long TipRefreshTicks = 30;
        /// <summary>提示框宽度（像素）。0 表示用游戏默认值。</summary>
        /// <summary>
        /// 提示框宽度。1.3.2 的排版里"第 N 槽位（产出）：物品名"加上图标后更宽，
        /// 太窄会折行破坏对齐，所以给足宽度。
        /// </summary>
        private const int TipWidth = 465;

        private static UIStationWindow _window;
        private static UIButton _helpButton;
        private static GameObject _metadataBar;
        private static UIButton _metadataButton;
        private static Image _metadataCheck;
        private static Text _metadataLabel;
        private static StationComponent _metadataStation;
        private static WorkshopUiHost _host;
        private static StationComponent _lastTipStation;
        private static long _lastTipTick;

        // 注：曾尝试给提示框 Text 设置 `tableColWidths` / `tableColAlignments` 做真表格，
        //     但**未生效**——日志里从未出现"已启用表格排版"，说明 `UIButtonTip.SetTip`
        //     并不是本提示框实际走的渲染分支（`UIButton.SetTip` 里 `UISimpleGeneralTip`
        //     优先于 `UIButtonTip`）。表格对齐因此改为在文本层按显示宽度单位补齐实现。

        /// <summary>
        /// 在 `Awake`（必定是主线程）里记录主线程 id。
        /// 核心逻辑用它判断自己是否跑在主线程上——`PlanetTransport.GameTick` 在 DSP 里是多线程的。
        /// </summary>
        private static int _mainThreadId;

        internal static void CaptureMainThread()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        internal static bool IsMainThread()
        {
            return _mainThreadId == 0
                   || System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        /// <summary>行距倍率（1.5 倍行距）。</summary>
        private const float TipLineSpacing = 1.5f;


        /// <summary>
        /// 表格列宽（**像素绝对值**）。两列：名称 / 算力。
        ///
        /// **原版机制（IL 实证，`TextGenerator.Populate` @IL_0ADD）**：
        /// ```text
        /// tmpTableColCoords[0] = Mathf.Round(bound.x);
        /// for (i = 0; i &lt; n; i++)
        ///     tmpTableColCoords[i+1] = tmpTableColCoords[i] + tmpTableColWidths[i];
        /// ```
        /// 即**列宽是精确像素值、直接累加成列坐标**，单元格按 `ChangeTableCell`
        /// 用 `[coords[col], coords[col+1]]` 定位并按 `tableColAlignments[col]` 对齐。
        /// 所以引擎本来就能做精确对齐 —— 之前失败是**宽度值本身没给对**。
        ///
        /// 单位语义（`Populate` IL）：`&gt; 1.001` 视为像素、`≤ 1.001` 视为比例。
        /// 这里用**像素绝对值**（与游戏指引文件里 `tablerowheights=30` 的用法一致）。
        ///
        /// 宽度依据实测字宽：图标 ≈28px、汉字 ≈13px、数字 ≈7px、空格 ≈7px。
        /// - 名称列：最长 `物质重组器` = 28 + 65 = 93px，**再加名称侧间隔 4 空格 ≈28px**
        ///   ⇒ 需求 ≈121px → 取 **140**（留少量余量）
        /// - 算力列：最长 `3000` = 28px → 取 **80**（右对齐余量）
        /// </summary>
        private static readonly float[] TipTableColWidths = { 140f, 80f };

        /// <summary>
        /// 表格行高（像素）。行数 = 表头 1 + 矩阵 6 + **空行 1** + 小标题 1 + 材料 6 = **14**。
        ///
        /// ⚠️ **必须非空**：`TextGenerationSettings.get_useTable` 的 IL 要求
        /// `tableColWidths` 与 `tableRowHeights` **两者都非空**，表格模式才启用
        /// （1.3.8/1.3.9 传空数组 ⇒ `useTable=false` ⇒ 全部表格属性被静默忽略）。
        /// </summary>
        private static readonly float[] TipTableRowHeights =
        {
            24f, 24f, 24f, 24f, 24f, 24f, 24f,
            24f, 24f, 24f, 24f, 24f, 24f, 24f,
        };

        /// <summary>
        /// 列水平对齐（`HorzAlignment`：0=Left、1=Center、2=Right）。
        ///
        /// ⚠️ **数组长度必须 ≥ 列数**：`GetColAlignment` 的 IL 在
        /// `col &lt; 0 || col &gt;= tableColAlignments.Length` 时**回退到 `textAnchor`**，
        /// 属于静默失效。所以两列就要给两个元素。
        /// </summary>
        private static readonly Text.HorzAlignment[] TipTableColAlignments =
        {
            Text.HorzAlignment.Left, Text.HorzAlignment.Right,
        };

        /// <summary>
        /// 给通用提示框的正文设置**游戏自带的表格排版**。
        ///
        /// 行距由文本里的 `&lt;size=21&gt; &lt;/size&gt;` 标签实现（见 `DarkFogWorkshopPatch.Line`），
        /// 那是 1.4.6 验证过可用的写法；不要改成 `lineSpacing` 属性。
        /// </summary>
        internal static void ApplyTipLineSpacing(UISimpleGeneralTip tip)
        {
            if (tip == null || tip.subTextComp == null)
            {
                return;
            }
            try
            {
                Text text = tip.subTextComp;

                // 启用游戏表格排版：列宽/行高/对齐三者都设全。
                // 注意 tableRowHeights 必须非空，否则 useTable=false、表格属性被忽略。
                text.tableColWidths = TipTableColWidths;
                text.tableRowHeights = TipTableRowHeights;
                text.tableColAlignments = TipTableColAlignments;
                text.tableRowAlignments = new[] { Text.VertAlignment.Middle };
                text.tableCellPadding = new Vector4(0f, 0f, 0f, 0f);
                text.tableLineColor = new Color(0f, 0f, 0f, 0f);   // 不画表格线（同游戏指引）
                text.tableLineTint = false;

                // 关掉字距拉伸与智能断行调整，避免列内文字被改写宽度。
                text.latinCharSpacing = 0f;
                text.latinCharStretch = new Vector2(1f, 1f);
                text.wideCharStretch = new Vector2(1f, 1f);
                text.lineBreakSmartAdjust = false;
            }
            catch (System.Exception e)
            {
                DarkFogWorkshop.Log.LogWarning($"[提示] 设置提示框属性失败：{e.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnCreate")]
        public static void OnCreate_Postfix(UIStationWindow __instance)
        {
            _window = __instance;
            EnsureWidget(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnOpen")]
        public static void OnOpen_Postfix(UIStationWindow __instance)
        {
            _window = __instance;
            EnsureWidget(__instance);
            Refresh(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnClose")]
        public static void OnClose_Postfix(UIStationWindow __instance)
        {
            Hide();
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnStationIdChange")]
        public static void OnStationIdChange_Postfix(UIStationWindow __instance)
        {
            _window = __instance;
            EnsureWidget(__instance);
            Refresh(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("_OnUpdate")]
        public static void OnUpdate_Postfix(UIStationWindow __instance)
        {
            EnsureWidget(__instance);
            Refresh(__instance);
        }

        /* ------------------------------------------------------------------ */
        /* 控件                                                                */
        /* ------------------------------------------------------------------ */

        private static StationComponent GetStation(UIStationWindow window)
        {
            if (window == null)
            {
                return null;
            }
            PlanetTransport transport = window.transport ?? (window.factory != null ? window.factory.transport : null);
            int stationId = window.stationId;
            if (transport == null || stationId <= 0 || stationId >= transport.stationCursor)
            {
                return null;
            }
            StationComponent station = transport.stationPool[stationId];
            return station != null && station.id == stationId ? station : null;
        }

        private static void EnsureWidget(UIStationWindow window)
        {
            if (window == null || window.windowTrans == null)
            {
                return;
            }
            if (_helpButton == null)
            {
                CreateWidget(window);
            }
            EnsureMetadataBar(window);
            if (_helpButton == null)
            {
                return;
            }
            // 窗口是复用的单例，换父节点与层级只在需要一个周期时做一次，避免每帧重排。
            if (_helpButton.transform.parent != window.windowTrans)
            {
                _helpButton.transform.SetParent(window.windowTrans, false);
                _helpButton.transform.SetAsLastSibling();
                LayoutHelpButton();
            }
            if (_host == null)
            {
                _host = _helpButton.gameObject.AddComponent<WorkshopUiHost>();
            }
            _host.window = window;
        }

        /// <summary>
        /// 元数据兑换开关：完全克隆原版「翘曲必备」控件（外观与原版一致）。
        /// 位置：与物流站命名盒（nameInput，"本地站点 #N"）垂直居中平齐、位于其右侧，
        /// **不改变窗口原有布局**（不动槽位行、窗口高度与底部面板）。
        /// </summary>
        private static void EnsureMetadataBar(UIStationWindow window)
        {
            if (window == null || window.windowTrans == null || window.warperNecessaryGroup == null)
            {
                return;
            }
            if (_metadataBar != null)
            {
                if (_metadataBar.transform.parent != window.windowTrans)
                {
                    _metadataBar.transform.SetParent(window.windowTrans, false);
                    LayoutMetadataBar(window);
                }
                return;
            }

            _metadataBar = UnityEngine.Object.Instantiate(window.warperNecessaryGroup.gameObject, window.windowTrans);
            _metadataBar.name = "dark-fog-workshop-metadata-exchange";
            LayoutMetadataBar(window);

            // 清掉克隆来的原版提示与点击，换成黑雾工坊的说明与切换逻辑
            _metadataButton = _metadataBar.GetComponentInChildren<UIButton>(true);
            foreach (UIButton button in _metadataBar.GetComponentsInChildren<UIButton>(true))
            {
                button.tipTitleFormatString = string.Empty;
                button.tipTextFormatString = string.Empty;
                button.tips.tipTitle = DarkFogWorkshop.HELP_TITLE;
                button.tips.tipText = DarkFogWorkshop.Tr(
                    "勾选后，第 1 槽位将开始自动兑换元数据，将对应的科学矩阵直接放入该槽位。取消勾选即可停止兑换。",
                    "When checked, slot 1 automatically exchanges metadata for its selected science matrix and deposits it directly into the slot. Uncheck to stop.");
                button.tips.itemId = 0;
                button.tips.type = UIButton.ItemTipType.Other;
                button.UpdateTip();
            }
            if (_metadataButton != null)
            {
                _metadataButton.onClick -= OnMetadataExchangeClick;
                _metadataButton.onClick += OnMetadataExchangeClick;
            }

            string checkPath = RelativePath(window.warperNecessaryGroup, window.warperNecessaryCheck != null
                ? window.warperNecessaryCheck.transform : null);
            Transform checkTransform = string.IsNullOrEmpty(checkPath)
                ? null : _metadataBar.transform.Find(checkPath);
            _metadataCheck = checkTransform != null
                ? checkTransform.GetComponent<Image>()
                : _metadataBar.GetComponentInChildren<Image>(true);

            Text[] labels = _metadataBar.GetComponentsInChildren<Text>(true);
            if (labels != null && labels.Length > 0)
            {
                _metadataLabel = labels[0];
                Localizer localizer = _metadataLabel.GetComponent<Localizer>();
                if (localizer != null)
                {
                    localizer.stringKey = null;
                    localizer.translation = null;
                }
                _metadataLabel.text = "元数据兑换科学矩阵";
            }
        }

        /// <summary>
        /// 与物流站命名盒（nameInput）垂直居中平齐、放在其右侧（间隔 24px）。
        /// 每次刷新都按命名盒实际位置计算，窗口尺寸变化也能跟随。
        /// </summary>
        private static void LayoutMetadataBar(UIStationWindow window)
        {
            if (_metadataBar == null || window == null || window.windowTrans == null)
            {
                return;
            }
            RectTransform rect = _metadataBar.transform as RectTransform;
            if (rect == null)
            {
                return;
            }
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = Vector3.one;

            RectTransform wt = window.windowTrans;
            // 垂直：与右上角「?」提示按钮平齐（HelpTop=49、尺寸 24 → 中心在 -61）
            float centerY = -(HelpTop + HelpSize * 0.5f);
            float cloneH = rect.rect.height > 1f ? rect.rect.height : 26f;
            RectTransform nameRect = window.nameInput != null
                ? window.nameInput.transform as RectTransform
                : null;
            float targetLocalX = 320f;
            if (nameRect != null)
            {
                // 命名盒右边缘 → windowTrans 本地坐标
                Vector3 corner = nameRect.TransformPoint(new Vector3(
                    nameRect.rect.width * (1f - nameRect.pivot.x),
                    nameRect.rect.height * (0.5f - nameRect.pivot.y),
                    0f));
                Vector3 local = wt.InverseTransformPoint(corner);
                targetLocalX = local.x + 24f;
            }

            // 锚点 (0,1)、pivot (0,1)：anchoredPosition = 目标点(本地) - 锚点(本地)。
            // 锚点在窗口左上角，本地坐标为 (-w*px, h*(1-py))。
            float anchorX = -wt.rect.width * wt.pivot.x;
            float anchorY = wt.rect.height * (1f - wt.pivot.y);
            // 不越出窗口右缘（留 20px 边距）
            float maxLocalX = wt.rect.width * (1f - wt.pivot.x) - 20f - rect.rect.width;
            if (targetLocalX > maxLocalX)
            {
                targetLocalX = maxLocalX;
            }
            rect.anchoredPosition3D = new Vector3(
                targetLocalX - anchorX,
                centerY - cloneH * 0.5f - anchorY,
                0f);
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
            {
                return string.Empty;
            }
            StringBuilder path = new StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path.Insert(0, current.name + "/");
                current = current.parent;
            }
            return current == root ? path.ToString() : string.Empty;
        }

        private static void OnMetadataExchangeClick(int obj)
        {
            StationComponent station = _metadataStation;
            if (station == null || !DarkFogWorkshopPatch.IsMetadataExchangeEligible(station))
            {
                return;
            }
            DarkFogWorkshopPatch.SetMetadataExchangeEnabled(
                station, !DarkFogWorkshopPatch.IsMetadataExchangeEnabled(station));
            if (_window != null)
            {
                Refresh(_window);
            }
        }

        private static void RefreshMetadataWidget(UIStationWindow window, StationComponent station, bool windowActive)
        {
            if (_metadataBar == null)
            {
                return;
            }
            bool eligible = station != null && DarkFogWorkshopPatch.IsMetadataExchangeEligible(station);
            if (!eligible && station != null && DarkFogWorkshopPatch.IsMetadataExchangeEnabled(station))
            {
                DarkFogWorkshopPatch.SetMetadataExchangeEnabled(station, false);
            }
            bool visible = windowActive && eligible;

            if (_metadataBar.activeSelf != visible)
            {
                _metadataBar.SetActive(visible);
            }

            if (visible)
            {
                LayoutMetadataBar(window);
                if (_metadataCheck != null)
                {
                    _metadataCheck.enabled = DarkFogWorkshopPatch.IsMetadataExchangeEnabled(station);
                }
            }
            _metadataStation = eligible ? station : null;
        }

        private static void CreateWidget(UIStationWindow window)
        {
            Font font = window.titleText != null ? window.titleText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            _helpButton = CreateHelpButton(window, font);
            LayoutHelpButton();
        }

        private static UIButton CreateHelpButton(UIStationWindow window, Font font)
        {
            GameObject source = FindNativeTipButton();
            if (source != null)
            {
                GameObject clone = UnityEngine.Object.Instantiate(source);
                clone.name = "dark-fog-workshop-help";
                UIButton btn = clone.GetComponent<UIButton>();
                if (btn != null)
                {
                    UIButton.TipSettings tips = btn.tips;
                    ResetButton(btn);
                    tips.topLevel = true;
                    tips.type = UIButton.ItemTipType.Other;
                    tips.delay = 0.2f;
                    btn.tips = tips;
                    PrepareLabel(clone, font);
                    clone.transform.SetParent(window.windowTrans, false);
                    // 布局必须先算好，否则重新激活时会闪一帧。
                    LayoutHelpButton(btn);
                    clone.SetActive(true);
                    return btn;
                }
                UnityEngine.Object.Destroy(clone);
            }

            // 兜底：自绘一个只用于显示提示的控件（不挂 Button，不响应点击）。
            GameObject btnObj = new GameObject("dark-fog-workshop-help");
            btnObj.transform.SetParent(window.windowTrans, false);
            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.22f, 0.5f, 0.6f, 0.9f);
            img.raycastTarget = true;
            UIButton fallback = btnObj.AddComponent<UIButton>();
            fallback.button = null;
            fallback.tips = new UIButton.TipSettings
            {
                topLevel = true,
                type = UIButton.ItemTipType.Other,
                delay = 0.2f
            };
            PrepareLabel(btnObj, font);
            return fallback;
        }

        /// <summary>找一个游戏原生的"?"提示控件作为克隆模板（做法参考 UXAssist）。</summary>
        private static GameObject FindNativeTipButton()
        {
            try
            {
                UIGalaxySelect galaxy = UIRoot.instance != null ? UIRoot.instance.galaxySelect : null;
                if (galaxy == null)
                {
                    return null;
                }
                if (galaxy.sandboxToggle != null)
                {
                    Transform parent = galaxy.sandboxToggle.gameObject.transform.parent;
                    if (parent != null)
                    {
                        Transform found = parent.Find("tip-button");
                        if (found != null)
                        {
                            return found.gameObject;
                        }
                    }
                }
                if (galaxy.darkFogDetailBtn != null)
                {
                    return galaxy.darkFogDetailBtn.gameObject;
                }
            }
            catch (System.Exception e)
            {
                DarkFogWorkshop.Log.LogWarning($"找不到原生提示控件模板，改用自绘控件：{e.Message}");
            }
            return null;
        }

        /// <summary>
        /// 克隆体上的文字统一成 "?"，并清掉会把它改回去的本地化键。</summary>
        private static void PrepareLabel(GameObject root, Font font)
        {
            Text label = root.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                Localizer localizer = label.GetComponent<Localizer>();
                if (localizer != null)
                {
                    localizer.stringKey = null;
                    localizer.translation = null;
                }
                label.text = "?";
                label.fontStyle = FontStyle.Normal;
                label.color = Color.white;
                label.raycastTarget = false;
                return;
            }
            GameObject labelObj = new GameObject("Text");
            labelObj.transform.SetParent(root.transform, false);
            Text created = labelObj.AddComponent<Text>();
            created.font = font;
            created.text = "?";
            created.fontSize = 15;
            created.fontStyle = FontStyle.Normal;
            created.alignment = TextAnchor.MiddleCenter;
            created.color = Color.white;
            created.raycastTarget = false;
            RectTransform rect = labelObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 只让控件负责"显示提示"：清掉克隆来的点击监听，并把按钮设为不可交互，
        /// 这样点它不会有任何反应（但悬停仍然走 UIButton 的提示）。
        /// </summary>
        private static void ResetButton(UIButton button)
        {
            button.Init();
            button.enabled = true;
            button.highlighted = false;
            button.updating = true;
            button.data = 0;
            button.tips = default;
            if (button.button != null)
            {
                button.button.onClick.RemoveAllListeners();
                button.button.interactable = false;
            }
            button.onClick -= OnHelpButtonClicked;
            button.MReset();
        }

        private static void OnHelpButtonClicked(int obj)
        {
            // 刻意留空：这个控件只做提示，点击不弹任何说明。
        }

        /// <summary>锚点统一为"右上角 + 左上轴心"，与 UXAssist 的布局方式一致。</summary>
        private static RectTransform AnchorTopRight(Component cmp, float right, float top)
        {
            RectTransform rect = cmp.transform as RectTransform;
            if (rect == null)
            {
                return null;
            }
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition3D = new Vector3(-right, -top, 0f);
            return rect;
        }

        private static void LayoutHelpButton()
        {
            LayoutHelpButton(_helpButton);
        }

        private static void LayoutHelpButton(UIButton button)
        {
            if (button == null)
            {
                return;
            }
            RectTransform rect = AnchorTopRight(button, HelpRight, HelpTop);
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(HelpSize, HelpSize);
            }
        }

        private static void Hide()
        {
            if (_helpButton != null && _helpButton.gameObject.activeSelf)
            {
                _helpButton.gameObject.SetActive(false);
            }
            if (_metadataBar != null && _metadataBar.activeSelf)
            {
                _metadataBar.SetActive(false);
            }
        }

        /// <summary>每帧刷新（只在窗口打开、且满足黑雾工坊条件时显示）。</summary>
        private static void Refresh(UIStationWindow window)
        {
            if (_helpButton == null)
            {
                return;
            }
            StationComponent station = GetStation(window);
            RefreshMetadataWidget(window, station, window.active);
            bool visible = window.active && station != null
                && (DarkFogWorkshopPatch.IsWorkshopShape(station)
                    || (DarkFogWorkshopPatch.IsMetadataExchangeEligible(station)
                        && DarkFogWorkshopPatch.IsMetadataExchangeEnabled(station)));
            if (_helpButton.gameObject.activeSelf != visible)
            {
                _helpButton.gameObject.SetActive(visible);
            }
            if (visible)
            {
                UpdateHelpTip(station);
            }
        }


        /// <summary>
        /// 悬停提示：标题固定为「黑雾工坊DarkFogWorkshop」，正文是本塔的消耗/产出明细。
        ///
        /// 内容会随材料增减、槽位变化而变，所以按固定间隔（默认每 0.5 秒）刷新一次，
        /// 而不是每帧都重建字符串。
        /// </summary>
        private static void UpdateHelpTip(StationComponent station)
        {
            long now = GameMain.gameTick;
            if (now - _lastTipTick < TipRefreshTicks && _lastTipStation == station)
            {
                return;
            }
            _lastTipTick = now;
            _lastTipStation = station;

            string detail = DarkFogWorkshopPatch.DescribeDetail(station);
            if (_helpButton.tips.tipTitle == DarkFogWorkshop.HELP_TITLE && _helpButton.tips.tipText == detail)
            {
                return;
            }
            _helpButton.tips.tipTitle = DarkFogWorkshop.HELP_TITLE;
            _helpButton.tips.tipText = detail;
            _helpButton.tips.type = UIButton.ItemTipType.Other;
            _helpButton.tips.topLevel = true;
            // 提示框略加宽，保证「物品名 + 算力表数值列」不会换行折断。
            _helpButton.tips.width = TipWidth;
            _helpButton.UpdateTip();
        }

        private sealed class WorkshopUiHost : MonoBehaviour
        {
            public UIStationWindow window;
        }
    }
}

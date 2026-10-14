using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BlueprintBeltSaver
{
    /// <summary>
    /// BlueprintBeltSaver: blueprint belt merging (F3/F4) merged with LongerBelts' hand-drawn
    /// long belts (F1), a shared settings window (F2) and runtime avoidance of LongerBelts /
    /// DSPBlueprintTools (F5).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class BlueprintBeltSaverPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.dsp.blueprintbeltsaver";
        public const string Name = "BlueprintBeltSaver";
        public const string Version = "1.0.0";

        private const int MaxBlueprintBuildings = 15000;
        private const int BeltItemIdFirst = 2001;
        private const int BeltItemIdLast = 2003;
        private const string LongerBeltsGuid = "shisang_LongerBelts";
        private const string LongerBeltsTypeName = "LongerBelts.LongerBelts";
        private const string DspBlueprintToolsGuid = "dsp.byboy.blueprinttools";
        private const int SettingsWindowId = 20241012;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _hotkey;
        private ConfigEntry<float> _currentDistance;
        private ConfigEntry<float> _longitudeDistance;
        private ConfigEntry<float> _latitudeDistance;
        private ConfigEntry<bool> _longerOnGrid;
        private ConfigEntry<int> _slopePathMode;
        private ConfigEntry<bool> _shortestUnlimit;
        private ConfigEntry<bool> _rebindSorterLinks;
        private ConfigEntry<bool> _sorterForceRebind;
        private ConfigEntry<bool> _gridExactMerging;

        private Harmony _harmony;
        private static FieldInfo _longerBeltsDistanceField;
        private bool _longerBeltsLookupDone;
        private float _nextLongerBeltsLookup;
        private float _nextLanguageCheck;
        private GUIStyle _wrapLabelStyle;
        private GUISkin _wrapLabelSkin;
        private bool _longerBeltsInstalled;
        private bool _dspBlueprintToolsInstalled;
        private bool _f1Patched;

        private bool _windowVisible;
        private Rect _windowRect = new Rect(280f, 150f, 560f, 420f);
        private BeltSaverTranslate _text;
        private bool _textIsChinese;
        private string _status = "";

        private UIBlueprintInspector _inspector;
        private GameObject _optimizeButton;
        private Text _optimizeButtonText;
        private Button _optimizeButtonButton;
        private UIButton _anchoredAddFieldBtn;
        private bool _anchoredUnderAddField;
        private float _nextButtonTry;
        private int _updateErrors;
        private string _lastButtonState = "";
        private bool _inspectorLogged;
        private string _lastInjectWarning = "";

        private BlueprintData _pendingBlueprint;
        private string _pendingSummary = "";
        private float _pendingCap;

        /// <summary>
        /// 省带前的原件快照（Base64）与粘贴位置重算用的状态。
        /// 长度模型按"本次实际粘贴位置"精确算，所以玩家在预览里移动/旋转之后必须按新位置重算；
        /// 重算是**增量 + 节流**的：只有位置移动超过阈值或朝向转了象限、且距离上次超过
        /// <see cref="ReoptimizeInterval"/> 才做一次。绝不放在 OnGUI/每帧路径上。
        /// </summary>
        private string _originalSnapshot;
        /// <summary>
        /// 原件的"格位跨度"集合（与 <see cref="_originalSnapshot"/> 同源同生命周期）：
        /// 粘贴位置变化后的增量复核用它区分"新造跨度"与"原件同跨度"——原件里就存在的超限跨度，
        /// 游戏对原件同样拒绝（BuildTool_BlueprintPaste ≈2087 逐条判存储连接），不是省带造成的。
        /// </summary>
        private HashSet<(long, long)> _originalSpans;
        private string _pendingSourceSnapshot;
        private string _lastObservationKey = "";
        private float _nextReoptimize;
        private const float ReoptimizeInterval = 0.25f;

        private void Awake()
        {
            Log.Init(Logger);
            _enabled = Config.Bind("General", "Enabled", true, "总开关。关闭后不安装任何补丁，蓝图窗口的长带优化按钮也不生效。");
            _hotkey = Config.Bind("General", "WindowHotkey", new KeyboardShortcut(KeyCode.LeftAlt, KeyCode.B), "打开/关闭本 MOD 设置窗口的快捷键。格式：修饰键 + 主键，例如 LeftAlt + B、LeftControl + F8、None + F9。");
            _currentDistance = Config.Bind("Belt", "CurrentDistance", 1.9f, "最远距离（米）。用于徒手拉带（测地线分段）；未安装 LongerBelts 时，同时作为蓝图长带优化的单段上限。取值范围 0.400001 ~ 2.301172。实测：极地蓝图 1.9 时省 7 段，2.301172 时省 20 段。");
            _longitudeDistance = Config.Bind("Belt", "LongitudeDistance", 1.75f, "经纬线模式的沿纬线跨度（格）。范围 1 ~ 3，仅影响徒手拉带。");
            _latitudeDistance = Config.Bind("Belt", "LatitudeDistance", 1.5f, "经纬线模式的沿经线跨度（格）。范围 1 ~ 3，仅影响徒手拉带。");
            _longerOnGrid = Config.Bind("Belt", "LongerOnGrid", false, "开启经纬线模式的更大跨度（徒手拉带用，与蓝图长带优化无关）。");
            _slopePathMode = Config.Bind("Belt", "SlopePathMode", 0, "斜坡带模式：0 = 原版升降逻辑，1 = 阿基米德螺线等距分割（仅影响徒手拉带）。");
            _rebindSorterLinks = Config.Bind("Belt", "RebindSorterLinks", false, "默认关闭。开启后被分拣器/建筑引用的传送带节点也可参与合并，这些建筑的连接会重绑到覆盖它的新合并段（即多个分拣器共用同一长带）。守卫：被删节点与覆盖段的朝向必须一致，被删节点位置必须落在新段跨度内；只改写引用，槽位与沿带偏移原样保留。开启后请先在粘贴预览确认无红色报错、分拣器能正常工作再保存。");
            _shortestUnlimit = Config.Bind("Belt", "ShortestUnlimit", false, "忽略最小间距限制（测试用，可能产出过短或过长的非法带，请勿在正式蓝图使用）。");
            _gridExactMerging = Config.Bind("Belt", "GridExactMerging", true, "默认开启（推荐）。合并时只保留原有传送带节点，不生成插值坐标：粘贴时不会因网格换算而变长，跨纬度带与高空传送带都安全。关闭后改为按弧长均匀插值新节点（省得更多，但新节点不在格点上，跨纬度/高空粘贴时可能被算得更长而报「距离太远」）。");
            _sorterForceRebind = Config.Bind("Belt", "SorterForceRebind", false, "默认关闭（实验，需与 RebindSorterLinks 同时开启）。三件套：① 合并完全忽略分拣器自由重布线；② 合并后按每个分拣器的取货点坐标把引用强制重绑到覆盖它的新带节点，并按新段中心重锚定沿带偏移（取货点世界坐标保持不动）；③ 重排建造顺序——带节点全部排到分拣器之前，修复「分拣器先于目标带被建造导致链接失效」。开启后请先在粘贴预览逐个确认分拣器正常再保存。");
            _enabled.SettingChanged += delegate { PushConfigToStatics(); };
            _longerOnGrid.SettingChanged += delegate { PushConfigToStatics(); };
            _slopePathMode.SettingChanged += delegate { PushConfigToStatics(); };
            _shortestUnlimit.SettingChanged += delegate { PushConfigToStatics(); };
            _currentDistance.SettingChanged += delegate { PushConfigToStatics(); };
            _longitudeDistance.SettingChanged += delegate { PushConfigToStatics(); };
            _latitudeDistance.SettingChanged += delegate { PushConfigToStatics(); };
            PushConfigToStatics();
            _harmony = new Harmony(Guid);
            LogStartupBanner();
        }

        /// <summary>
        /// One unmistakable line in LogOutput.log proving this plugin was discovered and loaded.
        /// Version mismatches against the bundled BepInEx/HarmonyX are informational only: sibling
        /// plugins in the same profile reference 0Harmony 2.7.0.0/2.9.0.0 and load fine.
        /// </summary>
        private void LogStartupBanner()
        {
            string dll = "unknown";
            string harmony = "unknown";
            string bepInEx = "unknown";
            string config = "unknown";
            try
            {
                dll = typeof(BlueprintBeltSaverPlugin).Assembly.Location;
            }
            catch
            {
            }
            try
            {
                harmony = typeof(Harmony).Assembly.GetName().Version.ToString();
            }
            catch
            {
            }
            try
            {
                bepInEx = typeof(BaseUnityPlugin).Assembly.GetName().Version.ToString();
            }
            catch
            {
            }
            try
            {
                config = Config.ConfigFilePath;
            }
            catch
            {
            }
            Log.Info($"{Name} {Version} loaded | dll={dll} | BepInEx={bepInEx} | HarmonyX={harmony} | enabled={_enabled.Value} | hotkey={_hotkey.Value} | config={config}");
        }

        private void Start()
        {
            // Plugin detection runs in Start so every plugin has registered its GUID by now.
            DetectPlugins();
            ApplyF1PatchesIfNeeded();
            if (_enabled.Value)
            {
                PastePostRenderGuard.Install(_harmony);
            }
        }

        private void OnDestroy()
        {
            SaveStaticsToConfig();
        }

        private void DetectPlugins()
        {
            try
            {
                var infos = Chainloader.PluginInfos;
                _longerBeltsInstalled = infos != null && infos.ContainsKey(LongerBeltsGuid);
                _dspBlueprintToolsInstalled = infos != null && infos.ContainsKey(DspBlueprintToolsGuid);
            }
            catch (Exception ex)
            {
                Log.Warn("plugin detection failed: " + ex.Message);
                _longerBeltsInstalled = false;
                _dspBlueprintToolsInstalled = false;
            }
            if (_longerBeltsInstalled)
            {
                Log.Info($"detected LongerBelts ({LongerBeltsGuid}); hand-drawn belt transpilers stay off and the merge cap is read from LongerBelts.");
            }
            if (_dspBlueprintToolsInstalled)
            {
                Log.Info($"detected DSPBlueprintTools ({DspBlueprintToolsGuid}); its built-in belt merging overlaps with this mod. Both buttons keep working; this mod's button is recommended.");
            }
        }

        private void ApplyF1PatchesIfNeeded()
        {
            if (_f1Patched || _harmony == null)
            {
                return;
            }
            _f1Patched = true;
            if (!_enabled.Value)
            {
                Log.Info("mod disabled: F1 patches are not applied.");
                return;
            }
            if (_longerBeltsInstalled)
            {
                Log.Info("F1 skipped on purpose: transpiling PlanetAuxData.SnapLineNonAlloc twice (this mod plus LongerBelts) is undefined behaviour.");
                return;
            }
            var snapLine = AccessTools.Method(typeof(PlanetAuxData), "SnapLineNonAlloc");
            var determinePreviews = AccessTools.Method(typeof(BuildTool_Path), "DeterminePreviews");
            bool allOk = true;
            if (snapLine == null)
            {
                Log.Error("PlanetAuxData.SnapLineNonAlloc not found; grid-line long belts are unavailable.");
                allOk = false;
            }
            else
            {
                try
                {
                    _harmony.Patch(snapLine, transpiler: new HarmonyMethod(AccessTools.Method(typeof(LongerBeltPath), nameof(LongerBeltPath.SnapLineNonAllocTranspiler))));
                    Log.Info("F1 patch applied: PlanetAuxData.SnapLineNonAlloc");
                }
                catch (Exception ex)
                {
                    Log.Error("F1 patch failed for PlanetAuxData.SnapLineNonAlloc: " + ex);
                    allOk = false;
                }
            }
            if (determinePreviews == null)
            {
                Log.Error("BuildTool_Path.DeterminePreviews not found; belt path mode switch is unavailable.");
                allOk = false;
            }
            else
            {
                try
                {
                    _harmony.Patch(determinePreviews, transpiler: new HarmonyMethod(AccessTools.Method(typeof(LongerBeltPath), nameof(LongerBeltPath.DeterminePreviewsTranspiler))));
                    Log.Info("F1 patch applied: BuildTool_Path.DeterminePreviews");
                }
                catch (Exception ex)
                {
                    Log.Error("F1 patch failed for BuildTool_Path.DeterminePreviews: " + ex);
                    allOk = false;
                }
            }
            Log.Info("F1 hand-drawn long belts " + (allOk ? "enabled." : "partially enabled; see warnings above."));
        }

        private void PushConfigToStatics()
        {
            LongerBeltPath.longerOnGrid = _longerOnGrid.Value;
            LongerBeltPath.isometricSegmentation = Mathf.Clamp(_slopePathMode.Value, 0, 1);
            LongerBeltPath.currentDistance = Mathf.Clamp(_currentDistance.Value, LongerBeltPath.MinimumDistance, LongerBeltPath.MaximumDistance);
            LongerBeltPath.longitudeDistance = Mathf.Clamp(_longitudeDistance.Value, LongerBeltPath.MinimumGridDistance, LongerBeltPath.MaximumGridDistance);
            LongerBeltPath.latitudeDistance = Mathf.Clamp(_latitudeDistance.Value, LongerBeltPath.MinimumGridDistance, LongerBeltPath.MaximumGridDistance);
            LongerBeltPath.shortestUnlimit = _shortestUnlimit.Value;
        }

        private void SaveStaticsToConfig()
        {
            if (_longerOnGrid == null)
            {
                return;
            }
            try
            {
                _longerOnGrid.Value = LongerBeltPath.longerOnGrid;
                _slopePathMode.Value = LongerBeltPath.isometricSegmentation;
                _currentDistance.Value = LongerBeltPath.currentDistance;
                _longitudeDistance.Value = LongerBeltPath.longitudeDistance;
                _latitudeDistance.Value = LongerBeltPath.latitudeDistance;
                _shortestUnlimit.Value = LongerBeltPath.shortestUnlimit;
                Config.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("saving settings failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Live cap resolution: LongerBelts' own slider wins whenever that plugin exists.
        /// The reflective lookup is cached because AccessTools.TypeByName walks every type of every
        /// loaded assembly; calling it per frame (as the settings window used to) costs milliseconds
        /// and is the reason the window tanked the frame rate.
        /// </summary>
        private float ResolveCap(out bool fromLongerBelts)
        {
            fromLongerBelts = false;
            float value = LongerBeltPath.currentDistance;
            if (_longerBeltsDistanceField == null && _longerBeltsInstalled && (!_longerBeltsLookupDone || Time.unscaledTime >= _nextLongerBeltsLookup))
            {
                _longerBeltsLookupDone = true;
                _nextLongerBeltsLookup = Time.unscaledTime + 5f;
                try
                {
                    var type = AccessTools.TypeByName(LongerBeltsTypeName);
                    _longerBeltsDistanceField = type != null ? AccessTools.Field(type, "currentDistance") : null;
                    if (_longerBeltsDistanceField != null)
                    {
                        Log.Info("LongerBelts.currentDistance located; the merge cap follows that plugin's own slider.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("locating LongerBelts.currentDistance failed: " + ex.Message);
                }
            }
            if (_longerBeltsDistanceField != null)
            {
                try
                {
                    value = Convert.ToSingle(_longerBeltsDistanceField.GetValue(null));
                    fromLongerBelts = true;
                }
                catch
                {
                    // Fall through to this mod's own value.
                }
            }
            return Mathf.Clamp(value, LongerBeltPath.MinimumDistance, LongerBeltPath.MaximumDistance);
        }

        private void Update()
        {
            try
            {
                // Probing the game's localization singleton every frame is unnecessary; twice a
                // second is plenty for a language switch.
                if (Time.unscaledTime >= _nextLanguageCheck)
                {
                    _nextLanguageCheck = Time.unscaledTime + 0.5f;
                    bool chinese;
                    try
                    {
                        chinese = Localization.isZHCN;
                    }
                    catch
                    {
                        chinese = _textIsChinese;
                    }
                    if (_text == null || _textIsChinese != chinese)
                    {
                        _text = BeltSaverTranslate.Create(chinese);
                        _textIsChinese = chinese;
                    }
                }
                if (_hotkey.Value.IsDown())
                {
                    _windowVisible = !_windowVisible;
                    Log.Info("设置窗口" + (_windowVisible ? "已打开" : "已关闭") + "（快捷键 " + _hotkey.Value + "）");
                    if (!_windowVisible)
                    {
                        SaveStaticsToConfig();
                    }
                }
                UpdateInspectorButton();
                // 玩家在预览里移动/旋转之后的按新位置重算（内部自带节流，位置没变时立刻返回）。
                ReoptimizeForPastePosition();
            }
            catch (Exception ex)
            {
                if (_updateErrors++ < 5)
                {
                    Log.Error("Update failed (further identical errors are suppressed): " + ex);
                }
            }
        }

        private void OnGUI()
        {
            if (_text == null)
            {
                return;
            }
            if (_windowVisible)
            {
                _windowRect = GUI.Window(SettingsWindowId, _windowRect, new GUI.WindowFunction(DrawSettingsWindow), _text.Title);
            }
        }

        /// <summary>
        /// Word-wrapping label style, created once per GUI skin. Allocating a GUIStyle per OnGUI
        /// call is expensive and shows up as engine cost in the game's own profiler.
        /// </summary>
        private GUIStyle WrapLabel()
        {
            if (_wrapLabelStyle == null || !ReferenceEquals(_wrapLabelSkin, GUI.skin))
            {
                _wrapLabelSkin = GUI.skin;
                _wrapLabelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            }
            return _wrapLabelStyle;
        }

        private void DrawSettingsWindow(int id)
        {
            var label = WrapLabel();
            var slider = GUI.skin.horizontalSlider;
            var thumb = GUI.skin.horizontalSliderThumb;
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 22f));
            GUILayout.BeginVertical();
            if (!_enabled.Value)
            {
                GUILayout.Label(_text.Disabled, label);
            }
            LongerBeltPath.longerOnGrid = GUILayout.Toggle(LongerBeltPath.longerOnGrid, _text.IfLongerOnGrid);
            GUILayout.BeginHorizontal();
            GUILayout.Label(_text.LongitudeDistance, GUILayout.Width(150f));
            LongerBeltPath.longitudeDistance = GUILayout.HorizontalSlider(LongerBeltPath.longitudeDistance, LongerBeltPath.MinimumGridDistance, LongerBeltPath.MaximumGridDistance, slider, thumb, GUILayout.Width(230f));
            GUILayout.Label(LongerBeltPath.longitudeDistance.ToString("0.000"), GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(_text.LatitudeDistance, GUILayout.Width(150f));
            LongerBeltPath.latitudeDistance = GUILayout.HorizontalSlider(LongerBeltPath.latitudeDistance, LongerBeltPath.MinimumGridDistance, LongerBeltPath.MaximumGridDistance, slider, thumb, GUILayout.Width(230f));
            GUILayout.Label(LongerBeltPath.latitudeDistance.ToString("0.000"), GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(_text.DistanceSetting, GUILayout.Width(150f));
            LongerBeltPath.currentDistance = GUILayout.HorizontalSlider(LongerBeltPath.currentDistance, LongerBeltPath.MinimumDistance, LongerBeltPath.MaximumDistance, slider, thumb, GUILayout.Width(230f));
            GUILayout.Label(LongerBeltPath.currentDistance.ToString("0.000000"), GUILayout.Width(80f));
            GUILayout.EndHorizontal();
            GUILayout.Label(_text.PathMode, label);
            LongerBeltPath.isometricSegmentation = GUILayout.SelectionGrid(LongerBeltPath.isometricSegmentation, _text.PathModeStrings, 1);
            GUILayout.Label(_text.WarningNotice, label);
            LongerBeltPath.shortestUnlimit = GUILayout.Toggle(LongerBeltPath.shortestUnlimit, _text.UnlimitInstruction);
            GUILayout.Label(_text.CapLinkage, label);
            bool fromLongerBelts;
            float cap = ResolveCap(out fromLongerBelts);
            GUILayout.Label($"{_text.CurrentCap}: {cap:F6} m ({(fromLongerBelts ? _text.CapFromLongerBelts : _text.CapFromOwn)})", label);
            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Label(_status, label);
            }
            GUILayout.EndVertical();
            EatInputInRect(_windowRect);
        }

        internal static void EatInputInRect(Rect eatRect)
        {
            if ((Input.GetMouseButton(0) || Input.GetMouseButtonDown(0)) && eatRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                Input.ResetInputAxes();
            }
        }

        private void UpdateInspectorButton()
        {
            if (!_enabled.Value)
            {
                if (_optimizeButton != null)
                {
                    _optimizeButton.SetActive(false);
                }
                return;
            }
            if (Time.unscaledTime < _nextButtonTry)
            {
                SyncOptimizeButton(false);
                return;
            }
            _nextButtonTry = Time.unscaledTime + 0.5f;

            // The blueprint library and the in-world paste tool use SEPARATE UIBlueprintInspector
            // instances, and applying an optimization makes the game rebuild the panel. A cached
            // instance that went inactive keeps the injected button invisible (its parent is hidden),
            // so the instance is re-resolved and the button rebuilt whenever that happens.
            bool panelActive = _inspector != null && _inspector.gameObject.activeInHierarchy;
            if (!panelActive)
            {
                UIBlueprintInspector found = null;
                try
                {
                    found = UnityEngine.Object.FindObjectOfType<UIBlueprintInspector>();
                }
                catch
                {
                    found = null;
                }
                if (found != null)
                {
                    if (found != _inspector)
                    {
                        Log.Info("蓝图界面实例已切换（原实例不可用），省带按钮将重新注入。");
                        DestroyOptimizeButton();
                        _inspector = found;
                        _inspectorLogged = false;
                        _anchoredAddFieldBtn = null;
                    }
                    panelActive = true;
                }
            }
            if (!panelActive)
            {
                // No blueprint panel on screen right now: the panel's own GameObject is inactive, so
                // the injected button is hidden with it. Do not touch its active flag here, otherwise
                // reopening the panel would look like a missing button and force a rebuild.
                return;
            }
            if (!_inspectorLogged)
            {
                _inspectorLogged = true;
                Log.Info("UIBlueprintInspector found; usage=" + _inspector.usage + ", addFieldBtn=" + (_inspector.addFieldBtn != null));
            }
            UIButton addField = _inspector.addFieldBtn;
            RectTransform addFieldRect = addField != null ? addField.transform as RectTransform : null;
            bool wantUnderAddField = addFieldRect != null && addFieldRect.gameObject.activeInHierarchy;
            // Rebuild when the button is missing, was destroyed by a panel refresh, or ended up under
            // a hidden parent (activeInHierarchy false while the panel itself is active).
            bool rebuild = _optimizeButton == null
                || !_optimizeButton.activeInHierarchy
                || _optimizeButton.transform.parent == null
                || _anchoredAddFieldBtn != addField
                || _anchoredUnderAddField != wantUnderAddField;
            if (rebuild)
            {
                CreateOptimizeButton();
            }
            SyncOptimizeButton(true);
        }

        private void DestroyOptimizeButton()
        {
            if (_optimizeButton != null)
            {
                UnityEngine.Object.Destroy(_optimizeButton);
            }
            _optimizeButton = null;
            _optimizeButtonText = null;
            _optimizeButtonButton = null;
            _anchoredAddFieldBtn = null;
            _anchoredUnderAddField = false;
            _lastButtonState = "";
        }

        private void CreateOptimizeButton()
        {
            try
            {
                var inspector = _inspector;
                if (inspector == null)
                {
                    return;
                }
                DestroyOptimizeButton();
                UIButton addFieldBtn = inspector.addFieldBtn;
                RectTransform addFieldRect = addFieldBtn != null ? addFieldBtn.transform as RectTransform : null;
                RectTransform copyRect = inspector.copyBtnTrans;
                // Copy-button metrics are the requested button size, and the copy row is visible in
                // every inspector usage, so it doubles as the fallback anchor.
                Vector2 size = copyRect != null && copyRect.sizeDelta.x > 1f ? copyRect.sizeDelta : new Vector2(60f, 20f);
                if (size.y < 18f)
                {
                    size.y = 20f;
                }
                RectTransform anchor;
                Transform parent;
                bool underAddField = addFieldRect != null && addFieldRect.gameObject.activeInHierarchy;
                if (underAddField)
                {
                    anchor = addFieldRect;
                    parent = addFieldRect.parent;
                }
                else if (copyRect != null)
                {
                    anchor = copyRect;
                    parent = copyRect.parent;
                }
                else
                {
                    WarnOnce("no usable UI anchor found; the optimize button is not injected.");
                    return;
                }
                _anchoredUnderAddField = underAddField;

                // The 新建属性 widget keeps its graphic on a child object (or behind
                // Button.targetGraphic), so an Image on the anchor itself must not be required.
                Sprite sprite = null;
                Image.Type imageType = Image.Type.Simple;
                float alpha = 1f;
                Image sourceImage = anchor.GetComponent<Image>();
                if (sourceImage == null)
                {
                    sourceImage = anchor.GetComponentInChildren<Image>(true);
                }
                if (sourceImage != null)
                {
                    sprite = sourceImage.sprite;
                    imageType = sourceImage.type;
                    alpha = sourceImage.color.a;
                }
                if (sprite == null && addFieldBtn != null && addFieldBtn.button != null)
                {
                    var target = addFieldBtn.button.targetGraphic as Image;
                    if (target != null)
                    {
                        sprite = target.sprite;
                        imageType = target.type;
                        alpha = target.color.a;
                    }
                }
                if (sprite == null)
                {
                    Log.Warn("anchor exposes no sprite; the optimize button uses a plain tint instead.");
                }
                var go = new GameObject("BlueprintBeltSaver_OptimizeBtn", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = anchor.anchorMin;
                rect.anchorMax = anchor.anchorMax;
                rect.pivot = anchor.pivot;
                rect.sizeDelta = size;
                rect.anchoredPosition = underAddField
                    ? anchor.anchoredPosition - new Vector2(0f, size.y + 6f)
                    : anchor.anchoredPosition + new Vector2(-(size.x + 10f), 0f);
                var image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.type = imageType;
                image.color = sprite != null ? new Color(0.65f, 0.92f, 1f, alpha) : new Color(0.16f, 0.42f, 0.62f, 1f);
                image.raycastTarget = true;
                Text template = inspector.copyBtnText;
                if (template == null)
                {
                    template = anchor.GetComponentInChildren<Text>(true);
                }
                if (template != null)
                {
                    GameObject textGo = UnityEngine.Object.Instantiate(template.gameObject, rect, false);
                    textGo.name = "Text";
                    _optimizeButtonText = textGo.GetComponent<Text>();
                    if (_optimizeButtonText != null)
                    {
                        _optimizeButtonText.text = _text != null ? _text.OptimizeButton : "省带";
                        _optimizeButtonText.raycastTarget = false;
                        var textRect = _optimizeButtonText.rectTransform;
                        textRect.anchorMin = Vector2.zero;
                        textRect.anchorMax = Vector2.one;
                        textRect.offsetMin = Vector2.zero;
                        textRect.offsetMax = Vector2.zero;
                    }
                }
                else
                {
                    Log.Warn("no text template found; the long-belt button appears without a label.");
                }
                _optimizeButtonButton = go.GetComponent<Button>();
                _optimizeButtonButton.onClick = new Button.ButtonClickedEvent();
                _optimizeButtonButton.onClick.AddListener(new UnityAction(OnOptimizeButtonClick));
                _optimizeButton = go;
                _anchoredAddFieldBtn = addFieldBtn;
                Log.Info("省带按钮已注入：" + (underAddField ? "新建属性下方" : "复制按钮左侧") + "，尺寸 " + size.x + "x" + size.y);
            }
            catch (Exception ex)
            {
                Log.Error("injecting the long-belt button failed: " + ex);
            }
        }

        private void WarnOnce(string message)
        {
            if (_lastInjectWarning == message)
            {
                return;
            }
            _lastInjectWarning = message;
            Log.Warn(message);
        }

        private void SyncOptimizeButton(bool logState)
        {
            if (_optimizeButton == null || _inspector == null)
            {
                return;
            }
            var blueprint = _inspector.blueprint;
            bool hasBuildings = blueprint != null && blueprint.buildings != null && blueprint.buildings.Length > 0;
            // The button stays visible the whole time the blueprint panel is open: it used to vanish
            // after one use (panel rebuilt + empty editor), which looked like a bug. It is only
            // greyed out while there is nothing to merge.
            bool visible = _enabled.Value;
            if (_optimizeButton.activeSelf != visible)
            {
                _optimizeButton.SetActive(visible);
            }
            UIButton styleSource = _anchoredUnderAddField ? _inspector.addFieldBtn : null;
            bool interactable = hasBuildings;
            if (interactable && styleSource != null && styleSource.button != null)
            {
                interactable = styleSource.button.interactable;
            }
            if (_optimizeButtonButton != null && _optimizeButtonButton.interactable != interactable)
            {
                _optimizeButtonButton.interactable = interactable;
            }
            // Assigning Text.text - even the same value - dirties the text mesh and rebuilds it.
            // Doing that every frame was a second source of the frame-rate drop.
            if (_optimizeButtonText != null && _text != null && _optimizeButtonText.text != _text.OptimizeButton)
            {
                _optimizeButtonText.text = _text.OptimizeButton;
            }
            if (!logState)
            {
                return;
            }
            string state = "已创建=" + (_optimizeButton != null) + ", 锚点=新建属性下方=" + _anchoredUnderAddField + ", 有建筑=" + hasBuildings + ", 已启用=" + _enabled.Value + ", 可见=" + visible + ", 可点击=" + interactable;
            if (state != _lastButtonState)
            {
                _lastButtonState = state;
                Log.Info("长带优化按钮状态：" + state);
            }
        }

        private void OnOptimizeButtonClick()
        {
            _pendingBlueprint = null;
            _pendingSourceSnapshot = null;
            if (!_enabled.Value)
            {
                _status = _text != null ? _text.Disabled : "disabled";
                return;
            }
            if (_inspector == null || _inspector.blueprint == null)
            {
                return;
            }
            var source = _inspector.blueprint;
            if (source.buildings == null || !source.buildings.Any(IsBelt))
            {
                _status = _text != null ? _text.NoBelts : "no belts";
                return;
            }
            BlueprintData optimized;
            string summary;
            float cap;
            if (TryOptimize(source, out optimized, out summary, out cap))
            {
                _pendingBlueprint = optimized;
                _pendingSummary = summary;
                _pendingCap = cap;
                _pendingSourceSnapshot = SafeSnapshot(source);
                ShowConfirmDialog(summary);
            }
        }

        /// <summary>
        /// Confirmation uses the game's own message box (the same widget UXAssist shows for
        /// "初始化本行星"): warning icon on the left, title bar, and a cancel/confirm pair.
        /// Item icon tags (\tag;) render inline there, exactly like the game's tech-node text.
        /// </summary>
        private void ShowConfirmDialog(string message)
        {
            string title = _text != null ? _text.ConfirmTitle : "BlueprintBeltSaver 蓝图长带化";
            string cancel = "取消";
            string confirm = "确定";
            try
            {
                // The game localizes its own button captions through this extension.
                cancel = "取消".Translate();
                confirm = "确定".Translate();
            }
            catch
            {
                // Keep the plain Chinese captions when localization is not ready.
            }
            try
            {
                UIMessageBox.Show(title, message, cancel, confirm, UIMessageBox.WARNING, new UIMessageBox.Response(CancelPending), new UIMessageBox.Response(ApplyPending));
                Log.Info("确认弹窗已显示；" + message.Replace(Environment.NewLine, " | "));
            }
            catch (Exception ex)
            {
                Log.Error("showing the game message box failed: " + ex);
                _pendingBlueprint = null;
                _status = _text != null ? _text.ConfirmFailed : "confirm dialog failed";
            }
        }

        private void CancelPending()
        {
            _pendingBlueprint = null;
            _status = _text != null ? _text.Cancelled : "cancelled";
        }

        private void ApplyPending()
        {
            var optimized = _pendingBlueprint;
            _pendingBlueprint = null;
            if (optimized == null)
            {
                return;
            }
            try
            {
                BlueprintData target = _inspector != null ? _inspector.blueprint : null;
                if (target == null)
                {
                    Log.Warn("no blueprint is open in the inspector; nothing was applied.");
                    return;
                }
                // 记下原件与"本次建模用的粘贴位置"，供玩家在预览里移动/旋转后按新位置增量重算。
                _originalSnapshot = _pendingSourceSnapshot ?? SafeSnapshot(target);
                _pendingSourceSnapshot = null;
                _originalSpans = SpansFromSnapshot(_originalSnapshot);
                _lastObservationKey = ObservationKey(ReadPasteObservation());
                Log.Info("省带位置重算已就绪：原件快照 " + (_originalSnapshot == null ? "不可用（位置变化后不会重算）" : _originalSnapshot.Length + " 字符") +
                    "，当前位置键 " + _lastObservationKey);
                // Mutate the instance the inspector already holds instead of swapping the object, so
                // nothing else can keep pointing at the pre-merge blueprint.
                target.buildings = optimized.buildings;
                target.CheckBuildingData();
                // 写入本 MOD 优化标记（externalFields 扩展字段，随保存/加载往返）：之后这张蓝图
                // 直接从库里拖到地上时，位置复核发现越限也能按当前位置从当前状态重省。
                try
                {
                    if (string.IsNullOrEmpty(target.externalFields))
                    {
                        target.externalFields = OptimizeMarker;
                    }
                    else if (target.externalFields.IndexOf(OptimizeMarker, StringComparison.Ordinal) < 0)
                    {
                        target.externalFields = target.externalFields + ";" + OptimizeMarker;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("写入优化标记失败（不影响本次省带）：" + ex.Message);
                }
                try
                {
                    _inspector.Refresh(true, true, true);
                }
                catch (Exception ex)
                {
                    Log.Warn("inspector refresh failed: " + ex.Message);
                }
                GUIUtility.systemCopyBuffer = target.ToBase64String();
                SyncPasteTool(target, true);
                _status = string.Format(_text.ApplyDone, CountBelts(target));
                Log.Info("已应用优化：" + _pendingSummary.Replace(Environment.NewLine, " | "));
            }
            catch (Exception ex)
            {
                Log.Error("applying the optimized blueprint failed: " + ex);
                _status = ex.Message;
            }
        }

        /// <summary>
        /// 粘贴位置/朝向变化后的重算。三层保护，代价从低到高：
        ///   ① 节流：观测键没变（位置按 1 m 量化、朝向按偏航象限）或距上次不足
        ///      <see cref="ReoptimizeInterval"/>（0.25 s）→ 立即返回，不做任何几何计算；
        ///   ② **增量复核**：先用当前位置的模型把"当前结果"逐条带-带链节量一遍（O(链节数)，
        ///      1~3 ms）。全部合格就什么都不用做——玩家小幅移动位置时绝大多数情况走这一条；
        ///   ③ 真重算：只有复核不过才从**原件快照**重新省带（不是在上一次结果上再合并），
        ///      因此结果与"一开始就粘在这里"完全一致。
        /// 绝不在 OnGUI 或每帧无条件路径上做几何计算。
        /// </summary>
        private void ReoptimizeForPastePosition()
        {
            if (Time.unscaledTime < _nextReoptimize)
            {
                return;
            }
            // 只在"粘贴视图 + 工具打开 + 光标有效"时才做，避免在别的界面里空转。
            if (_inspector == null || _inspector.usage != UIBlueprintInspector.EUsage.Paste)
            {
                return;
            }
            var observation = ReadPasteObservation();
            if (!observation.HasValue)
            {
                return;
            }
            string key = ObservationKey(observation);
            if (key == _lastObservationKey)
            {
                return;
            }
            BlueprintData target = _inspector != null ? _inspector.blueprint : null;
            if (target == null || target.buildings == null)
            {
                return;
            }
            _nextReoptimize = Time.unscaledTime + ReoptimizeInterval;
            _lastObservationKey = key;
            try
            {
                // ② 增量复核：当前位置下现有结果还合格吗？只看"新造跨度"——原件里就有的超限跨度，
                //    游戏对原件同样拒绝，不是省带造成的，不该触发重算（否则原件本身带断损段的蓝图
                //    会在每次位置变化时白白重算一次并永远失败）。
                var live = BeltSpacingCalibration.Resolve(target.areas, target.buildings, IsBelt, observation);
                var check = VerifyHardLimit(target.buildings, live, _originalSpans);
                if (check.Over == 0)
                {
                    Log.Info($"粘贴位置变化（{key}）：现有省带结果仍然合格（最坏新链节 {check.Worst:F4} m" +
                        (check.KeptOver > 0 ? $"；原件同跨度越限 {check.KeptOver} 条，游戏对原件同样拒绝" : "") + $"），无需重算。");
                    return;
                }
                // ③A 有原件快照：从原件按新位置重新省带（结果与"一开始就粘在这里"完全一致）。
                if (_originalSnapshot != null)
                {
                    // 快照只读取、不清空：清空曾导致"第一次位置重算之后，继续拖动完全不再受检"
                    // （增量复核与真重算都被入口处的 null 检查拦下）——拖动中可能出现越限结果而无人拦截，
                    // 是铁律①的洞。防重入由 _lastObservationKey 与 ReoptimizeInterval 节流负责。
                    // ⚠ 必须用完整 FromBase64String（含头部）还原，不能用 BlueprintData.CreateNew：
                    //   CreateNew 只解析内容段，shortDesc/gameVersion/author 等头部字段全为 null（游戏
                    //   私有构造不调 Reset），后续 TryOptimize 里 source.Clone() 要走 ToBase64String →
                    //   FromBase64String 往返，null 头部会让序列化失败、Clone 返回空——实测拖动后的
                    //   每一次位置重算都因此报废（2026-10-13 玩家日志：连续 19 次 ArgumentNullException）。
                    string snapshot = _originalSnapshot;
                    var source = new BlueprintData();
                    if (source.FromBase64String(snapshot) != BlueprintDataIOError.OK
                        || source.buildings == null || source.buildings.Length == 0)
                    {
                        Log.Warn("粘贴位置变化，但原件快照无法还原；保持当前结果。");
                        return;
                    }
                    Log.Info($"粘贴位置变化（{key}）：现有结果在新位置有 {check.Over} 条越限链节，开始按新位置重新省带。");
                    BlueprintData rebuilt;
                    string summary;
                    float cap;
                    if (!TryOptimize(source, out rebuilt, out summary, out cap))
                    {
                        // 新位置下算不出安全结果：把原件放回粘贴工具，宁可玩家自己看到原件，
                        // 也不交付一个会被游戏拒绝「距离太远」的蓝图。
                        Log.Warn("新粘贴位置下无法得到安全的省带结果，已把原件放回粘贴工具（请在该位置重新点一次省带，或换回原来的位置）。");
                        target.buildings = source.buildings;
                        target.CheckBuildingData();
                        SyncPasteTool(target, false);
                        return;
                    }
                    target.buildings = rebuilt.buildings;
                    target.CheckBuildingData();
                    try
                    {
                        _inspector.Refresh(true, true, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("inspector refresh failed: " + ex.Message);
                    }
                    SyncPasteTool(target, false);
                    _status = string.Format(_text.ApplyDone, CountBelts(target));
                    Log.Info("已按新粘贴位置重算：" + summary.Replace(Environment.NewLine, " | "));
                    return;
                }
                // ③B 无原件快照，但蓝图带本 MOD 的优化标记（库里保存过的优化结果直接拖到地上）：
                //     从**当前状态**按新位置重新省带——越限的链会被按当前位置重新插值，不再把
                //     "按别的纬度优化出的跨度"硬粘到当前位置（玩家实测：反复省带后部分带子过长）。
                if (HasOptimizeMarker(target))
                {
                    string preState = SafeSnapshot(target);
                    var preSpans = BuildSpanIndex(target.buildings, IsBelt);
                    Log.Info($"粘贴位置变化（{key}）：当前结果在新位置有 {check.Over} 条越限链节（无原件快照），从当前状态按新位置重省。");
                    BlueprintData rebuilt;
                    string summary;
                    float cap;
                    if (!TryOptimize(target, out rebuilt, out summary, out cap))
                    {
                        Log.Warn("当前位置下无法得到安全的省带结果，保持当前状态（粘贴时游戏可能报「距离太远」）。");
                        return;
                    }
                    _originalSnapshot = preState;
                    _originalSpans = preSpans;
                    target.buildings = rebuilt.buildings;
                    target.CheckBuildingData();
                    try
                    {
                        _inspector.Refresh(true, true, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("inspector refresh failed: " + ex.Message);
                    }
                    SyncPasteTool(target, false);
                    _status = string.Format(_text.ApplyDone, CountBelts(target));
                    Log.Info("已按新粘贴位置从当前状态重省：" + summary.Replace(Environment.NewLine, " | "));
                    return;
                }
                Log.Warn($"粘贴位置变化（{key}）：当前结果在新位置有 {check.Over} 条越限链节，但无本 MOD 优化标记也无原件快照，保持原样。");
            }
            catch (Exception ex)
            {
                Log.Warn("按新粘贴位置重算失败，保持当前结果：" + ex.Message);
            }
        }

        /// <summary>本 MOD 的优化标记（写在蓝图 externalFields 扩展字段里，随保存/加载往返）。</summary>
        private const string OptimizeMarker = "BBSOPT=1";

        private static bool HasOptimizeMarker(BlueprintData bp)
            => bp != null && !string.IsNullOrEmpty(bp.externalFields)
                && bp.externalFields.IndexOf(OptimizeMarker, StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Pushes the merged blueprint into the paste tool, but only where the game itself would do
        /// so. Forcing previews outside the paste flow leaves <c>bpCursor &gt; 0</c> while the player
        /// build model can be null, which makes <c>BuildTool_BlueprintPaste.OnCameraPostRender</c>
        /// throw every frame.
        /// </summary>
        private void SyncPasteTool(BlueprintData optimized)
        {
            SyncPasteTool(optimized, true);
        }

        /// <summary>
        /// Pushes the merged blueprint into the paste tool, but only where the game itself would do
        /// so. Forcing previews outside the paste flow leaves <c>bpCursor &gt; 0</c> while the player
        /// build model can be null, which makes <c>BuildTool_BlueprintPaste.OnCameraPostRender</c>
        /// throw every frame.
        /// </summary>
        /// <param name="forceDetermine">
        /// true = 立即重建预览（省带确认那一次）；false = 只换蓝图，让游戏在它自己的下一帧里刷新。
        /// 重算路径用 false：<c>ForceDetermineBP</c> 会重建全部预览、动画与 GPU 缓冲，每 0.25 s
        /// 调一次就是实打实的掉帧来源。
        /// </param>
        private void SyncPasteTool(BlueprintData optimized, bool forceDetermine)
        {
            try
            {
                if (_inspector == null)
                {
                    return;
                }
                if (_inspector.usage != UIBlueprintInspector.EUsage.Paste)
                {
                    Log.Info($"paste tool left untouched: the inspector is not in paste view (usage={_inspector.usage}).");
                    return;
                }
                BuildTool_BlueprintPaste tool = _inspector.pasteBuildTool;
                if (tool == null)
                {
                    Log.Info("paste tool left untouched: unavailable.");
                    return;
                }
                if (!tool.active)
                {
                    Log.Info("paste tool left untouched: the tool is not open.");
                    return;
                }
                Player player = GameMain.mainPlayer;
                PlayerAction_Build actionBuild = player != null && player.controller != null ? player.controller.actionBuild : null;
                if (actionBuild == null || actionBuild.model == null)
                {
                    Log.Warn("paste tool left untouched: the player build model is not ready (the state that makes OnCameraPostRender throw).");
                    return;
                }
                BlueprintData clone = optimized.Clone();
                clone.CheckBuildingData();
                tool.blueprint = clone;
                if (forceDetermine)
                {
                    tool.ForceDetermineBP();
                    Log.Info("paste tool updated with the optimized blueprint.");
                }
                else
                {
                    Log.Info("paste tool updated with the re-optimized blueprint (previews refresh on the game's own next tick).");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("syncing the paste tool failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 读取"本次粘贴的锚点位置与朝向"。
        ///
        /// 粘贴锚点就是 <c>BuildTool_BlueprintPaste.dotsSnapped[0]</c>（BuildTool_BlueprintPaste
        /// ≈578 行由 RecalculateCursorPos + SnapTropic 写入，之后只被 ArrangeOverlapBP 这类
        /// 覆盖消解改写）。它已经是贴到经纬网格上的点，也正是 RefreshBuildPreview 用来重建整片
        /// 蓝图的那个锚点，所以直接拿它的纬度即可，不需要再复算 <c>RecalculateCursorPos</c>。
        ///
        /// 位置不可用时返回 null：模型会退回"蓝图自身锚点纬度"，并在位置到手后重算。
        /// </summary>
        private BeltSpacingCalibration.Observation? ReadPasteObservation()
        {
            try
            {
                if (_inspector == null || _inspector.usage != UIBlueprintInspector.EUsage.Paste)
                {
                    return null;
                }
                BuildTool_BlueprintPaste tool = _inspector.pasteBuildTool;
                if (tool == null || !tool.active || !tool.cursorValid)
                {
                    return null;
                }
                Vector3[] dots = tool.dotsSnapped;
                if (dots == null || dots.Length == 0 || tool.dotsCursor <= 0)
                {
                    return null;
                }
                Vector3 dot = dots[0];
                Vector3 normalized = dot.normalized;
                if (normalized.sqrMagnitude < 0.5f)
                {
                    return null;
                }
                double latitudeRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0, normalized.y)));
                return BeltSpacingCalibration.Observation.FromYaw(latitudeRad, tool.yaw);
            }
            catch (Exception ex)
            {
                WarnOnce("读取粘贴锚点失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 粘贴位置变化后的重算节流键：位置按 1 m 量化、朝向按"偏航象限"区分。
        /// 位置在同一格内、朝向同一象限时键不变 → 不做任何计算。
        /// </summary>
        private static string ObservationKey(BeltSpacingCalibration.Observation? observation)
            => observation.HasValue
                ? observation.Value.Quadrant + ":" + (observation.Value.AnchorLatitudeRad * 200.2).ToString("F1")
                : "none";

        private bool TryOptimize(BlueprintData source, out BlueprintData optimized, out string summary, out float cap)
        {
            optimized = null;
            summary = "";
            bool fromLongerBelts;
            cap = ResolveCap(out fromLongerBelts);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double modelMs = 0;
            try
            {
                BlueprintData bp = source.Clone();
                if (bp == null || bp.buildings == null)
                {
                    // 游戏的 Clone 是 ToBase64String → FromBase64String 往返，失败时返回 null
                    // 而不是抛异常。拿到空说明原件无法安全序列化往返，直接放弃本次省带。
                    Log.Warn("原件蓝图序列化往返失败（Clone 返回空）；保留原件。");
                    _status = _text.NoChange;
                    return false;
                }
                // 原件"格位跨度"索引：Optimize 会就地改写引用，必须趁引用还是原件时按
                // (区域,x,y)→(区域,x,y) 记下原件已有的全部带-带跨度。VerifyHardLimit 用它区分
                // "新造跨度"（省带造出来的，必须全部 ≤ 硬上限）与"原件同跨度"（游戏对原件
                // 同样拒绝的断损段，合不合并都一样，不该由省带来背锅）。
                var originalSpans = BuildSpanIndex(bp.buildings, IsBelt);
                int before = CountBelts(bp);
                // 安全余量：模型已经与游戏逐点同式（误差只来自 float 与浮点累积），
                // 因此余量只需要吸收"顶点被 float 截断 + 游戏端浮点累积"这一点点，取 15% 太浪费，
                // 0.98 对应约 2% 余量（硬上限 2.3022 m 处约 4.6 cm），实测足以覆盖全部蓝图。
                float capForOptimize = cap * SafetyMargin;
                bool usedResample = !_gridExactMerging.Value;
                // Iron rule guard: on a blueprint that spans several latitude bands, pin the neighbourhood
                // of any stored link the model judges over the hard limit, so a chain the game has to
                // rebuild around such a link keeps the anchors it needs. Zero on blueprints whose links
                // all fit, which is every blueprint measured so far.
                // 长度模型：按本次实际粘贴位置/朝向精确建模（见 BeltSpacingCalibration）。
                // 粘贴位置拿不到时（蓝图库、尚未进入放置状态）由模型自己在合法纬度里按"实测省带量"
                // 挑一个默认值（MeasureSavingsAt 就是那个试算回调），并在位置到手后重算。
                var observation = ReadPasteObservation();
                bool searchLogged = false;
                var calibration = BeltSpacingCalibration.Resolve(bp.areas, bp.buildings, IsBelt, observation,
                    (latDeg, stride) =>
                    {
                        if (!searchLogged)
                        {
                            searchLogged = true;
                            Log.Info("蓝图库场景：位置未知，开始按实测省带量挑选默认锚点纬度（试算中）。");
                        }
                        return MeasureSavingsAt(bp, latDeg, capForOptimize, usedResample, stride);
                    });
                modelMs = watch.Elapsed.TotalMilliseconds;
                Log.Info("间距模型：" + calibration);
                Func<BlueprintBuilding, BlueprintBuilding, double> meters =
                    (a, b) => BeltSpacingCalibration.Distance(a, b, calibration);
                BeltPathOptimizer.Report report;
                bool pinOverLimit = calibration.MultiBand;
                // Snapshot before the first run: the optimizer rewires links in place, and the
                // fallback below needs the untouched blueprint.
                string snapshot = usedResample ? null : SafeSnapshot(bp);
                var preMerge = bp.buildings;
                bp.buildings = BeltPathOptimizer.Optimize(bp.buildings, IsBelt, b => capForOptimize, meters, out report, _rebindSorterLinks.Value, !usedResample, pinOverLimit, _sorterForceRebind.Value && _rebindSorterLinks.Value);
                int after = CountBelts(bp);
                if (!usedResample && snapshot != null)
                {
                    // 双模式取优。保点模式（保留原节点子集）受一个数学上限：纬度方向 1 格 = 1.2566 m，
                    // 2 格跨度 = 2.5132 m 永远超过优化上限 2.254 m，所以均匀 1 格直链一节都合不掉
                    // （16.4小火箭 保点只能省 152，全部来自曲线/亚格距段）。插值模式按弧长把链重排到
                    // ceil(长度/上限) 个跨度，直链可省 ~44%（同一张图实测省 634）。这里在保点结果之外
                    // 总是试一次插值，只有当它**省得更多且通过硬上限自检**时才采用；插值结果不合格就
                    // 保留保点结果，绝不让试验失败拖垮原本可交付的结果。
                    var trial = BlueprintData.CreateNew(snapshot);
                    if (trial != null && trial.buildings != null && trial.buildings.Length > 0)
                    {
                        BeltPathOptimizer.Report trialReport;
                        var trialPreMerge = trial.buildings;
                        var trialBuildings = BeltPathOptimizer.Optimize(trial.buildings, IsBelt, b => capForOptimize, meters, out trialReport, _rebindSorterLinks.Value, false, pinOverLimit, _sorterForceRebind.Value && _rebindSorterLinks.Value);
                        int trialAfter = 0;
                        foreach (var building in trialBuildings)
                        {
                            if (IsBelt(building))
                            {
                                trialAfter++;
                            }
                        }
                        if (trialAfter >= after)
                        {
                            Log.Info($"插值模式无额外收益（保点 {before} → {after}，插值 → {trialAfter}），保留保点结果。");
                        }
                        else
                        {
                            // 采纳前先按"只看新造跨度"的硬上限口径自检插值结果；原型经 Optimize 的
                            // 逐跨度复测（> 上限即拒绝整条链），这里是对同一模型的第二次独立复核。
                            var trialCheck = VerifyHardLimit(trialBuildings, calibration, originalSpans);
                            if (trialCheck.Over > 0)
                            {
                                Log.Warn($"插值模式可省 {before - trialAfter} 节但硬上限自检不过（越限新链节 {trialCheck.Over} 条），保留保点结果 {before} → {after}。");
                            }
                            else
                            {
                                Log.Info($"插值模式省带更多：{before} → {trialAfter}（保点模式 → {after}），采用插值结果（最坏新链节 {trialCheck.Worst:F4} m）。");
                                trial.buildings = trialBuildings;
                                trial.CheckBuildingData();
                                bp = trial;
                                preMerge = trialPreMerge;
                                after = trialAfter;
                                report = trialReport;
                                usedResample = true;
                            }
                        }
                    }
                }
                bp.CheckBuildingData();
                // Merging removes belt nodes, so an inserter that pointed at a removed node loses its
                // target (the game nulls the reference and the sorter is dead in game). Every inserter
                // end is verified here and re-bound when it is empty, dangling or beyond the game's
                // reach; machine ends are never touched.
                var binderReport = InserterBinder.Repair(bp.buildings, IsBelt, meters);
                Log.Info("分拣器校验：" + binderReport);
                // Full savings are taken first; only then is every inserter judged. If a merged piece no
                // longer passes through the window the game needs (1.414 m from the inserter body or
                // its arm end), the original node is put back so the path returns to its old shape
                // exactly there - the merge keeps its savings everywhere else.
                // Window repair is implemented (BeltWindowRepair) but NOT enabled: the 1.414 m window is
                // the rule the game uses to AUTO-MATCH a missing target, not the rule it applies to a
                // stored link. Measuring it offline predicted 46 dead ends on a blueprint the user only
                // sees ~5 failures on, so acting on it would cost belts for nothing. Kept for the case
                // where a target is genuinely missing.
#pragma warning disable CS0162 // 故意保留一条永不执行的修复分支，供日后启用（见上面的理由）。
                if (false)
                {
                    var windowList = bp.buildings.ToList();
                    var windowReport = BeltWindowRepair.Repair(windowList, preMerge, IsBelt);
                    bp.buildings = windowList.ToArray();
                    after = CountBelts(bp);
                    Log.Info("分拣器取带窗口：" + windowReport);
                }
#pragma warning restore CS0162
                if (bp.buildings == null || bp.buildings.Length > MaxBlueprintBuildings)
                {
                    Log.Error($"optimized blueprint has {bp.buildings?.Length ?? 0} buildings, above the {MaxBlueprintBuildings} limit; keeping the original.");
                    _status = _text.TooManyBuildings;
                    return false;
                }
                WriteAudit(report, cap, fromLongerBelts);
                summary = BuildSummary(bp, before, after, cap, fromLongerBelts, report, usedResample, binderReport);
                Log.Info($"干跑：传送带 {before} → {after}（分拣器连接重绑={_rebindSorterLinks.Value}，保留原节点={!usedResample}）；{report}");
                // 铁律①的自检：合并结果的每一条**新造**带-带链节都要在**本模型**下 ≤ 硬上限。
                // 由于本模型与游戏逐点同式，这条自检就是对游戏 sqrMagnitude > 5.3 的直接复核。
                // 原件同跨度的超限链节单独计数：它们在原件里就存在，游戏对原件同样拒绝那几节带，
                // 合并不会让任何一节带变得比原件更差；因它们放弃整次省带只会白扔收益。
                double beforeCheckMs = watch.Elapsed.TotalMilliseconds;
                var check = VerifyHardLimit(bp.buildings, calibration, originalSpans);
                double checkMs = watch.Elapsed.TotalMilliseconds - beforeCheckMs;
                double mergeMs = beforeCheckMs - modelMs;
                Log.Info($"省带耗时：{watch.Elapsed.TotalMilliseconds:F1} ms（模型 {modelMs:F1} ms / 合并 {mergeMs:F1} ms / 校验 {checkMs:F1} ms）；" +
                    $"模型下最坏新链节 {check.Worst:F4} m，越限新链节 {check.Over} 条（上限 {BeltPathOptimizer.MaximumMeters:F4} m），" +
                    $"原件同跨度越限 {check.KeptOver} 条，原件最坏链节 {calibration.LongestOriginalLink:F4} m");
                if (check.Over > 0)
                {
                    // 自检没过：不交付一个会因省带新增「距离太远」的蓝图。
                    Log.Error($"省带结果模型自检不过（越限新链节 {check.Over} 条，最坏 {check.Worst:F4} m），已放弃本次省带，保留原件。");
                    _status = _text.NoChange;
                    return false;
                }
                optimized = bp;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("belt optimization failed, the blueprint is left untouched: " + ex);
                _status = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 优化上限相对玩家配置的安全余量。旧版用 0.85，是为了吸收"按最坏纬度建模"的粗粒度；
        /// 本版模型与游戏逐点同式，误差只剩蓝图坐标的 float 截断（0.0001 格 ≈ 0.13 mm）与游戏端
        /// 浮点累积，所以余量收到 2%（硬上限 2.3022 m 处约 4.6 cm）。这直接换回省带量。
        /// </summary>
        private const float SafetyMargin = 0.98f;

        /// <summary>
        /// 铁律①的自检结果。<see cref="Over"/>/<see cref="Worst"/> 只统计**新造跨度**
        /// （原件里没有同样格位跨度的链节）；原件里就有的超限跨度记入 <see cref="KeptOver"/>，
        /// 它们不触发放弃——游戏对原件同样拒绝那几节带，省带没有让任何一节带变得更差。
        /// </summary>
        private readonly struct LimitCheck
        {
            internal readonly int Over;
            internal readonly double Worst;
            internal readonly int KeptOver;
            internal readonly double WorstAll;
            internal LimitCheck(int over, double worst, int keptOver, double worstAll)
            {
                Over = over;
                Worst = worst;
                KeptOver = keptOver;
                WorstAll = worstAll;
            }
        }

        /// <summary>
        /// 逐条复核合并结果的带-带链节是否在游戏硬上限（sqrMagnitude &gt; 5.3）之内。
        /// 只查"带 → 带"的连接，与 BuildTool_BlueprintPaste ≈2087 行的判据一致；
        /// 带 → 非带 用的是另一套（16 / 28）阈值，不在此列。
        /// <paramref name="originalSpans"/> 为原件"格位跨度"集合；为 null 时退回旧行为
        /// （所有越限链节都算在 Over 里，宁可保守）。
        /// </summary>
        private static LimitCheck VerifyHardLimit(BlueprintBuilding[] buildings, BeltSpacingCalibration.Result model,
            HashSet<(long, long)> originalSpans)
        {
            if (buildings == null)
            {
                return new LimitCheck(0, 0.0, 0, 0.0);
            }
            var set = new HashSet<BlueprintBuilding>(buildings);
            int over = 0, keptOver = 0;
            double worst = 0.0, worstAll = 0.0;
            double hard = BeltPathOptimizer.MaximumMeters;
            foreach (var building in buildings)
            {
                if (building == null || !IsBelt(building) || building.outputObj == null || !IsBelt(building.outputObj)
                    || !set.Contains(building.outputObj))
                {
                    continue;
                }
                double distance = BeltSpacingCalibration.Distance(building, building.outputObj, model);
                if (double.IsNaN(distance) || double.IsInfinity(distance))
                {
                    continue;
                }
                if (distance > worstAll)
                {
                    worstAll = distance;
                }
                bool preexisting = originalSpans != null
                    && originalSpans.Contains((SpanSideKey(building), SpanSideKey(building.outputObj)));
                if (distance > hard)
                {
                    if (preexisting)
                    {
                        keptOver++;
                    }
                    else
                    {
                        over++;
                        if (distance > worst)
                        {
                            worst = distance;
                        }
                    }
                }
                else if (!preexisting && distance > worst)
                {
                    worst = distance;
                }
            }
            return new LimitCheck(over, worst, keptOver, worstAll);
        }

        /// <summary>
        /// 原件带-带链节的"格位跨度"索引：合并只是从原件节点集合里删点 + 引用改指，存活节点的
        /// localOffset 从不改动，所以合并结果里每条链节的两端格位要么是原件已有跨度（改指的产物），
        /// 要么在原件里本来就相邻。键 = (区域, x, y) 有序对，坐标按 1/8192 格（≈0.16 mm）量化。
        /// </summary>
        private static HashSet<(long, long)> BuildSpanIndex(BlueprintBuilding[] buildings, Func<BlueprintBuilding, bool> isBelt)
        {
            var spans = new HashSet<(long, long)>();
            if (buildings == null)
            {
                return spans;
            }
            foreach (var building in buildings)
            {
                if (building == null || !isBelt(building) || building.outputObj == null || !isBelt(building.outputObj))
                {
                    continue;
                }
                spans.Add((SpanSideKey(building), SpanSideKey(building.outputObj)));
            }
            return spans;
        }

        /// <summary>单个端点的格位键：8 位区域号 + 28 位量化 x + 28 位量化 y（±16384 格以内精确）。</summary>
        private static long SpanSideKey(BlueprintBuilding node)
        {
            long area = (long)(node.areaIndex & 0xFF) << 56;
            long x = ((long)Math.Round(node.localOffset_x * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
            long y = ((long)Math.Round(node.localOffset_y * 8192.0, MidpointRounding.AwayFromZero) + (1L << 27)) & 0xFFFFFFF;
            return area | (x << 28) | y;
        }

        /// <summary>一次性告警守卫（跨度索引构建失败只报一次）。</summary>
        private static bool _warnedSpanIndex;

        /// <summary>从原件快照反解出跨度索引（一次性代价，随快照缓存）。</summary>
        private static HashSet<(long, long)> SpansFromSnapshot(string snapshot)
        {
            try
            {
                if (string.IsNullOrEmpty(snapshot))
                {
                    return null;
                }
                var data = BlueprintData.CreateNew(snapshot);
                return data?.buildings == null ? null : BuildSpanIndex(data.buildings, IsBelt);
            }
            catch (Exception ex)
            {
                if (!_warnedSpanIndex)
                {
                    _warnedSpanIndex = true;
                    Log.Warn("原件跨度索引构建失败，硬上限自检按全量保守判定：" + ex.Message);
                }
                return null;
            }
        }

        /// <summary>
        /// 蓝图库场景的试算：把蓝图放在给定锚点纬度上，量一次"能省多少条带"。
        ///
        /// 这是 <see cref="BeltSpacingCalibration.ChooseBestLatitude"/> 的目标函数——只按几何近似指标
        /// 挑纬度会挑到 0 收益的位置（实测 16.4小火箭 在 76° 省 157、在 46° 省 0），所以必须用真正的
        /// 优化器量。回调在**克隆出来的一次性副本**上跑，绝不碰玩家原件。
        ///
        /// 试算固定用**插值模式**（gridExact=false）：主流程是"保点 + 插值取更优"，而插值模式在
        /// 均匀 1 格直链上的收益（省 ~44%）严格高于保点模式（省 0），用保点口径挑纬度会选错位置
        /// （实测 16.4 插值在 60° 省 634、在 88° 只省 177，保点口径完全看不出这个差异）。
        ///
        /// <paramref name="stride"/> &gt; 1 时把蓝图按空间分块抽稀（每块只留 1 个节点），把粗扫的代价
        /// 压到 1/stride² 量级；挑纬度只看相对排序，最终那次合并是全量跑的，所以精度不受影响。
        /// 抽稀按 (区域, x, y) 分块，保证不会把某片区域整块抽没。
        /// </summary>
        private int? MeasureSavingsAt(BlueprintData source, double anchorLatDegrees, float capForOptimize, bool usedResample, int stride)
        {
            try
            {
                string snapshot = SafeSnapshot(source);
                var trial = BlueprintData.CreateNew(snapshot);
                if (trial == null || trial.buildings == null || trial.buildings.Length == 0)
                {
                    return null;
                }
                if (stride > 1)
                {
                    var thinned = ThinForProbe(trial.buildings, stride);
                    if (thinned.Length < 8)
                    {
                        return null;
                    }
                    trial.buildings = thinned;
                }
                int trialBefore = CountBelts(trial);
                var observation = BeltSpacingCalibration.Observation.FromYaw(anchorLatDegrees * Math.PI / 180.0, 0f);
                // 试算用不带 search 的 Resolve：Observation 已给出，不会再触发纬度搜索（无递归）。
                var trialModel = BeltSpacingCalibration.Resolve(trial.areas, trial.buildings, IsBelt, observation);
                if (trialModel.LongestOriginalLink > BeltPathOptimizer.MaximumMeters)
                {
                    return null;
                }
                var trialResult = BeltPathOptimizer.Optimize(trial.buildings, IsBelt, _ => capForOptimize,
                    (a, b) => BeltSpacingCalibration.Distance(a, b, trialModel), out _, _rebindSorterLinks.Value,
                    false, trialModel.MultiBand);
                int trialAfter = 0;
                foreach (var building in trialResult)
                {
                    if (IsBelt(building))
                    {
                        trialAfter++;
                    }
                }
                return trialBefore - trialAfter;
            }
            catch (Exception ex)
            {
                WarnOnce("试算锚点纬度失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 试算用的抽稀：**按建筑序号均匀取样**，而不是按空间分块。
        ///
        /// 空间分块会把整条链整段删掉，试算出来的相对收益与全量跑严重不符（实测 16.4小火箭
        /// 从 157 掉到 36，纬度搜索因此选错位置）。按序号均匀取样保留的是"每条链上稀疏的节点"，
        /// 链条数量与非带建筑（分拣器/机器）都在，相对排序仍然有效。
        /// 抽稀后只用于**比较不同纬度的相对收益**，不参与最终结果。
        /// </summary>
        private static BlueprintBuilding[] ThinForProbe(BlueprintBuilding[] buildings, int stride)
        {
            if (stride <= 1)
            {
                return buildings;
            }
            var byArea = new Dictionary<int, List<BlueprintBuilding>>();
            foreach (var building in buildings)
            {
                if (!byArea.TryGetValue(building.areaIndex, out var list))
                {
                    list = new List<BlueprintBuilding>();
                    byArea[building.areaIndex] = list;
                }
                list.Add(building);
            }
            var keptSet = new HashSet<BlueprintBuilding>();
            foreach (var list in byArea.Values)
            {
                for (int i = 0; i < list.Count; i += stride)
                {
                    keptSet.Add(list[i]);
                    // 非带建筑（分拣器、机器）全部保留：它们决定锚点，影响收益排序。
                    if (!IsBelt(list[i]))
                    {
                        continue;
                    }
                }
            }
            // 非带建筑与被引用节点一律保留，避免试算里出现悬空引用。
            foreach (var building in buildings)
            {
                if (!IsBelt(building))
                {
                    keptSet.Add(building);
                    if (building.inputObj != null && IsBelt(building.inputObj)) keptSet.Add(building.inputObj);
                    if (building.outputObj != null && IsBelt(building.outputObj)) keptSet.Add(building.outputObj);
                }
            }
            var kept = new List<BlueprintBuilding>(keptSet.Count);
            foreach (var building in buildings)
            {
                if (keptSet.Contains(building))
                {
                    kept.Add(building);
                }
            }
            return kept.ToArray();
        }

        /// <summary>Blueprint code snapshot used to retry with the resampling algorithm.</summary>
        private static string SafeSnapshot(BlueprintData bp)
        {
            try
            {
                return bp.ToBase64String();
            }
            catch (Exception ex)
            {
                Log.Warn("snapshotting the blueprint failed; the resampling fallback is skipped: " + ex.Message);
                return null;
            }
        }

        private string BuildSummary(BlueprintData optimized, int before, int after, float cap, bool fromLongerBelts, BeltPathOptimizer.Report report, bool usedResample, InserterBinder.Report binderReport)
        {
            var tiers = new SortedSet<int>();
            foreach (var building in optimized.buildings)
            {
                if (IsBelt(building))
                {
                    tiers.Add(building.itemId);
                }
            }
            int sumBefore = 0;
            int sumAfter = 0;
            var tierLines = new List<string>();
            foreach (int itemId in tiers)
            {
                int n0;
                int n1;
                CountTier(_inspector != null ? _inspector.blueprint : null, itemId, out n0);
                CountTier(optimized, itemId, out n1);
                sumBefore += n0;
                sumAfter += n1;
                if (n0 == n1)
                {
                    // Tiers without a change are omitted, as requested.
                    continue;
                }
                tierLines.Add(ItemIconName(itemId) + $": {n0} → {n1}（{_text.Saved} {n0 - n1}）");
            }
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(_text.TotalLine, sumBefore, sumAfter, sumBefore - sumAfter));
            if (tierLines.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine(_text.NoChange);
            }
            else
            {
                sb.AppendLine();
                foreach (string line in tierLines)
                {
                    sb.AppendLine(line);
                }
            }
            sb.AppendLine();
            sb.AppendLine($"{_text.CurrentCap}: {cap:F6} m ({(fromLongerBelts ? _text.CapFromLongerBelts : _text.CapFromOwn)})");
            sb.AppendLine(usedResample ? _text.ModeResampled : _text.ModeKeptOriginal);
            sb.AppendLine(string.Format(_text.SorterCheck, binderReport.Inserters, binderReport.EmptyBefore, binderReport.Rebond, binderReport.Unresolved, binderReport.Verified));
            if (_rebindSorterLinks.Value)
            {
                sb.AppendLine($"{_text.RebindEnabled}: {report.Rebound}, {_text.RebindYawSkipped}: {report.AttachmentYawSkipped}");
                sb.AppendLine(_text.RebindWarning);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Rich-text icon + name for an item. The game's own text generator (Youthcat.TextGenerator
        /// with Youthcat.TextIconMapping) resolves the "\tag-;" form embedded in any Text, so the
        /// message box renders the item icon inline exactly like the tech tree does.
        /// </summary>
        private static string ItemIconName(int itemId)
        {
            try
            {
                ItemProto proto = LDB.items.Select(itemId);
                if (proto == null)
                {
                    return BeltTierName(itemId);
                }
                string tag = proto.iconNameTagString;
                return string.IsNullOrEmpty(tag) ? proto.name : tag;
            }
            catch
            {
                return BeltTierName(itemId);
            }
        }

        private void CountTier(BlueprintData data, int itemId, out int count)
        {
            count = 0;
            if (data == null || data.buildings == null)
            {
                return;
            }
            foreach (var building in data.buildings)
            {
                if (building != null && building.itemId == itemId && IsBelt(building))
                {
                    count++;
                }
            }
        }

        private static string BeltTierName(int itemId)
        {
            try
            {
                var proto = LDB.items.Select(itemId);
                if (proto != null && !string.IsNullOrEmpty(proto.name))
                {
                    return proto.name;
                }
            }
            catch
            {
                // LDB may not be ready yet; fall back to a stable label.
            }
            if (itemId == 2001)
            {
                return "MK.I";
            }
            if (itemId == 2002)
            {
                return "MK.II";
            }
            if (itemId == 2003)
            {
                return "MK.III";
            }
            return "itemId " + itemId;
        }

        private static int CountBelts(BlueprintData data)
        {
            if (data == null || data.buildings == null)
            {
                return 0;
            }
            int count = 0;
            foreach (var building in data.buildings)
            {
                if (IsBelt(building))
                {
                    count++;
                }
            }
            return count;
        }

        private static void WriteAudit(BeltPathOptimizer.Report report, float cap, bool fromLongerBelts)
        {
            try
            {
                string folder = Path.Combine(Paths.ConfigPath, "BlueprintBeltSaver-audit");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "chains-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
                var lines = new List<string>
                {
                    "# cap_m=" + cap.ToString("F6") + " source=" + (fromLongerBelts ? "LongerBelts" : "own-config"),
                    "# " + report.ToString()
                };
                lines.AddRange(report.Chains);
                File.WriteAllLines(file, lines.ToArray(), Encoding.UTF8);
                Log.Info("per-chain audit written to " + file);
            }
            catch (Exception ex)
            {
                Log.Warn("writing the per-chain audit failed: " + ex.Message);
            }
        }

        private static bool IsBelt(BlueprintBuilding building)
        {
            if (building == null)
            {
                return false;
            }
            if (building.itemId >= BeltItemIdFirst && building.itemId <= BeltItemIdLast)
            {
                return true;
            }
            try
            {
                var proto = LDB.models.Select((int)building.modelIndex);
                return proto != null && proto.prefabDesc != null && proto.prefabDesc.isBelt;
            }
            catch
            {
                return false;
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BBSaver.SelfTest
{
    public partial class SelfTestPlugin
    {
        private const BindingFlags RestoreFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static object Get(object o, string n) => o.GetType().GetField(n, RestoreFlags).GetValue(o);
        private static void Set(object o, string n, object v) => o.GetType().GetField(n, RestoreFlags).SetValue(o, v);
        private static object Call(object o, string n, params object[] p) => o.GetType().GetMethod(n, RestoreFlags).Invoke(o, p);
        private static bool ConfigValue(object o, string field) => (bool)Get(o, field).GetType().GetProperty("Value").GetValue(Get(o, field), null);
        private static void ConfigValue(object o, string field, bool v) => Get(o, field).GetType().GetProperty("Value").SetValue(Get(o, field), v, null);

        private void RunRestoreTests(Assembly mod, object plugin)
        {
            if (plugin == null) { Record("restore-plugin", false, "plugin unavailable"); return; }
            bool height = ConfigValue(plugin, "_restoreQuantizeHeight"), strict = ConfigValue(plugin, "_restoreStrictGrid");
            string output = Path.Combine(Dir, "restored-B");
            Directory.CreateDirectory(output);
            try
            {
                ConfigValue(plugin, "_restoreQuantizeHeight", true);
                ConfigValue(plugin, "_restoreStrictGrid", false);
                var engine = mod.GetType("BlueprintBeltSaver.BlueprintGridRestorer").GetMethod("TryRestore", RestoreFlags);
                foreach (var file in Directory.GetFiles(BOutDir, "B_lib_*.txt").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string name = Path.GetFileNameWithoutExtension(file).Substring(6);
                    try
                    {
                        var source = new BlueprintData();
                        if (source.FromBase64String(File.ReadAllText(file)) != BlueprintDataIOError.OK) throw new Exception("parse failed");
                        string unchanged = source.ToBase64String();
                        var args = new object[] { source, null, null, false };
                        bool ok = (bool)Call(plugin, "TryRestore", args);
                        var result = args[1] as BlueprintData;
                        Record("restore-wrapper:" + name, ok && result != null && unchanged == source.ToBase64String(), args[2] as string ?? "no summary");
                        if (!ok || result == null) continue;
                        var direct = new object[] { source, null, null, true, false };
                        bool directOk = (bool)engine.Invoke(null, direct);
                        Record("restore-counts:" + name, directOk && (int)Get(direct[2], "After") == result.buildings.Count(IsBelt),
                            $"{source.buildings.Count(IsBelt)}→{result.buildings.Count(IsBelt)}; protected={Get(direct[2], "ProtectedNodes")}");
                        int protectedCount = (int)Get(direct[2], "ProtectedNodes");
                        if (protectedCount > 0)
                        {
                            var auditDir = Path.Combine(BepInEx.Paths.ConfigPath, "BlueprintBeltSaver-restore-audit");
                            string audit = Directory.GetFiles(auditDir, "*.csv").OrderByDescending(File.GetLastWriteTimeUtc).First();
                            Record("restore-protected-report:" + name, File.ReadAllLines(audit).Length == protectedCount + 1, $"CSV rows={File.ReadAllLines(audit).Length - 1}");
                        }
                        File.WriteAllText(Path.Combine(output, "B_R_" + name + ".txt"), result.ToBase64String(), new UTF8Encoding(false));
                        var repeated = new object[] { result.Clone(), null, null, true, false };
                        bool again = (bool)engine.Invoke(null, repeated);
                        Record("restore-idempotent:" + name, again && Fingerprint(result.buildings) == Fingerprint(((BlueprintData)repeated[1]).buildings), "restore→serialize→restore");
                        RealPreviewAudit(mod, source, result, name);
                    }
                    catch (Exception ex) { Record("restore:" + name, false, ex.InnerException?.ToString() ?? ex.ToString()); }
                }
                RestoreUiTests(mod, plugin);
            }
            finally
            {
                ConfigValue(plugin, "_restoreQuantizeHeight", height);
                ConfigValue(plugin, "_restoreStrictGrid", strict);
            }
        }

        // Ground truth: call the game's real RefreshBuildPreview, not the copied distance model.
        // A belt-only projection avoids requiring a save/factory to compute inserter machine slots.
        private void RealPreviewAudit(Assembly mod, BlueprintData source, BlueprintData result, string name)
        {
            var envelope = mod.GetType("BlueprintBeltSaver.PasteEnvelope");
            var env = envelope.GetMethod("Build", RestoreFlags).Invoke(null, new object[] { source.areas, source.primaryAreaIdx, source.buildings, new Func<BlueprintBuilding, bool>(IsBelt), true });
            var frames = (IEnumerable)envelope.GetProperty("ProtectedFrames", RestoreFlags).GetValue(env, null);
            var bp = result.Clone();
            bp.buildings = bp.buildings.Where(IsBelt).ToArray();
            var keep = new HashSet<BlueprintBuilding>(bp.buildings);
            for (int i = 0; i < bp.buildings.Length; i++)
            {
                var n = bp.buildings[i]; n.index = i;
                if (n.inputObj != null && !keep.Contains(n.inputObj)) n.inputObj = null;
                if (n.outputObj != null && !keep.Contains(n.outputObj)) n.outputObj = null;
            }
            BuildPreview[] previews = null;
            BlueprintUtils.InitBuildPreviewByBPData(bp, ref previews, 1);
            var planet = new PlanetData { radius = 200f, scale = 1f };
            var conds = new IntVector4[bp.areas.Length];
            var boxes = new BPGratBox[bp.areas.Length];
            byte[] reforms = null;
            int poses = 0, over = 0; float worstSq = 0;
            foreach (var frame in frames)
            {
                int c = (int)Get(frame, "LatGrid"); float yaw = (float)Get(frame, "Yaw");
                var cursor = (Vector3)envelope.GetMethod("CursorAt", RestoreFlags).Invoke(null, new object[] { c });
                var dots = new[] { cursor };
                BlueprintUtils.GenerateAreaGratBoxByBPData(bp, boxes, conds, dots, 1, yaw, 200);
                BlueprintUtils.RefreshBuildPreview(planet, bp, previews, ref reforms, false, dots, 1, conds, yaw, false, 200);
                poses++;
                foreach (var n in bp.buildings)
                {
                    if (n.outputObj == null || !IsBelt(n.outputObj)) continue;
                    float d2 = (previews[n.outputObj.index].lpos - previews[n.index].lpos).sqrMagnitude;
                    worstSq = Mathf.Max(worstSq, d2);
                    if (d2 > 5.3f) over++;
                }
            }
            Record("restore-real-preview:" + name, poses > 0 && over == 0, $"game RefreshBuildPreview: poses={poses}, over={over}, worst={Math.Sqrt(worstSq):F4} m");
        }

        private void RestoreUiTests(Assembly mod, object plugin)
        {
            GameObject root = null;
            object oldInspector = Get(plugin, "_inspector");
            bool enabled = ConfigValue(plugin, "_enabled");
            string clipboard = GUIUtility.systemCopyBuffer;
            try
            {
                root = new GameObject("BBSaver_RestoreUiTest", typeof(RectTransform), typeof(Canvas));
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var panel = new GameObject("Inspector", typeof(RectTransform), typeof(UIBlueprintInspector));
                panel.transform.SetParent(root.transform, false);
                var pr = panel.GetComponent<RectTransform>(); pr.sizeDelta = new Vector2(800, 400);
                var inspector = panel.GetComponent<UIBlueprintInspector>(); inspector.rectTrans = pr;
                var anchor = new GameObject("AddField", typeof(RectTransform), typeof(Image), typeof(Button), typeof(UIButton));
                anchor.transform.SetParent(panel.transform, false);
                var ar = anchor.GetComponent<RectTransform>(); ar.anchoredPosition = new Vector2(180, 100); ar.sizeDelta = new Vector2(80, 24);
                var ui = anchor.GetComponent<UIButton>(); ui.button = anchor.GetComponent<Button>(); inspector.addFieldBtn = ui;
                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text)); textGo.transform.SetParent(anchor.transform, false);
                var text = textGo.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); text.fontSize = 14;
                inspector.copyBtnText = text; inspector.copyBtnTrans = ar; inspector.usage = UIBlueprintInspector.EUsage.Browser;
                var seed = new BlueprintData(); seed.FromBase64String(File.ReadAllText(Path.Combine(BlueprintDir, "80环带.txt")));
                seed.buildings = Enumerable.Range(0, 4).Select(i => new BlueprintBuilding
                {
                    index = i, areaIndex = 0, itemId = 2001, modelIndex = 35, localOffset_x = i * 2, localOffset_x2 = i * 2,
                    localOffset_y = 0, localOffset_y2 = 0, localOffset_z = 0, localOffset_z2 = 0,
                    parameters = new int[0], content = "", inputToSlot = 1, outputToSlot = 1
                }).ToArray();
                for (int i = 1; i < 4; i++) { seed.buildings[i - 1].outputObj = seed.buildings[i]; seed.buildings[i].inputObj = seed.buildings[i - 1]; }
                inspector.blueprint = seed;
                ConfigValue(plugin, "_enabled", true); Set(plugin, "_inspector", inspector);
                foreach (float scale in new[] { 1f, 1.6f, 0.8f })
                {
                    panel.transform.localScale = Vector3.one * scale;
                    Call(plugin, "CreateOptimizeButton"); Canvas.ForceUpdateCanvases(); Call(plugin, "SyncOptimizeButton", true);
                    var restore = (GameObject)Get(plugin, "_restoreButton"); var save = (GameObject)Get(plugin, "_optimizeButton");
                    var corners = new Vector3[4]; save.GetComponent<RectTransform>().GetWorldCorners(corners); float left = corners.Min(v => v.x);
                    restore.GetComponent<RectTransform>().GetWorldCorners(corners); float right = corners.Max(v => v.x);
                    Record("restore-button-left:" + scale, left - right >= 9.5f && restore.GetComponent<RectTransform>().sizeDelta == save.GetComponent<RectTransform>().sizeDelta,
                        $"screen gap={left - right:F2}px; sizes={restore.GetComponent<RectTransform>().sizeDelta}");
                }
                ui.button.interactable = false; Call(plugin, "SyncOptimizeButton", false);
                Record("restore-button-disabled-sync", !((Button)Get(plugin, "_restoreButtonButton")).interactable, "source button disabled");
                ui.button.interactable = true; Call(plugin, "SyncOptimizeButton", false);
                anchor.SetActive(false); inspector.usage = UIBlueprintInspector.EUsage.Paste;
                Call(plugin, "CreateOptimizeButton"); Canvas.ForceUpdateCanvases(); Call(plugin, "SyncOptimizeButton", true);
                var fallbackRestore = (GameObject)Get(plugin, "_restoreButton"); var fallbackSave = (GameObject)Get(plugin, "_optimizeButton");
                var fallbackCorners = new Vector3[4]; fallbackSave.GetComponent<RectTransform>().GetWorldCorners(fallbackCorners); float fallbackLeft = fallbackCorners.Min(v => v.x);
                fallbackRestore.GetComponent<RectTransform>().GetWorldCorners(fallbackCorners); float fallbackRight = fallbackCorners.Max(v => v.x);
                Record("restore-copy-fallback-layout", !(bool)Get(plugin, "_anchoredUnderAddField") && fallbackRestore.activeInHierarchy && fallbackLeft - fallbackRight >= 9.5f, $"copy fallback gap={fallbackLeft - fallbackRight:F2}px");
                anchor.SetActive(true); inspector.usage = UIBlueprintInspector.EUsage.Browser;
                Call(plugin, "CreateOptimizeButton"); Call(plugin, "SyncOptimizeButton", true);
                ((Button)Get(plugin, "_restoreButtonButton")).onClick.Invoke();
                Record("restore-confirm-modal", (bool)Get(plugin, "_pendingRestore") && UIMessageBox.TopMessage() != null, UIMessageBox.TopMessage()?.m_TitleText.text ?? "none");
                UIMessageBox.CloseTopMessage();
                Record("restore-cancel-immutable", Get(plugin, "_pendingBlueprint") == null && inspector.blueprint == seed && seed.buildings.Length == 4, "cancel did not apply candidate");
                seed.externalFields = "OTHER=BBSOPT=1;BBSOPT=1;keep=value";
                Set(plugin, "_originalSnapshot", seed.ToBase64String());
                ((Button)Get(plugin, "_restoreButtonButton")).onClick.Invoke();
                UIMessageBox.TopMessage().OnButton2Click();
                Record("restore-apply-clipboard", seed.buildings.Length == 7 && GUIUtility.systemCopyBuffer == seed.ToBase64String(), "4→7, clipboard equals applied blueprint");
                Record("restore-marker-state-cleared", seed.externalFields == "OTHER=BBSOPT=1;keep=value" && Get(plugin, "_originalSnapshot") == null && Get(plugin, "_originalSpans") == null, seed.externalFields);
                ((Button)Get(plugin, "_restoreButtonButton")).onClick.Invoke();
                var other = seed.Clone(); inspector.blueprint = other;
                UIMessageBox.TopMessage().OnButton2Click();
                Record("restore-stale-confirm-refused", inspector.blueprint == other && Get(plugin, "_pendingBlueprint") == null, "changed inspector blueprint cannot receive old pending candidate");
                while (UIMessageBox.activeCount > 0) UIMessageBox.CloseTopMessage();
                ((Button)Get(plugin, "_restoreButtonButton")).onClick.Invoke();
                bool oldStrict = ConfigValue(plugin, "_restoreStrictGrid"); ConfigValue(plugin, "_restoreStrictGrid", !oldStrict);
                UIMessageBox.TopMessage().OnButton2Click();
                Record("restore-config-confirm-refused", inspector.blueprint == other && Get(plugin, "_pendingBlueprint") == null, "configuration changed after preview");
                ConfigValue(plugin, "_restoreStrictGrid", oldStrict);
                while (UIMessageBox.activeCount > 0) UIMessageBox.CloseTopMessage();
                var oldText = Get(plugin, "_text"); var oldChinese = Get(plugin, "_textIsChinese");
                var translation = mod.GetType("BlueprintBeltSaver.BeltSaverTranslate").GetMethod("Create", RestoreFlags).Invoke(null, new object[] { false });
                Set(plugin, "_text", translation); Set(plugin, "_textIsChinese", false); Call(plugin, "SyncOptimizeButton", true);
                Record("restore-english-label", ((Text)Get(plugin, "_restoreButtonText")).text == "Restore", "English game label");
                Set(plugin, "_text", oldText); Set(plugin, "_textIsChinese", oldChinese);
            }
            catch (Exception ex) { Record("restore-ui-tests", false, ex.InnerException?.ToString() ?? ex.ToString()); }
            finally
            {
                while (UIMessageBox.activeCount > 0) UIMessageBox.CloseTopMessage();
                Call(plugin, "ClearPending"); Call(plugin, "DestroyOptimizeButton"); Call(plugin, "ClearOptimizationState");
                Set(plugin, "_inspector", oldInspector);
                ConfigValue(plugin, "_enabled", enabled);
                GUIUtility.systemCopyBuffer = clipboard;
                if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            }
        }
    }
}

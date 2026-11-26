using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SmartWarningIcons.Tests
{
    [BepInPlugin("TTenYX.DSP.SmartWarningIcons.SelfTest", "SmartWarningIcons.SelfTest", "0.1.0")]
    [BepInDependency(ModGuid)]
    public sealed partial class SelfTestPlugin : BaseUnityPlugin
    {
        private const string ModGuid = "TTenYX.DSP.SmartWarningIcons";
        private const uint Red = 31, Yellow = 25;
        private const int RedSignal = 531, YellowSignal = 525;
        private const int RedMask = 1 << ((int)Red - 1), YellowMask = 1 << ((int)Yellow - 1);
        private static readonly string SaveRoot = Environment.GetEnvironmentVariable("SWI_SELFTEST_DOCUMENTS");
        private static readonly bool SmokeOnly = Environment.GetEnvironmentVariable("SWI_SELFTEST_SMOKE_ONLY") == "1";
        private readonly List<string> _lines = new List<string>();
        private const string ReloadSlot = "SWI-regression";
        private static bool _reloadedSlot;
        private Harmony _reloadProbe;
        private int _stage, _passes, _failures, _frames;
        private GameData _beforeReload;
        private IconSet _iconsBeforeReload;
        private System.Reflection.FieldInfo _profilePlanetFinderWorker;
        private string _directory;
        private ConfigFile _config;
        private string _modDirectory;

        private void Awake()
        {
            if (string.IsNullOrEmpty(SaveRoot)) { enabled = false; return; }
            _directory = Path.GetDirectoryName(Info.Location);
            _lines.Add($"SmartWarningIcons in-game regression {DateTimeOffset.Now:o}");
            Logger.LogInfo("Waiting for isolated game launch.");
        }

        private void Update()
        {
            if (_stage == 3) return;
            try
            {
                if (Time.realtimeSinceStartup > 130f) throw new TimeoutException($"Game did not reach test stage {_stage}.");
                if (_stage == 0)
                {
                    if (!VFPreload.dbDone || !DSPGame.MenuDemoLoaded) return;
                    var expected = Path.Combine(SaveRoot, "Dyson Sphere Program", "Save");
                    var actual = Path.GetFullPath(GameConfig.gameSaveFolder).TrimEnd('\\', '/');
                    Require(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase), $"Expected isolated saves, got {actual}");
                    _lines.Add($"Runtime: DSP {GameConfig.gameVersion.ToFullString()}; Unity {Application.unityVersion}; GPU {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}");
                    Check("private save folder", () => actual);
                    var plugin = Chainloader.PluginInfos[ModGuid];
                    _config = plugin.Instance.Config;
                    if (!SmokeOnly) _config.SaveOnConfigSet = false;
                    _modDirectory = Path.GetDirectoryName(plugin.Location);
                    DSPGame.backgroundPause = false;
                    var desc = new GameDesc();
                    desc.SetForNewGame(UniverseGen.algoVersion, 98540723, 16, 1, 1f);
                    desc.isSandboxMode = true;
                    desc.achievementEnable = false;
                    DSPGame.StartGameSkipPrologue(desc);
                    _stage = 1;
                    Logger.LogInfo("Isolated sandbox requested.");
                    return;
                }
                if (GameMain.loadErrored) throw new InvalidOperationException("Game reported a sandbox load error.");
                if (_stage == 4)
                {
                    if (!GameMain.isRunning || GameMain.data == _beforeReload || GameMain.data?.localLoadedPlanetFactory == null) return;
                    GameSave.dontSaveToLastExit = true;
                    Check("temporary sandbox save reload", () =>
                    {
                        Require(_reloadedSlot && !GameMain.errored, $"native reader did not finish loading {ReloadSlot}; errored={GameMain.errored}");
                        Require(GameMain.iconSet != _iconsBeforeReload, "reload did not rebuild IconSet");
                        return $"{ReloadSlot}: {TestAtlas()}; {TestIconSet()}";
                    });
                    Finish();
                    return;
                }
                if (_stage == 1)
                {
                    if (!GameMain.isRunning || DSPGame.IsMenuDemo || GameMain.localPlanet?.factory == null || GameMain.logic == null) return;
                    GameSave.dontSaveToLastExit = true;
                    GameMain.logic.onFactoryFrameEnd += CountFrame;
                    if (SmokeOnly)
                    {
                        var planetFinder = AccessTools.TypeByName("PlanetFinderMod.UIPlanetFinderWindow");
                        if (planetFinder != null) _profilePlanetFinderWorker = AccessTools.Field(planetFinder, "_planetCalculateThread");
                    }
                    _stage = 2;
                    return;
                }
                if (_frames < (SmokeOnly ? 120 : 3)) return;
                if (SmokeOnly && _profilePlanetFinderWorker?.GetValue(null) is System.Threading.Thread worker && worker.IsAlive)
                    return;
                GameMain.logic.onFactoryFrameEnd -= CountFrame;
                Check("new sandbox and live factory ticks", () =>
                {
                    Require(!GameMain.errored, "game flagged a simulation error");
                    return $"planet={GameMain.localPlanet.id}; factory ticks observed={_frames}";
                });
                if (SmokeOnly)
                {
                    Check("TEST loaded the new atlas", TestAtlas);
                    Check("TEST red/yellow signal sprites and buttons", TestSmokeUi);
                }
                else
                {
                    RunScanCases();
                    Check("entity atlas red and yellow pixels", TestAtlas);
                    Check("IconSet and signal sprites", TestIconSet);
                    Check("actual option clicks and independent persisted masks", TestOptions);
                    Check("HUD counts and building alarm independence", TestWarnings);
                    Check("actual sign shader pixels and independent display masks", TestRenderedSigns);
                    Check("atlas heals after a simulated material reset", TestAtlasHealing);
                    if (_failures == 0)
                    {
                        Check("temporary sandbox save creation", () =>
                        {
                            Require(GameSave.SaveCurrentGame(ReloadSlot), "native save failed");
                            var length = new FileInfo(Path.Combine(GameConfig.gameSaveFolder, ReloadSlot + GameSave.saveExt)).Length;
                            Require(length > 0, "save file is empty");
                            return $"{ReloadSlot}: {length} bytes";
                        });
                        if (_failures == 0)
                        {
                            _beforeReload = GameMain.data;
                            _iconsBeforeReload = GameMain.iconSet;
                            _reloadedSlot = false;
                            _reloadProbe = new Harmony("TTenYX.DSP.SmartWarningIcons.SelfTest.ReloadProbe");
                            _reloadProbe.Patch(AccessTools.Method(typeof(GameSave), nameof(GameSave.LoadCurrentGame)),
                                postfix: new HarmonyMethod(typeof(SelfTestPlugin), nameof(ObserveLoadedSlot)));
                            DSPGame.StartGame(ReloadSlot);
                            _stage = 4;
                            return;
                        }
                    }
                }
                Finish();
            }
            catch (Exception ex)
            {
                Fail("fatal", ex);
                Finish();
            }
        }

        private static void ObserveLoadedSlot(string __0, bool __result)
        {
            if (__0 == ReloadSlot && __result) _reloadedSlot = true;
        }

        private void CountFrame() => _frames++;

        private void Finish()
        {
            _stage = 3;
            _reloadProbe?.UnpatchSelf();
            GameSave.dontSaveToLastExit = true;
            try
            {
                _lines.Add($"SUMMARY PASS={_passes} FAIL={_failures}");
                File.WriteAllLines(Path.Combine(_directory, "selftest.log"), _lines);
                Logger.LogInfo(_lines[_lines.Count - 1]);
            }
            finally { Application.Quit(_failures == 0 ? 0 : 1); }
        }

        private void Check(string name, Func<string> test)
        {
            try { _lines.Add($"PASS {name} :: {test()}"); _passes++; }
            catch (Exception ex) { Fail(name, ex); }
        }

        private void Fail(string name, Exception ex)
        {
            _failures++;
            _lines.Add($"FAIL {name} :: {ex.GetBaseException()}");
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }

        private ConfigEntry<T> Entry<T>(string section, string key)
        {
            var value = _config[new ConfigDefinition(section, key)] as ConfigEntry<T>;
            return value ?? throw new InvalidOperationException($"Missing {section}.{key}");
        }

        private static T Blank<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void SetProperty(object owner, string property, object value) =>
            AccessTools.Property(owner.GetType(), property).GetSetMethod(true).Invoke(owner, new[] { value });

        private sealed class Fixture
        {
            private static readonly System.Reflection.MethodInfo FrameEnd = AccessTools.Method(typeof(GameLogic), "OnFactoryFrameEnd");
            internal readonly GameData Data = Blank<GameData>();
            internal readonly PlanetFactory Local, Remote;
            private readonly GameLogic _logic = Blank<GameLogic>();

            internal Fixture()
            {
                Local = MakeFactory(101);
                Remote = MakeFactory(102);
                Data.factories = new[] { Remote, Local };
                Data.factoryCount = 2;
                SetProperty(Local, nameof(PlanetFactory.index), 1);
                SetProperty(Local, nameof(PlanetFactory.gameData), Data);
                SetProperty(Data, nameof(GameData.localPlanet), Local.planet);
                Data.history = Blank<GameHistoryData>();
                Research(1101);
                _logic.data = Data;
            }

            internal void Research(int id) => SetProperty(Data.history, nameof(GameHistoryData.currentTech), id);

            private static PlanetFactory MakeFactory(int planetId)
            {
                var factory = Blank<PlanetFactory>();
                SetProperty(factory, nameof(PlanetFactory.planet), new PlanetData { id = planetId, factory = factory, factoryLoaded = true });
                factory.entitySignPool = new SignData[12];
                factory.entityPool = new EntityData[12];
                factory.prebuildPool = new PrebuildData[1];
                factory.veinPool = new VeinData[4];
                var system = Blank<FactorySystem>();
                factory.factorySystem = system;
                system.factory = factory;
                system.assemblerPool = new AssemblerComponent[3];
                system.assemblerCursor = 3;
                system.labPool = new LabComponent[3];
                system.labCursor = 3;
                system.minerPool = new MinerComponent[4];
                system.minerCursor = 4;
                return factory;
            }

            internal void Tick()
            {
                // Invoke the actual Harmony-patched game entry, not a copy of the production classifier.
                var previous = GameMain.data;
                GameMain.data = Data;
                try { FrameEnd.Invoke(_logic, null); }
                finally { GameMain.data = previous; }
            }

            internal string Expect(int id, uint sign)
            {
                var actual = Local.entitySignPool[id].signType;
                Require(actual == sign, $"entity {id}: expected sign {sign}, actual {actual}");
                return $"entity={id}; sign={actual}";
            }
        }

        private static RecipeExecuteData Recipe(int ticks = 100) =>
            new RecipeExecuteData(new[] { 1101 }, new[] { 1 }, new[] { 1102 }, new[] { 2 }, ticks, 100, false);

        private static AssemblerComponent Assembler(int time = 0, bool replicating = false, int served = 0, int produced = 0) =>
            new AssemblerComponent { id = 1, entityId = 1, recipeId = 1, recipeExecuteData = Recipe(), time = time,
                replicating = replicating, served = new[] { served }, produced = new[] { produced } };

        private static LabComponent ProductionLab(int time, bool replicating, int served, int produced) =>
            new LabComponent { id = 1, entityId = 2, recipeId = 1, recipeExecuteData = Recipe(), time = time,
                replicating = replicating, served = new[] { served }, produced = new[] { produced } };

        private void RunScanCases()
        {
            var red = Entry<bool>("InsufficientInput", "Enable");
            var yellow = Entry<bool>("OutputPile", "Enable");
            var vein = Entry<bool>("VeinDepletion", "Enable");
            var total = Entry<bool>("VeinDepletion", "UseTotalVeinAmount");
            var threshold = Entry<long>("VeinDepletion", "WarnThreshold");
            red.Value = yellow.Value = vein.Value = total.Value = true;
            threshold.Value = 1000;

            void Case(string name, Action<Fixture> set, int id, uint expected) => Check(name, () =>
            {
                var f = new Fixture();
                set(f);
                f.Tick();
                return f.Expect(id, expected);
            });

            Case("assembler missing material", f => f.Local.factorySystem.assemblerPool[1] = Assembler(), 1, Red);
            Case("assembler enough material", f => f.Local.factorySystem.assemblerPool[1] = Assembler(served: 1), 1, 0);
            Case("assembler stalls at cycle end with a blocked batch", f =>
                f.Local.factorySystem.assemblerPool[1] = Assembler(time: 100, produced: 20), 1, Yellow);
            Case("assembler still working with stored output", f =>
                f.Local.factorySystem.assemblerPool[1] = Assembler(80, true, 1, 5), 1, 0);
            Case("assembler at cycle end but still working with a full buffer", f =>
                f.Local.factorySystem.assemblerPool[1] = Assembler(102, true, 1, 20), 1, 0);
            Case("stored output does not override a real input shortage", f =>
                f.Local.factorySystem.assemblerPool[1] = Assembler(produced: 5), 1, Red);
            Case("idle assembler with some stored output and enough input is not blocked", f =>
                f.Local.factorySystem.assemblerPool[1] = Assembler(served: 1, produced: 5), 1, 0);
            Case("zero duration recipe is not a hard stall", f =>
            {
                f.Local.factorySystem.assemblerPool[1] = Assembler();
                f.Local.factorySystem.assemblerPool[1].recipeExecuteData = Recipe(0);
            }, 1, 0);
            Case("partially initialized recipe does not fault", f =>
            {
                f.Local.factorySystem.assemblerPool[1] = Assembler();
                f.Local.factorySystem.assemblerPool[1].recipeExecuteData = null;
                f.Local.entitySignPool[1].signType = SignData.NOT_WORKING;
            }, 1, SignData.NOT_WORKING);
            Case("recycled assembler slots are ignored", f => f.Local.factorySystem.assemblerPool[2] = Assembler(time: 100), 1, 0);
            Case("lab manufacturing without input", f => f.Local.factorySystem.labPool[1] = ProductionLab(0, false, 0, 0), 2, Red);
            Case("production lab stalls at cycle end with a blocked batch", f =>
                f.Local.factorySystem.labPool[1] = ProductionLab(100, false, 1, 10), 2, Yellow);
            Case("production lab at cycle end but still working with a full buffer", f =>
                f.Local.factorySystem.labPool[1] = ProductionLab(102, true, 1, 10), 2, 0);
            Case("stored lab output does not override a real input shortage", f =>
                f.Local.factorySystem.labPool[1] = ProductionLab(0, false, 0, 6), 2, Red);

            var previousMatrix = LabComponent.matrixPoints;
            try
            {
                LabComponent.matrixPoints = new[] { 100, 0, 0, 0, 0, 0 };
                void Research(Fixture f, int stored, int tech, bool replicating = false) =>
                    f.Local.factorySystem.labPool[1] = new LabComponent { id = 1, entityId = 2, techId = tech, researchMode = true,
                        replicating = replicating, matrixServed = new[] { stored, 0, 0, 0, 0, 0 } };
                Case("research lab required matrix below cost", f => Research(f, 99, 1101), 2, Red);
                Case("research lab at cost with unused matrix slots empty", f => Research(f, 100, 1101), 2, 0);
                Case("research lab running without buffered matrices", f => Research(f, 0, 1101, true), 2, 0);
                Case("research with no current tech despite stale global matrix cost", f => { f.Research(0); Research(f, 0, 0); }, 2, 0);
                Case("research lab not yet updated after switching tech", f => Research(f, 0, 1102), 2, 0);
            }
            finally { LabComponent.matrixPoints = previousMatrix; }

            void Miner(Fixture f, EMinerType type = EMinerType.Vein)
            {
                f.Local.veinPool[1] = new VeinData { id = 1, amount = 400 };
                f.Local.veinPool[2] = new VeinData { id = 2, amount = 600 };
                f.Local.factorySystem.minerPool[1] = new MinerComponent
                {
                    id = 1, entityId = 3, type = type, veins = new[] { 1, 2 }, veinCount = 2,
                    minimumVeinAmount = 1000, totalVeinAmount = -1
                };
            }
            Check("miner total equals threshold, refresh is written back", () =>
            {
                var f = new Fixture(); Miner(f); f.Tick();
                var sum = f.Local.factorySystem.minerPool[1].totalVeinAmount;
                Require(sum == 1000, $"expected refreshed total 1000; actual {sum}");
                return f.Expect(3, 0) + $"; sum={sum}";
            });
            Case("miner total below threshold", f => { Miner(f); f.Local.veinPool[2].amount--; }, 3, SignData.CUT_PRODUCTION_SOON);
            total.Value = false;
            Case("miner minimum refreshed after overlapping miner reduces a vein", f => Miner(f), 3, SignData.CUT_PRODUCTION_SOON);
            Case("miner minimum exactly at threshold", f => { Miner(f); f.Local.veinPool[1].amount = f.Local.veinPool[2].amount = 1000; }, 3, 0);
            Case("water pump excluded", f => Miner(f, EMinerType.Water), 3, 0);
            Case("oil extractor excluded", f => Miner(f, EMinerType.Oil), 3, 0);

            void SetUtilizationLevel(Fixture f, int level)
            {
                f.Data.history.techStates = new Dictionary<int, TechState> { [3606] = new TechState { curLevel = level } };
            }
            Check("infinite resources hide the depletion sign", () =>
            {
                var f = new Fixture();
                f.Data.gameDesc = new GameDesc { resourceMultiplier = 100f };
                Miner(f);
                f.Local.veinPool[2].amount--;
                f.Tick();
                return f.Expect(3, 0) + "; total 999 < threshold 1000 but resources are infinite";
            });
            Check("vein utilization level 390 hides the depletion sign", () =>
            {
                var f = new Fixture();
                SetUtilizationLevel(f, 390);
                Miner(f);
                f.Local.veinPool[2].amount--;
                f.Tick();
                return f.Expect(3, 0) + "; tech 3606 at level 390, total 999";
            });
            Case("vein utilization level 389 still warns", f =>
            { SetUtilizationLevel(f, 389); Miner(f); f.Local.veinPool[2].amount--; }, 3, SignData.CUT_PRODUCTION_SOON);
            Check("blank history without tech dictionary still warns", () =>
            {
                var f = new Fixture();
                Miner(f);
                f.Local.veinPool[2].amount--;
                f.Tick();
                return f.Expect(3, SignData.CUT_PRODUCTION_SOON) + "; techStates dict is null";
            });
            Case("vein depletion disabled leaves native warning intact", f =>
            { vein.Value = false; Miner(f); f.Local.entitySignPool[3].signType = SignData.CUT_PRODUCTION_SOON; }, 3, SignData.CUT_PRODUCTION_SOON);
            vein.Value = true;

            for (uint power = 1; power <= 3; power++)
            {
                uint expected = power;
                Check($"power sign {expected} preserved on all three components", () =>
                {
                    var f = new Fixture();
                    f.Local.factorySystem.assemblerPool[1] = Assembler(produced: 5);
                    f.Local.factorySystem.labPool[1] = ProductionLab(0, false, 0, 0);
                    Miner(f);
                    for (var i = 1; i <= 3; i++) f.Local.entitySignPool[i].signType = expected;
                    f.Tick();
                    return string.Join(" / ", Enumerable.Range(1, 3).Select(i => f.Expect(i, expected)));
                });
            }
            Check("remote factory left untouched", () =>
            {
                var f = new Fixture();
                f.Remote.factorySystem.assemblerPool[1] = Assembler(time: 100);
                f.Remote.entitySignPool[1].signType = SignData.NOT_WORKING;
                f.Tick();
                Require(f.Remote.entitySignPool[1].signType == SignData.NOT_WORKING, "remote sign changed");
                return $"planet={f.Remote.planetId}; sign={f.Remote.entitySignPool[1].signType}";
            });
            Case("disabling red leaves native not-working sign", f =>
            { red.Value = false; f.Local.factorySystem.assemblerPool[1] = Assembler(); f.Local.entitySignPool[1].signType = SignData.NOT_WORKING; }, 1, SignData.NOT_WORKING);
            red.Value = true;
            Case("disabling yellow leaves native not-working sign", f =>
            { yellow.Value = false; f.Local.factorySystem.assemblerPool[1] = Assembler(time: 100); f.Local.entitySignPool[1].signType = SignData.NOT_WORKING; }, 1, SignData.NOT_WORKING);
            yellow.Value = true;
            Check("native assembler shows yellow only while actually blocked", () => TestNativeOutputStall(isLab: false));
            Check("native production lab shows yellow only while actually blocked", () => TestNativeOutputStall(isLab: true));
            Check("native multi-output recipe stalls when one product cannot fit", TestNativeMultiOutputStall);
            total.Value = true;
        }

        private string TestAtlas()
        {
            var texture = Configs.builtin.entitySignMat.mainTexture as Texture2D;
            Require(texture != null && texture.isReadable, "patched atlas is unavailable");
            CompareCell(texture, (int)Red % 4 * 256, (int)Red / 4 * 256, "InsufficientInput.png", 256);
            CompareCell(texture, (int)Yellow % 4 * 256, (int)Yellow / 4 * 256, "OutputPile.png", 256);
            return $"{texture.width}x{texture.height}; both 256x256 cells match PNG samples";
        }

        private string TestIconSet()
        {
            var set = GameMain.iconSet;
            var red = LDB.signals.IconSprite(RedSignal);
            var yellow = LDB.signals.IconSprite(YellowSignal);
            Require(red != null && yellow != null && red != yellow, "red/yellow sprites invalid");
            var r = set.signalIconIndex[RedSignal];
            var y = set.signalIconIndex[YellowSignal];
            Require(r != 0 && y != 0 && r != y && set.spriteIndexMap[red] == r && set.spriteIndexMap[yellow] == y, "IconSet mapping invalid");
            var indices = new uint[set.signalIconIndex.Length];
            set.signalIconIndexBuffer.GetData(indices);
            Require(indices[RedSignal] == r && indices[YellowSignal] == y, "GPU signal mapping differs");
            CompareCell(set.texture, (int)r % 25 * 80, (int)r / 25 * 80, "InsufficientInputSmall.png", 80);
            CompareCell(set.texture, (int)y % 25 * 80, (int)y / 25 * 80, "OutputPileSmall.png", 80);
            set.Create();
            Require(set.signalIconIndex[RedSignal] == r && set.signalIconIndex[YellowSignal] == y, "repeated IconSet.Create shifted registered indices");
            return $"signal {RedSignal} -> {r}, {YellowSignal} -> {y}; pixels, GPU indices and repeated Create verified";
        }

        private void CompareCell(Texture2D atlas, int x, int y, string asset, int size)
        {
            var expected = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(ImageConversion.LoadImage(expected, File.ReadAllBytes(Path.Combine(_modDirectory, "assets", asset))) &&
                    expected.width == size && expected.height == size, $"invalid {asset}");
                int sampledOpaque = 0;
                for (int dy = 3; dy < size; dy += 7)
                for (int dx = 3; dx < size; dx += 7)
                {
                    var want = expected.GetPixel(dx, dy);
                    if (want.a > 0.5f) sampledOpaque++;
                    var got = atlas.GetPixel(x + dx, y + dy);
                    Require(Mathf.Abs(want.a - got.a) < 0.02f &&
                        (want.a < 0.05f || (Mathf.Abs(want.r - got.r) < 0.025f && Mathf.Abs(want.g - got.g) < 0.025f && Mathf.Abs(want.b - got.b) < 0.025f)),
                        $"{asset} mismatch at ({dx},{dy}): expected {want}, got {got}");
                }
                Require(sampledOpaque > 20, $"insufficient samples for {asset}");
            }
            finally { Object.Destroy(expected); }
        }

        private static string TestSmokeUi()
        {
            var buttons = UIRoot.instance.optionWindow.buildingWarnButtons;
            var red = buttons.Single(b => b.data == RedMask);
            var yellow = buttons.Single(b => b.data == YellowMask);
            var redSprite = LDB.signals.IconSprite(RedSignal);
            var yellowSprite = LDB.signals.IconSprite(YellowSignal);
            Require(redSprite != null && yellowSprite != null && redSprite != yellowSprite, "expected distinct loaded signal sprites");
            Require(red.transform.Find("image").GetComponent<Image>().sprite == redSprite &&
                yellow.transform.Find("image").GetComponent<Image>().sprite == yellowSprite, "TEST option sprite mismatch");
            return $"{buttons.Length} options; custom signals {RedSignal}/{YellowSignal} and masks 0x{RedMask:X8}/0x{YellowMask:X8} present";
        }

        private static string TestOptions()
        {
            var window = UIRoot.instance.optionWindow;
            var old = DSPGame.globalOption;
            var red = window.buildingWarnButtons.Single(b => b.data == RedMask);
            var yellow = window.buildingWarnButtons.Single(b => b.data == YellowMask);
            Require(red.transform.Find("image").GetComponent<Image>().sprite == LDB.signals.IconSprite(RedSignal), "red option image mismatch");
            Require(yellow.transform.Find("image").GetComponent<Image>().sprite == LDB.signals.IconSprite(YellowSignal), "yellow option image mismatch");
            try
            {
                DSPGame.globalOption.buildingWarningMask = int.MaxValue;
                window._Open();
                var tab = window.tabButtons.Length - 1;
                AccessTools.Method(typeof(UIOptionWindow), "SetTabIndex").Invoke(window, new object[] { tab, true });
                var click = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                Require(red.highlighted && yellow.highlighted, "new toggles aren't initially enabled");
                var originalLanguage = Localization.CurrentLanguageLCID;
                try
                {
                    Localization.CurrentLanguageLCID = 2052;
                    Require(red.tips.tipTitle == "缺原料" && yellow.tips.tipTitle == "产物堆砌", "Chinese tooltip mismatch");
                    Localization.CurrentLanguageLCID = 1033;
                    Require(red.tips.tipTitle == "Insufficient Input" && yellow.tips.tipTitle == "Output Piled Up", "English tooltip mismatch");
                }
                finally { Localization.CurrentLanguageLCID = originalLanguage; }
                red.OnPointerClick(click);
                Require(!red.highlighted && yellow.highlighted, "red button did not toggle just the red bit");
                yellow.OnPointerClick(click);
                Require(!red.highlighted && !yellow.highlighted, "yellow button did not toggle just the yellow bit");
                red.OnPointerClick(click);
                Require(red.highlighted && !yellow.highlighted, "red button did not re-enable independently");
                window.OnApplyClick(0);
                var expected = int.MaxValue & ~YellowMask;
                Require(EntitySignRenderer.buildingWarningMask == expected, "applied mask mismatch");
                Require(File.ReadAllText(GameConfig.gameXMLOptionPath).Contains($"<BuildingWarningMask>{expected}</BuildingWarningMask>"),
                    "options XML did not persist the display mask");
                window._Open();
                Require(red.highlighted && !yellow.highlighted, "reopened options lost the applied bits");
                return $"buttons 0x{RedMask:X8}/0x{YellowMask:X8}; mask 0x{expected:X8} persisted; Chinese and English tooltips";
            }
            finally
            {
                window._Close();
                DSPGame.globalOption = old;
                DSPGame.globalOption.Apply();
            }
        }

        private static string TestWarnings()
        {
            var fixture = new Fixture();
            var f = fixture.Local;
            var warningSystem = new WarningSystem();
            fixture.Data.warningSystem = warningSystem;
            warningSystem.Init(fixture.Data);
            var oldMask = EntitySignRenderer.buildingWarningMask;
            try
            {
                warningSystem.checkUpdate = false;
                EntitySignRenderer.buildingWarningMask = int.MaxValue;
                f.entityPool[1].id = 1;
                f.entityPool[1].protoId = 2303;
                f.entityPool[2].id = 2;
                f.entityPool[2].protoId = 2303;
                f.entitySignPool[1].signType = Red;
                f.entitySignPool[2].signType = Yellow;
                f.EnableEntityWarning(1);
                f.EnableEntityWarning(2);
                warningSystem.WarningLogic(0L);
                Require(warningSystem.warningCounts[RedSignal] == 1 && warningSystem.warningCounts[YellowSignal] == 1, "warning counts did not aggregate 531/525");
                f.DisableEntityWarning(1);
                warningSystem.WarningLogic(1L);
                Require(warningSystem.warningCounts[RedSignal] == 0 && warningSystem.warningCounts[YellowSignal] == 1 &&
                    f.entitySignPool[1].signType == Red, "building alarm switch wrongly changed the overhead sign or other count");
                return "warning counts (1 red, 1 yellow) -> (0 red, 1 yellow); red entity sign remains 31";
            }
            finally
            {
                EntitySignRenderer.buildingWarningMask = oldMask;
                warningSystem.Free();
            }
        }

        private string TestAtlasHealing()
        {
            var material = Configs.builtin.entitySignMat;
            var prior = material.mainTexture;
            var reset = new Texture2D(prior.width, prior.height, TextureFormat.RGBA32, false);
            try
            {
                material.mainTexture = reset;
                var fixture = new Fixture();
                fixture.Tick();
                Require(material.mainTexture != reset, "atlas not replaced after reset");
                var healed = material.mainTexture;
                fixture.Tick();
                Require(material.mainTexture == healed, "steady-state frame replaced the healed atlas again");
                return TestAtlas();
            }
            finally
            {
                if (material.mainTexture == reset) material.mainTexture = prior;
                Object.Destroy(reset);
            }
        }
    }
}

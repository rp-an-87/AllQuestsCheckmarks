using AllQuestsCheckmarks.Helpers;
using AllQuestsCheckmarks.Patches;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.IO;
using System.Reflection;

namespace AllQuestsCheckmarks
{
    [
        BepInPlugin("com.zgfuedkx.allquestscheckmarks", "ZGFueDkx-AllQuestsCheckmarks", "1.4.0"),
        BepInDependency("com.SPT.core", "4.1.0"),
        BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency),
        BepInDependency("com.zgfuedkx.wishlistextended", BepInDependency.DependencyFlags.SoftDependency),
    ]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource? LogSource;
        internal static Plugin? Instance;

        // Milliseconds this mod spent in the current frame (panel builds, hover, tooltip text, overlay drawing, snapshot rebuilds)
        internal static double FrameMs;
        public static bool isFikaInstalled = false;
        public static string? modPath;

        public void Awake()
        {
            LogSource = Logger;
            Instance = this;

            modPath = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Plugin)).Location);
            modPath.Replace("\\", "/");

            isFikaInstalled = Chainloader.PluginInfos.ContainsKey("com.fika.core");

            Settings.Init(Config);
            Helpers.Assets.LoadAssets();

            if (isFikaInstalled)
            {
                FikaBridge.Init();
            }
            else
            {
                new LocalGameStartPatch().Enable();
            }

            new QuestClassPatch().Enable();
            new ProfileSelectionPatch().Enable();
            new QuestItemViewPanelPatch().Enable();
            new ItemSpecificationPanelPatch().Enable();
            new ItemHoverShowTooltipPatch().Enable();
            new ItemHoverExitPatch().Enable();
            new CheckmarkHoverPatch().Enable();
            new SimpleTooltipShowPatch().Enable();
            NeedsOverlay.EnablePatches();
            new UpdateApplicationLanguagePatch().Enable();

            LogSource.LogInfo($"AllQuestCheckmarks by ZGFueDkx version {Info.Metadata.Version} started");
        }

        private static float _lastSlowLog = -10f;

        // Reports frames that took long, with how much of that was this mod: tells at once whether a drop is ours
        public void Update()
        {
            float frame = UnityEngine.Time.unscaledDeltaTime * 1000f;

            if (frame > 50f)
            {
                LogSlow($"frame {frame:0} ms, of which this mod {FrameMs:0.0} ms in the previous frame");
            }

            FrameMs = 0;
        }

        // Slow paths are reported as warnings so they show in LogOutput.log without any setting; at most one every half second
        public static void LogSlow(string msg)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            if (now - _lastSlowLog < 0.5f)
            {
                return;
            }

            _lastSlowLog = now;
            LogSource?.LogWarning("[slow] " + msg);
        }

        public static void LogDebug(string msg)
        {
            if (Settings.ShowDebug!.Value)
            {
                LogSource?.LogInfo("[debug] " + msg);
            }
        }
    }
}

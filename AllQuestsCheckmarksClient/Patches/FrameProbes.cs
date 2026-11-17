using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Diagnostics;
using System.Reflection;

namespace AllQuestsCheckmarks.Patches
{
    /// <summary>
    /// Timing probes for the frame monitor (only enabled with "Debug logs"). Each one starts before every other patch of the method
    /// and stops after all of them, so the time includes the other mods' patches and the game's own code.
    /// </summary>
    internal static class FrameProbes
    {
        public static double ShowMs;
        public static double UpdateMs;
        public static double EnterMs;

        public static void Reset()
        {
            ShowMs = UpdateMs = EnterMs = 0;
        }

        public static void EnablePatches()
        {
            new TooltipShowProbe().Enable();
            new TooltipUpdateProbe().Enable();
            new PointerEnterProbe().Enable();
        }
    }

    internal class TooltipShowProbe : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(SimpleTooltip), nameof(SimpleTooltip.Show));
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out Stopwatch __state) => __state = Stopwatch.StartNew();

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Stopwatch __state) => FrameProbes.ShowMs += __state.Elapsed.TotalMilliseconds;
    }

    internal class TooltipUpdateProbe : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Tooltip), nameof(Tooltip.Update));
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out Stopwatch __state) => __state = Stopwatch.StartNew();

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Stopwatch __state) => FrameProbes.UpdateMs += __state.Elapsed.TotalMilliseconds;
    }

    internal class PointerEnterProbe : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GridItemView).GetMethod("OnPointerEnter", BindingFlags.Instance | BindingFlags.Public);
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out Stopwatch __state) => __state = Stopwatch.StartNew();

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Stopwatch __state) => FrameProbes.EnterMs += __state.Elapsed.TotalMilliseconds;
    }
}

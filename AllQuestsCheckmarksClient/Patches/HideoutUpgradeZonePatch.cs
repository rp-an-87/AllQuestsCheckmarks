using AllQuestsCheckmarks.Helpers;
using EFT.Hideout;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace AllQuestsCheckmarks.Patches
{
    // An upgrade was started: it is paid for, so the hideout needs change
    internal class HideoutUpgradeZonePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutRepresentation), nameof(HideoutRepresentation.UpgradeZone));
        }

        [PatchPostfix]
        private static void Postfix()
        {
            HideoutNeedsData.Invalidate();
            NeedsSnapshot.Invalidate();
        }
    }
}

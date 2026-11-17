namespace AllQuestsCheckmarks.Helpers
{
    // Same thin line the CustomRpTweaks and LootValuePlus tooltips use: em dashes in a small, dim font
    internal static class TooltipSeparator
    {
        public static readonly string Line = "<size=7><color=#555555>" + new string('\u2014', 48) + "</color></size>";
    }
}

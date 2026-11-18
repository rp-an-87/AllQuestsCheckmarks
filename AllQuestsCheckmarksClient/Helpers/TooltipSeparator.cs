namespace AllQuestsCheckmarks.Helpers
{
    // Same thin line the CustomRpTweaks and LootValuePlus tooltips use: em dashes in a small, dim font
    internal static class TooltipSeparator
    {
        public static readonly string Line = "<size=7><color=#555555>" + new string('\u2014', 48) + "</color></size>";
        // fainter and dotted: sets a group of lines apart without cutting the tooltip in two
        public static readonly string Soft = "<size=7><color=#6A6A6A>" + string.Concat(System.Linq.Enumerable.Repeat("— ", 24)) + "</color></size>";
    }
}

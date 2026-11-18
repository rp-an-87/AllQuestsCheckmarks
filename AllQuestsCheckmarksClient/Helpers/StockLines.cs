using EFT;
using ZGFueDkx.ZGCLib.helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// The first block of the tooltip: what you hold, what is needed in all, what is missing. Each line has one main figure,
    /// the details under it are small (found in raid yellow, not found in raid grey, what a raid would add purple).
    /// </summary>
    internal static class StockLines
    {
        private const string Green = "#66FF66";
        private const string Red = "#FF6666";
        private const string FirColor = "#FFFF66";
        private const string NonFirColor = "#8A8A8A";
        private const string GainColor = "#C58BFF";
        private const string Indent = "    ";

        private static string Num(int n) => NumberFormat.Compact(n);

        private static string Small(string text) => "<size=80%>" + text + "</size>";

        private static string Fir(string number) => $"<color={FirColor}>{number} {"aqc_fir".Localized(null)}</color>";

        private static string NonFir(string number) => $"<color={NonFirColor}>{number} {"aqc_nonfir".Localized(null)}</color>";

        // "5 FiR · 8 Non-FiR", only the kinds that are there (both when always)
        private static string Parts(int fir, int nonFir, bool always = false)
        {
            List<string> parts = [];

            if (fir > 0 || always) parts.Add(Fir(Num(fir)));
            if (nonFir > 0 || always) parts.Add(NonFir(Num(nonFir)));

            return string.Join(" · ", parts);
        }

        private static string Gain(string text) => $"<b><color={GainColor}>{text}</color></b>";

        private static string Main(string label, int total, string color) => $"{label}: <color={color}>{Num(total)}</color>";

        private static string Detail(string text) => Indent + Small(text);

        public static string Build(MongoID id)
        {
            StashHelper.ItemsCount stash = NeedsSnapshot.Held(id);
            bool haveNeed = NeedsSnapshot.TryGet(id, out NeedsSnapshot.ItemNeed need) && !need.Shared;

            int needFir = haveNeed ? need.NeedFirBy.Sum() : 0;
            int needAny = haveNeed ? need.NeedAnyBy.Sum() : 0;
            int missFir = haveNeed ? need.FirBy.Sum() : 0;
            int missNon = haveNeed ? need.BuyBy.Sum() + need.UnbuyableBy.Sum() : 0;
            bool covered = haveNeed && needFir + needAny > 0 && missFir + missNon == 0;

            // what a raid would add (the setting that counts the inventory in the stash number makes it part of it already)
            StashHelper.ItemsCount carried = NeedsSnapshot.InRaid && !Settings.IncludeRaidItems!.Value ? StashHelper.GetCarried(id) : new StashHelper.ItemsCount();

            List<string> groups = [];

            // in stash
            List<string> lines = [Main("aqc_stock_in_stash".Localized(null), stash.Total, covered ? Green : "#FFFFFF"), Detail(Parts(stash.Fir, stash.NonFir, true))];

            if (carried.Total > 0)
            {
                string gain = "+" + Num(carried.Total) + (carried.NonFir > 0 ? $" ({Num(carried.NonFir)} {"aqc_nonfir".Localized(null)})" : "");
                lines.Add(Detail(Gain(gain)));
            }

            groups.Add(string.Join("\n", lines));

            // needed in all
            if (needFir + needAny > 0)
            {
                lines = [Main("aqc_stock_needed".Localized(null), needFir + needAny, "#FFFFFF"), Detail(Parts(needFir, needAny))];

                int now = need.NeedFirBy[NeedsSnapshot.QuestNow] + need.NeedFirBy[NeedsSnapshot.HideoutNow]
                    + need.NeedAnyBy[NeedsSnapshot.QuestNow] + need.NeedAnyBy[NeedsSnapshot.HideoutNow];
                int later = needFir + needAny - now;

                if (now > 0 && later > 0)
                {
                    lines.Add(Detail($"<color={NonFirColor}>{"aqc_stock_now".Localized(null)} {Num(now)} · {"aqc_stock_later".Localized(null)} {Num(later)}</color>"));
                }

                groups.Add(string.Join("\n", lines));
            }

            // still missing
            if (missFir + missNon > 0)
            {
                lines = [Main("aqc_stock_missing".Localized(null), missFir + missNon, Red), Detail(Parts(missFir, missNon))];

                // what the copies you carry would cover of it (found in raid ones first, the spare ones go to the rest)
                int coveredFir = Math.Min(missFir, carried.Fir);
                int coveredNon = Math.Min(missNon, carried.NonFir + carried.Fir - coveredFir);

                if (coveredFir + coveredNon > 0)
                {
                    List<string> parts = [];

                    if (coveredFir > 0) parts.Add($"+{Num(coveredFir)} {"aqc_fir".Localized(null)}");
                    if (coveredNon > 0) parts.Add($"+{Num(coveredNon)} {"aqc_nonfir".Localized(null)}");

                    lines.Add(Detail(Gain(string.Join(" · ", parts))));
                }

                groups.Add(string.Join("\n", lines));
            }

            return string.Join("\n<size=5> </size>\n", groups);
        }
    }
}

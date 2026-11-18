using EFT;
using ZGFueDkx.ZGCLib.helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// The first block of the tooltip, three short lines: what you hold, what is needed in all, what is missing.
    /// The figures are plain, only the words FiR (yellow) and Non-FiR (grey) are colored; the details after each figure are small.
    /// What the copies you carry in a raid would cover of the missing part is shown in purple.
    /// </summary>
    internal static class StockLines
    {
        private const string FirColor = "#FFFF66";
        private const string NonFirColor = "#8A8A8A";
        private const string GainColor = "#C58BFF";

        private static string Num(int n) => NumberFormat.Compact(n);

        private static string Small(string text) => "<size=80%>" + text + "</size>";

        // "5 FiR", with what a raid would cover of it
        private static string Part(int number, bool fir, int gain)
        {
            string word = (fir ? "aqc_fir" : "aqc_nonfir").Localized(null);
            string text = $"{Num(number)} <color={(fir ? FirColor : NonFirColor)}>{word}</color>";

            return gain > 0 ? text + $" <b><color={GainColor}>(+{Num(gain)})</color></b>" : text;
        }

        // "5 FiR · 8 Non-FiR": the kinds that are there
        private static string Parts(int fir, int nonFir, int gainFir = 0, int gainNonFir = 0)
        {
            List<string> parts = [];

            if (fir > 0) parts.Add(Part(fir, true, gainFir));
            if (nonFir > 0) parts.Add(Part(nonFir, false, gainNonFir));

            return string.Join(" · ", parts);
        }

        private static string Line(string label, int total, string details)
        {
            string line = $"{label}: {Num(total)}";

            return details.Length > 0 ? line + "<space=1em>" + Small(details) : line;
        }

        public static string Build(MongoID id)
        {
            StashHelper.ItemsCount stash = NeedsSnapshot.Held(id);
            bool haveNeed = NeedsSnapshot.TryGet(id, out NeedsSnapshot.ItemNeed need) && !need.Shared;

            int needFir = haveNeed ? need.NeedFirBy.Sum() : 0;
            int needAny = haveNeed ? need.NeedAnyBy.Sum() : 0;
            int missFir = haveNeed ? need.FirBy.Sum() : 0;
            int missNon = haveNeed ? need.BuyBy.Sum() + need.UnbuyableBy.Sum() : 0;

            // what a raid would add (the setting that counts the inventory in the stash number makes it part of it already)
            StashHelper.ItemsCount carried = NeedsSnapshot.InRaid && !Settings.IncludeRaidItems!.Value ? StashHelper.GetCarried(id) : new StashHelper.ItemsCount();

            List<string> lines = [Line("aqc_stock_in_stash".Localized(null), stash.Total, Parts(stash.Fir, stash.NonFir))];

            if (needFir + needAny > 0)
            {
                string details = Parts(needFir, needAny);

                int now = need.NeedFirBy[NeedsSnapshot.QuestNow] + need.NeedFirBy[NeedsSnapshot.HideoutNow]
                    + need.NeedAnyBy[NeedsSnapshot.QuestNow] + need.NeedAnyBy[NeedsSnapshot.HideoutNow];

                if (now > 0 && now < needFir + needAny)
                {
                    details += $" <color={NonFirColor}>({Num(now)} {"aqc_stock_now".Localized(null)})</color>";
                }

                lines.Add(Line("aqc_stock_needed".Localized(null), needFir + needAny, details));
            }

            if (missFir + missNon > 0)
            {
                // what the copies you carry would cover (found in raid ones go to the found in raid part first, the spare ones to the rest)
                int coveredFir = Math.Min(missFir, carried.Fir);
                int coveredNon = Math.Min(missNon, carried.NonFir + carried.Fir - coveredFir);

                lines.Add(Line("aqc_stock_missing".Localized(null), missFir + missNon, Parts(missFir, missNon, coveredFir, coveredNon)));
            }

            return string.Join("\n", lines);
        }
    }
}

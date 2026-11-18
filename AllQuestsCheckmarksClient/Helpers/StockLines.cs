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

        private const string WhiteColor = "#FFFFFF";
        private const string MissingColor = "#FF6666";

        // figures are white, not the blue of the tooltip text
        private static string Num(int n, string color = WhiteColor) => $"<color={color}>{NumberFormat.Compact(n)}</color>";

        private static string Small(string text) => "<size=80%>" + text + "</size>";

        // "5 FiR", with what a raid would cover of it
        private static string Part(int number, bool fir, int gain)
        {
            string word = (fir ? "aqc_fir" : "aqc_nonfir").Localized(null);
            string text = $"{Num(number)} <color={(fir ? FirColor : NonFirColor)}>{word}</color>";

            // the one figure that is not white: what the raid adds, purple and smaller
            return gain > 0 ? text + $" <size=75%><b><color={GainColor}>(+{NumberFormat.Compact(gain)})</color></b></size>" : text;
        }

        // "5 FiR · 8 Non-FiR": the kinds that are there
        private static string Parts(int fir, int nonFir, int gainFir = 0, int gainNonFir = 0)
        {
            List<string> parts = [];

            if (fir > 0) parts.Add(Part(fir, true, gainFir));
            if (nonFir > 0) parts.Add(Part(nonFir, false, gainNonFir));

            return string.Join(" · ", parts);
        }

        private class Row(string label, int total, string details, string color = WhiteColor, string suffix = "")
        {
            // small grey text right after the figure ("(at least)")
            public string Suffix = suffix;
            public string Label = label;
            public int Total = total;
            public string Details = details;
            public string Color = color;
        }

        // the details of every row start in the same column, set from the longest "label: figure" (about 10 px per character)
        private static string[] Layout(List<Row> rows)
        {
            int column = rows.Max(r => (r.Label.Length + 2 + NumberFormat.Compact(r.Total).Length) * 10 + r.Suffix.Length * 7) + 16;

            return [.. rows.Select(r =>
            {
                string line = $"{r.Label}: {Num(r.Total, r.Color)}" + (r.Suffix.Length > 0 ? " " + Small($"<color={NonFirColor}>{r.Suffix}</color>") : "");
                return r.Details.Length > 0 ? line + $"<pos={column}px>" + Small(r.Details) : line;
            })];
        }

        public static string Build(MongoID id)
        {
            StashHelper.ItemsCount stash = NeedsSnapshot.Held(id);
            bool haveNeed = NeedsSnapshot.TryGet(id, out NeedsSnapshot.ItemNeed need);

            // a quest condition that several items share has no fixed number for this item: what is counted is what the other needs fix, so "at least"
            bool shared = haveNeed && need.Shared;

            int needFir = haveNeed ? need.NeedFirBy.Sum() : 0;
            int needAny = haveNeed ? need.NeedAnyBy.Sum() : 0;
            int missFir = haveNeed && !shared ? need.FirBy.Sum() : 0;
            int missNon = haveNeed && !shared ? need.BuyBy.Sum() + need.UnbuyableBy.Sum() : 0;

            // what a raid would add (the setting that counts the inventory in the stash number makes it part of it already)
            StashHelper.ItemsCount carried = NeedsSnapshot.InRaid && !Settings.IncludeRaidItems!.Value ? StashHelper.GetCarried(id) : new StashHelper.ItemsCount();

            Row held = new("aqc_stock_in_stash".Localized(null), stash.Total, Parts(stash.Fir, stash.NonFir));
            List<Row> rows = [];

            if (needFir + needAny > 0)
            {
                string details = Parts(needFir, needAny);

                int now = need.NeedFirBy[NeedsSnapshot.QuestNow] + need.NeedFirBy[NeedsSnapshot.HideoutNow]
                    + need.NeedAnyBy[NeedsSnapshot.QuestNow] + need.NeedAnyBy[NeedsSnapshot.HideoutNow];

                if (now > 0 && now < needFir + needAny)
                {
                    details += $" <color={NonFirColor}>({Num(now)} {"aqc_stock_now".Localized(null)})</color>";
                }

                rows.Add(new Row("aqc_stock_needed".Localized(null), needFir + needAny, details, WhiteColor, shared ? "aqc_stock_at_least".Localized(null) : ""));
            }

            if (missFir + missNon > 0)
            {
                // what the copies you carry would cover (found in raid ones go to the found in raid part first, the spare ones to the rest)
                int coveredFir = Math.Min(missFir, carried.Fir);
                int coveredNon = Math.Min(missNon, carried.NonFir + carried.Fir - coveredFir);

                rows.Add(new Row("aqc_stock_missing".Localized(null), missFir + missNon, Parts(missFir, missNon, coveredFir, coveredNon), MissingColor));
            }

            // what you hold, a light separator, then what is needed and missing; one column for the details of all rows
            string[] laid = Layout([held, .. rows]);

            return rows.Count == 0 ? laid[0] : laid[0] + "\n" + TooltipSeparator.Soft + "\n" + string.Join("\n", laid.Skip(1));
        }
    }
}

using EFT;
using ZGFueDkx.ZGCLib.helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// The first block of the tooltip, two lines: what you hold and what is needed, each with its found in raid and not found in raid figures.
    /// Figures are white; red where the stash can not fill the need, purple where it can not but what you carry in the raid would.
    /// Only the words FiR (yellow) and Non-FiR (grey) are colored. What you carry in a raid is added to the stash line in purple.
    /// </summary>
    internal static class StockLines
    {
        private const string WhiteColor = "#FFFFFF";
        private const string MissingColor = "#FF6666";
        private const string FirColor = "#FFFF66";
        private const string NonFirColor = "#8A8A8A";
        private const string RaidColor = "#C58BFF";

        // figures are white, not the blue of the tooltip text
        private static string Num(int n, string color = WhiteColor) => $"<color={color}>{NumberFormat.Compact(n)}</color>";

        private static string Small(string text) => "<size=80%>" + text + "</size>";

        // "5 FiR": the figure in the given color, the word in its own
        private static string Part(int number, bool fir, string color, int raid = 0)
        {
            string word = (fir ? "aqc_fir" : "aqc_nonfir").Localized(null);
            string text = $"{Num(number, color)} <color={(fir ? FirColor : NonFirColor)}>{word}</color>";

            // what you carry in the raid, right behind the figure it adds to: the only purple text besides the needed figures
            return raid > 0 ? text + $" <size=75%><b><color={RaidColor}>(+{NumberFormat.Compact(raid)})</color></b></size>" : text;
        }

        // the stash and needed numbers do not fill a need of that kind
        private static string Judge(bool filled, bool filledWithRaid) => filled ? WhiteColor : filledWithRaid ? RaidColor : MissingColor;

        private class Row(string label, int total, string totalColor, string details, string suffix = "")
        {
            public string Label = label;
            public int Total = total;
            public string TotalColor = totalColor;
            public string Details = details;

            // small text right after the figure
            public string Suffix = suffix;
        }

        // the details of every row start in the same column, set from the longest "label: figure" (about 10 px per character)
        private static string Layout(List<Row> rows)
        {
            int column = rows.Max(r => (r.Label.Length + 2 + NumberFormat.Compact(r.Total).Length) * 10 + r.Suffix.Length * 5) + 8;

            return string.Join("\n", rows.Select(r =>
            {
                string line = $"{r.Label}: {Num(r.Total, r.TotalColor)}" + (r.Suffix.Length > 0 ? " " + Small($"<color={NonFirColor}>{r.Suffix}</color>") : "");
                return r.Details.Length > 0 ? line + $"<pos={column}px>" + r.Details : line;
            }));
        }

        public static string Build(MongoID id)
        {
            StashHelper.ItemsCount stash = NeedsSnapshot.Held(id);
            bool haveNeed = NeedsSnapshot.TryGet(id, out NeedsSnapshot.ItemNeed need);

            // a quest condition that several items share has no fixed number for this item: what is counted is what the other needs fix, so "at least"
            bool shared = haveNeed && need.Shared;
            int needFir = haveNeed ? need.NeedFirBy.Sum() : 0;
            int needAny = haveNeed ? need.NeedAnyBy.Sum() : 0;

            // what a raid would add (the setting that counts the inventory in the stash number makes it part of it already)
            StashHelper.ItemsCount carried = NeedsSnapshot.InRaid && !Settings.IncludeRaidItems!.Value ? StashHelper.GetCarried(id) : new StashHelper.ItemsCount();

            // found in raid copies fill the found in raid needs first, the spare ones count for the needs that take any copy
            bool FirFilled(int fir) => fir >= needFir;
            bool AnyFilled(int fir, int nonFir) => nonFir + Math.Max(0, fir - needFir) >= needAny;

            bool firOk = FirFilled(stash.Fir);
            bool anyOk = AnyFilled(stash.Fir, stash.NonFir);
            bool firOkRaid = FirFilled(stash.Fir + carried.Fir);
            bool anyOkRaid = AnyFilled(stash.Fir + carried.Fir, stash.NonFir + carried.NonFir);

            // in stash: plain figures (found in raid copies fill non-FiR needs too, so a red part would mislead); a kind is shown when you hold or carry some or it is needed
            List<string> held = [];

            if (stash.Fir > 0 || needFir > 0 || carried.Fir > 0) held.Add(Part(stash.Fir, true, WhiteColor, carried.Fir));
            if (stash.NonFir > 0 || needAny > 0 || carried.NonFir > 0) held.Add(Part(stash.NonFir, false, WhiteColor, carried.NonFir));

            List<Row> rows = [new Row("aqc_stock_in_stash".Localized(null), stash.Total, WhiteColor, held.Count > 0 ? Small(string.Join(" · ", held)) : "")];

            if (needFir + needAny > 0)
            {
                List<string> needed = [];

                if (needFir > 0) needed.Add(Part(needFir, true, Judge(firOk, firOkRaid)));
                if (needAny > 0) needed.Add(Part(needAny, false, Judge(anyOk, anyOkRaid)));

                rows.Add(new Row("aqc_stock_needed".Localized(null), needFir + needAny, Judge(firOk && anyOk, firOkRaid && anyOkRaid),
                    Small(string.Join(" · ", needed)), shared ? "aqc_stock_at_least".Localized(null) : ""));
            }

            return Layout(rows);
        }
    }
}

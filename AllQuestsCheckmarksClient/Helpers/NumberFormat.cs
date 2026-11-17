using System.Globalization;

namespace AllQuestsCheckmarks.Helpers
{
    internal static class NumberFormat
    {
        // 9999 stays 9999, 25000 is 25k, 1200000 is 1.2m: money needs (roubles, dollars, euros) are big numbers
        public static string Compact(int value)
        {
            if (value >= 1_000_000)
            {
                return (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "m";
            }

            if (value >= 10_000)
            {
                return (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

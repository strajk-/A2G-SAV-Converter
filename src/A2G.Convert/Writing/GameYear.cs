using System;
using System.IO;
using System.Text;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// The year the game starts in. The game reads it from KLEINIG.SAV (the game's loader reads one number after the
    /// per-item blocks into the "current season year"; the day and month are fixed in the game). In the shipped file the line is 1998, followed
    /// by 16 and by 51, the number of (year, flag, value) triples for the even years 1998 to 2098. So the game runs for the years 1998-2098.
    /// </summary>
    public static class GameYear
    {
        public const int First = 1998, Last = 2098, ShippedYear = 1998;

        /// <summary>The year of an FM in-game date such as "2025-07-21" (0 when it cannot be read).</summary>
        public static int FromFmDate(string date)
        {
            DateTime d;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return d.Year;
            int y;
            return date != null && date.Length >= 4 && int.TryParse(date.Substring(0, 4), out y) ? y : 0;
        }

        /// <summary>The index of the start year line in the lines of KLEINIG.SAV, or -1: a year followed by 16 and 51 (the triple count).</summary>
        public static int FindYearLine(string[] lines)
        {
            for (var i = 1; i + 2 < lines.Length; i++)
                if (lines[i] == ShippedYear.ToString() && lines[i + 1] == "16" && lines[i + 2] == "51") return i;
            return -1;
        }

        /// <summary>Writes the year into KLEINIG.SAV (clamped to the years the game knows). Returns false when the file does not have the expected shape.</summary>
        public static bool Apply(string kleinigPath, int year, WriteReport report)
        {
            if (!File.Exists(kleinigPath)) { report.Warnings.Add("Start year: KLEINIG.SAV not found; the game starts in " + ShippedYear + "."); return false; }
            var latin1 = Encoding.GetEncoding(28591);
            var lines = File.ReadAllLines(kleinigPath, latin1);
            var index = FindYearLine(lines);
            if (index < 0) { report.Warnings.Add("Start year: the start year line of KLEINIG.SAV was not found; the game starts in " + ShippedYear + "."); return false; }
            var wanted = Math.Max(First, Math.Min(Last, year));
            lines[index] = wanted.ToString();
            File.WriteAllText(kleinigPath, string.Join("\r\n", lines) + "\r\n", latin1);
            if (wanted != year) report.Warnings.Add("Start year " + year + " is outside the years the game knows (" + First + "-" + Last + "); " + wanted + " is used.");
            return true;
        }
    }
}

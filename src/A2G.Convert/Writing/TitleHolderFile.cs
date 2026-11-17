using System.IO;
using System.Text;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// The winners of last season's European cups in KLEINIG.SAV: after the header (magic, the number of numbers that follow, one more number) the
    /// file has that many numbers in pairs (the ranking of the nations), then three (country number, team number) pairs: the Champions League,
    /// the Cup Winners' Cup and the UEFA Cup holder. The team number counts from 1 in the nation's Land file (verified with the editor's dialog
    /// "Kleinigkeiten": the shipped pairs are Spain 3, England 2 and France 36, shown as the Spanish, English and French team at those positions).
    /// </summary>
    public static class TitleHolderFile
    {
        private static readonly int[] MainNations = { 14, 16, 20, 28, 63 };

        /// <summary>The index of the first of the six numbers, or -1 when the file does not have the expected shape.</summary>
        public static int FindStart(string[] lines)
        {
            int count;
            if (lines.Length < 12 || !int.TryParse(lines[1], out count) || count < 2 || count > 200) return -1;
            var start = 3 + count;
            if (start + 6 > lines.Length) return -1;
            for (var k = 0; k < 3; k++)
            {
                int land, team;
                if (!int.TryParse(lines[start + 2 * k], out land) || !int.TryParse(lines[start + 2 * k + 1], out team)) return -1;
                if (System.Array.IndexOf(MainNations, land) < 0 || team < 1) return -1;
            }
            return start;
        }

        /// <summary>The pairs (country number, team number from 1) of the three competitions as written in a file, or null.</summary>
        public static int[][] Read(string[] lines)
        {
            var start = FindStart(lines);
            if (start < 0) return null;
            var result = new int[3][];
            for (var k = 0; k < 3; k++) result[k] = new[] { int.Parse(lines[start + 2 * k]), int.Parse(lines[start + 2 * k + 1]) };
            return result;
        }

        /// <summary>Writes the title holders (a null entry keeps the shipped one). Returns false when the file does not have the expected shape.</summary>
        public static bool Apply(string kleinigPath, int[][] holders, WriteReport report)
        {
            if (!File.Exists(kleinigPath)) { report.Warnings.Add("European cup title holders: KLEINIG.SAV not found."); return false; }
            var latin1 = Encoding.GetEncoding(28591);
            var lines = File.ReadAllLines(kleinigPath, latin1);
            var start = FindStart(lines);
            if (start < 0) { report.Warnings.Add("European cup title holders: the shape of KLEINIG.SAV is not the expected one; the shipped title holders are kept."); return false; }
            for (var k = 0; k < 3 && k < holders.Length; k++)
            {
                if (holders[k] == null) continue;
                lines[start + 2 * k] = holders[k][0].ToString();
                lines[start + 2 * k + 1] = holders[k][1].ToString();
            }
            File.WriteAllText(kleinigPath, string.Join("\r\n", lines) + "\r\n", latin1);
            return true;
        }
    }
}

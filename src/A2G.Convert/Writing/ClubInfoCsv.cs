using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using A2G.Convert.Engine;
using A2G.Convert.Fm;

namespace A2G.Convert.Writing
{
    /// <summary>One line of the club information file: the club id identifies the club, the last three columns are what the user edits.</summary>
    public sealed class ClubInfoRow
    {
        public string Uid, Nation, League, FmName, WrittenName, City, Nickname, Stadium;
        /// <summary>Stadium capacity as written in the file (empty = keep the current value).</summary>
        public string Capacity;
        /// <summary>The kit: home pattern, shirt, shorts, socks, then the same for away (numbers as text; empty = keep the current kit).</summary>
        public string[] Kit = new string[8];
    }

    /// <summary>The result of importing a club information file.</summary>
    public sealed class ClubInfoImport
    {
        public int Changed, Unchanged, Unknown;
    }

    /// <summary>
    /// Club information (city, nickname, stadium name) as a CSV file for spreadsheets: semicolon separated (opens directly in a
    /// German Excel), UTF-8 with a byte order mark. Reading accepts semicolon, comma or tab.
    /// </summary>
    public static class ClubInfoCsv
    {
        public static readonly string[] Header = { "ClubId", "Nation", "League", "Club (FM)", "Written as", "City", "Nickname", "Stadium", "Capacity",
            "Home pattern", "Home shirt", "Home shorts", "Home socks", "Away pattern", "Away shirt", "Away shorts", "Away socks" };

        public static void Write(string path, IEnumerable<ClubInfoRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(";", Header.Select(Quote))).Append("\r\n");
            foreach (var r in rows)
                sb.Append(string.Join(";", new[] { r.Uid, r.Nation, r.League, r.FmName, r.WrittenName, r.City, r.Nickname, r.Stadium, r.Capacity }.Concat(r.Kit ?? new string[8]).Select(Quote))).Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            return value.IndexOfAny(new[] { ';', '"', ',', '\r', '\n', '\t' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>Reads the rows; columns are found by their header names (a file without a header uses the exported order).</summary>
        public static List<ClubInfoRow> Read(string path)
        {
            return FromTable(Split(File.ReadAllText(path, Encoding.UTF8)));
        }

        /// <summary>Builds the club rows from a table of cells (CSV or spreadsheet): the header names find the columns.</summary>
        public static List<ClubInfoRow> FromTable(List<List<string>> lines)
        {
            var result = new List<ClubInfoRow>();
            if (lines.Count == 0) return result;
            int idUid = 0, idCity = 5, idNick = 6, idStadium = 7, idCapacity = 8, idNation = 1, idLeague = 2, idName = 3, idWritten = 4;
            var idKit = new[] { -1, -1, -1, -1, -1, -1, -1, -1 };
            var first = lines[0];
            var hasHeader = first.Any(c => string.Equals(c.Trim(), "ClubId", StringComparison.OrdinalIgnoreCase));
            if (hasHeader)
            {
                Func<string, int, int> find = (name, fallback) =>
                {
                    var i = first.FindIndex(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase));
                    return i >= 0 ? i : fallback;
                };
                idUid = find("ClubId", 0); idNation = find("Nation", 1); idLeague = find("League", 2); idName = find("Club (FM)", 3);
                idWritten = find("Written as", 4); idCity = find("City", 5); idNick = find("Nickname", 6); idStadium = find("Stadium", 7); idCapacity = find("Capacity", -1);
                for (var k = 0; k < 8; k++) idKit[k] = find(Header[9 + k], -1);
            }
            Func<List<string>, int, string> at = (cells, i) => i >= 0 && i < cells.Count ? cells[i] : string.Empty;
            foreach (var cells in lines.Skip(hasHeader ? 1 : 0))
            {
                var uid = at(cells, idUid).Trim();
                if (uid.Length == 0) continue;
                result.Add(new ClubInfoRow
                {
                    Uid = uid, Nation = at(cells, idNation), League = at(cells, idLeague), FmName = at(cells, idName), WrittenName = at(cells, idWritten),
                    City = at(cells, idCity), Nickname = at(cells, idNick), Stadium = at(cells, idStadium), Capacity = at(cells, idCapacity),
                    Kit = idKit.Select(i => at(cells, i)).ToArray()
                });
            }
            return result;
        }

        /// <summary>Splits CSV text into rows of cells, handling quotes (also line breaks inside quotes); the delimiter is taken from the first line.</summary>
        private static List<List<string>> Split(string text)
        {
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var firstLine = text.Split('\n')[0];
            var delimiter = new[] { ';', ',', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear();
                    if (row.Any(x => x.Length > 0)) rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); if (row.Any(x => x.Length > 0)) rows.Add(row); }
            return rows;
        }

        /// <summary>A capacity cell such as "65000", "65.000" or "65 000"; empty or unreadable keeps <paramref name="current"/>.</summary>
        public static int ParseCapacity(string text, int current)
        {
            var digits = new string((text ?? string.Empty).Where(char.IsDigit).ToArray());
            int v;
            return digits.Length > 0 && digits.Length < 9 && int.TryParse(digits, out v) && v > 0 ? v : current;
        }

        /// <summary>The kit of a row: all eight numbers must be there and in range (pattern 0-6, colours 0-12), else null (the current kit stays).</summary>
        public static ClubKit ParseKit(string[] cells)
        {
            if (cells == null || cells.Length != 8) return null;
            var values = new int[8];
            for (var i = 0; i < 8; i++)
            {
                int v;
                if (!int.TryParse((cells[i] ?? string.Empty).Trim(), out v) || v < 0 || v > (i % 4 == 0 ? Kits.PatternCount - 1 : Kits.ColourCount - 1)) return null;
                values[i] = v;
            }
            return ClubKit.FromArray(values);
        }

        /// <summary>Applies imported rows to the settings: clubs are matched by their FM id, ids not in this save are counted.</summary>
        public static ClubInfoImport Apply(ConversionSettings settings, FmDataSet fm, IEnumerable<ClubInfoRow> rows)
        {
            var result = new ClubInfoImport();
            foreach (var row in rows)
            {
                FmClub club;
                if (!fm.Clubs.TryGetValue(row.Uid, out club)) { result.Unknown++; continue; }
                var changed = ClubTexts.SetOverride(settings, club, row.City, row.Nickname, row.Stadium, ParseCapacity(row.Capacity, ClubTexts.Capacity(club, settings)));
                var kit = ParseKit(row.Kit);
                if (kit != null)
                {
                    var before = Kits.For(settings, club).ToArray();
                    Kits.SetOverride(settings, club, kit);
                    if (!before.SequenceEqual(kit.ToArray())) changed = true;
                }
                if (changed) result.Changed++; else result.Unchanged++;
            }
            return result;
        }
    }
}

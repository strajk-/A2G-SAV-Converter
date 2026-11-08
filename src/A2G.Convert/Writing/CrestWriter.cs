using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// Anstoss 2 has no crest field in its .sav files: the game loads WAPPEN\VW_{D,E,F,I,S}{n}.BMP, n = 1-based position of the
    /// team in the country's Land file. A team that moves to another slot therefore shows the crest of the original club of that
    /// slot. This writes a generated 32x32 8-bit crest (the format of the originals) for every club, so the crests match.
    /// </summary>
    public static class CrestWriter
    {
        public const int Size = 32;

        // band colour, cap colour (R,G,B each)
        private static readonly int[][] Pairs =
        {
            new[] { 200, 30, 40, 255, 255, 255 }, new[] { 20, 60, 160, 255, 255, 255 }, new[] { 0, 120, 0, 255, 255, 255 },
            new[] { 250, 210, 0, 20, 20, 20 }, new[] { 20, 20, 20, 230, 30, 30 }, new[] { 20, 20, 20, 250, 210, 0 },
            new[] { 120, 20, 60, 120, 180, 230 }, new[] { 20, 90, 200, 250, 210, 0 }, new[] { 230, 120, 0, 20, 20, 20 },
            new[] { 255, 255, 255, 20, 60, 160 }, new[] { 255, 255, 255, 200, 30, 40 }, new[] { 90, 40, 140, 255, 255, 255 },
            new[] { 0, 150, 160, 20, 20, 20 }, new[] { 30, 30, 100, 230, 30, 30 }, new[] { 0, 100, 50, 250, 210, 0 },
            new[] { 200, 30, 40, 20, 20, 20 }
        };

        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            { 'A', "010101111101101" }, { 'B', "110101110101110" }, { 'C', "011100100100011" }, { 'D', "110101101101110" },
            { 'E', "111100110100111" }, { 'F', "111100110100100" }, { 'G', "011100101101011" }, { 'H', "101101111101101" },
            { 'I', "111010010010111" }, { 'J', "001001001101010" }, { 'K', "101101110101101" }, { 'L', "100100100100111" },
            { 'M', "101111111101101" }, { 'N', "110101101101101" }, { 'O', "010101101101010" }, { 'P', "110101110100100" },
            { 'Q', "010101101110011" }, { 'R', "110101110101101" }, { 'S', "011100010001110" }, { 'T', "111010010010010" },
            { 'U', "101101101101111" }, { 'V', "101101101101010" }, { 'W', "101101111111101" }, { 'X', "101101010101101" },
            { 'Y', "101101010010010" }, { 'Z', "111001010100111" },
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "110001010100111" }, { '3', "110001010001110" },
            { '4', "101101111001001" }, { '5', "111100110001110" }, { '6', "011100111101111" }, { '7', "111001010010010" },
            { '8', "111101111101111" }, { '9', "111101111001110" }
        };

        private static readonly HashSet<string> Filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "FC", "SV", "TSV", "VFL", "VFB", "SC", "AC", "AS", "US", "CF", "RC", "SK", "FK", "AFC", "SSC", "ASC", "FSV", "SG", "SPVGG", "THE", "DE", "DI", "DEL", "DA", "1", "1899", "04", "05", "96", "98" };

        /// <summary>The letters on the crest: initials of the significant words, or the first letters of a single word.</summary>
        public static string Letters(string name)
        {
            var words = new List<string>();
            foreach (var raw in (name ?? string.Empty).Split(new[] { ' ', '-', '.', '/', '\'' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var w = Fold(raw);
                if (w.Length > 0) words.Add(w);
            }
            var significant = words.Where(w => !Filler.Contains(w)).ToList();
            if (significant.Count == 0) significant = words;
            if (significant.Count == 0) return "A2";
            if (significant.Count == 1) return significant[0].Substring(0, Math.Min(2, significant[0].Length));
            return new string(significant.Take(3).Select(w => w[0]).ToArray());
        }

        private static string Fold(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                var u = char.ToUpperInvariant(ch);
                if (u == 'ß') { sb.Append('S'); continue; }
                if (Font.ContainsKey(u)) sb.Append(u);
            }
            return sb.ToString();
        }

        /// <summary>The BMP bytes (32x32, 8 bit, 256-colour palette, 2102 bytes like the original crests).</summary>
        public static byte[] Render(string uid, string clubName, int[] bandRgb = null, int[] capRgb = null)
        {
            var h = Hash(uid);
            // the colours of the club's home kit when they are given (band = shirt, caps = shorts), else a pair made up from the club id
            var pair = bandRgb != null && capRgb != null
                ? new[] { bandRgb[0], bandRgb[1], bandRgb[2], capRgb[0], capRgb[1], capRgb[2] }
                : Pairs[h % Pairs.Length];
            var pattern = (h / Pairs.Length) % 4;
            var palette = new int[256][];
            for (var i = 0; i < 256; i++) palette[i] = new[] { 0, 0, 0 };
            palette[0] = new[] { 1, 1, 1 };                 // outline
            palette[1] = new[] { 251, 250, 249 };           // background of the original crests
            palette[2] = new[] { pair[0], pair[1], pair[2] };
            palette[3] = new[] { pair[3], pair[4], pair[5] };
            var luminance = 0.299 * pair[0] + 0.587 * pair[1] + 0.114 * pair[2];
            palette[4] = luminance < 150 ? new[] { 255, 255, 255 } : new[] { 1, 1, 1 };   // letters
            palette[5] = new[] { 255, 255, 255 };

            var px = new byte[Size, Size];
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    double dx = x - 15.5, dy = y - 15.5, d2 = dx * dx + dy * dy;
                    if (d2 > 15.6 * 15.6) { px[y, x] = 1; continue; }
                    if (d2 > 14.2 * 14.2) { px[y, x] = 0; continue; }
                    if (y >= 10 && y <= 21) { px[y, x] = 2; continue; }   // letter band
                    var top = y < 10;
                    byte c;
                    switch (pattern)
                    {
                        case 1: c = (x / 4) % 2 == 0 ? (byte)3 : (byte)5; break;
                        case 2: c = ((x + y) / 5) % 2 == 0 ? (byte)3 : (byte)5; break;
                        case 3: c = top ? (byte)3 : (byte)5; break;
                        default: c = 3; break;
                    }
                    px[y, x] = c;
                }
            // separating lines between band and caps
            for (var x = 0; x < Size; x++)
                foreach (var y in new[] { 9, 22 })
                {
                    double dx = x - 15.5, dy = y - 15.5;
                    if (dx * dx + dy * dy <= 14.2 * 14.2) px[y, x] = 0;
                }

            var letters = Letters(clubName);
            var width = letters.Length * 6 + (letters.Length - 1) * 2;
            var left = (Size - width) / 2;
            for (var k = 0; k < letters.Length; k++)
            {
                var glyph = Font[letters[k]];
                for (var gy = 0; gy < 5; gy++)
                    for (var gx = 0; gx < 3; gx++)
                        if (glyph[gy * 3 + gx] == '1')
                            for (var sy = 0; sy < 2; sy++)
                                for (var sx = 0; sx < 2; sx++)
                                    px[11 + gy * 2 + sy, left + k * 8 + gx * 2 + sx] = 4;
            }

            return Encode(palette, px);
        }

        /// <summary>Writes a 32x32 image (palette indexes, 256 RGB entries) as the 2102-byte 8-bit BMP of the original crests.</summary>
        public static byte[] Encode(int[][] palette, byte[,] px)
        {
            var bytes = new byte[1078 + Size * Size];
            bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
            Put(bytes, 2, bytes.Length); Put(bytes, 10, 1078); Put(bytes, 14, 40);
            Put(bytes, 18, Size); Put(bytes, 22, Size);
            bytes[26] = 1; bytes[28] = 8;
            Put(bytes, 34, Size * Size); Put(bytes, 46, 256);
            for (var i = 0; i < 256; i++)
            {
                bytes[54 + i * 4] = (byte)palette[i][2]; bytes[55 + i * 4] = (byte)palette[i][1]; bytes[56 + i * 4] = (byte)palette[i][0];
            }
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++) bytes[1078 + (Size - 1 - y) * Size + x] = px[y, x];
            return bytes;
        }

        /// <summary>
        /// A Markdown overview of the crest files: country, file (with the image, relative link), the club it belongs to and its league.
        /// Rows are country, file name, written club name, FM club name, league.
        /// </summary>
        public static string Overview(IEnumerable<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("# Club crests\r\n\r\n");
            sb.Append("Anstoss 2 shows `VW_<country letter><n>.BMP` for the n-th team of a country (D = Germany, E = England, F = France, I = Italy, S = Spain). ");
            sb.Append("This list shows which club each generated file belongs to. To use another crest, replace the BMP file (32x32 pixels, 8 bit).\r\n");
            foreach (var group in rows.GroupBy(r => r[0]))
            {
                sb.Append("\r\n## ").Append(group.Key).Append("\r\n\r\n");
                sb.Append("| Crest | File | Club (as written in the game) | FM club | League | Source |\r\n|:-:|---|---|---|---|---|\r\n");
                foreach (var r in group.OrderBy(x => NumberOf(x[1])))
                    sb.Append("| ![](").Append(Uri.EscapeUriString(r[1])).Append(") | `").Append(r[1]).Append("` | ").Append(Cell(r[2])).Append(" | ")
                      .Append(Cell(r[3])).Append(" | ").Append(Cell(r[4])).Append(" | ").Append(Cell(r.Length > 5 ? r[5] : string.Empty)).Append(" |\r\n");
            }
            return sb.ToString();
        }

        private static int NumberOf(string file)
        {
            var digits = new string(file.Where(char.IsDigit).ToArray());
            int n; return int.TryParse(digits, out n) ? n : 0;
        }

        private static string Cell(string s) { return (s ?? string.Empty).Replace("|", "\\|"); }

        private static void Put(byte[] b, int offset, int value)
        {
            b[offset] = (byte)value; b[offset + 1] = (byte)(value >> 8); b[offset + 2] = (byte)(value >> 16); b[offset + 3] = (byte)(value >> 24);
        }

        private static int Hash(string s)
        {
            unchecked { var h = 17; foreach (var c in s ?? string.Empty) h = h * 31 + c; return h & 0x7FFFFFFF; }
        }

        /// <summary>Path of the crest file for slot <paramref name="number"/> (1-based): an existing file keeps its spelling.</summary>
        public static string FilePath(string folder, string letter, int number)
        {
            var wanted = "VW_" + letter + number + ".BMP";
            var existing = Directory.Exists(folder)
                ? Directory.GetFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), wanted, StringComparison.OrdinalIgnoreCase)) : null;
            return existing ?? Path.Combine(folder, wanted);
        }
    }
}

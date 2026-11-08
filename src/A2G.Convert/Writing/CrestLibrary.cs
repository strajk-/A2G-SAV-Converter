using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using A2G.Convert.Fm;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// Community crests: a folder with one image per FM club. A file belongs to the club whose id (the "uid" column of clubs.csv) its
    /// name starts with: "1234.png", "1234.bmp", "1234 - Bayern Muenchen.png". Any size and format .NET reads (png, bmp, jpg, gif) works;
    /// the converter scales it to 32x32 and reduces it to 256 colours, the format Anstoss 2 loads. A BMP that already is 32x32 8-bit is
    /// copied unchanged. Clubs without a file get a generated crest (<see cref="CrestWriter"/>).
    /// </summary>
    public sealed class CrestLibrary
    {
        private static readonly string[] Extensions = { ".bmp", ".png", ".jpg", ".jpeg", ".gif" };
        private static readonly Regex NamePattern = new Regex(@"^(\d+)(?:\D.*)?$", RegexOptions.Compiled);

        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count { get { return _files.Count; } }

        public CrestLibrary(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var m = NamePattern.Match(Path.GetFileNameWithoutExtension(file));
                if (!m.Success) continue;
                var uid = m.Groups[1].Value.TrimStart('0');
                if (uid.Length == 0) uid = "0";
                if (!_files.ContainsKey(uid)) _files[uid] = file;
            }
        }

        /// <summary>The game-ready BMP of the club's crest file, or null (no file, or the image could not be read).</summary>
        public byte[] TryGet(FmClub club, out string fileName)
        {
            fileName = null;
            string path;
            var uid = (club.Uid ?? string.Empty).TrimStart('0');
            if (uid.Length == 0) uid = "0";
            if (!_files.TryGetValue(uid, out path)) return null;
            try
            {
                var bytes = ToCrest(path);
                if (bytes != null) fileName = Path.GetFileName(path);
                return bytes;
            }
            catch (Exception) { return null; }
        }

        public static byte[] ToCrest(string path)
        {
            var raw = File.ReadAllBytes(path);
            // already a game crest: 32x32, 8 bit, 256 palette entries
            if (raw.Length == 2102 && raw[0] == 'B' && raw[1] == 'M' && BitConverter.ToInt32(raw, 18) == 32 && BitConverter.ToInt32(raw, 22) == 32 && raw[28] == 8)
                return raw;
            using (var stream = new MemoryStream(raw))
            using (var source = new Bitmap(stream))
            using (var scaled = new Bitmap(CrestWriter.Size, CrestWriter.Size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.Clear(Color.FromArgb(251, 250, 249));   // the background of the original crests (transparent areas end up here)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    // keep the aspect ratio, centre the image
                    var scale = Math.Min((double)CrestWriter.Size / source.Width, (double)CrestWriter.Size / source.Height);
                    var w = Math.Max(1, (int)Math.Round(source.Width * scale));
                    var h = Math.Max(1, (int)Math.Round(source.Height * scale));
                    g.DrawImage(source, (CrestWriter.Size - w) / 2, (CrestWriter.Size - h) / 2, w, h);
                }
                var pixels = new int[CrestWriter.Size * CrestWriter.Size][];
                for (var y = 0; y < CrestWriter.Size; y++)
                    for (var x = 0; x < CrestWriter.Size; x++)
                    {
                        var c = scaled.GetPixel(x, y);
                        pixels[y * CrestWriter.Size + x] = new[] { (int)c.R, c.G, c.B };
                    }
                return Quantize(pixels);
            }
        }

        /// <summary>The game BMP for 32x32 RGB pixels (row by row, each pixel {R,G,B}), e.g. from the crest painter.</summary>
        public static byte[] FromRgb(int[][] pixels) { return Quantize(pixels); }

        /// <summary>Reduces the pixels to at most 256 colours (median cut) and encodes them as a game BMP.</summary>
        private static byte[] Quantize(int[][] pixels)
        {
            var distinct = pixels.Select(p => (p[0] << 16) | (p[1] << 8) | p[2]).Distinct().ToList();
            var palette = new int[256][];
            for (var i = 0; i < 256; i++) palette[i] = new[] { 0, 0, 0 };
            var colors = distinct.Select(c => new[] { (c >> 16) & 255, (c >> 8) & 255, c & 255 }).ToList();
            if (colors.Count <= 256)
            {
                for (var i = 0; i < colors.Count; i++) palette[i] = colors[i];
            }
            else
            {
                var boxes = new List<List<int[]>> { colors };
                while (boxes.Count < 256)
                {
                    var box = boxes.Where(b => b.Count > 1).OrderByDescending(b => Range(b)).FirstOrDefault();
                    if (box == null) break;
                    boxes.Remove(box);
                    var channel = Enumerable.Range(0, 3).OrderByDescending(ch => box.Max(c => c[ch]) - box.Min(c => c[ch])).First();
                    var sorted = box.OrderBy(c => c[channel]).ToList();
                    var half = sorted.Count / 2;
                    boxes.Add(sorted.Take(half).ToList());
                    boxes.Add(sorted.Skip(half).ToList());
                }
                for (var i = 0; i < boxes.Count; i++)
                    palette[i] = new[] { (int)boxes[i].Average(c => c[0]), (int)boxes[i].Average(c => c[1]), (int)boxes[i].Average(c => c[2]) };
            }
            var px = new byte[CrestWriter.Size, CrestWriter.Size];
            for (var i = 0; i < pixels.Length; i++)
            {
                var best = 0;
                var bestDist = int.MaxValue;
                for (var k = 0; k < 256; k++)
                {
                    var d = Sq(pixels[i][0] - palette[k][0]) + Sq(pixels[i][1] - palette[k][1]) + Sq(pixels[i][2] - palette[k][2]);
                    if (d < bestDist) { bestDist = d; best = k; if (d == 0) break; }
                }
                px[i / CrestWriter.Size, i % CrestWriter.Size] = (byte)best;
            }
            return CrestWriter.Encode(palette, px);
        }

        private static int Sq(int v) { return v * v; }

        private static int Range(List<int[]> box)
        {
            return Enumerable.Range(0, 3).Max(ch => box.Max(c => c[ch]) - box.Min(c => c[ch]));
        }

        // ---- the folder for the user ----------------------------------------------------------

        /// <summary>Creates the folder with an instruction file and a list of the FM club ids (so crest makers know which id is which club).</summary>
        public static void PrepareFolder(string folder, IEnumerable<FmClub> clubs, Func<FmClub, string> nationName)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "README.txt"),
                "Community crests for the Anstoss 2 export\r\n" +
                "==========================================\r\n\r\n" +
                "Put one image per club in this folder. The file name starts with the FM club id (see _club-ids.csv):\r\n" +
                "    1234.png        1234 - Bayern Muenchen.png        1234.bmp\r\n" +
                "Any size and format (png, bmp, jpg, gif) works: the export scales it to 32x32 pixels and 256 colours, the format Anstoss 2 loads.\r\n" +
                "Transparent areas become the off-white background of the original crests. A BMP that already is 32x32 with 8 bits is used unchanged.\r\n" +
                "Clubs without a file get a generated crest. Only the clubs of the five main countries (Germany, England, France, Italy, Spain) have a crest in the game.\r\n" +
                "After the export, WAPPEN\\Crests.md in the output shows which crest came from this folder.\r\n\r\n" +
                "Wappen fuer den Anstoss-2-Export\r\n" +
                "Pro Verein ein Bild in diesen Ordner legen; der Dateiname beginnt mit der FM-Vereins-ID (siehe _club-ids.csv).\r\n" +
                "Jede Groesse und jedes Format (png, bmp, jpg, gif) ist moeglich: Der Export skaliert auf 32x32 Pixel und 256 Farben.\r\n",
                new UTF8Encoding(true));
            var sb = new StringBuilder("ClubId;Club;Nation\r\n");
            foreach (var c in clubs.OrderBy(c => nationName(c), StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                sb.Append(c.Uid).Append(';').Append((c.Name ?? string.Empty).Replace(';', ',')).Append(';').Append((nationName(c) ?? string.Empty).Replace(';', ',')).Append("\r\n");
            File.WriteAllText(Path.Combine(folder, "_club-ids.csv"), sb.ToString(), new UTF8Encoding(true));
        }
    }
}

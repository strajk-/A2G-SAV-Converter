using System;
using System.IO;
using System.Linq;

namespace A2G.Sav.RoundTrip
{
    /// <summary>
    /// Reads every file of an Anstoss 2 install (DATA.DEF and DATA.VER), parses it into the typed model,
    /// serialises it again and requires the bytes to be identical.
    /// Usage: A2G.Sav.RoundTrip.exe [path to Anstoss 2 folder]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // the Anstoss 2 folder: first argument, else the environment variable A2G_ANSTOSS2
            var root = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("A2G_ANSTOSS2");
            if (string.IsNullOrWhiteSpace(root))
            {
                Console.WriteLine("Give the Anstoss 2 folder as the first argument or set the environment variable A2G_ANSTOSS2.");
                return 2;
            }
            var failures = 0;
            var total = 0;

            foreach (var folder in new[] { "DATA.DEF", "DATA.VER" })
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine("Missing folder: " + dir);
                    failures++;
                    continue;
                }

                Console.WriteLine("== " + folder);
                foreach (var path in Directory.GetFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    total++;
                    var name = Path.GetFileName(path);
                    try
                    {
                        var original = File.ReadAllBytes(path);
                        var model = SavLoader.FromBytes(path, original);
                        var written = model.ToBytes();
                        var diff = FirstDifference(original, written);
                        if (diff < 0)
                        {
                            Console.WriteLine("  OK    {0,-14} {1}", name, model.Describe());
                        }
                        else
                        {
                            failures++;
                            Console.WriteLine("  FAIL  {0,-14} differs at byte {1} (orig {2} bytes, new {3} bytes)",
                                name, diff, original.Length, written.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.WriteLine("  ERROR {0,-14} {1}", name, ex.Message);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "All " + total + " files round-trip byte-identically."
                : failures + " problem(s) in " + total + " files.");
            return failures == 0 ? 0 : 1;
        }

        private static int FirstDifference(byte[] a, byte[] b)
        {
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : n;
        }
    }
}

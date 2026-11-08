using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using A2G.Convert.Profiles;

namespace A2G.Convert.Cli
{
    /// <summary>
    /// Exercises the profile manager on a scratch copy of the data folders (never the real install).
    /// Usage: A2G.Convert.Cli.exe profiles-selftest [scratch folder] [real Anstoss 2 folder] [new Anstoss2 data folder]
    /// </summary>
    internal static class ProfileSelfTest
    {
        private static int _failed;

        public static int Run(string[] args)
        {
            var scratch = args[1];
            var realInstall = args[2];
            var newData = args[3];

            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(scratch, f));
            var original = Snapshot(scratch);

            var pm = new ProfileManager(scratch);
            Check("fresh install is unmanaged", pm.ActiveProfile() == null);

            pm.Initialize();
            Check("Initialize makes Original active", pm.ActiveProfile() == "Original");
            Check("Original profile stored", pm.List().Any(p => p.Name == "Original"));
            Check("status clean after Initialize", !pm.GetStatus().Dirty);

            pm.Install(newData, "FM2025", DirtyPolicy.Abort);
            Check("FM2025 active after install", pm.ActiveProfile() == "FM2025");
            Check("live LandDeut.sav is the new one", Hash(scratch, @"DATA.DEF\LandDeut.sav") == Hash(newData, @"DATA.DEF\LandDeut.sav"));
            Check("live LandDeut.sav differs from original", Hash(scratch, @"DATA.DEF\LandDeut.sav") != original[@"DATA.DEF\LandDeut.sav"]);
            Check("no staging leftovers", !Directory.GetDirectories(scratch).Any(d => d.Contains(".a2g-")));

            pm.Activate("Original", DirtyPolicy.Abort);
            var back = Snapshot(scratch);
            Check("switching back restores every original file", SameSnapshot(original, back));

            pm.Activate("FM2025", DirtyPolicy.Abort);
            File.AppendAllText(Path.Combine(scratch, "DATA.DEF", "KLEINIG.SAV"), "x\r\n");
            Check("hand edit is detected", pm.GetStatus().Dirty);
            Check("Abort policy refuses a dirty switch", Throws(() => pm.Activate("Original", DirtyPolicy.Abort)));
            pm.Activate("Original", DirtyPolicy.SaveToActive);
            Check("dirty files were saved into FM2025", pm.List().Single(p => p.Name == "FM2025").Files
                .Single(f => f.Path == @"DATA.DEF\KLEINIG.SAV").Sha256 != original[@"DATA.DEF\KLEINIG.SAV"]);
            Check("Original active again and untouched", SameSnapshot(original, Snapshot(scratch)));

            pm.Install(newData, "NamesOnly", DirtyPolicy.Abort, rel => rel.IndexOf("JUGEDEUT", StringComparison.OrdinalIgnoreCase) >= 0);
            Check("partial install takes the chosen file", Hash(scratch, @"DATA.DEF\JUGEDEUT.SAV") == Hash(newData, @"DATA.DEF\JUGEDEUT.SAV"));
            Check("partial install keeps the other files", Hash(scratch, @"DATA.DEF\LandDeut.sav") == original[@"DATA.DEF\LandDeut.sav"]);

            Check("duplicate profile name is refused", Throws(() => pm.Install(newData, "FM2025", DirtyPolicy.Abort)));
            Check("active profile cannot be deleted", Throws(() => pm.Delete("NamesOnly")));
            pm.Activate("Original", DirtyPolicy.Abort);
            pm.Rename("NamesOnly", "Names");
            Check("rename works", pm.List().Any(p => p.Name == "Names") && pm.List().All(p => p.Name != "NamesOnly"));
            pm.Delete("Names");
            Check("delete works", pm.List().All(p => p.Name != "Names"));
            Check("Original cannot be deleted", Throws(() => pm.Delete("Original")));
            Check("final state equals the original files", SameSnapshot(original, Snapshot(scratch)));

            Console.WriteLine();
            Console.WriteLine(_failed == 0 ? "All profile checks passed." : _failed + " profile check(s) FAILED.");
            return _failed == 0 ? 0 : 3;
        }

        private static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "PASS" : "FAIL", what);
            if (!ok) _failed++;
        }

        private static bool Throws(Action a)
        {
            try { a(); return false; } catch (Exception) { return true; }
        }

        private static string Hash(string root, string rel)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(Path.Combine(root, rel))) return BitConverter.ToString(sha.ComputeHash(s));
        }

        private static System.Collections.Generic.Dictionary<string, string> Snapshot(string root)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in new[] { "DATA.DEF", "DATA.VER" })
            {
                var dir = Path.Combine(root, folder);
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    d[folder + "\\" + f.Substring(dir.Length).TrimStart('\\')] = Hash(root, folder + "\\" + f.Substring(dir.Length).TrimStart('\\'));
            }
            return d;
        }

        private static bool SameSnapshot(System.Collections.Generic.Dictionary<string, string> a, System.Collections.Generic.Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(kv => { string h; return b.TryGetValue(kv.Key, out h) && h == kv.Value; });
        }

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(source)) Copy(d, Path.Combine(target, Path.GetFileName(d)));
        }
    }
}

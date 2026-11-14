using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using A2G.Convert.Patching;
using A2G.Convert.Profiles;
using A2G.Convert.Projects;

namespace A2G.Convert.Cli
{
    /// <summary>Checks the patch file (program + data), its installation through the profile manager and the way back. Works on scratch copies only.</summary>
    internal static class PatchSelfTest
    {
        private static int _failed;

        public static int Run(string[] args)
        {
            var scratch = args[1];
            var realInstall = args[2];
            var newData = args[3];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            var install = Path.Combine(scratch, "install");
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(install, f));
            var original = Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav"));
            var changed = Hash(Path.Combine(newData, "DATA.DEF", "LandDeut.sav"));
            Check("the test data differs (new LandDeut.sav is not the original)", original != changed);

            // a stand-in for the program
            var program = Path.Combine(scratch, "program.exe");
            var bytes = new byte[40000]; new Random(7).NextBytes(bytes);
            File.WriteAllBytes(program, bytes);
            Check("a program without data is no patch", PatchPackage.TryOpen(program) == null && PatchPackage.ProgramLength(program) == bytes.Length);

            var patchFile = Path.Combine(scratch, "Full Patch.exe");
            var info = PatchPackage.Build(program, newData, patchFile, "Test Patch", "A test", null);
            var patch = PatchPackage.TryOpen(patchFile);
            var expectedFiles = Directory.GetFiles(newData, "*", SearchOption.AllDirectories).Length;
            Check("the patch is found at the end of the file with its name, description and all files",
                patch != null && patch.Info.Name == "Test Patch" && patch.Info.Description == "A test" && !patch.Info.Partial && patch.Info.Files.Count == expectedFiles && info.Files.Count == expectedFiles);
            Check("the program part is unchanged", PatchPackage.ProgramLength(patchFile) == bytes.Length && File.ReadAllBytes(patchFile).Take(bytes.Length).SequenceEqual(bytes));

            var extracted = Path.Combine(scratch, "extracted");
            patch.Extract(extracted);
            Check("the files unpack exactly", Hash(Path.Combine(extracted, "DATA.DEF", "LandDeut.sav")) == changed
                && Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).Length == expectedFiles);

            // a patch with the project inside: somebody else can unpack it and continue the work
            {
                Check("a patch made without a project has none", !patch.Info.HasProject);
                var proj = Path.Combine(scratch, "sender-project");
                Directory.CreateDirectory(Path.Combine(proj, "fm_export"));
                Directory.CreateDirectory(Path.Combine(proj, "mapping"));
                Directory.CreateDirectory(Path.Combine(proj, "Crests"));
                Directory.CreateDirectory(Path.Combine(proj, "Anstoss2"));
                File.WriteAllText(Path.Combine(proj, "fm_export", "clubs.csv"), "uid,name\r\n1,Test FC\r\n");
                File.WriteAllText(Path.Combine(proj, "mapping", "settings.json"), "{\"StartYear\":2026}");
                File.WriteAllBytes(Path.Combine(proj, "Crests", "1.bmp"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllText(Path.Combine(proj, "Anstoss2", "ignored.txt"), "not part of the project");
                File.WriteAllText(Path.Combine(proj, "project.json"),
                    "{\"Anstoss2Path\":\"E:\\\\Games\\\\ASCARON\\\\ANSTOSS 2\",\"FmInstallPath\":null,\"FmSavePath\":\"https:\\/\\/example.org\\/x.fm\",\"FmVersion\":26,\"FmYear\":2025,\"LibraryApplied\":false}");
                var withProject = Path.Combine(scratch, "With Project.exe");
                PatchPackage.Build(program, newData, withProject, "Shared Set", "has a project", null, proj);
                var pp = PatchPackage.TryOpen(withProject);
                Check("a patch built with a project lists its files", pp != null && pp.Info.HasProject && pp.Info.ProjectFiles.Count == 4 && pp.Info.Files.Count == expectedFiles);

                var catalog = Path.Combine(scratch, "receiver-data");
                ProjectCatalog.BaseFolder = catalog;
                Directory.CreateDirectory(ProjectCatalog.PathOf("Shared Set"));   // the name is taken: the project gets another one
                bool created;
                var opened = PatchProject.Import(pp, out created);
                var config = File.ReadAllText(Path.Combine(opened, "project.json"));
                Check("the project is unpacked into a new project folder, paths of the sender removed",
                    created && Path.GetFileName(opened) == "Shared Set-2"
                    && File.ReadAllText(Path.Combine(opened, "fm_export", "clubs.csv")).Contains("Test FC")
                    && File.ReadAllText(Path.Combine(opened, "mapping", "settings.json")).Contains("2026")
                    && File.ReadAllBytes(Path.Combine(opened, "Crests", "1.bmp")).SequenceEqual(new byte[] { 1, 2, 3, 4 })
                    && !Directory.Exists(Path.Combine(opened, "Anstoss2"))
                    && !config.Contains("ASCARON") && !config.Contains("example.org") && config.Contains("\"FmYear\":2025") && config.Contains("\"LibraryApplied\":true"));
                bool again;
                var second2 = PatchProject.Import(PatchPackage.TryOpen(withProject), out again);
                Check("opening the same patch again gives the same project, not a copy", !again && second2 == opened && ProjectCatalog.List().Count == 2);
                bool refused2 = false;
                try { PatchProject.Import(patch, out created); } catch (InvalidOperationException) { refused2 = true; }
                Check("a patch without a project cannot be opened for editing", refused2);
                ProjectCatalog.BaseFolder = null;
            }

            // a patch made from a patch carries only the new data
            var second = Path.Combine(scratch, "Second.exe");
            PatchPackage.Build(patchFile, newData, second, "Second", null, null);
            Check("a patch built from a patch has the program once", PatchPackage.ProgramLength(second) == bytes.Length && PatchPackage.TryOpen(second).Info.Name == "Second");

            // a damaged patch is refused
            var damaged = Path.Combine(scratch, "damaged.exe");
            var all = File.ReadAllBytes(patchFile);
            all[bytes.Length + 200] ^= 0xFF;
            File.WriteAllBytes(damaged, all);
            var damagedPatch = PatchPackage.TryOpen(damaged);
            var refused = false;
            if (damagedPatch == null) refused = true;
            else { try { damagedPatch.Extract(Path.Combine(scratch, "damaged-out")); } catch (Exception) { refused = true; } }
            Check("a damaged patch is not installed", refused);

            // installation through the profile manager, and the way back
            var pm = new ProfileManager(install);
            Check("suggested profile name avoids names that exist", PatchInstaller.SuggestProfileName("Test Patch", new[] { "Test Patch", "Test Patch-2" }) == "Test Patch-3"
                && PatchInstaller.SuggestProfileName("a:b?", new string[0]) == "ab");
            PatchInstaller.Install(patch, install, "Test Patch", DirtyPolicy.Abort);
            Check("the patch is live as its own profile", pm.ActiveProfile() == "Test Patch" && Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav")) == changed);
            Check("the original data is kept as the Original profile", pm.List().Any(p => p.Name == "Original"));
            Check("the list names the patch as the source, not a temporary folder", pm.List().First(p => p.Name == "Test Patch").Source == "Patch: Test Patch");
            pm.Activate("Original", DirtyPolicy.Abort);
            Check("rolling back restores the original", Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav")) == original && pm.ActiveProfile() == "Original");
            pm.Activate("Test Patch", DirtyPolicy.Abort);
            Check("the patch can be made live again", Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav")) == changed);
            pm.Activate("Original", DirtyPolicy.Abort);

            // a partial patch only replaces its files
            var partialFile = Path.Combine(scratch, "Partial.exe");
            PatchPackage.Build(program, newData, partialFile, "Partial", null, rel => rel.Equals("DATA.DEF\\LandDeut.sav", StringComparison.OrdinalIgnoreCase));
            var partial = PatchPackage.TryOpen(partialFile);
            Check("a partial patch has only the chosen file", partial.Info.Partial && partial.Info.Files.Count == 1);
            var otherFile = Directory.GetFiles(Path.Combine(newData, "DATA.DEF"), "*.sav").Select(Path.GetFileName)
                .First(n => !n.Equals("LandDeut.sav", StringComparison.OrdinalIgnoreCase) && Hash(Path.Combine(newData, "DATA.DEF", n)) != Hash(Path.Combine(install, "DATA.DEF", n)));
            var otherBefore = Hash(Path.Combine(install, "DATA.DEF", otherFile));
            PatchInstaller.Install(partial, install, "Partial", DirtyPolicy.Abort);
            Check("a partial patch replaces its file and leaves the others", Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav")) == changed
                && Hash(Path.Combine(install, "DATA.DEF", otherFile)) == otherBefore);
            pm.Activate("Original", DirtyPolicy.Abort);
            Check("and the way back works again", Hash(Path.Combine(install, "DATA.DEF", "LandDeut.sav")) == original);

            // quiet install (installers): no questions, exit codes, repeatable
            Check("the patch signature is recognised", PatchPackage.HasSignature(patchFile) && !PatchPackage.HasSignature(program));
            var install2 = Path.Combine(scratch, "install2");
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(install2, f));
            var pm2 = new ProfileManager(install2);
            var messages = new System.Collections.Generic.List<string>();
            Check("quiet install: installs and activates the patch (exit code 0)", PatchInstaller.InstallQuiet(patch, install2, messages.Add) == PatchInstaller.Done
                && pm2.ActiveProfile() == "Test Patch" && Hash(Path.Combine(install2, "DATA.DEF", "LandDeut.sav")) == changed && messages.Count > 0);
            var profileCount = pm2.List().Count;
            Check("quiet install: running it again only activates it (no second data set)", PatchInstaller.InstallQuiet(patch, install2, messages.Add) == PatchInstaller.Done
                && pm2.List().Count == profileCount && pm2.ActiveProfile() == "Test Patch");
            var tweaked = Path.Combine(install2, "DATA.DEF", "LandDeut.sav");
            File.AppendAllText(tweaked, "x");
            var tweakedHash = Hash(tweaked);
            var other = Path.Combine(scratch, "Other.exe");
            PatchPackage.Build(program, newData, other, "Other Patch", null, rel => rel.Equals("DATA.DEF\\LandDeut.sav", StringComparison.OrdinalIgnoreCase));
            Check("quiet install: edited live files are saved into the active data set, not lost",
                PatchInstaller.InstallQuiet(PatchPackage.TryOpen(other), install2, messages.Add) == PatchInstaller.Done
                && Hash(Path.Combine(install2, "Profiles", "Test Patch", "DATA.DEF", "LandDeut.sav")) == tweakedHash && pm2.ActiveProfile() == "Other Patch");
            messages.Clear();
            Check("quiet install: a folder that is not Anstoss 2 gives exit code 2 and a message, nothing is created",
                PatchInstaller.InstallQuiet(patch, Path.Combine(scratch, "nothing here"), messages.Add) == PatchInstaller.NotAnstoss2 && messages.Count == 1
                && !Directory.Exists(Path.Combine(scratch, "nothing here")) && PatchInstaller.InstallQuiet(patch, "", messages.Add) == PatchInstaller.NotAnstoss2);

            // replacing a data set of the same name
            var install3 = Path.Combine(scratch, "install3");
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(install3, f));
            var pm3 = new ProfileManager(install3);
            PatchInstaller.Install(patch, install3, "Test Patch", DirtyPolicy.Abort);
            Func<Action, bool> throws = action => { try { action(); return false; } catch (Exception) { return true; } };
            Check("the same name without replacing is refused", throws(() => PatchInstaller.Install(patch, install3, "Test Patch", DirtyPolicy.Abort)));
            var olderFile = Path.Combine(scratch, "Older.exe");
            PatchPackage.Build(program, realInstall, olderFile, "Test Patch", null, null);   // another content under the same name
            var count3 = pm3.List().Count;
            PatchInstaller.Install(PatchPackage.TryOpen(olderFile), install3, "Test Patch", DirtyPolicy.Abort, true);
            Check("replacing: the data set of that name now holds the new data and is live, nothing else was added",
                pm3.List().Count == count3 && pm3.ActiveProfile() == "Test Patch" && Hash(Path.Combine(install3, "DATA.DEF", "LandDeut.sav")) == original
                && Hash(Path.Combine(install3, "Profiles", "Test Patch", "DATA.DEF", "LandDeut.sav")) == original);
            Check("replacing leaves no leftovers", !Directory.GetDirectories(Path.Combine(install3, "Profiles")).Any(d => d.EndsWith(".a2g-new") || d.EndsWith(".a2g-old")));
            Check("the Original data set cannot be replaced", throws(() => PatchInstaller.Install(patch, install3, "Original", DirtyPolicy.Abort, true))
                && Hash(Path.Combine(install3, "Profiles", "Original", "DATA.DEF", "LandDeut.sav")) == original);
            var install4 = Path.Combine(scratch, "install4");
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(install4, f));
            var pm4 = new ProfileManager(install4);
            var log4 = new System.Collections.Generic.List<string>();
            PatchInstaller.InstallQuiet(patch, install4, log4.Add);
            var count4 = pm4.List().Count;
            Check("quiet install replaces an older version of the same name instead of adding another one",
                PatchInstaller.InstallQuiet(PatchPackage.TryOpen(olderFile), install4, log4.Add) == PatchInstaller.Done && pm4.List().Count == count4
                && pm4.ActiveProfile() == "Test Patch" && log4.Last().StartsWith("Replaced") && Hash(Path.Combine(install4, "DATA.DEF", "LandDeut.sav")) == original);
            // partial installs and partial patches take the crests of a country and, if asked, the start year
            var germany = new A2G.Convert.Writing.InstallParts(); germany.Countries.Add("DEU");
            var filterDe = A2G.Convert.Writing.PartialInstall.Filter(germany);
            germany.StartYear = true;
            var filterDeYear = A2G.Convert.Writing.PartialInstall.Filter(germany);
            Check("partial filter: a country brings its files and its crests, not another country's, and the start year only when asked",
                filterDe("DATA.DEF\\LandDeut.sav") && filterDe("DATA.DEF\\WAPPEN\\VW_D5.BMP") && filterDe("DATA.VER\\WAPPEN\\D0_00.BMP")
                && !filterDe("DATA.DEF\\WAPPEN\\VW_E1.BMP") && !filterDe("DATA.DEF\\LANDENGL.SAV") && !filterDe("DATA.DEF\\Avereine.sav")
                && !filterDe("DATA.DEF\\KLEINIG.SAV") && filterDeYear("DATA.DEF\\KLEINIG.SAV"));
            var crestPatch = Path.Combine(scratch, "Crests.exe");
            PatchPackage.Build(program, newData, crestPatch, "Crest Patch", null, filterDeYear);
            var crestInfo = PatchPackage.TryOpen(crestPatch).Info;
            Check("a partial patch of a country contains its crests and the start year file",
                crestInfo.Partial && crestInfo.Files.Any(f => f.Path.Equals("DATA.DEF\\WAPPEN\\VW_D1.BMP", StringComparison.OrdinalIgnoreCase))
                && crestInfo.Files.Any(f => f.Path.Equals("DATA.VER\\KLEINIG.SAV", StringComparison.OrdinalIgnoreCase))
                && !crestInfo.Files.Any(f => f.Path.IndexOf("VW_E1", StringComparison.OrdinalIgnoreCase) >= 0));
            var install5 = Path.Combine(scratch, "install5");
            foreach (var f in new[] { "DATA.DEF", "DATA.VER" }) Copy(Path.Combine(realInstall, f), Path.Combine(install5, f));
            var crestFile = Path.Combine("DATA.DEF", "WAPPEN", "VW_D1.BMP");
            var crestBefore = Hash(Path.Combine(install5, crestFile));
            PatchInstaller.Install(PatchPackage.TryOpen(crestPatch), install5, "Crest Patch", DirtyPolicy.Abort);
            Check("installing it replaces the crests of that country and leaves the other countries' crests",
                Hash(Path.Combine(install5, crestFile)) == Hash(Path.Combine(newData, crestFile)) && Hash(Path.Combine(newData, crestFile)) != crestBefore
                && Hash(Path.Combine(install5, "DATA.DEF", "WAPPEN", "VW_E1.BMP")) == Hash(Path.Combine(realInstall, "DATA.DEF", "WAPPEN", "VW_E1.BMP")));

            Console.WriteLine(_failed == 0 ? "All patch checks passed." : _failed + " patch check(s) FAILED.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what);
            if (!ok) _failed++;
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(s));
        }

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(source)) Copy(d, Path.Combine(target, Path.GetFileName(d)));
        }
    }
}

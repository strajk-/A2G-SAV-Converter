using System;
using System.IO;
using System.Linq;
using A2G.Convert.Projects;

namespace A2G.Convert.Cli
{
    /// <summary>Checks the list of named projects (create, list, rename, delete, last project). Works in a scratch folder only.</summary>
    internal static class ProjectSelfTest
    {
        private static int _failed;

        public static int Run(string[] args)
        {
            var scratch = args[1];
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            ProjectCatalog.BaseFolder = scratch;

            Check("no projects at first", ProjectCatalog.List().Count == 0 && ProjectCatalog.LastProject() == null);
            var fm26 = ProjectCatalog.Create("FM26");
            ProjectCatalog.Create("Default");
            Check("projects are folders below the projects folder, listed by name", ProjectCatalog.List().Select(p => p.Name).SequenceEqual(new[] { "Default", "FM26" })
                && fm26 == Path.Combine(scratch, "Projects", "FM26") && Directory.Exists(fm26));
            Check("a taken name is refused (any case)", Throws(() => ProjectCatalog.Create("fm26")));
            Check("invalid names are refused", !ProjectCatalog.IsValidName("a:b") && !ProjectCatalog.IsValidName(" ") && !ProjectCatalog.IsValidName(".hidden") && ProjectCatalog.IsValidName("FM26 Test"));
            Check("suggested names count up", ProjectCatalog.SuggestName("FM26", new[] { "FM26", "FM26-2" }) == "FM26-3" && ProjectCatalog.SuggestName("FM26", new string[0]) == "FM26");

            File.WriteAllText(Path.Combine(fm26, "settings.txt"), "kept");
            var renamed = ProjectCatalog.Rename(fm26, "FM26 Final");
            Check("renaming keeps the content", File.ReadAllText(Path.Combine(renamed, "settings.txt")) == "kept" && !Directory.Exists(fm26)
                && ProjectCatalog.List().Select(p => p.Name).SequenceEqual(new[] { "Default", "FM26 Final" }));
            Check("renaming to a taken name is refused", Throws(() => ProjectCatalog.Rename(renamed, "Default")));

            ProjectCatalog.RememberLast(renamed);
            Check("the last project is remembered", ProjectCatalog.SamePath(ProjectCatalog.LastProject(), renamed));
            ProjectCatalog.Delete(renamed);
            Check("deleting removes the project; a missing last project is forgotten", !Directory.Exists(renamed) && ProjectCatalog.LastProject() == null
                && ProjectCatalog.List().Count == 1);

            var outside = Path.Combine(scratch, "elsewhere");
            Directory.CreateDirectory(outside);
            Check("a folder outside the projects folder is never renamed or deleted", !ProjectCatalog.IsManaged(outside)
                && Throws(() => ProjectCatalog.Rename(outside, "x")) && Throws(() => ProjectCatalog.Delete(outside)) && Directory.Exists(outside));

            Console.WriteLine(_failed == 0 ? "All project checks passed." : _failed + " project check(s) FAILED.");
            return _failed == 0 ? 0 : 1;
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; } catch (Exception) { return true; }
        }

        private static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what);
            if (!ok) _failed++;
        }
    }
}

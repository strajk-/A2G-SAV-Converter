using System;
using System.IO;
using System.Linq;
using A2G.Convert.Projects;

namespace A2G.Convert.Patching
{
    /// <summary>Turns the project inside a patch into a project of this PC, so the work can be continued without the FM save.</summary>
    public static class PatchProject
    {
        private const string IdFile = "patch-id.txt";

        /// <summary>
        /// Unpacks the project of the patch into the projects folder and returns its folder. A patch that was opened before gives the project
        /// made from it (with the edits the user made since) instead of a second copy. <paramref name="created"/> tells which of the two happened.
        /// </summary>
        public static string Import(PatchPackage patch, out bool created)
        {
            if (patch == null || !patch.Info.HasProject) throw new InvalidOperationException("The patch contains no project.");
            foreach (var project in ProjectCatalog.List())
            {
                try
                {
                    var marker = Path.Combine(project.Path, IdFile);
                    if (File.Exists(marker) && File.ReadAllText(marker).Trim() == patch.Id) { created = false; return project.Path; }
                }
                catch (Exception) { }
            }
            var taken = ProjectCatalog.List().Select(p => p.Name).ToList();
            var name = ProjectCatalog.SuggestName(CleanName(patch.Info.Name), taken);
            var path = ProjectCatalog.Create(name);
            try
            {
                patch.ExtractProject(path);
                File.WriteAllText(Path.Combine(path, IdFile), patch.Id);
            }
            catch (Exception)
            {
                try { Directory.Delete(path, true); } catch (Exception) { }
                throw;
            }
            created = true;
            return path;
        }

        private static string CleanName(string name)
        {
            var clean = new string((name ?? string.Empty).Trim().Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray()).Trim('.', ' ');
            if (clean.Length > 50) clean = clean.Substring(0, 50);
            return ProjectCatalog.IsValidName(clean) ? clean : "Patch";
        }
    }
}

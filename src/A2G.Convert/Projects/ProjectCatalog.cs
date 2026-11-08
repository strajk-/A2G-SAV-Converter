using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace A2G.Convert.Projects
{
    public sealed class ProjectEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// The named projects of the user: one folder per project below %APPDATA%\A2G-SAV-Converter\Projects (exported FM data, settings, written
    /// .sav files, crests). The last open project is remembered. A project can also be opened from any other folder; such a folder is
    /// not part of the list and is never renamed or deleted by the program.
    /// </summary>
    public static class ProjectCatalog
    {
        public const string DefaultName = "Default";
        private static string _base;

        /// <summary>The program's data folder (%APPDATA%\A2G-SAV-Converter unless set otherwise, e.g. by tests).</summary>
        public static string BaseFolder
        {
            get { return _base ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter"); }
            set { _base = value; }
        }

        public static string Root { get { return System.IO.Path.Combine(BaseFolder, "Projects"); } }
        private static string LastFile { get { return System.IO.Path.Combine(BaseFolder, "last-project.txt"); } }

        public static string PathOf(string name) { return System.IO.Path.Combine(Root, name); }

        public static List<ProjectEntry> List()
        {
            if (!Directory.Exists(Root)) return new List<ProjectEntry>();
            return Directory.GetDirectories(Root)
                .Select(d => new ProjectEntry { Name = System.IO.Path.GetFileName(d), Path = d })
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\'), System.IO.Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        /// <summary>True for a project folder directly below the projects folder (only those are renamed and deleted by the program).</summary>
        public static bool IsManaged(string path)
        {
            try { return SamePath(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path).TrimEnd('\\')), Root); }
            catch (Exception) { return false; }
        }

        /// <summary>A usable project name: a valid folder name that does not start with a dot.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            return name.Length <= 60 && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && !name.StartsWith(".", StringComparison.Ordinal) && !name.EndsWith(".", StringComparison.Ordinal);
        }

        public static bool NameTaken(string name)
        {
            return List().Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"FM26", or "FM26-2", "FM26-3" ... when that name is taken.</summary>
        public static string SuggestName(string baseName, IEnumerable<string> taken)
        {
            var set = new HashSet<string>(taken ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var name = string.IsNullOrWhiteSpace(baseName) ? "Project" : baseName.Trim();
            var result = name;
            for (var n = 2; set.Contains(result); n++) result = name + "-" + n;
            return result;
        }

        public static string Create(string name)
        {
            if (!IsValidName(name)) throw new ArgumentException("Invalid project name: '" + name + "'");
            name = name.Trim();
            if (NameTaken(name)) throw new InvalidOperationException("A project named '" + name + "' exists already.");
            var path = PathOf(name);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string Rename(string path, string newName)
        {
            if (!IsManaged(path)) throw new InvalidOperationException("Only projects in the projects folder can be renamed.");
            if (!IsValidName(newName)) throw new ArgumentException("Invalid project name: '" + newName + "'");
            newName = newName.Trim();
            if (NameTaken(newName) && !string.Equals(System.IO.Path.GetFileName(path), newName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A project named '" + newName + "' exists already.");
            var target = PathOf(newName);
            if (SamePath(path, target)) return path;
            Directory.Move(path, target);
            return target;
        }

        public static void Delete(string path)
        {
            if (!IsManaged(path)) throw new InvalidOperationException("Only projects in the projects folder can be deleted.");
            Directory.Delete(path, true);
        }

        /// <summary>The project that was open last (when its folder still exists), else null.</summary>
        public static string LastProject()
        {
            try
            {
                if (!File.Exists(LastFile)) return null;
                var path = File.ReadAllText(LastFile).Trim();
                return Directory.Exists(path) ? path : null;
            }
            catch (Exception) { return null; }
        }

        public static void RememberLast(string path)
        {
            try { Directory.CreateDirectory(BaseFolder); File.WriteAllText(LastFile, path ?? string.Empty); }
            catch (Exception) { }
        }
    }
}

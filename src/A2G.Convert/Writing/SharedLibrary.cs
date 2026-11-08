using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using A2G.Convert.Engine;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// A per-user library that belongs to no project: club information (city, nickname, stadium, capacity, kit) and crests, both
    /// keyed by the FM club id, which stays the same in every FM save. A new project starts with the library's content; what the
    /// user edits in a project is stored in the library again, so the next project already has it.
    /// </summary>
    public static class SharedLibrary
    {
        private static string _root;

        /// <summary>The library folder (%APPDATA%\A2G-SAV-Converter\Library unless set otherwise, e.g. by tests).</summary>
        public static string Root
        {
            get { return _root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", "Library"); }
            set { _root = value; }
        }

        public static string ClubsPath { get { return Path.Combine(Root, "clubs.json"); } }
        public static string CrestFolder { get { return Path.Combine(Root, "Crests"); } }

        public static List<ClubInfoOverride> LoadClubs()
        {
            try
            {
                if (!File.Exists(ClubsPath)) return new List<ClubInfoOverride>();
                using (var s = File.OpenRead(ClubsPath))
                    return ((List<ClubInfoOverride>)new DataContractJsonSerializer(typeof(List<ClubInfoOverride>)).ReadObject(s)) ?? new List<ClubInfoOverride>();
            }
            catch (Exception) { return new List<ClubInfoOverride>(); }
        }

        private static void SaveClubs(List<ClubInfoOverride> list)
        {
            Directory.CreateDirectory(Root);
            using (var s = File.Create(ClubsPath))
                new DataContractJsonSerializer(typeof(List<ClubInfoOverride>)).WriteObject(s, list.OrderBy(o => o.Uid, StringComparer.Ordinal).ToList());
        }

        private static ClubInfoOverride Copy(ClubInfoOverride o)
        {
            return new ClubInfoOverride { Uid = o.Uid, City = o.City, Nickname = o.Nickname, Stadium = o.Stadium, Capacity = o.Capacity, Kit = o.Kit == null ? null : (int[])o.Kit.Clone() };
        }

        /// <summary>
        /// Brings the library and the project's club information together: library entries for clubs the project has no override for
        /// are added to the project (<paramref name="adoptLibrary"/>), and the project's overrides are stored in the library (they win).
        /// <paramref name="previous"/> holds the club ids this project synced before; those it no longer has are removed from the library.
        /// Returns how many library entries were added to the project.
        /// </summary>
        public static int Sync(ConversionSettings settings, ISet<string> previous, bool adoptLibrary)
        {
            var added = 0;
            try
            {
                var library = LoadClubs();
                var byUid = library.GroupBy(o => o.Uid).ToDictionary(g => g.Key, g => g.Last());
                if (adoptLibrary)
                    foreach (var entry in library)
                        if (settings.FindClubInfo(entry.Uid) == null) { settings.ClubInfo.Add(Copy(entry)); added++; }
                foreach (var o in settings.ClubInfo) byUid[o.Uid] = Copy(o);
                var current = new HashSet<string>(settings.ClubInfo.Select(o => o.Uid));
                foreach (var gone in previous.Where(u => !current.Contains(u)).ToList()) byUid.Remove(gone);
                SaveClubs(byUid.Values.ToList());
                previous.Clear();
                foreach (var u in current) previous.Add(u);
            }
            catch (Exception) { }
            return added;
        }

        // ---- crests -----------------------------------------------------------------------------

        private static string UidOf(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var digits = new string(name.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || (name.Length > digits.Length && char.IsDigit(name[digits.Length]))) return null;
            var id = digits.TrimStart('0');
            return id.Length == 0 ? "0" : id;
        }

        private static readonly string[] Extensions = { ".bmp", ".png", ".jpg", ".jpeg", ".gif" };

        private static IEnumerable<string> CrestFiles(string folder)
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && UidOf(f) != null) : new string[0];
        }

        /// <summary>Copies the library crests the project folder has no file for, and the project's crests the library lacks. Returns the number copied into the project.</summary>
        public static int SyncCrests(string projectFolder, bool adoptLibrary)
        {
            var copied = 0;
            try
            {
                Directory.CreateDirectory(CrestFolder);
                Directory.CreateDirectory(projectFolder);
                var mine = new HashSet<string>(CrestFiles(projectFolder).Select(UidOf));
                if (adoptLibrary)
                    foreach (var f in CrestFiles(CrestFolder))
                        if (!mine.Contains(UidOf(f))) { File.Copy(f, Path.Combine(projectFolder, Path.GetFileName(f))); copied++; }
                var shared = new HashSet<string>(CrestFiles(CrestFolder).Select(UidOf));
                foreach (var f in CrestFiles(projectFolder))
                    if (!shared.Contains(UidOf(f))) File.Copy(f, Path.Combine(CrestFolder, Path.GetFileName(f)));
            }
            catch (Exception) { }
            return copied;
        }

        /// <summary>Stores a crest (game BMP) for the club in the library, replacing an older one.</summary>
        public static void StoreCrest(string uid, byte[] bmp)
        {
            try { RemoveCrest(uid); Directory.CreateDirectory(CrestFolder); File.WriteAllBytes(Path.Combine(CrestFolder, uid + ".bmp"), bmp); }
            catch (Exception) { }
        }

        public static void RemoveCrest(string uid)
        {
            try
            {
                var id = uid.TrimStart('0');
                if (id.Length == 0) id = "0";
                foreach (var f in CrestFiles(CrestFolder).Where(f => UidOf(f) == id).ToList()) File.Delete(f);
            }
            catch (Exception) { }
        }
    }
}

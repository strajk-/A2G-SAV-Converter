using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Convert.Writing;
using A2G.Sav;
using Microsoft.Win32;

namespace A2G.SavConverter.Services
{
    /// <summary>Per-project paths, stored as project.json in the project folder.</summary>
    [DataContract]
    public sealed class ProjectConfig
    {
        [DataMember] public string FmSavePath { get; set; }
        [DataMember] public string FmInstallPath { get; set; }
        [DataMember] public int FmVersion { get; set; }
        /// <summary>The year of the in-game date of the exported FM save (0 = unknown).</summary>
        [DataMember] public int FmYear { get; set; }
        [DataMember] public string Anstoss2Path { get; set; }
        /// <summary>True once the shared library (club info, kits, crests) was added to this project; later the project only feeds the library.</summary>
        [DataMember] public bool LibraryApplied { get; set; }
    }

    /// <summary>Everything the screens share: the project, its settings, the loaded FM data and the current plan.</summary>
    public sealed class AppState
    {
        public AppState()
        {
            Config = new ProjectConfig();
            Settings = new ConversionSettings();
        }

        /// <summary>Manual edits of the current plan (null until a plan was built).</summary>
        public PlanEditor Editor { get; private set; }
        public string OverridesPath { get { return Path.Combine(Project.Mapping, "overrides.json"); } }

        public ProjectLayout Project { get; private set; }
        public ProjectConfig Config { get; private set; }
        public ConversionSettings Settings { get; set; }
        public FmDataSet Fm { get; private set; }
        public ConversionPlan Plan { get; private set; }

        private string ConfigPath { get { return Path.Combine(Project.Root, "project.json"); } }
        public string SettingsPath { get { return Path.Combine(Project.Mapping, "settings.json"); } }

        /// <summary>Opens (and creates) a project folder, loading its config and settings if present.</summary>
        public void OpenProject(string folder)
        {
            Project = new ProjectLayout(folder);
            Project.Ensure();
            Fm = null;
            Plan = null;
            Config = File.Exists(ConfigPath) ? ReadJson<ProjectConfig>(ConfigPath) : new ProjectConfig();
            Settings = File.Exists(SettingsPath) ? ConversionSettings.Load(SettingsPath) : new ConversionSettings();
            FmVersions = DetectFmVersions();
            var version = FmVersions.FirstOrDefault(v => v.Number == Config.FmVersion) ?? FmVersions.FirstOrDefault();
            if (version != null && Config.FmVersion == 0) Config.FmVersion = version.Number;
            if (version != null && string.IsNullOrEmpty(Config.FmSavePath)) Config.FmSavePath = version.Saves;
            if (!IsA2Install(Config.Anstoss2Path)) Config.Anstoss2Path = DetectA2Install();
            Config.FmSavePath = NormalizePath(Config.FmSavePath);
            Config.Anstoss2Path = NormalizePath(Config.Anstoss2Path);
            // the shared library: a new project gets its club info, kits and crests; what the project has is stored there too
            _syncedClubs.Clear();
            var first = !Config.LibraryApplied;
            LibraryAdded = SharedLibrary.Sync(Settings, _syncedClubs, first) + SharedLibrary.SyncCrests(Project.Crests, first);
            if (first) { Config.LibraryApplied = true; SaveConfig(); }
            if (LibraryAdded > 0 || first) Settings.Save(SettingsPath);
        }

        private readonly HashSet<string> _syncedClubs = new HashSet<string>();

        /// <summary>How many club entries and crests the shared library added when the project was opened.</summary>
        public int LibraryAdded { get; private set; }

        public void SaveConfig()
        {
            if (Project == null) return;
            WriteJson(ConfigPath, Config);
        }

        public void SaveSettings()
        {
            if (Project == null) return;
            Settings.Save(SettingsPath);
            SharedLibrary.Sync(Settings, _syncedClubs, false);
        }

        private LaenderFile _laender;
        private string _laenderFolder;

        /// <summary>The nations of the Anstoss 2 install (LAENDER.SAV), or null while no install is selected.</summary>
        public LaenderFile Laender
        {
            get
            {
                var folder = Config.Anstoss2Path;
                if (string.IsNullOrEmpty(folder)) return null;
                if (_laender == null || _laenderFolder != folder)
                {
                    var path = Path.Combine(folder, "DATA.DEF", "LAENDER.SAV");
                    _laender = File.Exists(path) ? (LaenderFile)SavLoader.Load(path) : null;
                    _laenderFolder = folder;
                }
                return _laender;
            }
        }

        /// <summary>A player mapper using the current settings (re-created so setting changes apply).</summary>
        public PlayerMapper CreateMapper()
        {
            var names = Laender != null ? Laender.Nations.Select(n => n.Name).ToList() : new List<string>();
            return new PlayerMapper(Settings, Fm, new A2Nations(names));
        }

        public bool HasExport
        {
            get
            {
                return Project != null && new[] { "clubs.csv", "players.csv", "nations.csv", "nation_names.csv" }
                    .All(f => File.Exists(Path.Combine(Project.FmExport, f)));
            }
        }

        /// <summary>Loads the exported FM data and builds the automatic conversion plan.</summary>
        public Task BuildPlanAsync()
        {
            var folder = Project.FmExport;
            var settings = Settings;
            var overrides = OverridesPath;
            return Task.Run(() =>
            {
                var fm = FmDataSet.Load(folder);
                var plan = PlanBuilder.Build(fm, settings);
                var editor = new PlanEditor(plan, fm, settings);
                var saved = EditLog.Load(overrides);
                ReplayedEdits = editor.Replay(saved);
                SavedEdits = saved.Ops.Count;
                Fm = fm;
                Plan = plan;
                Editor = editor;
            });
        }

        /// <summary>How many of the saved manual edits were applied by the last plan build (and how many were saved).</summary>
        public int ReplayedEdits { get; private set; }
        public int SavedEdits { get; private set; }

        /// <summary>Stores the manual edits so they survive a rebuild of the plan.</summary>
        public void SaveEdits()
        {
            if (Editor != null) Editor.Log.Save(OverridesPath);
        }

        /// <summary>Forgets all manual edits (the plan must be rebuilt afterwards).</summary>
        public void ClearEdits()
        {
            if (File.Exists(OverridesPath)) File.Delete(OverridesPath);
        }

        /// <summary>Writes the plan as .sav files into the project's Anstoss2 folder and validates the result.</summary>
        public Task<WriteOutcome> WriteAsync()
        {
            var plan = Plan;
            var fm = Fm;
            var settings = Settings;
            var a2 = Config.Anstoss2Path;
            var output = Project.Anstoss2;
            return Task.Run(() =>
            {
                PrepareCrestFolder();
                var report = SavWriter.Write(plan, fm, settings, a2, output, Project.Crests, Config.FmYear);
                var errors = SavValidator.Validate(Project.DataDef, Path.Combine(a2, "DATA.DEF"));
                errors.AddRange(SavValidator.Validate(Project.DataVer, Path.Combine(a2, "DATA.VER")));
                return new WriteOutcome { Report = report, Problems = errors };
            });
        }

        /// <summary>Creates the project's crest folder with its instructions and the list of FM club ids (clubs of the main five nations).</summary>
        public void PrepareCrestFolder()
        {
            if (Project == null) return;
            var fm = Fm;
            var clubs = fm == null ? new List<A2G.Convert.Fm.FmClub>() : fm.Clubs.Values.Where(c => c.IsMainMensClub).ToList();
            CrestLibrary.PrepareFolder(Project.Crests, clubs, c => { var n = fm == null ? null : fm.NationBySaveId(c.NationId); return n != null ? n.Name : string.Empty; });
        }

        // ---- defaults -----------------------------------------------------------------------

        /// <summary>Windows form of a path: backslashes and an upper-case drive letter (Steam reports "e:/steam/...").</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (CommunitySaves.IsUrl(path.Trim())) return path.Trim();   // a community save is kept as its address
            try
            {
                var full = Path.GetFullPath(path.Trim());
                if (full.Length >= 2 && full[1] == ':') full = char.ToUpperInvariant(full[0]) + full.Substring(1);
                return full;
            }
            catch (Exception) { return path; }
        }

        /// <summary>The Football Manager versions found on this PC (newest first).</summary>
        public IList<FmVersionInfo> FmVersions { get; private set; } = new List<FmVersionInfo>();

        /// <summary>fmsave reads FM26 and later.</summary>
        public const int FirstSupportedFm = 26;

        /// <summary>
        /// Finds installed Football Manager versions: "Football Manager NN" folders in the Steam libraries
        /// (install) and under Documents\Sports Interactive (saves).
        /// </summary>
        public static IList<FmVersionInfo> DetectFmVersions()
        {
            var found = new Dictionary<int, FmVersionInfo>();
            Func<int, FmVersionInfo> get = n =>
            {
                FmVersionInfo v;
                if (!found.TryGetValue(n, out v)) found[n] = v = new FmVersionInfo { Number = n, Install = string.Empty, Saves = string.Empty };
                return v;
            };
            try
            {
                var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                var libraries = new List<string>();
                if (!string.IsNullOrEmpty(steam))
                {
                    libraries.Add(steam);
                    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                foreach (var lib in libraries)
                    foreach (var dir in FmFolders(Path.Combine(lib, "steamapps", "common")))
                        if (Directory.Exists(Path.Combine(dir.Value, "shared")) && string.IsNullOrEmpty(get(dir.Key).Install))
                            get(dir.Key).Install = NormalizePath(dir.Value);
            }
            catch (Exception) { /* detection is a convenience only */ }
            try
            {
                var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive");
                foreach (var dir in FmFolders(docs))
                {
                    var games = Path.Combine(dir.Value, "games");
                    if (Directory.Exists(games)) get(dir.Key).Saves = NormalizePath(games);
                }
            }
            catch (Exception) { /* detection is a convenience only */ }
            return found.Values.Where(v => v.Number >= FirstSupportedFm).OrderByDescending(v => v.Number).ToList();
        }

        private static IEnumerable<KeyValuePair<int, string>> FmFolders(string parent)
        {
            if (!Directory.Exists(parent)) yield break;
            foreach (var dir in Directory.GetDirectories(parent))
            {
                var m = Regex.Match(Path.GetFileName(dir), @"^Football Manager (\d+)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var number = int.Parse(m.Groups[1].Value);
                if (number >= 2000) number -= 2000; // older versions are named by year ("Football Manager 2024")
                yield return new KeyValuePair<int, string>(number, dir);
            }
        }

        /// <summary>An Anstoss 2 install folder is the one holding DATA.DEF\LAENDER.SAV.</summary>
        public static bool IsA2Install(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "DATA.DEF", "LAENDER.SAV")); }
            catch (Exception) { return false; }
        }

        private static readonly string[] A2VendorNames = { "ASCARON Entertainment", "ASCARON", "Ascaron" };
        private static readonly string[] A2GameNames = { "ANSTOSS 2 Gold", "ANSTOSS 2", "Anstoss 2 Gold", "Anstoss2", "ANSTOSS2" };
        private static readonly string[] SkipDirs = { "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "Users", "node_modules", "Recovery" };

        /// <summary>
        /// Finds Anstoss 2: first the usual install folders (Program Files, Program Files (x86), drive roots, Games...
        /// under Ascaron Entertainment / ASCARON), then a shallow scan of all fixed local drives
        /// (network, CD-ROM, floppy and removable drives are skipped).
        /// </summary>
        public static string DetectA2Install()
        {
            try
            {
                var bases = new List<string>();
                var pf = new[]
                {
                    Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                    Environment.GetEnvironmentVariable("ProgramFiles"),
                    Environment.GetEnvironmentVariable("ProgramW6432")
                };
                bases.AddRange(pf.Where(p => !string.IsNullOrEmpty(p)));
                var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
                foreach (var d in drives)
                {
                    var root = d.RootDirectory.FullName;
                    bases.Add(root);
                    foreach (var sub in new[] { "Games", "Spiele", "Program Files (x86)", "Program Files", "Programme", "Programme (x86)" })
                        bases.Add(Path.Combine(root, sub));
                }
                foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(b)) continue;
                    foreach (var game in A2GameNames)
                    {
                        var direct = Path.Combine(b, game);
                        if (IsA2Install(direct)) return direct;
                        foreach (var vendor in A2VendorNames)
                        {
                            var nested = Path.Combine(b, vendor, game);
                            if (IsA2Install(nested)) return nested;
                        }
                    }
                }
                // Shallow scan: up to three levels below each fixed drive, only descending into plausible names.
                foreach (var d in drives)
                {
                    var found = ScanFor(d.RootDirectory.FullName, 3);
                    if (found != null) return found;
                }
            }
            catch (Exception) { /* detection is a convenience only */ }
            return string.Empty;
        }

        private static string ScanFor(string dir, int depth)
        {
            if (IsA2Install(dir)) return dir;
            if (depth == 0) return null;
            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (Exception) { return null; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (SkipDirs.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) continue;
                var found = ScanFor(child, depth - 1);
                if (found != null) return found;
            }
            return null;
        }

        private static T ReadJson<T>(string path)
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                new DataContractJsonSerializer(value.GetType()).WriteObject(writer, value);
        }
    }

    /// <summary>One detected Football Manager version.</summary>
    public sealed class FmVersionInfo
    {
        public int Number { get; set; }
        public string Install { get; set; }
        public string Saves { get; set; }
        public override string ToString() { return "FM" + Number; }
    }

    public sealed class WriteOutcome
    {
        public WriteReport Report { get; set; }
        public List<string> Problems { get; set; }
    }
}

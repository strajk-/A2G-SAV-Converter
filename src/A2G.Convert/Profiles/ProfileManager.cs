using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace A2G.Convert.Profiles
{
    [DataContract]
    public sealed class ProfileFile
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public long Size { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }

    [DataContract]
    public sealed class ProfileManifest
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string CreatedUtc { get; set; }
        [DataMember] public string Source { get; set; }
        [DataMember] public List<ProfileFile> Files { get; set; }
    }

    [DataContract]
    internal sealed class ActiveMarker
    {
        [DataMember] public string Profile { get; set; }
        [DataMember] public string ActivatedUtc { get; set; }
        [DataMember] public string Tool { get; set; }
    }

    public enum DirtyPolicy
    {
        /// <summary>Stop and report when the live files differ from the active profile.</summary>
        Abort,
        /// <summary>Save the live files into the active profile first.</summary>
        SaveToActive,
        /// <summary>Throw the live changes away.</summary>
        Discard
    }

    public sealed class ProfileStatus
    {
        public ProfileStatus() { Changed = new List<string>(); }
        /// <summary>Name of the live profile, or null if this install has never been managed.</summary>
        public string Active { get; set; }
        public bool Dirty { get { return Changed.Count > 0; } }
        public List<string> Changed { get; private set; }
    }

    /// <summary>
    /// Manages data-set profiles of an Anstoss 2 install (see docs\Dev\profiles.md). A profile is a full copy of DATA.DEF and
    /// DATA.VER stored in <c>Profiles\&lt;name&gt;</c> with a manifest of sizes and SHA-256 hashes; the marker file
    /// <c>A2G_PROFILE.json</c> in the install root names the live profile. Everything is copied and verified before the
    /// live folders are replaced, and the live folders are swapped by rename so an interruption can be repaired.
    /// </summary>
    public sealed class ProfileManager
    {
        public const string OriginalName = "Original";
        public const string MarkerFileName = "A2G_PROFILE.json";
        private static readonly string[] DataFolders = { "DATA.DEF", "DATA.VER" };

        private readonly string _install;

        public ProfileManager(string installDir)
        {
            if (!Directory.Exists(Path.Combine(installDir, "DATA.DEF")))
                throw new DirectoryNotFoundException("Not an Anstoss 2 folder (DATA.DEF missing): " + installDir);
            _install = Path.GetFullPath(installDir);
        }
        public string ProfilesDir { get { return Path.Combine(_install, "Profiles"); } }
        private string MarkerPath { get { return Path.Combine(_install, MarkerFileName); } }

        // ---- queries ------------------------------------------------------------------------

        public IList<ProfileManifest> List()
        {
            var result = new List<ProfileManifest>();
            if (!Directory.Exists(ProfilesDir)) return result;
            foreach (var dir in Directory.GetDirectories(ProfilesDir))
            {
                if (dir.EndsWith(".a2g-new", StringComparison.OrdinalIgnoreCase) || dir.EndsWith(".a2g-old", StringComparison.OrdinalIgnoreCase)) continue;   // leftovers of an interrupted replacement
                var m = ReadManifest(dir);
                if (m != null) result.Add(m);
            }
            return result.OrderBy(m => m.Name == OriginalName ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string ActiveProfile()
        {
            var marker = ReadMarker();
            return marker != null ? marker.Profile : null;
        }

        /// <summary>Compares the live files with the manifest of the active profile.</summary>
        public ProfileStatus GetStatus()
        {
            var status = new ProfileStatus { Active = ActiveProfile() };
            if (status.Active == null) return status;
            var manifest = ReadManifest(ProfileDir(status.Active));
            if (manifest == null) { status.Changed.Add("profile data of '" + status.Active + "' is missing"); return status; }

            var live = BuildManifest(_install, "live").Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var stored = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in stored)
            {
                ProfileFile f;
                if (!live.TryGetValue(kv.Key, out f)) status.Changed.Add("removed: " + kv.Key);
                else if (f.Sha256 != kv.Value.Sha256) status.Changed.Add("modified: " + kv.Key);
            }
            foreach (var key in live.Keys.Where(k => !stored.ContainsKey(k))) status.Changed.Add("added: " + key);
            return status;
        }

        // ---- operations ---------------------------------------------------------------------

        /// <summary>First use: stores the live data as the "Original" profile and marks it active.</summary>
        public void Initialize()
        {
            if (ActiveProfile() != null) return;
            var originalDir = ProfileDir(OriginalName);
            if (ReadManifest(originalDir) == null)
            {
                if (Directory.Exists(originalDir)) Directory.Delete(originalDir, true);
                CaptureLive(OriginalName, "Shipped data found in the install on first use");
            }
            WriteMarker(OriginalName);
        }

        /// <summary>
        /// Installs a data set (a folder holding DATA.DEF and DATA.VER, e.g. a project's Anstoss2 folder) as a new profile
        /// and makes it live. With <paramref name="fileFilter"/> only the matching files (relative paths such as
        /// "DATA.DEF\LandDeut.sav") are taken from the source; all others stay as they are in the live data.
        /// With <paramref name="overwrite"/> a profile of the same name is replaced by the new data (never the Original profile): the new data
        /// is built and verified next to the old one first, so a failure leaves the old profile as it was.
        /// </summary>
        public void Install(string sourceDir, string profileName, DirtyPolicy policy, Func<string, bool> fileFilter = null, bool overwrite = false, string sourceNote = null)
        {
            AssertGameNotRunning();
            ValidateName(profileName);
            Initialize();
            var exists = ReadManifest(ProfileDir(profileName)) != null;
            if (exists && !overwrite)
                throw new InvalidOperationException("A profile named '" + profileName + "' already exists.");
            if (exists && string.Equals(profileName, OriginalName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Original profile cannot be replaced.");
            HandleDirty(policy);

            var final = ProfileDir(profileName);
            var target = exists ? final + ".a2g-new" : final;   // a replacement is built next to the old profile
            Directory.CreateDirectory(ProfilesDir);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            try
            {
                if (fileFilter == null)
                {
                    foreach (var folder in DataFolders)
                    {
                        var src = Path.Combine(sourceDir, folder);
                        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("Source lacks " + folder + ": " + sourceDir);
                        CopyDirectory(src, Path.Combine(target, folder));
                    }
                }
                else
                {
                    foreach (var folder in DataFolders) CopyDirectory(Path.Combine(_install, folder), Path.Combine(target, folder));
                    foreach (var folder in DataFolders)
                    {
                        var src = Path.Combine(sourceDir, folder);
                        if (!Directory.Exists(src)) continue;
                        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                        {
                            var rel = Path.Combine(folder, file.Substring(src.Length).TrimStart('\\', '/'));
                            if (!fileFilter(rel)) continue;
                            var dest = Path.Combine(target, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            File.Copy(file, dest, true);
                        }
                    }
                }
                var manifest = BuildManifest(target, profileName);
                manifest.Source = sourceNote ?? sourceDir;   // what the list shows as the origin (a patch passes its name instead of its temporary folder)
                WriteManifest(target, manifest);
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                throw;
            }
            string backup = null;
            if (exists)
            {
                // put the new data in the place of the old profile; the old one is kept until everything worked
                backup = final + ".a2g-old";
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(final, backup);
                Directory.Move(target, final);
            }
            try
            {
                ReplaceLive(profileName);
                WriteMarker(profileName);
            }
            catch
            {
                if (backup != null)
                {
                    if (Directory.Exists(final)) Directory.Delete(final, true);
                    Directory.Move(backup, final);
                }
                throw;
            }
            if (backup != null && Directory.Exists(backup)) Directory.Delete(backup, true);
        }

        /// <summary>Makes an existing profile live.</summary>
        public void Activate(string profileName, DirtyPolicy policy)
        {
            AssertGameNotRunning();
            Initialize();
            if (ReadManifest(ProfileDir(profileName)) == null)
                throw new InvalidOperationException("No such profile: " + profileName);
            if (string.Equals(ActiveProfile(), profileName, StringComparison.OrdinalIgnoreCase) && !GetStatus().Dirty) return;
            HandleDirty(policy);
            ReplaceLive(profileName);
            WriteMarker(profileName);
        }

        public void Rename(string oldName, string newName)
        {
            ValidateName(newName);
            if (ReadManifest(ProfileDir(newName)) != null) throw new InvalidOperationException("A profile named '" + newName + "' already exists.");
            var dir = ProfileDir(oldName);
            var manifest = ReadManifest(dir);
            if (manifest == null) throw new InvalidOperationException("No such profile: " + oldName);
            Directory.Move(dir, ProfileDir(newName));
            manifest.Name = newName;
            WriteManifest(ProfileDir(newName), manifest);
            if (string.Equals(ActiveProfile(), oldName, StringComparison.OrdinalIgnoreCase)) WriteMarker(newName);
        }

        public void Delete(string profileName)
        {
            if (string.Equals(profileName, OriginalName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Original profile cannot be deleted.");
            if (string.Equals(ActiveProfile(), profileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The live profile cannot be deleted; activate another one first.");
            var dir = ProfileDir(profileName);
            if (ReadManifest(dir) == null) throw new InvalidOperationException("No such profile: " + profileName);
            Directory.Delete(dir, true);
        }

        // ---- internals ----------------------------------------------------------------------

        private void HandleDirty(DirtyPolicy policy)
        {
            var status = GetStatus();
            if (!status.Dirty) return;
            switch (policy)
            {
                case DirtyPolicy.Abort:
                    throw new InvalidOperationException("The live files differ from profile '" + status.Active + "' (" +
                        string.Join("; ", status.Changed.Take(5)) + (status.Changed.Count > 5 ? "; ..." : "") +
                        "). Save them into that profile or discard them first.");
                case DirtyPolicy.SaveToActive:
                    var dir = ProfileDir(status.Active);
                    var created = ReadManifest(dir).CreatedUtc;
                    Directory.Delete(dir, true);
                    CaptureLive(status.Active, "Live files saved back");
                    var m = ReadManifest(dir);
                    m.CreatedUtc = created;
                    WriteManifest(dir, m);
                    break;
            }
        }

        private void CaptureLive(string name, string source)
        {
            var dir = ProfileDir(name);
            Directory.CreateDirectory(ProfilesDir);
            try
            {
                foreach (var folder in DataFolders) CopyDirectory(Path.Combine(_install, folder), Path.Combine(dir, folder));
                var manifest = BuildManifest(dir, name);
                manifest.Source = source;
                // verify against the live files before anything is replaced
                var live = BuildManifest(_install, "live").Files.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
                foreach (var f in manifest.Files)
                {
                    string hash;
                    if (!live.TryGetValue(f.Path, out hash) || hash != f.Sha256)
                        throw new IOException("Verification failed for " + f.Path);
                }
                WriteManifest(dir, manifest);
            }
            catch
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                throw;
            }
        }

        /// <summary>Copies the profile next to the live folders, verifies it, then swaps the folders by rename.</summary>
        private void ReplaceLive(string profileName)
        {
            var profileDir = ProfileDir(profileName);
            var manifest = ReadManifest(profileDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var staged = new List<KeyValuePair<string, string>>();    // staging folder -> live folder

            try
            {
                foreach (var folder in DataFolders)
                {
                    var staging = Path.Combine(_install, folder + ".a2g-new");
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    CopyDirectory(Path.Combine(profileDir, folder), staging);
                    staged.Add(new KeyValuePair<string, string>(staging, Path.Combine(_install, folder)));
                }
                foreach (var f in manifest.Files)
                {
                    var parts = f.Path.Split('\\');
                    var staging = Path.Combine(_install, parts[0] + ".a2g-new", string.Join("\\", parts.Skip(1)));
                    if (!File.Exists(staging) || new FileInfo(staging).Length != f.Size || HashFile(staging) != f.Sha256)
                        throw new IOException("Verification failed for " + f.Path);
                }
            }
            catch
            {
                foreach (var s in staged) if (Directory.Exists(s.Key)) Directory.Delete(s.Key, true);
                throw;
            }

            var moved = new List<KeyValuePair<string, string>>();     // old backup -> live folder
            try
            {
                foreach (var s in staged)
                {
                    var backup = s.Value + ".a2g-old-" + stamp;
                    Directory.Move(s.Value, backup);
                    moved.Add(new KeyValuePair<string, string>(backup, s.Value));
                    Directory.Move(s.Key, s.Value);
                }
            }
            catch
            {
                // put the previous folders back
                foreach (var m in moved)
                {
                    if (Directory.Exists(m.Value)) Directory.Delete(m.Value, true);
                    Directory.Move(m.Key, m.Value);
                }
                foreach (var s in staged) if (Directory.Exists(s.Key)) Directory.Delete(s.Key, true);
                throw;
            }
            foreach (var m in moved) Directory.Delete(m.Key, true);
        }

        private string ProfileDir(string name)
        {
            return Path.Combine(ProfilesDir, name);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith(".", StringComparison.Ordinal))
                throw new ArgumentException("Invalid profile name: '" + name + "'");
        }

        private static void AssertGameNotRunning()
        {
            foreach (var name in new[] { "anstoss2", "Editor" })
            {
                var running = Process.GetProcessesByName(name);
                if (running.Length > 0)
                {
                    foreach (var p in running) p.Dispose();
                    throw new InvalidOperationException("Close Anstoss 2 / the editor (" + name + ".exe) before changing profiles.");
                }
            }
        }

        private ProfileManifest BuildManifest(string root, string name)
        {
            var files = new List<ProfileFile>();
            foreach (var folder in DataFolders)
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    files.Add(new ProfileFile
                    {
                        Path = folder + "\\" + file.Substring(dir.Length).TrimStart('\\', '/').Replace('/', '\\'),
                        Size = new FileInfo(file).Length,
                        Sha256 = HashFile(file)
                    });
                }
            }
            return new ProfileManifest { Name = name, CreatedUtc = DateTime.UtcNow.ToString("o"), Files = files };
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        // ---- JSON files ---------------------------------------------------------------------

        private ProfileManifest ReadManifest(string profileDir)
        {
            var path = Path.Combine(profileDir, "profile.json");
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
                return (ProfileManifest)new DataContractJsonSerializer(typeof(ProfileManifest)).ReadObject(stream);
        }

        private static void WriteManifest(string profileDir, ProfileManifest manifest)
        {
            WriteJson(Path.Combine(profileDir, "profile.json"), manifest);
        }

        private ActiveMarker ReadMarker()
        {
            if (!File.Exists(MarkerPath)) return null;
            using (var stream = File.OpenRead(MarkerPath))
                return (ActiveMarker)new DataContractJsonSerializer(typeof(ActiveMarker)).ReadObject(stream);
        }

        private void WriteMarker(string profile)
        {
            WriteJson(MarkerPath, new ActiveMarker { Profile = profile, ActivatedUtc = DateTime.UtcNow.ToString("o"), Tool = "A2G-SAV-Converter" });
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                new DataContractJsonSerializer(value.GetType()).WriteObject(writer, value);
        }
    }
}

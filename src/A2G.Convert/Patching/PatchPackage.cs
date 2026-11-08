using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using A2G.Convert.Profiles;

namespace A2G.Convert.Patching
{
    [DataContract]
    public sealed class PatchFileInfo
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public long Size { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }

    /// <summary>What a patch is: its name and description and the files it contains.</summary>
    [DataContract]
    public sealed class PatchInfo
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public string CreatedUtc { get; set; }
        /// <summary>True when only some files of DATA.DEF / DATA.VER are in the patch: the others stay as they are in the install.</summary>
        [DataMember] public bool Partial { get; set; }
        [DataMember] public List<PatchFileInfo> Files { get; set; }

        public PatchInfo() { Files = new List<PatchFileInfo>(); }
    }

    /// <summary>
    /// A patch: the program itself followed by a ZIP with the data files and a trailer (payload length and a signature). The same
    /// executable is the converter and the patcher: with a payload it opens the patcher window. Because the data hangs behind
    /// the program, a patch is one file that can be passed on.
    /// </summary>
    public sealed class PatchPackage
    {
        private const string Magic = "A2GPATCH";   // the last 8 bytes of a patch
        private const string InfoEntry = "patch.json";
        private const string DataPrefix = "data/";

        public PatchInfo Info { get; private set; }
        public string ExePath { get; private set; }
        private long _payloadStart, _payloadLength;

        private PatchPackage() { }

        /// <summary>The patch behind <paramref name="exePath"/>, or null when the file carries none.</summary>
        public static PatchPackage TryOpen(string exePath)
        {
            try
            {
                long start, length;
                if (!FindPayload(exePath, out start, out length)) return null;
                using (var file = File.OpenRead(exePath))
                using (var zip = new ZipArchive(new SubStream(file, start, length), ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry(InfoEntry);
                    if (entry == null) return null;
                    using (var s = entry.Open())
                    {
                        var info = (PatchInfo)new DataContractJsonSerializer(typeof(PatchInfo)).ReadObject(s);
                        if (info == null || info.Files == null) return null;
                        return new PatchPackage { Info = info, ExePath = exePath, _payloadStart = start, _payloadLength = length };
                    }
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>True when the file ends with the patch signature (even if the patch inside turns out to be damaged).</summary>
        public static bool HasSignature(string exePath)
        {
            try
            {
                using (var file = File.OpenRead(exePath))
                {
                    if (file.Length < 16) return false;
                    file.Seek(-8, SeekOrigin.End);
                    var tail = new byte[8];
                    var read = 0;
                    while (read < 8) { var n = file.Read(tail, read, 8 - read); if (n <= 0) return false; read += n; }
                    return Encoding.ASCII.GetString(tail) == Magic;
                }
            }
            catch (Exception) { return false; }
        }

        private static bool FindPayload(string exePath, out long start, out long length)
        {
            start = length = 0;
            using (var file = File.OpenRead(exePath))
            {
                if (file.Length < 16) return false;
                file.Seek(-16, SeekOrigin.End);
                var trailer = new byte[16];
                var read = 0;
                while (read < 16) { var n = file.Read(trailer, read, 16 - read); if (n <= 0) return false; read += n; }
                if (Encoding.ASCII.GetString(trailer, 8, 8) != Magic) return false;
                length = BitConverter.ToInt64(trailer, 0);
                start = file.Length - 16 - length;
                return length > 0 && start > 0;
            }
        }

        /// <summary>The size of the program part of the file (everything before an attached patch).</summary>
        public static long ProgramLength(string exePath)
        {
            long start, length;
            return FindPayload(exePath, out start, out length) ? start : new FileInfo(exePath).Length;
        }

        /// <summary>
        /// Writes a patch: a copy of the program (without a patch it may already carry) plus the files of the data folder
        /// (DATA.DEF and DATA.VER below <paramref name="sourceDir"/>). With a <paramref name="filter"/> only the files for which it
        /// returns true (relative paths such as "DATA.DEF\LandDeut.sav") are taken and the patch is marked partial.
        /// </summary>
        public static PatchInfo Build(string programExe, string sourceDir, string outputExe, string name, string description, Func<string, bool> filter)
        {
            var info = new PatchInfo { Name = name, Description = description ?? string.Empty, CreatedUtc = DateTime.UtcNow.ToString("o"), Partial = filter != null };
            var files = new List<KeyValuePair<string, string>>();   // relative path -> full path
            foreach (var folder in new[] { "DATA.DEF", "DATA.VER" })
            {
                var dir = System.IO.Path.Combine(sourceDir, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var rel = folder + "\\" + file.Substring(dir.Length).TrimStart('\\', '/').Replace('/', '\\');
                    if (filter != null && !filter(rel)) continue;
                    files.Add(new KeyValuePair<string, string>(rel, file));
                }
            }
            if (files.Count == 0) throw new InvalidOperationException("There are no data files to put into the patch: " + sourceDir);

            var zipPath = outputExe + ".payload.tmp";
            try
            {
                using (var zipFile = File.Create(zipPath))
                using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
                {
                    foreach (var f in files)
                    {
                        info.Files.Add(new PatchFileInfo { Path = f.Key, Size = new FileInfo(f.Value).Length, Sha256 = HashFile(f.Value) });
                        zip.CreateEntryFromFile(f.Value, DataPrefix + f.Key.Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                    var entry = zip.CreateEntry(InfoEntry);
                    using (var s = entry.Open()) new DataContractJsonSerializer(typeof(PatchInfo)).WriteObject(s, info);
                }
                var programLength = ProgramLength(programExe);
                var tmpOut = outputExe + ".tmp";
                using (var output = File.Create(tmpOut))
                {
                    using (var program = File.OpenRead(programExe)) CopyBytes(program, output, programLength);
                    using (var payload = File.OpenRead(zipPath)) payload.CopyTo(output);
                    output.Write(BitConverter.GetBytes(new FileInfo(zipPath).Length), 0, 8);
                    var magic = Encoding.ASCII.GetBytes(Magic);
                    output.Write(magic, 0, magic.Length);
                }
                if (File.Exists(outputExe)) File.Delete(outputExe);
                File.Move(tmpOut, outputExe);
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (File.Exists(outputExe + ".tmp")) File.Delete(outputExe + ".tmp");
            }
            return info;
        }

        /// <summary>Unpacks the data files into <paramref name="targetDir"/> (DATA.DEF and DATA.VER below it) and checks every file against its hash.</summary>
        public void Extract(string targetDir)
        {
            using (var file = File.OpenRead(ExePath))
            using (var zip = new ZipArchive(new SubStream(file, _payloadStart, _payloadLength), ZipArchiveMode.Read))
            {
                foreach (var f in Info.Files)
                {
                    var entry = zip.GetEntry(DataPrefix + f.Path.Replace('\\', '/'));
                    if (entry == null) throw new InvalidDataException("The patch is damaged: " + f.Path + " is missing.");
                    var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(targetDir, f.Path));
                    if (!dest.StartsWith(System.IO.Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid path in the patch: " + f.Path);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest));
                    using (var s = entry.Open())
                    using (var o = File.Create(dest)) s.CopyTo(o);
                    if (new FileInfo(dest).Length != f.Size || HashFile(dest) != f.Sha256)
                        throw new InvalidDataException("The patch is damaged: " + f.Path + " does not match its checksum.");
                }
            }
        }

        private static void CopyBytes(Stream from, Stream to, long count)
        {
            var buffer = new byte[1 << 16];
            while (count > 0)
            {
                var n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (n <= 0) throw new EndOfStreamException();
                to.Write(buffer, 0, n);
                count -= n;
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        /// <summary>A read-only window into a part of a stream (the payload at the end of the program file).</summary>
        private sealed class SubStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _start, _length;
            private long _position;

            public SubStream(Stream inner, long start, long length) { _inner = inner; _start = start; _length = length; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return _length; } }
            public override long Position { get { return _position; } set { _position = value; } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _length) return 0;
                count = (int)Math.Min(count, _length - _position);
                _inner.Seek(_start + _position, SeekOrigin.Begin);
                var n = _inner.Read(buffer, offset, count);
                _position += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                _position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : _length + offset;
                return _position;
            }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }

    /// <summary>Installs a patch into an Anstoss 2 folder through the profile manager: a new profile that becomes live; the previous data stays as its own profile for the way back.</summary>
    public static class PatchInstaller
    {
        public static void Install(PatchPackage patch, string installDir, string profileName, DirtyPolicy policy, bool overwrite = false)
        {
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a2g-patch-" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new ProfileManager(installDir);   // checks that it is an Anstoss 2 folder
                patch.Extract(temp);
                Func<string, bool> filter = null;
                if (patch.Info.Partial)
                {
                    var wanted = new HashSet<string>(patch.Info.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
                    filter = wanted.Contains;
                }
                manager.Install(temp, profileName, policy, filter, overwrite, "Patch: " + patch.Info.Name);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception) { }
            }
        }

        /// <summary>Exit codes of the quiet install.</summary>
        public const int Done = 0, NoFolder = 1, NotAnstoss2 = 2, Damaged = 3, Failed = 4;

        /// <summary>
        /// Installs the patch without asking anything (quiet mode for installers) and makes it live. Edited live files are saved into the
        /// active data set first, so nothing is lost. A patch that is already installed (a data set of the same name holds all its files) is
        /// only made live again, so running it twice changes nothing; one of the same name with other content (an older version) is replaced. Returns an exit code: 0 done, 2 not an Anstoss 2 folder,
        /// 3 damaged patch, 4 other problem (for example the game is running).
        /// </summary>
        public static int InstallQuiet(PatchPackage patch, string installDir, Action<string> log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(Path.Combine(installDir, "DATA.DEF")) || !Directory.Exists(Path.Combine(installDir, "DATA.VER")))
                {
                    log("Not an Anstoss 2 folder (DATA.DEF and DATA.VER are expected next to anstoss2.exe): " + installDir);
                    return NotAnstoss2;
                }
                var manager = new ProfileManager(installDir);
                var existing = manager.List();
                var same = existing.FirstOrDefault(p => string.Equals(p.Name, patch.Info.Name, StringComparison.OrdinalIgnoreCase));
                if (same != null && same.Files != null)
                {
                    var hashes = new HashSet<string>(same.Files.Select(f => f.Path + "|" + f.Sha256), StringComparer.OrdinalIgnoreCase);
                    if (patch.Info.Files.All(f => hashes.Contains(f.Path + "|" + f.Sha256)))
                    {
                        manager.Activate(same.Name, DirtyPolicy.SaveToActive);
                        log("The patch is already installed as '" + same.Name + "'; it is live now.");
                        return Done;
                    }
                }
                if (same != null && !string.Equals(same.Name, ProfileManager.OriginalName, StringComparison.OrdinalIgnoreCase))
                {
                    // a data set of this name exists with other content (an older version of the patch): replace it
                    Install(patch, installDir, same.Name, DirtyPolicy.SaveToActive, true);
                    log("Replaced the data set '" + same.Name + "' with this patch; it is live now.");
                    return Done;
                }
                var name = SuggestProfileName(patch.Info.Name, existing.Select(p => p.Name));
                Install(patch, installDir, name, DirtyPolicy.SaveToActive);
                log("Installed the patch as '" + name + "'; it is live now. Go back with the patcher (select another data set).");
                return Done;
            }
            catch (InvalidDataException ex) { log("The patch is damaged: " + ex.Message); return Damaged; }
            catch (Exception ex) { log(ex.Message); return Failed; }
        }

        /// <summary>The patch's name made usable as a profile name that is not taken yet in the install.</summary>
        public static string SuggestProfileName(string patchName, IEnumerable<string> existing)
        {
            var clean = new string((patchName ?? string.Empty).Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim().TrimStart('.');
            if (clean.Length == 0) clean = "Patch";
            var taken = new HashSet<string>(existing ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var name = clean;
            for (var n = 2; taken.Contains(name); n++) name = clean + "-" + n;
            return name;
        }
    }
}

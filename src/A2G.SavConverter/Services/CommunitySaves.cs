using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace A2G.SavConverter.Services
{
    /// <summary>A save file offered in the repository's CommunitySaves folder.</summary>
    public sealed class CommunitySave
    {
        public string Folder { get; set; }
        public string Name { get; set; }
        /// <summary>Git blob hash of the file (what the repository lists); used to tell whether the local copy is current.</summary>
        public string Sha { get; set; }
        public long Size { get; set; }
        public string Url { get; set; }
        public bool Downloaded { get; set; }
        public string SizeText { get { return (Size / 1048576.0).ToString("0.0") + " MB"; } }
    }

    /// <summary>
    /// Community saves: .fm files kept in the folder CommunitySaves/&lt;group&gt;/ of the GitHub repository (for example an update save with
    /// the current transfers). A group folder may hold a settings.json with defaults for its saves (for now only "StartYear").
    /// A save is downloaded once into the program's data folder and reused while its hash is unchanged.
    /// </summary>
    public static class CommunitySaves
    {
        public const string Repository = "strajk-/A2G-SAV-Converter";
        public const string Branch = "main";
        public const string FolderName = "CommunitySaves";
        public const string SettingsFile = "settings.json";

        /// <summary>Where the files are served from and where the list comes from; settable so a test can point them at a local server.</summary>
        public static string RawBase = "https://raw.githubusercontent.com/" + Repository + "/" + Branch + "/" + FolderName + "/";
        public static string TreeUrl = "https://api.github.com/repos/" + Repository + "/git/trees/" + Branch + "?recursive=1";

        public static string LocalRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", FolderName); }
        }

        public static bool IsUrl(string text)
        {
            return text != null && (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The save a URL of the community folder points to (folder + file name), or null for any other address.</summary>
        public static bool TryParseUrl(string url, out string folder, out string name)
        {
            folder = name = null;
            if (!IsUrl(url) || !url.StartsWith(RawBase, StringComparison.OrdinalIgnoreCase)) return false;
            var parts = Uri.UnescapeDataString(url.Substring(RawBase.Length)).Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0 || parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            folder = parts[0];
            name = parts[1];
            return true;
        }

        public static string LocalPath(string folder, string name) { return Path.Combine(LocalRoot, folder, name); }

        private static HttpClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("A2G-SAV-Converter");
            return client;
        }

        // ---- the list -------------------------------------------------------------------------

        [DataContract] private sealed class TreeResponse { [DataMember(Name = "tree")] public List<TreeEntry> Tree { get; set; } }
        [DataContract] private sealed class TreeEntry
        {
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "sha")] public string Sha { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
        }

        /// <summary>Lists every .fm file below CommunitySaves/ (one request). Throws when there is no connection.</summary>
        public static async Task<List<CommunitySave>> ListAsync(CancellationToken ct)
        {
            string json;
            using (var client = NewClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                json = await client.GetStringAsync(TreeUrl).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            TreeResponse tree;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                tree = (TreeResponse)new DataContractJsonSerializer(typeof(TreeResponse)).ReadObject(stream);
            var result = new List<CommunitySave>();
            foreach (var entry in tree.Tree ?? new List<TreeEntry>())
            {
                if (entry.Type != "blob" || entry.Path == null || !entry.Path.StartsWith(FolderName + "/", StringComparison.Ordinal)) continue;
                var parts = entry.Path.Substring(FolderName.Length + 1).Split('/');
                if (parts.Length != 2 || !parts[1].EndsWith(".fm", StringComparison.OrdinalIgnoreCase)) continue;
                var save = new CommunitySave
                {
                    Folder = parts[0],
                    Name = parts[1],
                    Sha = entry.Sha,
                    Size = entry.Size,
                    Url = RawBase + Uri.EscapeDataString(parts[0]) + "/" + Uri.EscapeDataString(parts[1])
                };
                save.Downloaded = IsCurrent(save);
                result.Add(save);
            }
            return result.OrderByDescending(s => s.Folder, StringComparer.OrdinalIgnoreCase).ThenByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ---- the group's settings --------------------------------------------------------------

        [DataContract] private sealed class GroupSettings { [DataMember] public int StartYear { get; set; } }

        /// <summary>The start year a group folder asks for in its settings.json, or 0 (no file, no year, or no connection).</summary>
        public static async Task<int> ReadStartYearAsync(string folder)
        {
            try
            {
                string json;
                using (var client = NewClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                    json = await client.GetStringAsync(RawBase + Uri.EscapeDataString(folder) + "/" + SettingsFile).ConfigureAwait(false);
                }
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('﻿'))))
                {
                    var settings = (GroupSettings)new DataContractJsonSerializer(typeof(GroupSettings)).ReadObject(stream);
                    return settings.StartYear >= 1998 && settings.StartYear <= 2098 ? settings.StartYear : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        // ---- the local copy --------------------------------------------------------------------

        private static string ShaFile(string localPath) { return localPath + ".sha"; }

        private static bool IsCurrent(CommunitySave save)
        {
            try
            {
                var path = LocalPath(save.Folder, save.Name);
                return File.Exists(path) && new FileInfo(path).Length == save.Size
                    && File.Exists(ShaFile(path)) && File.ReadAllText(ShaFile(path)).Trim() == save.Sha;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Makes sure the local copy of the save behind <paramref name="url"/> is the current one and returns its path. The repository's hash is
        /// compared with the stored one; the file is downloaded only when it is missing or changed. Without a connection an existing copy is used.
        /// <paramref name="progress"/> gets the fraction (0..1) while downloading; <paramref name="log"/> gets short messages.
        /// </summary>
        public static async Task<string> EnsureLocalAsync(string url, IProgress<double> progress, Action<string> log, CancellationToken ct)
        {
            string folder, name;
            if (!TryParseUrl(url, out folder, out name)) throw new InvalidOperationException(url);
            var path = LocalPath(folder, name);

            CommunitySave remote = null;
            try
            {
                var list = await ListAsync(ct).ConfigureAwait(false);
                remote = list.FirstOrDefault(s => s.Folder == folder && s.Name == name);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (File.Exists(path)) { log("offline|" + ex.Message); return path; }
                throw;
            }
            if (remote == null) throw new FileNotFoundException(url);
            if (remote.Downloaded) { log("current|" + remote.SizeText); progress.Report(1); return path; }

            log("download|" + remote.SizeText);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var part = path + ".part";
            var hash = SHA1.Create();
            var header = Encoding.ASCII.GetBytes("blob " + remote.Size + "\0");
            hash.TransformBlock(header, 0, header.Length, null, 0);
            using (var client = NewClient())
            using (var response = await client.GetAsync(remote.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = File.Create(part))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        hash.TransformBlock(buffer, 0, read, null, 0);
                        done += read;
                        progress.Report(remote.Size > 0 ? Math.Min(1.0, (double)done / remote.Size) : 0);
                    }
                }
            }
            hash.TransformFinalBlock(new byte[0], 0, 0);
            var actual = string.Concat(hash.Hash.Select(b => b.ToString("x2")));
            if (!string.Equals(actual, remote.Sha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException("hash");
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(part, path);
            File.WriteAllText(ShaFile(path), remote.Sha);
            return path;
        }
    }
}

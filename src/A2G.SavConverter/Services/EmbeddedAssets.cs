using System;
using System.IO;
using System.Reflection;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// Makes the program one single .exe: the libraries (A2G.Convert.dll, A2G.Sav.dll) and the Football Manager reader (fmsave.exe)
    /// are embedded resources of the program (the project file adds them). The libraries are loaded from the resources when they
    /// are not next to the program; fmsave.exe is unpacked once into the user's local application data folder.
    /// </summary>
    internal static class EmbeddedAssets
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
            using (var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream("Embedded." + name + ".dll"))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length) { var n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                return Assembly.Load(bytes);
            }
        }

        /// <summary>
        /// The path of fmsave.exe: the one next to the program if there is one (development build), else the embedded copy unpacked to
        /// %LOCALAPPDATA%\A2G-SAV-Converter\fmsave\&lt;size&gt;\ (a new version of fmsave goes to its own folder). Null when the program has none.
        /// </summary>
        public static string FmSavePath()
        {
            var appDir = Path.GetDirectoryName(typeof(EmbeddedAssets).Assembly.Location);
            var beside = Path.Combine(appDir, "fmsave", "fmsave.exe");
            if (File.Exists(beside)) return beside;
            try
            {
                var assembly = typeof(EmbeddedAssets).Assembly;
                using (var stream = assembly.GetManifestResourceStream("Embedded.fmsave.exe"))
                {
                    if (stream == null) return null;
                    var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2G-SAV-Converter", "fmsave", stream.Length.ToString());
                    var target = Path.Combine(folder, "fmsave.exe");
                    if (!File.Exists(target) || new FileInfo(target).Length != stream.Length)
                    {
                        Directory.CreateDirectory(folder);
                        var temp = target + ".tmp";
                        using (var output = File.Create(temp)) stream.CopyTo(output);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(temp, target);
                        using (var license = assembly.GetManifestResourceStream("Embedded.LICENSE-fmsave.txt"))
                            if (license != null)
                                using (var output = File.Create(Path.Combine(folder, "LICENSE-fmsave.txt"))) license.CopyTo(output);
                    }
                    return target;
                }
            }
            catch (Exception) { return null; }
        }
    }
}

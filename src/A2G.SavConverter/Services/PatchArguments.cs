using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// The command line of a patch: <c>-q</c> installs and activates the patch without any window (for installers),
    /// <c>-F &lt;folder&gt;</c> names the Anstoss 2 folder (the one with anstoss2.exe, DATA.DEF and DATA.VER). Also accepted: /q, --quiet,
    /// -F:folder, -F=folder, any case. Without -q the folder is only filled into the window.
    /// </summary>
    internal sealed class PatchArguments
    {
        public bool Quiet;
        public string Folder;

        public static PatchArguments Parse(IList<string> args)
        {
            var result = new PatchArguments();
            for (var i = 0; i < args.Count; i++)
            {
                var a = args[i];
                if (a.Length < 2 || (a[0] != '-' && a[0] != '/')) continue;
                var body = a.TrimStart('-', '/');
                string value = null;
                var cut = body.IndexOfAny(new[] { ':', '=' });
                if (cut > 0) { value = body.Substring(cut + 1); body = body.Substring(0, cut); }
                switch (body.ToLowerInvariant())
                {
                    case "q":
                    case "quiet":
                        result.Quiet = true;
                        break;
                    case "f":
                    case "folder":
                        if (value == null && i + 1 < args.Count) value = args[++i];
                        result.Folder = value == null ? null : value.Trim().Trim('"');
                        break;
                }
            }
            return result;
        }

        // ---- text output for installers: a WinExe has no console, so attach to the one that started it ----

        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);

        /// <summary>Writes a line for the caller of a quiet install: to the redirected output when there is one (installers capture it), else to the console that started the program.</summary>
        public static Action<string> ConsoleLog()
        {
            try
            {
                var handle = GetStdHandle(-11);   // STD_OUTPUT_HANDLE: set when the caller redirected the output
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) AttachConsole(-1);
            }
            catch (Exception) { }
            return text =>
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(text + Environment.NewLine);
                    using (var stream = Console.OpenStandardOutput()) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
                }
                catch (Exception) { }
            };
        }
    }
}

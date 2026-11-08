using System.Diagnostics;
using System.Windows;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    public partial class App : Application
    {
        static App()
        {
            EmbeddedAssets.Register();   // the program is one .exe: its libraries are resources
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (System.Linq.Enumerable.Contains(System.Environment.GetCommandLineArgs(), "--selfcheck", System.StringComparer.OrdinalIgnoreCase))
            {
                // used by tools\publish.ps1: no window and nothing of the user's settings is touched
                var problem = SelfCheck();
                if (problem != null) PatchArguments.ConsoleLog()(problem);
                System.Environment.ExitCode = problem == null ? 0 : 1;
                Shutdown(System.Environment.ExitCode);
                return;
            }
            Loc.Initialize();
            ThemeManager.Initialize();
            base.OnStartup(e);
            // The same program is the converter and, with a patch attached to its end, the patcher.
            var arguments = PatchArguments.Parse(System.Linq.Enumerable.ToList(System.Linq.Enumerable.Skip(System.Environment.GetCommandLineArgs(), 1)));
            var patch = OpenPatch();
            if (patch == null)
            {
                if (IsDamagedPatch())
                {
                    // a patch whose data cannot be read: quiet mode only reports it through the exit code, never with a window
                    const string message = "The patch is damaged and cannot be installed.";
                    if (arguments.Quiet) { Finish(A2G.Convert.Patching.PatchInstaller.Damaged, message); return; }
                    ThemedMessageBox.Show(null, message, "Anstoss 2", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(A2G.Convert.Patching.PatchInstaller.Damaged);
                    return;
                }
                new MainWindow().Show();
                return;
            }
            if (arguments.Quiet)
            {
                // quiet mode (installers): no window and no message box at all; problems end the program with an exit code and a console message
                int code;
                string problem = null;
                try { code = RunQuiet(patch, arguments); }
                catch (System.Exception ex) { code = A2G.Convert.Patching.PatchInstaller.Failed; problem = ex.Message; }
                Finish(code, problem);
                return;
            }
            new PatcherWindow(patch, arguments.Folder).Show();
        }

        /// <summary>Ends the program with an exit code (the generated entry point ignores Run()'s result) and, for a problem, a console message.</summary>
        private static void Finish(int code, string problem)
        {
            if (problem != null) PatchArguments.ConsoleLog()(problem);
            System.Environment.ExitCode = code;
            Current.Shutdown(code);
        }

        private static bool IsDamagedPatch()
        {
            try { return A2G.Convert.Patching.PatchPackage.HasSignature(Process.GetCurrentProcess().MainModule.FileName); }
            catch (System.Exception) { return false; }
        }
        /// <summary>Checks that the single exe is complete: the embedded libraries load and fmsave.exe can be unpacked. Null when everything is fine.</summary>
        private static string SelfCheck()
        {
            try
            {
                var settings = new A2G.Convert.Engine.ConversionSettings();          // A2G.Convert.dll
                var land = new A2G.Sav.LandFile();                                  // A2G.Sav.dll
                if (settings == null || land == null) return "libraries did not load";
                if (A2G.Convert.Patching.PatchPackage.ProgramLength(Process.GetCurrentProcess().MainModule.FileName) <= 0) return "patch code did not load";
                var fmsave = EmbeddedAssets.FmSavePath();
                if (fmsave == null || !System.IO.File.Exists(fmsave)) return "fmsave.exe is not embedded or could not be unpacked";
                return null;
            }
            catch (System.Exception ex) { return "self check failed: " + ex.Message; }
        }

        private static A2G.Convert.Patching.PatchPackage OpenPatch()
        {
            string path;
            try { path = Process.GetCurrentProcess().MainModule.FileName; }
            catch (System.Exception) { return null; }
            return A2G.Convert.Patching.PatchPackage.TryOpen(path);
        }

        /// <summary>-q: installs the patch into the folder given with -F (else the remembered or detected one) and makes it live, with no window. Exit codes: see PatchInstaller.</summary>
        private static int RunQuiet(A2G.Convert.Patching.PatchPackage patch, PatchArguments arguments)
        {
            var log = PatchArguments.ConsoleLog();
            var folder = string.IsNullOrWhiteSpace(arguments.Folder) ? PatcherWindow.DefaultFolder() : AppState.NormalizePath(arguments.Folder);
            if (string.IsNullOrWhiteSpace(folder))
            {
                log("No Anstoss 2 folder: give it with -F <folder> (the folder where anstoss2.exe is located).");
                return A2G.Convert.Patching.PatchInstaller.NoFolder;
            }
            return A2G.Convert.Patching.PatchInstaller.InstallQuiet(patch, folder, log);
        }
    }
}
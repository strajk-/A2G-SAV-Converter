using A2G.SavConverter.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace A2G.SavConverter.Services
{
    /// <summary>One exported table.</summary>
    public sealed class ExportedTable
    {
        public string Table { get; set; }
        public string FilePath { get; set; }
        public bool Succeeded { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Extracts data from a Football Manager 26 save by driving the bundled fmsave.exe
    /// (see third_party\fmsave and tools\build-fmsave.ps1). Everything FM-specific stays behind
    /// this class so it can later be replaced by a native C# reader without touching the UI.
    /// </summary>
    public sealed class FmSaveExporter
    {
        public static string ExePath
        {
            get
            {
                // next to the program (development build) or unpacked from the program itself (the single .exe)
                var embedded = EmbeddedAssets.FmSavePath();
                if (embedded != null) return embedded;
                var appDir = Path.GetDirectoryName(typeof(FmSaveExporter).Assembly.Location);
                return Path.Combine(appDir, "fmsave", "fmsave.exe");
            }
        }

        public static bool IsAvailable
        {
            get { return File.Exists(ExePath); }
        }

        /// <summary>The tables the converter needs, in export order ("fixtures" is used to find each nation's leagues).</summary>
        public static readonly string[] ConversionTables = { "players", "clubs", "nations", "fixtures", "staff", "stadiums" };

        /// <summary>Tables the conversion works without (the stadium capacities then come from the reputation estimate); a failed export of one only produces a note.</summary>
        public static readonly string[] OptionalTables = { "stadiums" };

        /// <summary>
        /// Reads the nation names from the installed FM database (a save has only ids) and writes nation_names.csv.
        /// </summary>
        public async Task ExportNationNamesAsync(string fmInstallDir, string outFile, IProgress<string> log, CancellationToken cancellationToken)
        {
            if (!IsAvailable)
                throw new FileNotFoundException(Loc.T(26000), ExePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            if (log != null) log.Report(Loc.T(26001));
            var run = await RunAsync(new[] { "a2g-nations", "--fm-install", fmInstallDir, "-o", outFile }, log, cancellationToken).ConfigureAwait(false);
            if (run.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(run.StdErr, Loc.T(26002, run.ExitCode)));
            if (log != null) log.Report("  -> " + outFile);
        }

        /// <summary>
        /// For people without the game (a save received from somebody else): writes the nation names of FM26 that are
        /// built into this application. Nation ids stay the same inside one FM version; a newer FM may add nations.
        /// </summary>
        public void WriteBuiltInNationNames(string outFile, IProgress<string> log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            using (var stream = typeof(FmSaveExporter).Assembly.GetManifestResourceStream("Data.nation_names_fm26.csv"))
            {
                if (stream == null) throw new FileNotFoundException("Data.nation_names_fm26.csv");
                using (var file = File.Create(outFile)) stream.CopyTo(file);
            }
            if (log != null) log.Report(Loc.T(20074));
        }

        /// <summary>True when the folder looks like a Football Manager install (it has the "shared" database folder).</summary>
        public static bool IsFmInstall(string folder)
        {
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(Path.Combine(folder, "shared"));
        }

        /// <summary>Reads what fmsave knows about a save; never throws (a bad file gives an unrecognised result).</summary>
        public async Task<SaveInfo> ReadSaveInfoAsync(string savePath, CancellationToken cancellationToken)
        {
            if (!IsAvailable) return SaveInfo.Failed(Loc.T(26000));
            try
            {
                var result = await RunAsync(new[] { "info", savePath }, null, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0) return SaveInfo.Failed(FirstLine(result.StdErr, Loc.T(26003)));
                var info = SaveInfo.Parse(result.StdOut);
                return info.Recognised ? info : SaveInfo.Failed(FirstLine(result.StdErr, Loc.T(26003)));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return SaveInfo.Failed(ex.Message); }
        }

        /// <summary>Runs "fmsave info" and returns its text (game, build, in-game date).</summary>
        public async Task<string> GetInfoAsync(string savePath, CancellationToken cancellationToken)
        {
            var result = await RunAsync(new[] { "info", savePath }, null, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(FirstLine(result.StdErr, Loc.T(26003)));
            return result.StdOut.Trim();
        }

        /// <summary>Exports every row of each table to a UTF-8 CSV file in <paramref name="outputDirectory"/>.</summary>
        public async Task<IReadOnlyList<ExportedTable>> ExportAsync(
            string savePath,
            string outputDirectory,
            IEnumerable<string> tables,
            IProgress<string> log,
            CancellationToken cancellationToken)
        {
            if (!IsAvailable)
                throw new FileNotFoundException(Loc.T(26000), ExePath);
            if (!File.Exists(savePath))
                throw new FileNotFoundException(Loc.T(26004), savePath);

            Directory.CreateDirectory(outputDirectory);
            var results = new List<ExportedTable>();

            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outFile = Path.Combine(outputDirectory, table + ".csv");
                if (log != null) log.Report(Loc.T(26005, table));

                var run = await RunAsync(
                    new[] { "export", savePath, table, "--all", "--format", "csv", "-o", outFile },
                    log, cancellationToken).ConfigureAwait(false);

                var item = new ExportedTable
                {
                    Table = table,
                    FilePath = outFile,
                    Succeeded = run.ExitCode == 0 && File.Exists(outFile),
                    Error = run.ExitCode == 0 ? null : FirstLine(run.StdErr, "exit code " + run.ExitCode)
                };
                results.Add(item);
                if (log != null)
                    log.Report(item.Succeeded ? "  -> " + outFile : "  " + Loc.T(26006, item.Error));
            }
            return results;
        }

        // ---- process plumbing -------------------------------------------------------------

        private sealed class RunResult
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        private static Task<RunResult> RunAsync(string[] args, IProgress<string> log, CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ExePath,
                Arguments = JoinArguments(args),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(ExePath)
            };
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var completion = new TaskCompletionSource<RunResult>();
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (stderr) stderr.AppendLine(e.Data);
                if (log != null) log.Report("  " + e.Data);
            };

            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            process.Exited += (s, e) =>
            {
                // Exited can fire before the async readers drain; WaitForExit() with no argument waits for them.
                process.WaitForExit();
                registration.Dispose();
                var result = new RunResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = stdout.ToString(),
                    StdErr = stderr.ToString()
                };
                process.Dispose();
                if (ct.IsCancellationRequested) completion.TrySetCanceled();
                else completion.TrySetResult(result);
            };

            if (!process.Start())
            {
                completion.SetException(new InvalidOperationException(Loc.T(26007)));
                return completion.Task;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { /* already gone */ }
            });
            return completion.Task;
        }

        private static string JoinArguments(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Quote(arg));
            }
            return sb.ToString();
        }

        // Standard Windows command-line quoting (CommandLineToArgvW rules).
        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); }
                else { sb.Append('\\', backslashes).Append(c); }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }

        private static string FirstLine(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            // fmsave prints a traceback-free one-line error last; take the last non-empty line.
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : fallback;
        }
    }
}

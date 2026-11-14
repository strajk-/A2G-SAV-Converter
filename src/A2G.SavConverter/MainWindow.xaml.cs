using A2G.SavConverter.Localization;
using System.Windows.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using A2G.Convert.Plan;
using A2G.Convert.Profiles;
using A2G.Convert.Projects;
using A2G.SavConverter.Services;
using Microsoft.Win32;

namespace A2G.SavConverter
{
    public sealed class ProfileRow
    {
        public string Name { get; set; }
        public string Live { get; set; }
        public string Created { get; set; }
        public int FileCount { get; set; }
        public string Source { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly AppState _state = new AppState();
        private readonly FmSaveExporter _exporter = new FmSaveExporter();
        private CancellationTokenSource _cts;

        private sealed class ThemeChoice
        {
            public ThemeMode Mode;
            public override string ToString() { return Loc.T(Mode == ThemeMode.System ? 20050 : Mode == ThemeMode.Light ? 20051 : 20052); }
        }

        private bool _fillingHeader;

        /// <summary>Fills the theme and language drop-downs (the languages are those found in the application).</summary>
        private void FillHeaderCombos()
        {
            _fillingHeader = true;
            ThemeBox.ItemsSource = new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark }.Select(m => new ThemeChoice { Mode = m }).ToList();
            ThemeBox.SelectedIndex = (int)ThemeManager.Mode;
            LangBox.ItemsSource = Loc.Available.Select(l => l.Name).ToList();
            LangBox.SelectedItem = Loc.Available.Where(l => l.Code == Loc.CurrentCode).Select(l => l.Name).FirstOrDefault();
            _fillingHeader = false;
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var choice = ThemeBox.SelectedItem as ThemeChoice;
            if (!_fillingHeader && choice != null && choice.Mode != ThemeManager.Mode) ThemeManager.SetMode(choice.Mode);
        }

        private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var name = LangBox.SelectedItem as string;
            var code = Loc.Available.Where(l => l.Name == name).Select(l => l.Code).FirstOrDefault();
            if (!_fillingHeader && code != null) Loc.SetLanguage(code);
        }

        private void OnLanguageChanged()
        {
            FillHeaderCombos();
            RefreshProfiles();
            UpdateStepState();
            ShowSaveStatus();
            Options.RefreshLanguage();
            TeamsEditor.Reload();
            Inspector.Reload();
            CrestsEditor.Reload();
        }

        public MainWindow()
        {
            InitializeComponent();
            FillHeaderCombos();
            Loc.LanguageChanged += OnLanguageChanged;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            Inspector.Attach(_state);
            Options.Attach(_state);
            Options.Applied += _ => Inspector.Reload();
            TeamsEditor.Attach(_state);
            CrestsEditor.Attach(_state);
            TeamsEditor.Edited += () => { Inspector.Reload(); if (_state.Plan != null) SummaryBox.Text = PlanSummary.Build(_state.Plan); UpdateStepState(); };
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Stepper.ValueProperty, typeof(Stepper)).AddValueChanged(YearBox, (o, e) => YearBox_Changed());
            Tabs.SelectionChanged += (s, e) => { if (ReferenceEquals(e.OriginalSource, Tabs)) UpdateStepState(); };
            TeamsEditor.RebuildRequested += async () => await BuildPlanCoreAsync();
            Loaded += async (s, e) =>
            {
                // usage: A2G-SAV-Converter.exe [project folder] [--autoplan]
                var args = Environment.GetCommandLineArgs().Skip(1).ToList();
                var folder = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ProjectCatalog.LastProject() ?? ProjectLayout.DefaultRoot;
                OpenProject(folder);
                if (!FmSaveExporter.IsAvailable)
                    Log(Loc.T(20000, FmSaveExporter.ExePath));
                if (args.Contains("--autoplan") && _state.HasExport)
                {
                    await BuildPlanCoreAsync();
                    Tabs.SelectedItem = PlayersTab;
                    var rows = Inspector.FindName("PlayerList") as System.Windows.Controls.ListBox;
                    if (rows != null && rows.Items.Count > 0) rows.SelectedIndex = 0;
                }
                var rulesArg = args.FirstOrDefault(a => a.StartsWith("--rules=", StringComparison.Ordinal));
                int rulesIndex;
                if (rulesArg != null && int.TryParse(rulesArg.Substring(8), out rulesIndex) && rulesIndex >= 0 && rulesIndex < Options.RulesTabs.Items.Count)
                    Options.RulesTabs.SelectedIndex = rulesIndex;
                var tabArg = args.FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.Ordinal));
                int tabIndex;
                if (tabArg != null && int.TryParse(tabArg.Substring(6), out tabIndex) && tabIndex >= 0 && tabIndex < Tabs.Items.Count)
                    Tabs.SelectedIndex = tabIndex;
                // used to make the pictures of the user guide (see MainWindow.Screenshots.cs)
                var shotsArg = args.FirstOrDefault(a => a.StartsWith("--screenshots=", StringComparison.Ordinal));
                if (shotsArg != null) await TakeScreenshotsAsync(shotsArg.Substring(14).Trim('"'));
            };
        }

        // ---- dropping a save onto the window ----------------------------------------------------

        private static string DroppedSave(DragEventArgs e)
        {
            var files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            return files == null ? null : files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".fm", StringComparison.OrdinalIgnoreCase));
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DroppedSave(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            var file = DroppedSave(e);
            if (file == null || _state.Project == null || !Tabs.IsEnabled) return;
            SaveBox.Text = AppState.NormalizePath(file);
            SyncConfig();
            Tabs.SelectedIndex = 0;
            RefreshSaveStatus();
            Log(Loc.T(20075, SaveBox.Text));
            e.Handled = true;
        }

        // ---- project ------------------------------------------------------------------------

        private void OpenProject(string folder)
        {
            try
            {
                _state.OpenProject(folder);
                ProjectCatalog.RememberLast(_state.Project.Root);
                FillProjects();
                SaveBox.Text = _state.Config.FmSavePath ?? string.Empty;
                A2Box.Text = _state.Config.Anstoss2Path ?? string.Empty;
                RefreshSaveStatus();
                Log(Loc.T(20001, _state.Project.Root));
                RefreshProfiles();
                Options.Load();
                WriteResultBox.Text = string.Empty;
                UpdateStepState();
            }
            catch (Exception ex)
            {
                Log(Loc.T(20002, ex.Message));
            }
        }

        private void SyncConfig()
        {
            if (_state.Project == null) return;
            _state.Config.FmSavePath = SaveBox.Text;
            _state.Config.Anstoss2Path = A2Box.Text;
            _state.SaveConfig();
        }

        // ---- the project list ---------------------------------------------------------------

        private bool _fillingProjects;

        /// <summary>Lists the projects of the projects folder (and the open one when it lives elsewhere) and selects the open one.</summary>
        private void FillProjects()
        {
            _fillingProjects = true;
            var items = ProjectCatalog.List();
            var current = _state.Project == null ? null : _state.Project.Root;
            ProjectEntry selected = items.FirstOrDefault(p => ProjectCatalog.SamePath(p.Path, current));
            if (selected == null && current != null)
            {
                selected = new ProjectEntry { Name = current, Path = current };   // opened from another folder: shown with its path
                items.Add(selected);
            }
            ProjectCombo.ItemsSource = items;
            ProjectCombo.SelectedItem = selected;
            ProjectPathText.Text = current ?? string.Empty;
            _fillingProjects = false;
        }

        private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ProjectCombo.SelectedItem as ProjectEntry;
            if (_fillingProjects || item == null || ProjectCatalog.SamePath(item.Path, _state.Project == null ? null : _state.Project.Root)) return;
            if (!Tabs.IsEnabled) { FillProjects(); return; }   // busy
            OpenProject(item.Path);
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            if (!Tabs.IsEnabled) return;
            var suggestion = ProjectCatalog.SuggestName(KnownFmVersion > 0 ? "FM" + KnownFmVersion : "FM", ProjectCatalog.List().Select(p => p.Name));
            var name = InputDialog.Ask(this, Loc.T(28044), Loc.T(28045), suggestion);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (!ProjectCatalog.IsValidName(name) || ProjectCatalog.NameTaken(name)) { Warn(Loc.T(28048)); return; }
            try
            {
                var path = ProjectCatalog.Create(name);
                OpenProject(path);
                Log(Loc.T(28050, name));
            }
            catch (Exception ex) { Log(Loc.T(20002, ex.Message)); }
        }

        private void RenameProject_Click(object sender, RoutedEventArgs e)
        {
            if (!Tabs.IsEnabled || _state.Project == null) return;
            var path = _state.Project.Root;
            if (!ProjectCatalog.IsManaged(path)) { Warn(Loc.T(28053)); return; }
            var oldName = Path.GetFileName(path);
            var name = InputDialog.Ask(this, Loc.T(28041), Loc.T(28046, oldName), oldName);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == oldName) return;
            name = name.Trim();
            if (!ProjectCatalog.IsValidName(name) || ProjectCatalog.NameTaken(name)) { Warn(Loc.T(28048)); return; }
            try
            {
                var target = ProjectCatalog.Rename(path, name);
                OpenProject(target);
                Log(Loc.T(28052, oldName, name));
            }
            catch (Exception ex) { Log(Loc.T(20002, ex.Message)); OpenProject(path); }
        }

        private void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (!Tabs.IsEnabled || _state.Project == null) return;
            var path = _state.Project.Root;
            if (!ProjectCatalog.IsManaged(path)) { Warn(Loc.T(28053)); return; }
            var name = Path.GetFileName(path);
            if (ThemedMessageBox.Show(this, Loc.T(28047, name), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                ProjectCatalog.Delete(path);
                Log(Loc.T(28051, name));
            }
            catch (Exception ex) { Log(Loc.T(20002, ex.Message)); return; }
            var next = ProjectCatalog.List().FirstOrDefault();
            OpenProject(next != null ? next.Path : ProjectLayout.DefaultRoot);
        }

        private void BrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var folder = PickFolder(Loc.T(20003), _state.Project == null ? string.Empty : _state.Project.Root);
            if (folder != null) OpenProject(folder);
        }

        private void BrowseSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = Loc.T(20004), Filter = Loc.T(20005) };
            var current = SaveBox.Text;
            if (Directory.Exists(current)) dialog.InitialDirectory = current;
            else if (File.Exists(current)) dialog.InitialDirectory = Path.GetDirectoryName(current);
            if (dialog.ShowDialog(this) == true) { SaveBox.Text = AppState.NormalizePath(dialog.FileName); SyncConfig(); RefreshSaveStatus(); }
        }

        private void CommunitySave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommunitySavesWindow(this);
            if (dialog.ShowDialog() != true || dialog.SelectedUrl == null) return;
            SaveBox.Text = dialog.SelectedUrl;
            SyncConfig();
            if (dialog.StartYear > 0)
            {
                // the community save asks for its own start year (an FM26 update save is dated 2025 but is meant for 2026)
                _state.Settings.StartYear = dialog.StartYear;
                _state.SaveSettings();
                Log(Loc.T(28102, dialog.StartYear));
            }
            RefreshSaveStatus();
        }

        private void BrowseA2_Click(object sender, RoutedEventArgs e)
        {
            var folder = PickFolder(Loc.T(20007), A2Box.Text);
            if (folder != null) { A2Box.Text = AppState.NormalizePath(folder); SyncConfig(); RefreshProfiles(); }
        }

        private string PickFolder(string description, string start)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = description;
                if (Directory.Exists(start)) dialog.SelectedPath = start;
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        // ---- export -------------------------------------------------------------------------

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            SyncConfig();
            if (!ValidateSave() || !ToolAvailable()) return;
            var progress = new Progress<string>(Log);
            var save = SaveBox.Text;
            var outDir = _state.Project.FmExport;
            var versions = _state.FmVersions;
            await RunBusyAsync(async token =>
            {
                if (CommunitySaves.IsUrl(save))
                {
                    // a community save: download it first (or reuse the copy on this PC while its hash is unchanged)
                    var bar = new Progress<double>(f => { Busy.IsIndeterminate = false; Busy.Value = f * 100; Busy.ToolTip = Loc.T(28105, (int)(f * 100)); });
                    string folder, name;
                    CommunitySaves.TryParseUrl(save, out folder, out name);
                    try
                    {
                        save = await CommunitySaves.EnsureLocalAsync(save, bar, code =>
                        {
                            var p = code.Split(new[] { '|' }, 2);
                            Dispatcher.Invoke(() => Log(p[0] == "download" ? Loc.T(28099, name, p[1]) : p[0] == "current" ? Loc.T(28100, name, p[1]) : Loc.T(28101, p[1])));
                        }, token);
                    }
                    catch (System.IO.InvalidDataException) { Log(Loc.T(28103)); return; }
                    finally { Dispatcher.Invoke(() => { Busy.IsIndeterminate = true; Busy.Value = 0; Busy.ToolTip = null; }); }
                }
                var info = await _exporter.ReadSaveInfoAsync(save, token);
                if (!info.Recognised)
                {
                    Log(Loc.T(20082, info.Error));
                    return;
                }
                var results = await _exporter.ExportAsync(save, outDir, FmSaveExporter.ConversionTables, progress, token);
                // Nation names: from the installed game of the same version when there is one, otherwise the built-in FM26 list.
                var namesFile = Path.Combine(outDir, "nation_names.csv");
                var install = versions.FirstOrDefault(v => v.Number == info.Version);
                if (install != null && FmSaveExporter.IsFmInstall(install.Install))
                {
                    Log(Loc.T(20076, info.Version));
                    await _exporter.ExportNationNamesAsync(install.Install, namesFile, progress, token);
                }
                else
                {
                    _exporter.WriteBuiltInNationNames(namesFile, progress);
                    if (info.Version != 26) Log(Loc.T(20077, info.Version));
                }
                var failed = results.Where(r => !r.Succeeded && Array.IndexOf(FmSaveExporter.OptionalTables, r.Table) < 0).ToList();
                if (results.Any(r => !r.Succeeded && Array.IndexOf(FmSaveExporter.OptionalTables, r.Table) >= 0)) Log(Loc.T(28080));
                if (failed.Count == 0)
                {
                    _state.Config.FmVersion = info.Version;   // the FM version of the exported data: used for the suggested profile name
                    _state.Config.FmYear = A2G.Convert.Writing.GameYear.FromFmDate(info.InGameDate);   // the game can start in the same year
                    _state.SaveConfig();
                    Dispatcher.Invoke(UpdateSuggestedProfileName);
                }
                Log(failed.Count == 0
                    ? Loc.T(20010, outDir)
                    : Loc.T(20011, string.Join(", ", failed.Select(f => f.Table))));
            });
        }

        // ---- save compatibility line --------------------------------------------------------

        private SaveInfo _saveInfo;
        private int _saveCheckId;
        private bool _saveChecking;

        private void SaveBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SyncConfig();
            RefreshSaveStatus();
        }

        /// <summary>Asks fmsave what the chosen file is and shows the game version, build and whether it is compatible.</summary>
        private async void RefreshSaveStatus()
        {
            var path = SaveBox.Text;
            var id = ++_saveCheckId;
            _saveInfo = null;
            if (CommunitySaves.IsUrl(path))
            {
                _saveChecking = false;
                ShowSaveStatus();
                return;
            }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _saveChecking = false;
                ShowSaveStatus();
                return;
            }
            _saveChecking = true;
            ShowSaveStatus();
            SaveInfo info;
            try { info = await _exporter.ReadSaveInfoAsync(path, CancellationToken.None); }
            catch (Exception ex) { info = SaveInfo.Failed(ex.Message); }
            if (id != _saveCheckId) return;   // a newer check replaced this one
            _saveChecking = false;
            _saveInfo = info;
            ShowSaveStatus();
        }

        private void ShowSaveStatus()
        {
            string text;
            string brush = "MutedBrush";
            var path = SaveBox.Text;
            if (CommunitySaves.IsUrl(path)) text = Loc.T(28098);
            else if (_saveChecking) text = Loc.T(20079);
            else if (_saveInfo == null) text = Loc.T(20078);
            else if (!_saveInfo.Recognised) { text = Loc.T(20082, _saveInfo.Error); brush = "DangerBrush"; }
            else if (_saveInfo.Supported)
            {
                text = Loc.T(20080, _saveInfo.Version, _saveInfo.ShortBuild, _saveInfo.InGameDate);
                brush = "SuccessBrush";
            }
            else if (_saveInfo.Version < AppState.FirstSupportedFm) { text = Loc.T(20085, _saveInfo.Version); brush = "DangerBrush"; }
            else text = Loc.T(20081, _saveInfo.Version);
            SaveStatusText.Text = text;
            SaveStatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
            UpdateSuggestedProfileName();
            RefreshYearControls();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        private bool ValidateSave()
        {
            if (!CommunitySaves.IsUrl(SaveBox.Text) && !File.Exists(SaveBox.Text)) { Warn(Loc.T(20012)); return false; }
            return true;
        }

        private bool ToolAvailable()
        {
            if (FmSaveExporter.IsAvailable) return true;
            Warn(Loc.T(20013));
            return false;
        }

        // ---- convert ------------------------------------------------------------------------

        private async void BuildPlan_Click(object sender, RoutedEventArgs e)
        {
            SyncConfig();
            if (!_state.HasExport) { Warn(Loc.T(20014)); return; }
            await BuildPlanCoreAsync();
        }

        private Task BuildPlanCoreAsync()
        {
            return RunBusyAsync(async token =>
            {
                Log(Loc.T(20015));
                await _state.BuildPlanAsync();
                SummaryBox.Text = PlanSummary.Build(_state.Plan);
                Inspector.Reload();
                TeamsEditor.Reload();
                CrestsEditor.Reload();
                Options.RefreshCounts();
                Log(Loc.T(20016, _state.Fm.Players.Count, _state.Fm.Clubs.Count));
                if (_state.SavedEdits > 0) Log(Loc.T(20017, _state.ReplayedEdits, _state.SavedEdits));
            });
        }

        private async void Write_Click(object sender, RoutedEventArgs e)
        {
            SyncConfig();
            if (_state.Plan == null) { Warn(Loc.T(20018)); return; }
            if (!Directory.Exists(Path.Combine(A2Box.Text, "DATA.DEF"))) { Warn(Loc.T(20019)); return; }
            _state.SaveSettings();
            await RunBusyAsync(async token =>
            {
                Log(Loc.T(20020));
                var outcome = await _state.WriteAsync();
                var text = PlanSummary.Build(_state.Plan) + Environment.NewLine +
                           Loc.T(20021, string.Join(", ", outcome.Report.FilesWritten.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))) + Environment.NewLine;
                foreach (var w in outcome.Report.Warnings.Distinct()) text += Loc.T(20022, w) + Environment.NewLine;
                foreach (var n in outcome.Report.Notes) text += Loc.T(20023, n) + Environment.NewLine;
                text += outcome.Problems.Count == 0
                    ? Loc.T(20024) + Environment.NewLine
                    : Loc.T(20025, outcome.Problems.Count) + Environment.NewLine + string.Join(Environment.NewLine, outcome.Problems.Take(40)) + Environment.NewLine;
                WriteResultBox.Text = text;
                Log(outcome.Problems.Count == 0 ? Loc.T(20026, _state.Project.Anstoss2) : Loc.T(20027, outcome.Problems.Count));
                RefreshProfiles();
            });
        }

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            if (_state.Project == null) return;
            Directory.CreateDirectory(_state.Project.Anstoss2);
            Process.Start("explorer.exe", _state.Project.Anstoss2);
        }

        // ---- install & profiles -------------------------------------------------------------

        private ProfileManager OpenManager()
        {
            SyncConfig();
            if (!Directory.Exists(Path.Combine(A2Box.Text, "DATA.DEF")))
            {
                Warn(Loc.T(20019));
                return null;
            }
            return new ProfileManager(A2Box.Text);
        }

        private string _autoProfileName = "FM";

        /// <summary>The FM version of the data: the exported save, else the save file that is chosen now.</summary>
        private int KnownFmVersion
        {
            get
            {
                if (_state != null && _state.Config != null && _state.Config.FmVersion > 0 && _state.HasExport) return _state.Config.FmVersion;
                return _saveInfo != null && _saveInfo.Recognised ? _saveInfo.Version : 0;
            }
        }

        /// <summary>Suggests a profile name from the FM version ("FM26", "FM26-2" when taken); a name the user typed is left alone.</summary>
        private void UpdateSuggestedProfileName()
        {
            var existing = new HashSet<string>(((ProfileList.ItemsSource as IEnumerable<ProfileRow>) ?? new ProfileRow[0]).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var version = KnownFmVersion;
            // the data set (and the patch file) is named after the project; the generic "Default" project falls back to the FM version
            var projectName = _state != null && _state.Project != null ? Path.GetFileName(_state.Project.Root) : null;
            var baseName = !string.IsNullOrEmpty(projectName) && !string.Equals(projectName, ProjectCatalog.DefaultName, StringComparison.OrdinalIgnoreCase) && ProjectCatalog.IsValidName(projectName)
                ? projectName
                : version > 0 ? "FM" + version : "FM";
            var name = baseName;
            // the plain name, also when a data set of that name exists: installing then replaces it (after asking)
            var current = NewProfileBox.Text.Trim();
            if (current.Length == 0 || current == _autoProfileName) NewProfileBox.Text = name;
            _autoProfileName = name;
        }

        private void RefreshProfiles()
        {
            ProfileList.ItemsSource = null;
            if (string.IsNullOrEmpty(A2Box.Text) || !Directory.Exists(Path.Combine(A2Box.Text, "DATA.DEF")))
            {
                ProfileStatusText.Text = Loc.T(20028);
                UpdateSuggestedProfileName();
                return;
            }
            try
            {
                var pm = new ProfileManager(A2Box.Text);
                var active = pm.ActiveProfile();
                var status = pm.GetStatus();
                ProfileStatusText.Text = active == null
                    ? Loc.T(20029)
                    : Loc.T(20030, active) + (status.Dirty ? "  " + Loc.T(20031, status.Changed.Count) : "");
                ProfileList.ItemsSource = pm.List().Select(p => new ProfileRow
                {
                    Name = p.Name,
                    Live = string.Equals(p.Name, active, StringComparison.OrdinalIgnoreCase) ? Loc.T(20032) : string.Empty,
                    Created = (p.CreatedUtc ?? string.Empty).Replace('T', ' ').Split('.')[0],
                    FileCount = p.Files == null ? 0 : p.Files.Count,
                    Source = SourceLabel(p.Source)
                }).ToList();
            }
            catch (Exception ex)
            {
                ProfileStatusText.Text = Loc.T(20033, ex.Message);
            }
            UpdateSuggestedProfileName();
        }

        /// <summary>Where a data set came from: "Project FM26" for a project's Anstoss2 folder, else the text the profile manager stored.</summary>
        private static string SourceLabel(string source)
        {
            try
            {
                if (!string.IsNullOrEmpty(source) && Path.GetFileName(source.TrimEnd('\\', '/')).Equals("Anstoss2", StringComparison.OrdinalIgnoreCase))
                {
                    var project = Path.GetFileName(Path.GetDirectoryName(source.TrimEnd('\\', '/')));
                    if (!string.IsNullOrEmpty(project)) return Loc.T(28049, project);
                }
            }
            catch (Exception) { }
            return source;
        }

        private void RefreshProfiles_Click(object sender, RoutedEventArgs e)
        {
            RefreshProfiles();
        }

        private DirtyPolicy? AskDirtyPolicy(ProfileManager pm)
        {
            var status = pm.GetStatus();
            if (!status.Dirty) return DirtyPolicy.Abort;
            var answer = ThemedMessageBox.Show(this,
                Loc.T(20034, status.Active, string.Join("\n  ", status.Changed.Take(8)) + (status.Changed.Count > 8 ? "\n  ..." : "")),
                Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) return DirtyPolicy.SaveToActive;
            if (answer == MessageBoxResult.No) return DirtyPolicy.Discard;
            return null;
        }

        private async void InstallProfile_Click(object sender, RoutedEventArgs e)
        {
            var pm = OpenManager();
            if (pm == null) return;
            if (!Directory.Exists(_state.Project.DataDef) || !Directory.GetFiles(_state.Project.DataDef).Any())
            {
                Warn(Loc.T(20035));
                return;
            }
            var name = NewProfileBox.Text.Trim();
            // a data set of the same name is replaced (never the Original one), but only after the user agreed
            var exists = pm.List().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exists && string.Equals(name, ProfileManager.OriginalName, StringComparison.OrdinalIgnoreCase)) { Warn(Loc.T(28061)); return; }
            if (exists && ThemedMessageBox.Show(this, Loc.T(28060, name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var policy = AskDirtyPolicy(pm);
            if (policy == null) return;
            var filter = BuildFilter();
            var source = _state.Project.Anstoss2;
            await RunBusyAsync(async token =>
            {
                await Task.Run(() => pm.Install(source, name, policy.Value, filter, exists));
                Log(Loc.T(20036, name));
            });
            RefreshProfiles();
        }

        private async void ActivateProfile_Click(object sender, RoutedEventArgs e)
        {
            var row = ProfileList.SelectedItem as ProfileRow;
            if (row == null) { Warn(Loc.T(20037)); return; }
            var pm = OpenManager();
            if (pm == null) return;
            var policy = AskDirtyPolicy(pm);
            if (policy == null) return;
            await RunBusyAsync(async token =>
            {
                await Task.Run(() => pm.Activate(row.Name, policy.Value));
                Log(Loc.T(20038, row.Name));
            });
            RefreshProfiles();
        }

        /// <summary>One .exe with this project's data set: a copy of this program with the data attached (see PatchPackage); it opens as the patcher.</summary>
        private async void CreatePatch_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(_state.Project.DataDef) || !Directory.GetFiles(_state.Project.DataDef).Any())
            {
                Warn(Loc.T(20035));
                return;
            }
            // the same data set and name as "Install": the generated files of this project under the name in the box
            var name = NewProfileBox.Text.Trim();
            if (name.Length == 0) { Warn(Loc.T(28029)); return; }
            // the file is named A2G-Patch_<name>.exe: shown with the description so the user sees what will be created
            var fileName = PatchFileName(name);
            var description = InputDialog.Ask(this, Loc.T(28028), Loc.T(28020), string.Empty, Loc.T(28030, fileName));
            if (description == null) return;
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = Loc.T(28025), Filter = Loc.T(28021), FileName = fileName, InitialDirectory = _state.Project.Root, DefaultExt = ".exe" };
            if (dialog.ShowDialog(this) != true) return;
            var filter = BuildFilter();
            var source = _state.Project.Anstoss2;
            var program = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            var output = dialog.FileName;
            await RunBusyAsync(async token =>
            {
                try
                {
                    await Task.Run(() => A2G.Convert.Patching.PatchPackage.Build(program, source, output, name.Trim(), description.Trim(), filter));
                    var size = new FileInfo(output).Length / 1048576.0;
                    Log(Loc.T(28022, output, size.ToString("0.0")).Replace("\n\n", " ").Replace("\n", " "));
                    ThemedMessageBox.Show(this, Loc.T(28022, output, size.ToString("0.0")), Title, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { Log(Loc.T(28023, ex.Message)); }
            });
        }

        /// <summary>The file name of a patch: "A2G-Patch_" and the profile name (characters Windows does not allow in file names become "_").</summary>
        internal static string PatchFileName(string profileName)
        {
            var clean = new string((profileName ?? string.Empty).Trim().Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
            return "A2G-Patch_" + clean + ".exe";
        }

        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            var row = ProfileList.SelectedItem as ProfileRow;
            if (row == null) { Warn(Loc.T(20037)); return; }
            var pm = OpenManager();
            if (pm == null) return;
            var name = InputDialog.Ask(this, Loc.T(20039), Loc.T(20040, row.Name), row.Name);
            if (string.IsNullOrWhiteSpace(name) || name == row.Name) return;
            try { pm.Rename(row.Name, name.Trim()); Log(Loc.T(20041, row.Name, name.Trim())); }
            catch (Exception ex) { Log(Loc.T(20002, ex.Message)); }
            RefreshProfiles();
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var row = ProfileList.SelectedItem as ProfileRow;
            if (row == null) { Warn(Loc.T(20037)); return; }
            var pm = OpenManager();
            if (pm == null) return;
            if (ThemedMessageBox.Show(this, Loc.T(20042, row.Name), Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { pm.Delete(row.Name); Log(Loc.T(20043, row.Name)); }
            catch (Exception ex) { Log(Loc.T(20002, ex.Message)); }
            RefreshProfiles();
        }

        /// <summary>The file filter of a partial install, or null for a full install.</summary>
        private Func<string, bool> BuildFilter()
        {
            if (PartialCheck.IsChecked != true) return null;
            var parts = new A2G.Convert.Writing.InstallParts
            {
                NamePools = GrpNames.IsChecked == true,
                Avereine = GrpAvereine.IsChecked == true,
                StartYear = GrpYear.IsChecked == true
            };
            if (GrpDeu.IsChecked == true) parts.Countries.Add("DEU");
            if (GrpEng.IsChecked == true) parts.Countries.Add("ENG");
            if (GrpFra.IsChecked == true) parts.Countries.Add("FRA");
            if (GrpIta.IsChecked == true) parts.Countries.Add("ITA");
            if (GrpEsp.IsChecked == true) parts.Countries.Add("ESP");
            return A2G.Convert.Writing.PartialInstall.Filter(parts);
        }
        // ---- plumbing -----------------------------------------------------------------------

        private async Task RunBusyAsync(Func<CancellationToken, Task> work)
        {
            SetBusy(true);
            _cts = new CancellationTokenSource();
            try
            {
                await work(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log(Loc.T(20044));
            }
            catch (Exception ex)
            {
                Log(Loc.T(20002, ex.Message));
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            Busy.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            Tabs.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            if (!busy) UpdateStepState();
        }

        /// <summary>
        /// Keeps the numbered steps linear: step 3 needs an export, steps 4 and 5 need a plan, and the write
        /// button (step 6) needs a plan too. Also tells step 6 how many manual edits it will include.
        /// </summary>
        // ---- the year the game starts in -----------------------------------------------------

        private bool _yearLoading;

        /// <summary>Shows the setting: the check box says "like the FM save (year)"; unchecked, the stepper chooses the year.</summary>
        private void RefreshYearControls()
        {
            if (_state == null || _state.Project == null) return;
            _yearLoading = true;
            // the year of the exported save; a project exported before this setting existed learns it from the save file that is chosen
            if (_state.Config.FmYear == 0 && _saveInfo != null && _saveInfo.Recognised)
            {
                var known = A2G.Convert.Writing.GameYear.FromFmDate(_saveInfo.InGameDate);
                if (known > 0) { _state.Config.FmYear = known; _state.SaveConfig(); }
            }
            var fmYear = _state.Config.FmYear;
            var auto = _state.Settings.StartYear == 0;
            YearAuto.Content = Loc.T(22201, fmYear > 0 ? fmYear.ToString() : Loc.T(22203));
            YearAuto.IsChecked = auto;
            YearBox.IsEnabled = !auto;
            YearBox.Value = _state.Settings.StartYear > 0 ? _state.Settings.StartYear : (fmYear >= 1998 && fmYear <= 2098 ? fmYear : 1998);
            _yearLoading = false;
        }

        private void YearAuto_Click(object sender, RoutedEventArgs e)
        {
            if (_yearLoading || _state == null) return;
            _state.Settings.StartYear = YearAuto.IsChecked == true ? 0 : YearBox.IntValue;
            _state.SaveSettings();
            RefreshYearControls();
        }

        private void YearBox_Changed()
        {
            if (_yearLoading || _state == null || _state.Settings.StartYear == 0) return;
            _state.Settings.StartYear = YearBox.IntValue;
            _state.SaveSettings();
        }

        private void UpdateStepState()
        {
        RefreshYearControls();
            if (_state == null || _state.Project == null) return;
            var hasPlan = _state.Plan != null;
            PlanTabItem.IsEnabled = _state.HasExport;
            PlanTabItem.ToolTip = _state.HasExport ? null : Loc.T(20073);
            TeamsTabItem.IsEnabled = hasPlan;
            TeamsTabItem.ToolTip = hasPlan ? null : Loc.T(20072);
            PlayersTab.IsEnabled = hasPlan;
            PlayersTab.ToolTip = hasPlan ? null : Loc.T(20072);
            CrestsTabItem.IsEnabled = hasPlan;
            CrestsTabItem.ToolTip = hasPlan ? null : Loc.T(20072);
            WriteButton.IsEnabled = hasPlan;
            var edits = hasPlan && _state.Editor != null ? _state.Editor.Log.Ops.Count : 0;
            WriteInfoText.Text = hasPlan ? Loc.T(20071, edits) : Loc.T(20070);
            var selected = Tabs.SelectedItem as TabItem;
            if (selected != null && !selected.IsEnabled)
                Tabs.SelectedIndex = !PlanTabItem.IsEnabled ? 0 : 2;
        }

        private void Warn(string message)
        {
            ThemedMessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Log(string message)
        {
            LogBox.AppendText(message + Environment.NewLine);
            LogBox.ScrollToEnd();
        }
    }
}

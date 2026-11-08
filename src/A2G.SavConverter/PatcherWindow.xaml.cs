using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Patching;
using A2G.Convert.Profiles;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// The patcher: what the program shows when a patch is attached to it. The user picks the Anstoss 2 folder (found automatically like in
    /// the converter), installs the patch as a new data set (profile) and can switch back to the Original or any other data set.
    /// </summary>
    public partial class PatcherWindow : Window
    {
        private sealed class ProfileRow
        {
            public string Name { get; set; }
            public string Live { get; set; }
            public string Created { get; set; }
            public int FileCount { get; set; }
        }

        private static readonly string PrefPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", "patcher-install.txt");

        private readonly PatchPackage _patch;
        private string _autoName = string.Empty;
        private bool _busy;

        private readonly string _folderFromCommandLine;

        public PatcherWindow(PatchPackage patch, string folder = null)
        {
            _patch = patch;
            _folderFromCommandLine = folder;
            InitializeComponent();
            Title = Loc.T(28000, patch.Info.Name);
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            PatchTitle.Text = patch.Info.Name;
            PatchDescription.Text = patch.Info.Description ?? string.Empty;
            PatchDescription.Visibility = string.IsNullOrWhiteSpace(patch.Info.Description) ? Visibility.Collapsed : Visibility.Visible;
            var made = (patch.Info.CreatedUtc ?? string.Empty).Replace('T', ' ').Split('.')[0];
            PatchDetails.Text = (patch.Info.Partial ? Loc.T(28009, patch.Info.Files.Count) : Loc.T(28010, patch.Info.Files.Count)) + "  " + Loc.T(28011, made);
            Loaded += (s, e) => Start();
        }

        /// <summary>The folder used last, else the one the converter's detection finds; empty when there is none.</summary>
        public static string DefaultFolder()
        {
            var folder = string.Empty;
            try { if (File.Exists(PrefPath)) folder = File.ReadAllText(PrefPath).Trim(); } catch (Exception) { }
            if (!AppState.IsA2Install(folder)) folder = AppState.DetectA2Install();   // same detection as the converter
            return AppState.NormalizePath(folder) ?? string.Empty;
        }

        private void Start()
        {
            var given = !string.IsNullOrWhiteSpace(_folderFromCommandLine);
            InstallBox.Text = given ? AppState.NormalizePath(_folderFromCommandLine) : DefaultFolder();
            RefreshProfiles();
            // nothing found: the user has to choose the folder (say so right away)
            if (!given && string.IsNullOrWhiteSpace(InstallBox.Text))
                ThemedMessageBox.Show(this, Loc.T(28027), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool ValidInstall { get { return MissingFolders(InstallBox.Text.Trim()).Count == 0 && AppState.IsA2Install(InstallBox.Text.Trim()); } }

        /// <summary>The data folders (DATA.DEF, DATA.VER) the chosen folder lacks; an Anstoss 2 folder has both next to anstoss2.exe.</summary>
        private static List<string> MissingFolders(string folder)
        {
            var missing = new List<string>();
            foreach (var name in new[] { "DATA.DEF", "DATA.VER" })
            {
                bool exists;
                try { exists = !string.IsNullOrEmpty(folder) && Directory.Exists(Path.Combine(folder, name)); } catch (Exception) { exists = false; }
                if (!exists) missing.Add(name);
            }
            return missing;
        }

        private void InstallBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshProfiles();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.T(28003), ShowNewFolderButton = false })
            {
                if (Directory.Exists(InstallBox.Text)) dialog.SelectedPath = InstallBox.Text;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                InstallBox.Text = AppState.NormalizePath(dialog.SelectedPath);
            }
            // a folder without the data folders is most likely the wrong one: tell the user to double check
            var missing = MissingFolders(InstallBox.Text.Trim());
            if (missing.Count > 0)
                ThemedMessageBox.Show(this, Loc.T(28026, string.Join(" + ", missing)), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private ProfileManager Manager()
        {
            return ValidInstall ? new ProfileManager(InstallBox.Text.Trim()) : null;
        }

        private void RefreshProfiles()
        {
            ProfileList.ItemsSource = null;
            var text = InstallBox.Text.Trim();
            var valid = text.Length > 0 && ValidInstall;
            InstallButton.IsEnabled = valid && !_busy;
            ActivateButton.IsEnabled = valid && !_busy;
            StatusText.Text = text.Length == 0 ? Loc.T(28015) : valid ? string.Empty : Loc.T(28012);
            if (!valid) { ProfileStatusText.Text = string.Empty; return; }
            try
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)); File.WriteAllText(PrefPath, text); } catch (Exception) { }
                var pm = new ProfileManager(text);
                var active = pm.ActiveProfile();
                var status = pm.GetStatus();
                var list = pm.List();
                ProfileStatusText.Text = active == null ? Loc.T(20029) : Loc.T(20030, active) + (status.Dirty ? "  " + Loc.T(20031, status.Changed.Count) : string.Empty);
                ProfileList.ItemsSource = list.Select(p => new ProfileRow
                {
                    Name = p.Name,
                    Live = string.Equals(p.Name, active, StringComparison.OrdinalIgnoreCase) ? Loc.T(20032) : string.Empty,
                    Created = (p.CreatedUtc ?? string.Empty).Replace('T', ' ').Split('.')[0],
                    FileCount = p.Files == null ? 0 : p.Files.Count
                }).ToList();
                var current = ProfileNameBox.Text.Trim();
                // the patch's own name, even when a data set of that name exists: installing then replaces it (after asking)
                var suggestion = PatchInstaller.SuggestProfileName(_patch.Info.Name, new string[0]);
                if (current.Length == 0 || current == _autoName) ProfileNameBox.Text = suggestion;
                _autoName = suggestion;
            }
            catch (Exception ex) { ProfileStatusText.Text = Loc.T(20033, ex.Message); }
        }

        private DirtyPolicy? AskDirtyPolicy(ProfileManager pm)
        {
            var status = pm.GetStatus();
            if (!status.Dirty) return DirtyPolicy.Abort;
            var answer = ThemedMessageBox.Show(this,
                Loc.T(20034, status.Active, string.Join("\n  ", status.Changed.Take(8)) + (status.Changed.Count > 8 ? "\n  ..." : string.Empty)),
                Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) return DirtyPolicy.SaveToActive;
            if (answer == MessageBoxResult.No) return DirtyPolicy.Discard;
            return null;
        }

        private async Task Run(Action work, string done)
        {
            _busy = true;
            InstallButton.IsEnabled = false; ActivateButton.IsEnabled = false;
            ResultText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            ResultText.Text = Loc.T(28014);
            try
            {
                await Task.Run(work);
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                ResultText.Text = done;
            }
            catch (InvalidDataException ex)
            {
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                ResultText.Text = Loc.T(28024, ex.Message);
            }
            catch (Exception ex)
            {
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                ResultText.Text = ex.Message;
            }
            _busy = false;
            RefreshProfiles();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var pm = Manager();
            if (pm == null) return;
            var name = ProfileNameBox.Text.Trim();
            // a data set of the same name is replaced (never the Original one), but only after the user agreed
            var exists = pm.List().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exists && string.Equals(name, ProfileManager.OriginalName, StringComparison.OrdinalIgnoreCase))
            {
                ResultText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                ResultText.Text = Loc.T(28061);
                return;
            }
            if (exists && ThemedMessageBox.Show(this, Loc.T(28060, name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var policy = AskDirtyPolicy(pm);
            if (policy == null) return;
            var folder = InstallBox.Text.Trim();
            await Run(() => PatchInstaller.Install(_patch, folder, name, policy.Value, exists), Loc.T(28013, name));
        }

        private async void Activate_Click(object sender, RoutedEventArgs e)
        {
            var row = ProfileList.SelectedItem as ProfileRow;
            if (row == null) { ResultText.Text = Loc.T(20037); return; }
            var pm = Manager();
            if (pm == null) return;
            var policy = AskDirtyPolicy(pm);
            if (policy == null) return;
            await Run(() => pm.Activate(row.Name, policy.Value), Loc.T(20038, row.Name));
        }
    }
}

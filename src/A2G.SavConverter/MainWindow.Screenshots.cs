using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using A2G.Convert.Patching;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// A tour that renders the program's windows into PNG files for the user guide (docs\User\images). It is started with
    /// <c>A2G-SAV-Converter.exe &lt;project folder&gt; --screenshots=&lt;output folder&gt;</c>, draws every window off-screen (no screen capture, no
    /// clicking) and closes the program. Language and theme are set to English / light for the pictures and restored afterwards.
    /// It only looks at the project: nothing is written to it.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Draws the content area of a window (without the title bar) on the window's background colour.</summary>
        private static void SavePng(Window window, string path)
        {
            var content = (FrameworkElement)window.Content;
            content.UpdateLayout();
            var w = Math.Max(1, (int)Math.Ceiling(content.ActualWidth));
            var h = Math.Max(1, (int)Math.Ceiling(content.ActualHeight));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private async Task Settle(int milliseconds = 500)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(milliseconds);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private async Task Shoot(Window window, string folder, string name, double width, double height)
        {
            window.Width = width;
            window.Height = height;
            await Settle();
            SavePng(window, Path.Combine(folder, name + ".png"));
        }

        /// <summary>Shows a secondary window off-screen, draws it and closes it.</summary>
        private async Task ShootWindow(Window window, string folder, string name, double width, double height, Action prepare = null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = 0;
            window.ShowInTaskbar = false;
            window.Show();
            window.Width = width; window.Height = height;
            await Settle();
            if (prepare != null) { prepare(); await Settle(); }
            SavePng(window, Path.Combine(folder, name + ".png"));
            window.Close();
        }

        private async Task TakeScreenshotsAsync(string folder)
        {
            Directory.CreateDirectory(folder);
            var prefs = new[] { "theme.txt", "language.txt" }.Select(f => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", f)).ToList();
            var saved = prefs.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllText(p) : null);
            try
            {
                Loc.SetLanguage("en");
                ThemeManager.SetMode(ThemeMode.Light);
                if (_state.Plan == null && _state.HasExport) await BuildPlanCoreAsync();
                var editor = _state.Editor;
                Width = 1400; Height = 900;
                WindowState = WindowState.Normal;
                await Settle(800);

                // the tabs
                Tabs.SelectedIndex = 0;
                await Shoot(this, folder, "01-project-and-export", 1400, 640);

                Tabs.SelectedIndex = 1;
                Options.RulesTabs.SelectedIndex = 0;
                await Shoot(this, folder, "02-options-abilities", 1400, 1700);
                Options.RulesTabs.SelectedIndex = 1;
                await Shoot(this, folder, "02-options-traits", 1400, 1700);
                Options.RulesTabs.SelectedIndex = 0;

                Tabs.SelectedIndex = 2;
                await Shoot(this, folder, "03-plan", 1400, 900);

                Tabs.SelectedIndex = 3;
                TeamsEditor.CountryBox.SelectedIndex = 0;
                await Shoot(this, folder, "04-teams", 1400, 900);

                if (editor != null)
                {
                    var group = editor.Groups("DEU")[0];
                    var team = group.Teams.First();
                    Func<TeamInfoData> build = null;
                    build = () => new TeamInfoData
                    {
                        Club = team.Club, Squad = team.Squad.ToList(), GroupName = group.Name, MinSquad = group.MinSquad, MaxSquad = group.MaxSquad,
                        Quota = _state.Settings.SquadQuota, Problems = editor.Check(group, team), Team = team, Editor = editor, Edited = () => { }, Refresh = build
                    };
                    var info = new TeamInfoWindow(this, _state, build());
                    await ShootWindow(info, folder, "05-team-information", 1560, 1100);
                    var reserve = new TeamInfoWindow(this, _state, build());
                    await ShootWindow(reserve, folder, "05-team-reserves", 1560, 1100, () => reserve.ReserveToggle.IsChecked = true);

                    var picker = new PlayerPickerWindow(this, _state.Fm, team.Club.Name, p => null, (o, p) => { }, p => Tuple.Create(string.Empty, false));
                    await ShootWindow(picker, folder, "05-find-players", 1000, 800);

                    var seasons = new SeasonResultsWindow(this, _state, group);
                    await ShootWindow(seasons, folder, "05-season-results", 640, 760);
                    var avereine = new SeasonResultsWindow(this, _state, editor.Groups("AVE")[0]);
                    await ShootWindow(avereine, folder, "05-season-results-avereine", 820, 760);
                }

                TeamsEditor.CountryBox.SelectedIndex = TeamsEditor.CountryBox.Items.Count - 1;
                Tabs.SelectedIndex = 3;
                await Shoot(this, folder, "04-teams-avereine", 1400, 900);
                TeamsEditor.CountryBox.SelectedIndex = 0;

                Tabs.SelectedIndex = 4;
                if (_state.Plan != null)
                {
                    var star = _state.Plan.Countries.SelectMany(c => c.AllTeams).SelectMany(t => t.Squad).OrderByDescending(p => p.CurrentAbility).FirstOrDefault();
                    if (star != null) Inspector.SearchBox.Text = star.DisplayName;
                    await Settle();
                    if (Inspector.PlayerList.Items.Count > 0) Inspector.PlayerList.SelectedIndex = 0;
                }
                await Shoot(this, folder, "06-players", 1500, 1000);

                Tabs.SelectedIndex = 5;
                CrestsEditor.NationBox.SelectedIndex = 0;
                await Settle();
                if (CrestsEditor.ClubList.Items.Count > 0) CrestsEditor.ClubList.SelectedIndex = 0;
                await Shoot(this, folder, "07-crests", 1400, 900);
                if (editor != null)
                {
                    var club = editor.Groups("DEU")[0].Teams.First().Club;
                    var start = A2G.Convert.Writing.CrestWriter.Render(club.Uid, club.Name);
                    var painter = new CrestPainterWindow(this, "Paint crest: " + club.Name, start);
                    await ShootWindow(painter, folder, "07-crest-painter", 900, 780);
                }

                Tabs.SelectedIndex = 6;
                await Shoot(this, folder, "08-write-and-install", 1400, 900);

                // the patcher: a patch made of the project's written files, opened like a user would
                if (Directory.Exists(_state.Project.DataDef))
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    var tempPatch = Path.Combine(Path.GetTempPath(), "a2g-screenshot-patch.exe");
                    try
                    {
                        PatchPackage.Build(exe, _state.Project.Anstoss2, tempPatch, Path.GetFileName(_state.Project.Root), "A complete Anstoss 2 data set made from a Football Manager save.", null);
                        var patch = PatchPackage.TryOpen(tempPatch);
                        if (patch != null)
                        {
                            var patcher = new PatcherWindow(patch);
                            await ShootWindow(patcher, folder, "10-patcher", 900, 700);
                        }
                    }
                    finally { try { File.Delete(tempPatch); } catch (Exception) { } }
                }

                // the same screen in the dark theme
                ThemeManager.SetMode(ThemeMode.Dark);
                Tabs.SelectedIndex = 3;
                await Shoot(this, folder, "04-teams-dark", 1400, 900);
                Log("Screenshots written to " + folder);
            }
            finally
            {
                foreach (var kv in saved)
                {
                    try { if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); } else File.WriteAllText(kv.Key, kv.Value); } catch (Exception) { }
                }
                Application.Current.Shutdown();
            }
        }
    }
}

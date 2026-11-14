using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Writing;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;
using Microsoft.Win32;

namespace A2G.SavConverter
{
    /// <summary>
    /// Step 6: browses the clubs of the FM data by nation and manages their crests: import any image, paint a 32x32 crest, or remove a
    /// crest again. The crests are stored in the project's Crests folder as "[club id].bmp", which the export uses instead of the generated crests.
    /// </summary>
    public partial class CrestsTab : UserControl
    {
        private sealed class Row
        {
            public FmClub Club;
            public string Text;
            public override string ToString() { return Text; }
        }

        /// <summary>A view of the clubs: the teams of one playable league (or the spare teams) of the plan, or all FM clubs of a nation.</summary>
        private sealed class NationItem
        {
            public FmNation Nation;               // set: all clubs of this nation
            public A2G.Convert.Plan.TeamGroup Group;   // set: the teams of this group of the plan
            public string Label;
            public override string ToString() { return Label; }
        }

        private AppState _state;
        private HashSet<string> _planClubs = new HashSet<string>();
        private bool _loading = true;

        public CrestsTab()
        {
            InitializeComponent();
            ShowCrest();
        }

        public void Attach(AppState state) { _state = state; }

        /// <summary>Call after the plan was (re)built or the language changed.</summary>
        public void Reload()
        {
            if (_state == null || _state.Fm == null || _state.Project == null) return;
            _loading = true;
            var fm = _state.Fm;
            _planClubs = new HashSet<string>(_state.Plan == null ? new string[0]
                : _state.Plan.Countries.SelectMany(c => c.AllTeams).Select(t => t.Club.Uid));
            var keep = (NationBox.SelectedItem as NationItem);
            var items = new List<NationItem>();
            // first the teams that will use a crest in the game: the playable leagues and the spare teams of the five countries
            if (_state.Editor != null)
                foreach (var key in new[] { "DEU", "ENG", "FRA", "ITA", "ESP" })
                    foreach (var group in _state.Editor.Groups(key))
                        items.Add(new NationItem { Group = group, Label = CountryName(key) + " · " + group.Name + " (" + group.Teams.Count + ")" });
            // then every FM club by nation: crests are stored by FM club id, so they can be prepared for clubs added to the plan later
            var withClubs = new HashSet<int>(fm.Clubs.Values.Where(c => c.IsMainMensClub).Select(c => c.NationId));
            items.AddRange(fm.Nations.Where(n => withClubs.Contains(n.SaveId)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(n => new NationItem { Nation = n, Label = Loc.T(22180, n.Name) }));
            NationBox.ItemsSource = items;
            NationItem pick = keep == null ? null : items.FirstOrDefault(n => n.Label == keep.Label);
            NationBox.SelectedItem = pick ?? (items.Count > 0 ? items[0] : null);
            _loading = false;
            RefreshClubs();
            ShowCrest();
        }

        private static string CountryName(string key)
        {
            switch (key) { case "DEU": return "Germany"; case "ENG": return "England"; case "FRA": return "France"; case "ITA": return "Italy"; default: return "Spain"; }
        }

        private FmClub Selected { get { var row = ClubList.SelectedItem as Row; return row == null ? null : row.Club; } }
        private bool Ready { get { return _state != null && _state.Fm != null && _state.Project != null; } }

        // ---- list ---------------------------------------------------------------------------

        private void NationBox_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) RefreshClubs(); }
        private void SearchBox_Changed(object sender, TextChangedEventArgs e) { if (!_loading) RefreshClubs(); }

        private void RefreshClubs()
        {
            var item = NationBox.SelectedItem as NationItem;
            if (item == null || !Ready) { ClubList.ItemsSource = null; return; }
            var query = (SearchBox.Text ?? string.Empty).Trim();
            var library = new CrestLibrary(_state.Project.Crests);
            var selectedUid = Selected == null ? null : Selected.Uid;
            IEnumerable<FmClub> source;
            if (item.Group != null) source = item.Group.Teams.Select(t => t.Club).ToList();
            else
            {
                var nation = item.Nation;
                source = _state.Fm.Clubs.Values.Where(c => c.IsMainMensClub && c.NationId == nation.SaveId)
                    .OrderByDescending(c => _planClubs.Contains(c.Uid)).ThenByDescending(c => c.Reputation).ToList();
            }
            var rows = source
                .Where(c => query.Length == 0 || c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(600)
                .Select(c =>
                {
                    string file;
                    var inPlan = _planClubs.Contains(c.Uid);
                    var custom = library.TryGet(c, out file) != null;
                    return new Row { Club = c, Text = c.Name + "\n" + (inPlan ? Loc.T(22172) : string.Empty) + (custom ? (inPlan ? " · " : string.Empty) + Loc.T(22174) : string.Empty) };
                }).ToList();
            ClubList.ItemsSource = rows;
            // show a result right away: the club that was selected before, else the first one of the list
            ClubList.SelectedItem = rows.FirstOrDefault(r => r.Club.Uid == selectedUid) ?? rows.FirstOrDefault();
        }

        private void ClubList_Changed(object sender, SelectionChangedEventArgs e) { ShowCrest(); }

        // ---- the crest of the selected club ---------------------------------------------------

        private byte[] CurrentBmp(FmClub club, out string sourceFile)
        {
            var bmp = new CrestLibrary(_state.Project.Crests).TryGet(club, out sourceFile);
            return bmp ?? CrestWriter.Render(club.Uid, A2G.Convert.Writing.ClubTexts.Name(club, _state.Settings));
        }

        private void ShowCrest()
        {
            var club = Ready ? Selected : null;
            ImportButton.IsEnabled = PaintButton.IsEnabled = RemoveButton.IsEnabled = club != null;
            if (club == null)
            {
                ClubTitle.Text = Loc.T(22156);
                CrestImage.Source = null;
                SourceText.Text = string.Empty;
                return;
            }
            string file;
            var bytes = CurrentBmp(club, out file);
            ClubTitle.Text = club.Name;
            CrestImage.Source = Decode(bytes);
            SourceText.Text = file != null ? Loc.T(22152, file) : Loc.T(22151);
            RemoveButton.IsEnabled = file != null;
        }

        public static BitmapSource Decode(byte[] bmp)
        {
            using (var stream = new MemoryStream(bmp))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        // ---- changes ------------------------------------------------------------------------

        private void SaveCrest(FmClub club, byte[] bmp)
        {
            _state.PrepareCrestFolder();
            DeleteFiles(club);
            File.WriteAllBytes(Path.Combine(_state.Project.Crests, club.Uid + ".bmp"), bmp);
            SharedLibrary.StoreCrest(club.Uid, bmp);
        }

        private void DeleteFiles(FmClub club)
        {
            var uid = club.Uid.TrimStart('0');
            if (uid.Length == 0) uid = "0";
            foreach (var file in Directory.Exists(_state.Project.Crests) ? Directory.GetFiles(_state.Project.Crests) : new string[0])
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var digits = new string(name.TakeWhile(char.IsDigit).ToArray());
                if (digits.Length == 0 || (name.Length > digits.Length && char.IsDigit(name[digits.Length]))) continue;
                var id = digits.TrimStart('0');
                if (id.Length == 0) id = "0";
                if (id != uid) continue;
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif") File.Delete(file);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var club = Selected;
            if (club == null) { Show(Loc.T(22156), true); return; }
            var dialog = new OpenFileDialog { Filter = Loc.T(22157) };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                SaveCrest(club, CrestLibrary.ToCrest(dialog.FileName));
                Show(Loc.T(22158, club.Name), false);
            }
            catch (Exception ex) { Show(Loc.T(22160, ex.Message), true); return; }
            RefreshClubs();
            ShowCrest();
        }

        private void Paint_Click(object sender, RoutedEventArgs e)
        {
            var club = Selected;
            if (club == null) { Show(Loc.T(22156), true); return; }
            string file;
            var painter = new CrestPainterWindow(Window.GetWindow(this), Loc.T(22161, club.Name), CurrentBmp(club, out file));
            painter.ShowDialog();
            if (painter.Result == null) return;
            try { SaveCrest(club, painter.Result); Show(Loc.T(22158, club.Name), false); }
            catch (Exception ex) { Show(Loc.T(22160, ex.Message), true); return; }
            RefreshClubs();
            ShowCrest();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var club = Selected;
            if (club == null) { Show(Loc.T(22156), true); return; }
            try { DeleteFiles(club); SharedLibrary.RemoveCrest(club.Uid); Show(Loc.T(22159, club.Name), false); }
            catch (Exception ex) { Show(Loc.T(22160, ex.Message), true); return; }
            RefreshClubs();
            ShowCrest();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            try { _state.PrepareCrestFolder(); }
            catch (Exception ex) { Show(Loc.T(22160, ex.Message), true); return; }
            Process.Start("explorer.exe", _state.Project.Crests);
        }

        private void Show(string text, bool problem)
        {
            StatusText.Text = text;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, problem ? "DangerBrush" : "SuccessBrush");
        }
    }
}

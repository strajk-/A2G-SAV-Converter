using A2G.SavConverter.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>Swap teams between leagues or with unused FM clubs; the players of a team are edited in its popup (double-click or magnifier).</summary>
    public partial class TeamsTab : UserControl
    {
        private sealed class CountryItem
        {
            public string Key; public string Name;
            public override string ToString() { return Name; }
        }

        /// <summary>The entry of the right-hand selector that stands for the pool of unused clubs.</summary>
        private sealed class PoolChoice
        {
            public override string ToString() { return Loc.T(22000); }
        }

        private sealed class TeamRow
        {
            public TeamGroup Group; public int Index; public TeamPlan Team; public string Text;
            public override string ToString() { return Text; }
        }

        private sealed class PoolRow
        {
            public FmClub Club; public string Text;
            public override string ToString() { return Text; }
        }

        private AppState _state;
        private IList<TeamGroup> _groups = new List<TeamGroup>();
        private readonly PoolChoice _pool = new PoolChoice();
        private bool _loading;

        /// <summary>Raised after an edit so the other screens can refresh.</summary>
        public event Action Edited;
        /// <summary>Raised when all edits were discarded and the plan has to be rebuilt.</summary>
        public event Action RebuildRequested;

        public TeamsTab()
        {
            InitializeComponent();
        }

        public void Attach(AppState state)
        {
            _state = state;
        }

        private PlanEditor Editor { get { return _state != null ? _state.Editor : null; } }

        /// <summary>Call after the plan was built.</summary>
        public void Reload()
        {
            _loading = true;
            CountryBox.ItemsSource = null;
            if (Editor == null) { _loading = false; return; }
            var items = new List<CountryItem>
            {
                new CountryItem { Key = "DEU", Name = Loc.T(22020) }, new CountryItem { Key = "ENG", Name = Loc.T(22021) },
                new CountryItem { Key = "FRA", Name = Loc.T(22022) }, new CountryItem { Key = "ITA", Name = Loc.T(22023) },
                new CountryItem { Key = "ESP", Name = Loc.T(22024) }, new CountryItem { Key = "AVE", Name = Loc.T(22025) }
            };
            CountryBox.ItemsSource = items;
            _loading = false;
            CountryBox.SelectedIndex = 0;
        }

        // ---- selectors ----------------------------------------------------------------------

        private void CountryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var country = CountryBox.SelectedItem as CountryItem;
            if (_loading || country == null || Editor == null) return;
            _groups = Editor.Groups(country.Key);
            _loading = true;
            LeftGroupBox.ItemsSource = _groups;
            LeftGroupBox.SelectedIndex = 0;
            FillRightChoices(null);
            _loading = false;
            RefreshAll();
        }

        /// <summary>
        /// The right side offers every league except the one on the left (swapping inside one league changes
        /// nothing) plus the pool of unused FM clubs. Keeps the previous choice when it is still offered.
        /// </summary>
        private void FillRightChoices(object keep)
        {
            var left = LeftGroup;
            var right = new List<object>(_groups.Where(g => !ReferenceEquals(g, left)).Cast<object>());
            right.Add(_pool);
            RightGroupBox.ItemsSource = right;
            RightGroupBox.SelectedItem = keep != null && right.Contains(keep) ? keep : right[0];
        }

        private void LeftGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _loading = true;
            FillRightChoices(RightGroupBox.SelectedItem);
            _loading = false;
            RefreshLeft();
            RefreshRight();
        }
        private void RightGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) RefreshRight(); }
        private void PoolSearch_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) RefreshRight(); }
        private void LeftSearch_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) RefreshLeft(); }

        private TeamGroup LeftGroup { get { return LeftGroupBox.SelectedItem as TeamGroup; } }

        private void RefreshAll()
        {
            RefreshLeft();
            RefreshRight();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            var edits = Editor != null ? Editor.Log.Ops.Count : 0;
            InfoText.Text = edits == 0 ? Loc.T(22001) : Loc.T(22002, edits);
        }

        // ---- lists --------------------------------------------------------------------------

        private string TeamText(TeamGroup group, TeamPlan team)
        {
            var problems = Editor.Check(group, team);
            return team.Club.Name + "\n" + Loc.T(22003, team.Club.Reputation, team.Squad.Count) +
                   (problems.Count > 0 ? "   ⚠ " + string.Join("; ", problems) : string.Empty);
        }

        /// <summary>The teams of the group on the left; the search filters them by name, the index stays the position in the group (swaps use it).</summary>
        private void RefreshLeft()
        {
            var group = LeftGroup;
            var selectedRow = LeftList.SelectedItem as TeamRow;
            var selectedTeam = selectedRow == null ? null : selectedRow.Team;
            var query = (LeftSearch.Text ?? string.Empty).Trim();
            var rows = group == null ? null : group.Teams
                .Select((t, i) => new TeamRow { Group = group, Index = i, Team = t, Text = TeamText(group, t) })
                .Where(r => query.Length == 0 || r.Team.Club.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            LeftList.ItemsSource = rows;
            if (rows != null && selectedTeam != null) LeftList.SelectedItem = rows.FirstOrDefault(r => r.Team == selectedTeam);
        }

        private void RefreshRight()
        {
            if (Editor == null) return;
            var choice = RightGroupBox.SelectedItem;
            var selected = RightList.SelectedIndex;
            var pool = choice as PoolChoice;
            var group = choice as TeamGroup;
            if (group != null)
            {
                var teamQuery = (PoolSearch.Text ?? string.Empty).Trim();
                RightList.ItemsSource = group.Teams.Select((t, i) => new TeamRow { Group = group, Index = i, Team = t, Text = TeamText(group, t) })
                    .Where(r => teamQuery.Length == 0 || r.Team.Club.Name.IndexOf(teamQuery, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }
            else if (pool != null && CountryBox.SelectedItem is CountryItem)
            {
                var query = (PoolSearch.Text ?? string.Empty).Trim();
                var key = ((CountryItem)CountryBox.SelectedItem).Key;
                RightList.ItemsSource = Editor.Pool(key)
                    .Where(c => query.Length == 0 || c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(300)
                    .Select(c => new PoolRow { Club = c, Text = c.Name + "\n" + Loc.T(22004, c.Reputation, _state.Fm.SquadSize(c)) })
                    .ToList();
            }
            if (selected >= 0 && selected < RightList.Items.Count) RightList.SelectedIndex = selected;
        }

        // ---- team edits ---------------------------------------------------------------------

        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            var left = LeftList.SelectedItem as TeamRow;
            if (left == null) { Notify(Loc.T(22007)); return; }
            try
            {
                var rightTeam = RightList.SelectedItem as TeamRow;
                var rightClub = RightList.SelectedItem as PoolRow;
                if (rightTeam != null)
                {
                    if (ReferenceEquals(rightTeam.Group, left.Group)) { Notify(Loc.T(22040)); return; }
                    Editor.SwapTeams(left.Group, left.Index, rightTeam.Group, rightTeam.Index);
                }
                else if (rightClub != null)
                {
                    Editor.ReplaceWithPoolClub(left.Group, left.Index, rightClub.Club);
                }
                else
                {
                    Notify(Loc.T(22008));
                    return;
                }
            }
            catch (Exception ex)
            {
                Notify(ex.Message);
                return;
            }
            AfterEdit();
        }

        // ---- club information as a file (city, nickname, stadium, capacity and kit of every club of the plan) ----

        private List<A2G.Convert.Writing.ClubInfoRow> ClubInfoRows()
        {
            var rows = new List<A2G.Convert.Writing.ClubInfoRow>();
            var countries = new[] { "DEU", "ENG", "FRA", "ITA", "ESP", "AVE" };
            foreach (var key in countries)
                foreach (var group in Editor.Groups(key))
                    foreach (var team in group.Teams)
                    {
                        var club = team.Club;
                        var nation = _state.Fm.NationBySaveId(club.NationId);
                        rows.Add(new A2G.Convert.Writing.ClubInfoRow
                        {
                            Uid = club.Uid, Nation = nation != null ? nation.Name : string.Empty, League = group.Name, FmName = club.Name,
                            WrittenName = A2G.Convert.Engine.ClubNamer.ClubName(club),
                            City = A2G.Convert.Writing.ClubTexts.City(club, _state.Settings, A2G.Convert.Engine.ClubNamer.MaxCity),
                            Nickname = A2G.Convert.Writing.ClubTexts.Nickname(club, _state.Settings),
                            Stadium = A2G.Convert.Writing.ClubTexts.Stadium(club, _state.Settings, A2G.Convert.Engine.ClubNamer.MaxStadium),
                            Capacity = A2G.Convert.Writing.ClubTexts.Capacity(club, _state.Settings).ToString(),
                            Kit = A2G.Convert.Engine.Kits.For(_state.Settings, club).ToArray().Select(v => v.ToString()).ToArray()
                        });
                    }
            return rows;
        }

        /// <summary>The texts of the Excel workbook (instructions sheet, length check message, kit reference) in the language of the application.</summary>
        private static A2G.Convert.Writing.ClubInfoSheetText SheetText()
        {
            var text = new A2G.Convert.Writing.ClubInfoSheetText
            {
                InfoTitle = Loc.T(22129), TooLongTitle = Loc.T(22136), TooLongMessage = Loc.T(22137),
                BadCapacityTitle = Loc.T(22139), BadCapacityMessage = Loc.T(22140),
                MinCapacity = A2G.Convert.Writing.ClubTexts.MinCapacity, MaxCapacity = A2G.Convert.Writing.ClubTexts.MaxCapacity,
                MaxCity = A2G.Convert.Engine.ClubNamer.MaxCity, MaxNickname = A2G.Convert.Engine.ClubNamer.MaxNickname, MaxStadium = A2G.Convert.Engine.ClubNamer.MaxStadium
            };
            for (var key = 22130; key <= 22135; key++) text.InfoLines.Add(Loc.T(key));
            text.InfoLines.Add(Loc.T(22248));
            text.KitSheetName = Loc.T(22210); text.KitNumberHeader = Loc.T(22243); text.KitNameHeader = Loc.T(22244); text.KitSampleHeader = Loc.T(22245);
            text.KitColoursTitle = Loc.T(22246); text.KitPatternsTitle = Loc.T(22247);
            text.BadKitTitle = Loc.T(22249); text.BadPatternMessage = Loc.T(22250); text.BadColourMessage = Loc.T(22251);
            text.ColourNames = Enumerable.Range(0, A2G.Convert.Engine.Kits.ColourCount).Select(i => Loc.T(22230 + i)).ToArray();
            text.PatternNames = Enumerable.Range(0, A2G.Convert.Engine.Kits.PatternCount).Select(i => Loc.T(22220 + i)).ToArray();
            return text;
        }

        private void ExportClubInfo_Click(object sender, RoutedEventArgs e)
        {
            if (Editor == null || _state.Fm == null) { Notify(Loc.T(22010)); return; }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.T(22121), Filter = Loc.T(22120), FilterIndex = 1, DefaultExt = "xlsx", AddExtension = true, FileName = "club-information.xlsx",
                InitialDirectory = _state.Project != null && System.IO.Directory.Exists(_state.Project.Root) ? _state.Project.Root : null
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var rows = ClubInfoRows();
                A2G.Convert.Writing.ClubInfoFile.Write(dialog.FileName, rows, SheetText());
                Notify(Loc.T(22123, rows.Count, dialog.FileName));
            }
            catch (Exception ex) { Notify(Loc.T(22125, ex.Message)); }
        }

        private void ImportClubInfo_Click(object sender, RoutedEventArgs e)
        {
            if (Editor == null || _state.Fm == null) { Notify(Loc.T(22010)); return; }
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T(22122), Filter = Loc.T(22120),
                InitialDirectory = _state.Project != null && System.IO.Directory.Exists(_state.Project.Root) ? _state.Project.Root : null
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var rows = A2G.Convert.Writing.ClubInfoFile.Read(dialog.FileName);
                var result = A2G.Convert.Writing.ClubInfoCsv.Apply(_state.Settings, _state.Fm, rows);
                _state.SaveSettings();
                Notify(Loc.T(22124, rows.Count, result.Changed, result.Unchanged, result.Unknown));
            }
            catch (Exception ex) { Notify(Loc.T(22125, ex.Message)); }
        }

        // ---- double-click: information of a team ---------------------------------------------

        private static bool OnItem(ListBox list, System.Windows.Input.MouseButtonEventArgs e)
        {
            return A2G.SavConverter.Services.UiHelpers.IsOnItem<ListBoxItem>(e.OriginalSource);
        }

        /// <summary>The magnifying glass of a team row (a team of a league or a club of the pool).</summary>
        private void MagnifyTeam_Click(object sender, RoutedEventArgs e)
        {
            ShowTeamInfo((sender as FrameworkElement)?.DataContext);
        }

        private void TeamList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var list = (ListBox)sender;
            if (!OnItem(list, e)) return;
            ShowTeamInfo(list.SelectedItem);
        }

        private void ShowTeamInfo(object item)
        {
            if (Editor == null) return;
            var team = item as TeamRow;
            var pool = item as PoolRow;
            var country = CountryBox.SelectedItem as CountryItem;
            TeamInfoData data;
            if (team != null)
            {
                // the popup can swap players of the club (reserve view): it edits this team and asks for fresh data after every edit
                Func<TeamInfoData> build = null;
                build = () => new TeamInfoData
                {
                    Club = team.Team.Club, Squad = team.Team.Squad.ToList(), GroupName = team.Group.Name,
                    MinSquad = team.Group.MinSquad, MaxSquad = team.Group.MaxSquad,
                    Quota = team.Group.Country == "AVE" ? _state.Settings.AvereineQuota : _state.Settings.SquadQuota,
                    Problems = Editor.Check(team.Group, team.Team),
                    Team = team.Team, Editor = Editor, Edited = AfterEdit, Refresh = build
                };
                data = build();
            }
            else if (pool != null && country != null)
            {
                var avereine = country.Key == "AVE";
                var squad = avereine ? A2G.Convert.Engine.SquadSelector.SelectAvereine(pool.Club, _state.Fm, _state.Settings)
                                     : A2G.Convert.Engine.SquadSelector.SelectBalanced(pool.Club, _state.Fm, _state.Settings);
                data = new TeamInfoData
                {
                    Club = pool.Club, Squad = squad, GroupName = Loc.T(22112), InPlan = false,
                    MinSquad = avereine ? _state.Settings.AvereineSquadSize : 16, MaxSquad = avereine ? _state.Settings.AvereineSquadSize : 30,
                    Quota = avereine ? _state.Settings.AvereineQuota : _state.Settings.SquadQuota
                };
            }
            else return;
            new TeamInfoWindow(Window.GetWindow(this), _state, data).ShowDialog();
        }

        private void AfterEdit()
        {
            _state.SaveEdits();
            RefreshAll();
            if (Edited != null) Edited();
        }

        private void ResetEdits_Click(object sender, RoutedEventArgs e)
        {
            if (Editor == null || Editor.Log.Ops.Count == 0) { Notify(Loc.T(22011)); return; }
            if (ThemedMessageBox.Show(Window.GetWindow(this), Loc.T(22012, Editor.Log.Ops.Count), Loc.T(10077),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _state.ClearEdits();
            if (RebuildRequested != null) RebuildRequested();
        }

        private void SeasonResults_Click(object sender, RoutedEventArgs e) { ShowSeasonResults(null); }

        /// <summary>Opens the results of the last season for the league on the left (a country key such as AVE selects that country first).</summary>
        public void ShowSeasonResults(string country)
        {
            if (!string.IsNullOrEmpty(country))
            {
                var item = CountryBox.Items.OfType<CountryItem>().FirstOrDefault(c => c.Key == country);
                if (item != null) CountryBox.SelectedItem = item;
            }
            var group = LeftGroup;
            if (group == null || Editor == null) return;
            if (group.League == null && group.Country != "AVE") { Notify(Loc.T(22282)); return; }
            new SeasonResultsWindow(Window.GetWindow(this), _state, group).ShowDialog();
        }

        private static void Notify(string message)
        {
            ThemedMessageBox.Show(Application.Current.MainWindow, message, Loc.T(22013), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

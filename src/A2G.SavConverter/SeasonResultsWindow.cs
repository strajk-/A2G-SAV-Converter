using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Plan;
using A2G.Sav;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// The results of the last season. A league lists its slots (champion, runner-up, UEFA places, cup winner, cup finalist, promoted,
    /// relegated) and the club of the league that fills each one; the non-league clubs list every nation with its champion and cup winner.
    /// The rules fill the slots in; every one can be changed.
    /// </summary>
    public sealed class SeasonResultsWindow : Window
    {
        private sealed class SlotRow
        {
            public string Group, Slot;
            public ComboBox Club;
            public Button Reset;
            public TextBlock Label;
            public IList<FmClub> Clubs;
        }

        private readonly AppState _state;
        private readonly TeamGroup _group;
        private readonly bool _avereine;
        private readonly List<SlotRow> _rows = new List<SlotRow>();
        private readonly Dictionary<string, Dictionary<string, FmClub>> _effective = new Dictionary<string, Dictionary<string, FmClub>>();
        private readonly List<Action> _refreshers = new List<Action>();
        private LeagueCounts _counts = new LeagueCounts();
        private bool _loading;

        public SeasonResultsWindow(Window owner, AppState state, TeamGroup group)
        {
            _state = state;
            _group = group;
            _avereine = group.Country == "AVE";
            Title = Loc.T(22262, group.Name);
            Owner = owner;
            Width = _avereine ? 820 : 640; Height = 660; MinWidth = 520; MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "BgBrush");
            SetResourceReference(ForegroundProperty, "TextBrush");
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);

            var root = new DockPanel { Margin = new Thickness(16) };
            var info = new TextBlock
            {
                Text = _avereine ? Loc.T(22265) : group.Tier == 0 ? Loc.T(22263) : Loc.T(22264),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            };
            info.SetResourceReference(StyleProperty, "Muted");
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);
            var hint = new TextBlock { Text = Loc.T(22284), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 12 };
            hint.SetResourceReference(StyleProperty, "Muted");
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var resetAll = new Button { Content = Loc.T(22272), Margin = new Thickness(0, 0, 8, 0), MinWidth = 120 };
            resetAll.Click += (s, e) => ResetAll();
            var close = new Button { Content = Loc.T(22273), MinWidth = 100, IsDefault = true, IsCancel = true };
            close.SetResourceReference(StyleProperty, "PrimaryButton");
            close.Click += (s, e) => Close();
            buttons.Children.Add(resetAll);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var grid = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            if (_avereine) BuildNations(grid); else BuildLeague(grid);
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid });
            Content = root;
            Refresh();
        }

        // ---- layout -------------------------------------------------------------------------

        private static string SlotLabel(string slot)
        {
            if (slot == SeasonResults.Champion) return Loc.T(22275);
            if (slot == SeasonResults.RunnerUp) return Loc.T(22276);
            if (slot == SeasonResults.CupWinner) return Loc.T(22280);
            if (slot == SeasonResults.CupFinalist) return Loc.T(22281);
            if (slot.StartsWith("uefa", StringComparison.Ordinal)) return Loc.T(22277) + " " + slot.Substring(4);
            if (slot.StartsWith("promoted", StringComparison.Ordinal)) return Loc.T(22278) + " " + slot.Substring(8);
            if (slot.StartsWith("relegated", StringComparison.Ordinal)) return Loc.T(22279) + " " + slot.Substring(9);
            return slot;
        }

        private static Grid Columns(params double[] widths)
        {
            var grid = new Grid();
            foreach (var w in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private int AddRowDefinition(Grid grid)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return grid.RowDefinitions.Count - 1;
        }

        private static void Place(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private SlotRow NewRow(string group, string slot, IList<FmClub> clubs)
        {
            var row = new SlotRow { Group = group, Slot = slot, Clubs = clubs };
            row.Label = new TextBlock { Text = SlotLabel(slot), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
            row.Club = new ComboBox { Margin = new Thickness(4, 3, 4, 3), ItemsSource = clubs };
            row.Reset = new Button { Content = "", ToolTip = Loc.T(22283), Margin = new Thickness(4, 3, 0, 3), Width = 30 };
            row.Reset.SetResourceReference(StyleProperty, "IconButton");
            var captured = row;
            row.Club.SelectionChanged += (s, e) => Picked(captured);
            row.Reset.Click += (s, e) => { SeasonResults.SetSlot(_state.Settings, captured.Group, captured.Slot, null); _state.SaveSettings(); Refresh(); };
            _rows.Add(row);
            return row;
        }

        private void AddHeader(Grid grid, params string[] texts)
        {
            var line = AddRowDefinition(grid);
            for (var c = 0; c < texts.Length; c++)
                Place(grid, new TextBlock { Text = texts[c], FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 4, 8) }, line, c);
        }

        /// <summary>A league: one row per slot with the clubs of the league to choose from.</summary>
        private void BuildLeague(Grid grid)
        {
            if (_group.League == null) return;
            _counts = SeasonResults.CountsFor(_state.Config.Anstoss2Path, _group.CountrySpec, _group.League);
            var clubs = _group.Teams.Select(t => t.Club).OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
            var key = SeasonResults.LeagueKey(_group.League);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            AddHeader(grid, Loc.T(22285), Loc.T(22266), string.Empty);
            foreach (var slot in SeasonResults.Slots(_group.Tier == 0, _counts))
            {
                var row = NewRow(key, slot, clubs);
                var line = AddRowDefinition(grid);
                Place(grid, row.Label, line, 0); Place(grid, row.Club, line, 1); Place(grid, row.Reset, line, 2);
            }
            _refreshers.Add(() => _effective[key] = SeasonResults.Assign(_state.Settings, key, _group.Teams.Select(t => t.Club).ToList(), _group.Tier == 0, _counts));
        }

        /// <summary>The non-league clubs: every nation the game can store with its champion and cup winner.</summary>
        private void BuildNations(Grid grid)
        {
            var fm = _state.Fm;
            var laender = _state.Laender;
            var nations = new A2Nations(laender != null ? laender.Nations.Select(n => n.Name).ToList() : new List<string>());
            var countries = new List<int>();
            try
            {
                var avereine = (AvereineFile)SavLoader.Load(Path.Combine(_state.Config.Anstoss2Path, "DATA.DEF", "Avereine.sav"));
                countries = avereine.CountryTriplets.Select(t => t[0]).ToList();
            }
            catch (Exception) { }
            Func<FmClub, int> countryOf = c => { var n = fm.NationBySaveId(c.NationId); return nations.Resolve(n != null ? n.Name : null); };
            var byCountry = _group.Teams.Select(t => t.Club).GroupBy(countryOf).ToDictionary(g => g.Key, g => (IList<FmClub>)g.OrderByDescending(c => c.Reputation).ToList());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            AddHeader(grid, Loc.T(22271), Loc.T(22275), Loc.T(22280), string.Empty);
            var rowsPerNation = new Dictionary<int, SlotRow[]>();
            foreach (var country in countries.Where(c => c > 0 && c < (laender != null ? laender.Nations.Count : 0)).OrderBy(c => laender.Nations[c].Name, StringComparer.CurrentCultureIgnoreCase))
            {
                IList<FmClub> clubs;
                var has = byCountry.TryGetValue(country, out clubs);
                var key = SeasonResults.AvereineKey(country);
                var line = AddRowDefinition(grid);
                Place(grid, new TextBlock { Text = laender.Nations[country].Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) }, line, 0);
                if (!has)
                {
                    var none = new TextBlock { Text = Loc.T(22286), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                    none.SetResourceReference(StyleProperty, "Muted");
                    Grid.SetColumnSpan(none, 2);
                    Place(grid, none, line, 1);
                    continue;
                }
                var champion = NewRow(key, SeasonResults.Champion, clubs);
                var cup = NewRow(key, SeasonResults.CupWinner, clubs);
                Place(grid, champion.Club, line, 1); Place(grid, cup.Club, line, 2);
                // one reset button per nation
                var reset = champion.Reset;
                reset.Click += (s, e) => { SeasonResults.ResetGroup(_state.Settings, key); _state.SaveSettings(); Refresh(); };
                Place(grid, reset, line, 3);
                var k = key; var list = clubs;
                _refreshers.Add(() => _effective[k] = SeasonResults.AssignAvereine(_state.Settings, k, list));
            }
        }

        // ---- showing and changing -------------------------------------------------------------

        /// <summary>Shows what will be written: the rules with the user's changes.</summary>
        private void Refresh()
        {
            _loading = true;
            foreach (var r in _refreshers) r();
            foreach (var row in _rows)
            {
                Dictionary<string, FmClub> slots;
                FmClub club = null;
                if (_effective.TryGetValue(row.Group, out slots)) slots.TryGetValue(row.Slot, out club);
                row.Club.SelectedItem = club;
                var own = SeasonResults.IsOverridden(_state.Settings, row.Group, row.Slot);
                row.Label.FontWeight = own ? FontWeights.Bold : FontWeights.Normal;
                row.Club.FontWeight = own ? FontWeights.Bold : FontWeights.Normal;
                row.Reset.IsEnabled = _avereine ? SeasonResults.AvereineSlots.Any(s => SeasonResults.IsOverridden(_state.Settings, row.Group, s)) : own;
            }
            _loading = false;
        }

        private void Picked(SlotRow row)
        {
            if (_loading) return;
            var club = row.Club.SelectedItem as FmClub;
            if (club == null) return;
            SeasonResults.SetSlot(_state.Settings, row.Group, row.Slot, club);
            _state.SaveSettings();
            Refresh();
        }

        private void ResetAll()
        {
            foreach (var group in _rows.Select(r => r.Group).Distinct().ToList()) SeasonResults.ResetGroup(_state.Settings, group);
            _state.SaveSettings();
            Refresh();
        }
    }
}

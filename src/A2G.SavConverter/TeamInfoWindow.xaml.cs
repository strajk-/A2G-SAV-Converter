using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.Convert.Writing;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>What the team popup shows: a club, its squad (in the plan or as it would be picked) and the limits of its group.</summary>
    public sealed class TeamInfoData
    {
        public FmClub Club;
        public List<FmPlayer> Squad;
        public string GroupName;
        public int MinSquad, MaxSquad;
        /// <summary>Wanted players per A2 position (index = position - 1).</summary>
        public int[] Quota;
        public List<string> Problems = new List<string>();
        /// <summary>False for a club of the pool: the squad is the one the converter would pick.</summary>
        public bool InPlan = true;
        /// <summary>The team in the plan and the editor: set for a team of the plan, they enable the reserve view (swap players of the club).</summary>
        public A2G.Convert.Plan.TeamPlan Team;
        public A2G.Convert.Plan.PlanEditor Editor;
        /// <summary>Called after an edit so the other screens refresh.</summary>
        public Action Edited;
        /// <summary>Builds the data again (squad, problems) after an edit.</summary>
        public Func<TeamInfoData> Refresh;
    }

    /// <summary>Popup with the information of a team: its club, how it is written to the game, the squad by position and the squad list.</summary>
    public partial class TeamInfoWindow : Window
    {
        private sealed class Row
        {
            public FmPlayer Player;
            public string Name { get; set; }
            public int Age { get; set; }
            public string PositionText { get; set; }
            public int CA { get; set; }
            public int PA { get; set; }
        }

        private readonly AppState _state;
        private readonly FmClub _club;
        private TextBox _cityBox, _nickBox, _stadiumBox, _nameBox;
        private ComboBox _nameMode;   // 0 full name, 1 short name, 2 custom
        private Stepper _capacity;
        private TeamInfoData _data;
        private ClubKit _kit;
        private readonly List<Stepper> _kitSteppers = new List<Stepper>();   // home pattern, shirt, shorts, socks, then the same for away
        private Border _homeKitHost, _awayKitHost;
        private TextBlock _kitLegend;
        private bool _kitLoading;
        private TextBlock _statPlayers, _statCa, _statPa, _statAge;
        private bool _cycle;

        private static string PositionName(int pos) { return Loc.T(22079 + pos); }   // 22080 .. 22089

        public TeamInfoWindow(Window owner, AppState state, TeamInfoData data)
        {
            InitializeComponent();
            Owner = owner;
            _state = state;
            var club = data.Club;
            _club = club;
            _cycle = data.InPlan;   // only a squad in the plan is a team to cycle through
            Title = Loc.T(22078, club.Name);
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);

            var city = SavWriter.CityFor(club);
            var nation = state.Fm.NationBySaveId(club.NationId);
            _data = data;
            TitleText.Text = club.Name;
            NoteText.Text = data.InPlan ? string.Empty : Loc.T(22102);
            Fact(22090, nation != null ? nation.Name : "-");
            Fact(22091, data.GroupName);
            BuildNameRow();
            _cityBox = Editable(22097, ClubTexts.City(club, state.Settings, ClubNamer.MaxCity), ClubNamer.MaxCity);
            _nickBox = Editable(22098, ClubTexts.Nickname(club, state.Settings), ClubNamer.MaxNickname);
            _stadiumBox = Editable(22099, ClubTexts.Stadium(club, state.Settings, ClubNamer.MaxStadium), ClubNamer.MaxStadium);
            _capacity = EditableNumber(22138, ClubTexts.Capacity(club, state.Settings));
            Fact(22092, club.Reputation.ToString());
            _statPlayers = FactBlock(22093, string.Empty);
            _statCa = FactBlock(22094, string.Empty);
            _statPa = FactBlock(22095, string.Empty);
            _statAge = FactBlock(22096, string.Empty);

            BuildKitPanel();

            // the reserve view needs a team of the plan to edit
            var editable = data.InPlan && data.Team != null && data.Editor != null && data.Refresh != null;
            ReserveToggle.Visibility = RemoveButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
            Render(data);
        }

        // ---- kits ---------------------------------------------------------------------------

        private static string PatternName(int pattern) { return Loc.T(22220 + pattern); }
        private static string ColourName(int colour) { return Loc.T(22230 + colour); }

        /// <summary>The home and away kit: two drawings and a stepper per part (pattern, shirt, shorts, socks), like the kit view of the game's editor.</summary>
        private void BuildKitPanel()
        {
            _kit = Kits.For(_state.Settings, _club);
            KitPanel.Children.Clear();
            KitPanel.Children.Add(new TextBlock { Text = Loc.T(22210), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

            var drawings = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            drawings.ColumnDefinitions.Add(new ColumnDefinition()); drawings.ColumnDefinitions.Add(new ColumnDefinition());
            _homeKitHost = new Border { HorizontalAlignment = HorizontalAlignment.Center };
            _awayKitHost = new Border { HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(_awayKitHost, 1);
            drawings.Children.Add(_homeKitHost); drawings.Children.Add(_awayKitHost);
            KitPanel.Children.Add(drawings);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var labels = new[] { 22211, 22212, 22213, 22214 };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var c in new[] { 1, 2 })
            {
                var head = new TextBlock { Text = Loc.T(c == 1 ? 22215 : 22216), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
                head.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                Grid.SetColumn(head, c);
                grid.Children.Add(head);
            }
            _kitSteppers.Clear();
            for (var r = 0; r < 4; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = Loc.T(labels[r]), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 5) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                Grid.SetRow(label, r + 1);
                grid.Children.Add(label);
            }
            for (var set = 0; set < 2; set++)
                for (var r = 0; r < 4; r++)
                {
                    var stepper = new Stepper { Min = 0, Max = r == 0 ? Kits.PatternCount - 1 : Kits.ColourCount - 1, Step = 1, Margin = new Thickness(3, 0, 3, 5) };
                    Grid.SetRow(stepper, r + 1); Grid.SetColumn(stepper, set + 1);
                    grid.Children.Add(stepper);
                    _kitSteppers.Add(stepper);
                }
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Stepper.ValueProperty, typeof(Stepper));
            foreach (var s in _kitSteppers) descriptor.AddValueChanged(s, (o, e) => KitChanged());
            KitPanel.Children.Add(grid);

            _kitLegend = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            _kitLegend.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            KitPanel.Children.Add(_kitLegend);
            var hint = new TextBlock { Text = Loc.T(22217), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            KitPanel.Children.Add(hint);
            ShowKit();
        }

        /// <summary>Puts the kit into the steppers and draws it.</summary>
        private void ShowKit()
        {
            _kitLoading = true;
            var values = _kit.ToArray();
            for (var i = 0; i < 8; i++) _kitSteppers[i].Value = values[i];
            _kitLoading = false;
            DrawKit();
        }

        private void KitChanged()
        {
            if (_kitLoading || _kit == null) return;
            _kit = ClubKit.FromArray(_kitSteppers.Select(s => s.IntValue).ToArray());
            DrawKit();
        }

        private void DrawKit()
        {
            _homeKitHost.Child = KitDrawing.Draw(_kit.Home);
            _awayKitHost.Child = KitDrawing.Draw(_kit.Away);
            Func<KitSet, string> text = k => PatternName(k.Pattern) + " · " +ColourName(k.Shirt) + " / " + ColourName(k.Shorts) + " / " + ColourName(k.Socks);
            _kitLegend.Text = Loc.T(22215) + ": " + text(_kit.Home) + "\n" + Loc.T(22216) + ": " + text(_kit.Away);
        }

        /// <summary>The parts that change after an edit: statistics, position circles, problems, squad and reserves.</summary>
        private void Render(TeamInfoData data)
        {
            _data = data;
            var squad = data.Squad;
            var players = squad.Count == 0 ? 1 : squad.Count;
            _statPlayers.Text = squad.Count + "  (" + (data.MinSquad == data.MaxSquad ? data.MinSquad.ToString() : data.MinSquad + "-" + data.MaxSquad) + ")";
            _statCa.Text = (squad.Sum(p => p.CurrentAbility) / (double)players).ToString("0.0");
            _statPa.Text = (squad.Sum(p => p.PotentialAbility) / (double)players).ToString("0.0");
            _statAge.Text = (squad.Sum(p => p.Age) / (double)players).ToString("0.0");

            var counts = squad.GroupBy(p => PositionRules.Primary(PositionRules.Scores(p))).ToDictionary(g => g.Key, g => g.Count());
            Composition.Children.Clear();
            Composition.Children.Add(BuildPositionPitch(counts, data.Quota));
            if (data.Problems.Count > 0)
            {
                ProblemText.Text = Loc.T(22103) + ": " + string.Join("; ", data.Problems);
                ProblemText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            }
            else ProblemText.Text = string.Empty;

            SquadTitle.Text = Loc.T(22105, squad.Count);
            SquadList.ItemsSource = squad
                .Select(p => new { P = p, Pos = PositionRules.Primary(PositionRules.Scores(p)) })
                .OrderBy(x => x.Pos).ThenByDescending(x => x.P.CurrentAbility)
                .Select(x => new Row { Player = x.P, Name = x.P.DisplayName, Age = x.P.Age, PositionText = PositionName(x.Pos), CA = x.P.CurrentAbility, PA = x.P.PotentialAbility }).ToList();
            RenderReserves();
        }

        /// <summary>The other FM players of the club that are not in the squad.</summary>
        private void RenderReserves()
        {
            if (ReserveToggle.Visibility != Visibility.Visible) return;
            var reserves = _state.Fm.PlayersOf(_club).Where(p => !_data.Squad.Contains(p) && SquadSelector.Eligible(p)).OrderByDescending(p => p.CurrentAbility).ToList();
            ReserveTitle.Text = Loc.T(22196, reserves.Count);
            ReserveList.ItemsSource = reserves
                .Select(p => new Row { Player = p, Name = p.DisplayName, Age = p.Age, PositionText = PositionName(PositionRules.Primary(PositionRules.Scores(p))), CA = p.CurrentAbility, PA = p.PotentialAbility }).ToList();
        }

        private void ReserveToggle_Changed(object sender, RoutedEventArgs e)
        {
            var on = ReserveToggle.IsChecked == true;
            ReservePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            PitchPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) RenderReserves();
        }

        private string Describe(A2G.Convert.Plan.EditCheck check)
        {
            switch (check.Result)
            {
                case A2G.Convert.Plan.EditResult.TooYoung: return Loc.T(22041, check.Limit);
                case A2G.Convert.Plan.EditResult.TargetFull: return Loc.T(22072, check.Team.Club.Name, check.Limit);
                case A2G.Convert.Plan.EditResult.SourceTooSmall:
                case A2G.Convert.Plan.EditResult.TeamTooSmall: return Loc.T(22073, check.Team.Club.Name, check.Limit);
            }
            return string.Empty;
        }

        private void AfterEdit(string message)
        {
            if (_data.Edited != null) _data.Edited();
            Render(_data.Refresh());
            Show(message, false);
        }

        /// <summary>Takes the selected reserve into the squad; a full squad gives up the player selected in the squad list.</summary>
        private void AddReserve_Click(object sender, RoutedEventArgs e)
        {
            var reserve = ReserveList.SelectedItem as Row;
            if (reserve == null) { Show(Loc.T(22010), true); return; }
            var replace = SquadList.SelectedItem as Row;
            var check = _data.Editor.Add(_data.Team, reserve.Player, replace == null ? null : replace.Player);
            if (!check.Ok) { Show(Describe(check), true); return; }
            AfterEdit(check.Replaced != null ? Loc.T(22076, reserve.Player.DisplayName, check.Replaced.DisplayName, _club.Name)
                : check.Source != null ? Loc.T(22071, reserve.Player.DisplayName, check.Source.Club.Name, _club.Name)
                : Loc.T(22065, reserve.Player.DisplayName, _club.Name));
        }

        /// <summary>The player picker (positions, CA / PA, name): players of any club can join this team.</summary>
        private void FindPlayers_Click(object sender, RoutedEventArgs e)
        {
            var window = new PlayerPickerWindow(this, _state.Fm, _club.Name,
                p => { var t = _data.Editor.TeamOf(p); return t == null ? null : t.Club.Name; },
                (owner, p) => new PlayerPreviewWindow(owner, _state, p).ShowDialog(),
                player =>
                {
                    try
                    {
                        var replace = SquadList.SelectedItem as Row;   // a full squad takes the new player in place of the selected squad player
                        var check = _data.Editor.Add(_data.Team, player, replace == null ? null : replace.Player);
                        if (!check.Ok) return Tuple.Create(Describe(check), true);
                        var text = check.Replaced != null ? Loc.T(22076, player.DisplayName, check.Replaced.DisplayName, _club.Name)
                            : check.Source != null ? Loc.T(22071, player.DisplayName, check.Source.Club.Name, _club.Name)
                            : Loc.T(22065, player.DisplayName, _club.Name);
                        AfterEdit(text);
                        return Tuple.Create(text, false);
                    }
                    catch (Exception ex) { return Tuple.Create(ex.Message, true); }
                });
            window.ShowDialog();
        }
        private void RemoveSquad_Click(object sender, RoutedEventArgs e)
        {
            var row = SquadList.SelectedItem as Row;
            if (row == null) { Show(Loc.T(22009), true); return; }
            var check = _data.Editor.Remove(_data.Team, row.Player);
            if (!check.Ok) { Show(Describe(check), true); return; }
            AfterEdit(Loc.T(22197, row.Player.DisplayName, _club.Name));
        }

        private void ReserveList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = ReserveList.SelectedItem as Row;
            if (row != null && UiHelpers.IsOnItem<ListViewItem>(e.OriginalSource))
                new PlayerPreviewWindow(this, _state, row.Player, null, _club.Name).ShowDialog();
        }
        /// <summary>A line with a text box for a value the FM save does not have (city, nickname, stadium name).</summary>
        private TextBox Editable(int labelKey, string value, int maxLength)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var label = new TextBlock { Text = Loc.T(labelKey), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var box = new TextBox { Text = value, MaxLength = maxLength };
            Grid.SetColumn(box, 1);
            grid.Children.Add(label); grid.Children.Add(box);
            Facts.Children.Add(grid);
            return box;
        }

        /// <summary>The club name written to the game: a choice of the FM full name, the FM short name or the user's own text.</summary>
        private void BuildNameRow()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var label = new TextBlock { Text = Loc.T(22104), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _nameMode = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            _nameMode.Items.Add(Loc.T(28110)); _nameMode.Items.Add(Loc.T(28111)); _nameMode.Items.Add(Loc.T(28112));
            _nameBox = new TextBox { MaxLength = ClubNamer.MaxName };
            var panel = new StackPanel();
            panel.Children.Add(_nameMode); panel.Children.Add(_nameBox);
            Grid.SetColumn(panel, 1);
            grid.Children.Add(label); grid.Children.Add(panel);
            Facts.Children.Add(grid);
            ShowName();
            _nameMode.SelectionChanged += (s, e) => NameModeChanged();
        }

        private bool _nameLoading;

        /// <summary>Shows the stored choice: the box holds the written name and can be typed in only for "Custom".</summary>
        private void ShowName()
        {
            _nameLoading = true;
            var o = _state.Settings.FindClubInfo(_club.Uid);
            var mode = o == null ? ClubTexts.NameShort : o.NameMode;
            _nameMode.SelectedIndex = mode == ClubTexts.NameFull ? 0 : mode == ClubTexts.NameCustom ? 2 : 1;
            _nameBox.Text = ClubTexts.Name(_club, _state.Settings);
            _nameBox.IsEnabled = mode == ClubTexts.NameCustom;
            _nameLoading = false;
        }

        private void NameModeChanged()
        {
            if (_nameLoading) return;
            var index = _nameMode.SelectedIndex;
            _nameBox.IsEnabled = index == 2;
            if (index == 0) _nameBox.Text = ClubNamer.FullClubName(_club);
            else if (index == 1) _nameBox.Text = ClubNamer.ClubName(_club);
            else { _nameBox.Focus(); _nameBox.SelectAll(); }
        }

        private int ChosenNameMode { get { return _nameMode.SelectedIndex == 0 ? ClubTexts.NameFull : _nameMode.SelectedIndex == 2 ? ClubTexts.NameCustom : ClubTexts.NameShort; } }

        /// <summary>A line with a − / + stepper for the stadium capacity (no typing).</summary>
        private Stepper EditableNumber(int labelKey, int value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var label = new TextBlock { Text = Loc.T(labelKey), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var stepper = new Stepper { Min = ClubTexts.MinCapacity, Max = ClubTexts.MaxCapacity, Step = 500 };
            stepper.Value = value;
            Grid.SetColumn(stepper, 1);
            grid.Children.Add(label); grid.Children.Add(stepper);
            Facts.Children.Add(grid);
            return stepper;
        }

        /// <summary>One circle on the pitch per Anstoss 2 position: the number of players of the team in it.</summary>
        private sealed class Spot { public int Pos; public string Code; public double X, Y; }

        private static readonly Spot[] Spots =
        {
            new Spot { Pos = 10, Code = "ST", X = 100, Y = 46 },
            new Spot { Pos = 7, Code = "LM", X = 34, Y = 100 },
            new Spot { Pos = 9, Code = "OM", X = 100, Y = 100 },
            new Spot { Pos = 8, Code = "RM", X = 166, Y = 100 },
            new Spot { Pos = 6, Code = "DM", X = 100, Y = 152 },
            new Spot { Pos = 4, Code = "LB", X = 34, Y = 200 },
            new Spot { Pos = 3, Code = "CD", X = 100, Y = 200 },
            new Spot { Pos = 5, Code = "RB", X = 166, Y = 200 },
            new Spot { Pos = 2, Code = "LI", X = 100, Y = 238 },
            new Spot { Pos = 1, Code = "GK", X = 100, Y = 274 }
        };

        private static UIElement BuildPositionPitch(Dictionary<int, int> counts, int[] quota)
        {
            var wantedBy = (quota != null && quota.Length == 10 ? quota : new int[10]).ToArray();
            int liberos; counts.TryGetValue(PositionRules.Libero, out liberos);
            if (liberos == 0) { wantedBy[PositionRules.OffensiveMidfield - 1] += wantedBy[PositionRules.Libero - 1]; wantedBy[PositionRules.Libero - 1] = 0; }
            var canvas = new Canvas { Width = PitchDrawing.Width, Height = PitchDrawing.Height };
            PitchDrawing.Draw(canvas);
            foreach (var spot in Spots)
            {
                int n; counts.TryGetValue(spot.Pos, out n);
                var wanted = wantedBy[spot.Pos - 1];
                if (n == 0 && wanted == 0) continue;   // a position this squad does not use stays off the pitch
                var below = n < wanted;
                var fill = below ? Color.FromRgb(0xD9, 0x4A, 0x4A) : wanted == 0 ? Color.FromRgb(0x4A, 0x7B, 0xD9) : Color.FromRgb(0x2E, 0xA8, 0x5E);
                var dot = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(fill),
                    BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                    ToolTip = PositionName(spot.Pos) + ": " + n + (wanted > 0 ? " / " + wanted : string.Empty),
                    Child = new TextBlock { Text = n.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                Canvas.SetLeft(dot, spot.X - 14); Canvas.SetTop(dot, spot.Y - 14);
                canvas.Children.Add(dot);
                var caption = new TextBlock { Text = spot.Code, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 9, FontWeight = FontWeights.SemiBold, Width = 30, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(caption, spot.X - 15); Canvas.SetTop(caption, spot.Y + 13);
                canvas.Children.Add(caption);
            }
            return new Viewbox { Width = 288, Height = 418, HorizontalAlignment = HorizontalAlignment.Center, Child = canvas };
        }
        private TextBlock FactBlock(int labelKey, string value)
        {
            var grid = Pair(Loc.T(labelKey), value, null, false);
            Facts.Children.Add(grid);
            return (TextBlock)grid.Children[1];
        }

        private void Fact(int labelKey, string value)
        {
            Facts.Children.Add(Pair(Loc.T(labelKey), value, null, false));
        }

        /// <summary>A line with a muted label on the left and the value (and optionally a second value) on the right.</summary>
        private static Grid Pair(string label, string value, string second, bool header, bool problem = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = second == null ? new GridLength(1.4, GridUnitType.Star) : new GridLength(70) });
            if (second != null) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var l = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, TextAlignment = second == null ? TextAlignment.Left : TextAlignment.Right };
            if (second == null) l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            if (header) { l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); v.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); }
            if (problem) v.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
            if (second != null)
            {
                var s2 = new TextBlock { Text = second, TextAlignment = TextAlignment.Right };
                if (header) s2.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                Grid.SetColumn(s2, 2);
                grid.Children.Add(s2);
            }
            return grid;
        }

        private void Show(string text, bool problem)
        {
            StatusText.Text = text;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, problem ? "DangerBrush" : "SuccessBrush");
        }

        /// <summary>Stores the texts that differ from the generated defaults for this club (the project settings file).</summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ClubTexts.SetNameOverride(_state.Settings, _club, ChosenNameMode, _nameBox.Text);
            ClubTexts.SetOverride(_state.Settings, _club, _cityBox.Text, _nickBox.Text, _stadiumBox.Text, _capacity.IntValue);
            Kits.SetOverride(_state.Settings, _club, _kit);
            _state.SaveSettings();
            Show(Loc.T(22110), false);
        }
        private void Defaults_Click(object sender, RoutedEventArgs e)
        {
            var o = _state.Settings.FindClubInfo(_club.Uid);
            if (o != null) _state.Settings.ClubInfo.Remove(o);
            ShowName();
            _cityBox.Text = ClubTexts.City(_club, _state.Settings, ClubNamer.MaxCity);
            _nickBox.Text = ClubTexts.Nickname(_club, _state.Settings);
            _stadiumBox.Text = ClubTexts.Stadium(_club, _state.Settings, ClubNamer.MaxStadium);
            _capacity.Value = ClubTexts.Capacity(_club, _state.Settings);
            _kit = Kits.Default(_club);
            ShowKit();
            _state.SaveSettings();
            Show(Loc.T(22111), false);
        }

        private void MagnifyRow_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as Row;
            if (row != null) OpenPreview(row);
        }

        private void OpenPreview(Row row)
        {
            var order = SquadList.Items.Cast<Row>().Select(r => r.Player).ToList();
            new PlayerPreviewWindow(this, _state, row.Player, _cycle ? order : null, _club.Name).ShowDialog();
        }

        private void SquadList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = SquadList.SelectedItem as Row;
            if (row != null && UiHelpers.IsOnItem<ListViewItem>(e.OriginalSource))
                OpenPreview(row);
        }
    }
}

using A2G.SavConverter.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// Shows an FM player as Football Manager has him (left) and as he will appear in Anstoss 2's "Spielerinfo" window
    /// after the conversion (right), using the current conversion settings.
    /// </summary>
    public partial class PlayerInspector : UserControl
    {
        private sealed class Row
        {
            public FmPlayer Player;
            public string Text;
            public override string ToString() { return Text; }
        }

        private static readonly string[] PositionNames =
        {
            "", "Torwart", "Libero", "Manndecker", "linker Verteidiger", "rechter Verteidiger", "defensives Mittelfeld",
            "linkes Mittelfeld", "rechtes Mittelfeld", "offensives Mittelfeld", "Stürmer"
        };

        private static readonly Dictionary<string, string> AbilityNames = new Dictionary<string, string>
        {
            { "Kopfball", "Kopfball" }, { "Zweikampf", "Zweikampf" }, { "Schnelligkeit", "Schnelligkeit" },
            { "Schusskraft", "Schusskraft" }, { "Freistoss", "Freistoß" }, { "Flanken", "Flanken" },
            { "Torinstinkt", "Torinstinkt" }, { "Laufstaerke", "Laufstärke" }, { "Technik", "Technik" },
            { "Spielmacher", "Spielmacher" }, { "Beidfuessigkeit", "Beidfüßigkeit" },
            { "Elfmetertoeter", "Elfmetertöter" }, { "Reflexe", "Reflexe" }, { "Herauslaufen", "Herauslaufen" },
            { "Fangsicherheit", "Fangsicherheit" }, { "Fausten", "Fausten" }, { "Ballsicherheit", "Ballsicherheit" }
        };

        private AppState _state;
        private List<FmPlayer> _planPlayers = new List<FmPlayer>();

        // Anstoss 2 window elements updated on every selection
        private readonly Canvas _canvas;
        private TextBlock _last, _first, _club, _hp, _np, _alter, _staerke;
        private StackPanel _abilityPanel;
        private TextBlock _traitText;

        /// <summary>The game's own sentences (from its string table) for the traits, characters and health states.</summary>
        private static readonly Dictionary<string, string> TraitSentences = new Dictionary<string, string>
        {
            { "Fuehrungsperson", "Er ist eine echte Führungsperson" }, { "Kaempfernatur", "Er ist ein großer Kämpfer" },
            { "Trainingsweltmeister", "Er ist Trainingsweltmeister" }, { "Trainingsfaul", "Er ist sehr trainingsfaul" },
            { "Treter", "Er ist ein sehr harter Spieler." }, { "FairerSpieler", "Er ist ein sehr fairer Spieler" },
            { "Schwalbenkoenig", "Er ist ein Meister der Schwalbe" }, { "Allrounder", "Er ist überall einsetzbar" },
            { "FlexiblerSpieler", "Er ist ein sehr flexibler Spieler" }, { "Heimspieler", "Er ist ein typischer Heimspieler" },
            { "Auswaertsspieler", "Er ist ein typischer Auswärtsspieler" }, { "Talent", "Er gilt als Talent" },
            { "Hitzkopf", "Er gilt als Hitzkopf" }, { "Frohnatur", "Er gilt als wahre Frohnatur" },
            { "MannFuerGrosses", "Er gilt als Mann ohne Nerven" }, { "Nervenbuendel", "Er gilt als Nervenbündel" },
            { "Phlegma", "Er gilt als Phlegmat" }, { "Geldgeier", "Er gilt als geldgierig" },
            { "Musterprofi", "Er gilt als Musterprofi" }, { "Skandalnudel", "Er gilt als Skandalnudel" },
            { "Wegstecken", "Im Spiel kann er einiges wegstecken" }, { "Verletzungsanfaellig", "Leider ist er sehr verletzungsanfällig" },
            { "Knieprobleme", "Leider hatte er bereits größere Knieprobleme" }
        };

        private static bool RuleApplies(TraitRule rule, A2G.Sav.Player a)
        {
            var field = rule.Kind == TraitRule.KindCharacter || rule.Kind == TraitRule.KindHealth ? a.CharacterHealth : a.Traits;
            return (field & rule.Bit) != 0;
        }
        private Image _flag;
        private Ellipse _face;
        private Rectangle _hair;

        public PlayerInspector()
        {
            InitializeComponent();
            _canvas = BuildA2Canvas();
            A2Host.Child = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _canvas };
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Stepper.ValueProperty, typeof(Stepper));
            descriptor.AddValueChanged(SkinBox, (o, e) => LookChanged());
            descriptor.AddValueChanged(HairBox, (o, e) => LookChanged());
            ShowEmpty();
        }

        // ---- skin and hair (the FM save has none: a rule by nationality, changeable per player) ----

        private FmPlayer _lookPlayer;
        private bool _settingLook;

        private void LookChanged()
        {
            if (_settingLook || _lookPlayer == null || _state == null || _state.Fm == null) return;
            PlayerLook.SetOverride(_state.Settings, _state.Fm, _lookPlayer, SkinBox.IntValue, HairBox.IntValue);
            _state.SaveSettings();
            RenderA2(_lookPlayer);
        }

        private void LookReset_Click(object sender, RoutedEventArgs e)
        {
            if (_lookPlayer == null || _state == null) return;
            PlayerLook.ClearOverride(_state.Settings, _lookPlayer);
            _state.SaveSettings();
            RenderA2(_lookPlayer);
        }

        public void Attach(AppState state)
        {
            _state = state;
        }

        /// <summary>Call after the plan was (re)built or the settings changed.</summary>
        /// <summary>Popup use: hides the player list and shows the FM profile and the converted player of one FM player.</summary>
        public void ShowOnly(FmPlayer player)
        {
            ListColumn.Width = new GridLength(0);
            ListCard.Visibility = Visibility.Collapsed;
            Render(player);
        }
        public void Reload()
        {
            if (_state == null || _state.Fm == null || _state.Plan == null) { ShowEmpty(); return; }
            var ids = new HashSet<string>();
            _planPlayers = new List<FmPlayer>();
            var teams = _state.Plan.Countries.SelectMany(c => c.AllTeams).Concat(_state.Plan.Avereine.Teams);
            foreach (var p in teams.SelectMany(t => t.Squad))
                if (ids.Add(p.Uid)) _planPlayers.Add(p);
            ApplyFilter();
            if (PlayerList.SelectedItem != null) Render(((Row)PlayerList.SelectedItem).Player);
        }

        // ---- list ---------------------------------------------------------------------------

        /// <summary>Opens the position / CA / PA search (the same window as in the Teams tab); the chosen player is shown in the list.</summary>
        private void FindByPosition_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null || _state.Fm == null) return;
            var teamOf = _state.Plan == null ? null : _state.Plan.Countries.SelectMany(c => c.AllTeams).Concat(_state.Plan.Avereine.Teams).ToList();
            var window = new PlayerPickerWindow(Window.GetWindow(this), _state.Fm, string.Empty,
                p => { var t = teamOf == null ? null : teamOf.FirstOrDefault(x => x.Squad.Contains(p)); return t == null ? null : t.Club.Name; },
                (owner, p) => new PlayerPreviewWindow(owner, _state, p).ShowDialog(),
                player => { SelectPlayer(player); return Tuple.Create(string.Empty, false); },
                Loc.T(22143), Loc.T(22144), true);
            window.ShowDialog();
        }

        /// <summary>Shows a player in the list (switching to all players when he is not in the plan).</summary>
        public void SelectPlayer(FmPlayer player)
        {
            if (!_planPlayers.Contains(player)) AllPlayersCheck.IsChecked = true;
            SearchBox.Text = player.DisplayName;
            ApplyFilter();
            var row = ((IEnumerable<Row>)PlayerList.ItemsSource).FirstOrDefault(r => r.Player == player);
            if (row != null) { PlayerList.SelectedItem = row; PlayerList.ScrollIntoView(row); }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { ApplyFilter(); }
        private void Filter_Changed(object sender, RoutedEventArgs e) { ApplyFilter(); }

        private void ApplyFilter()
        {
            if (_state == null || _state.Fm == null) return;
            var fm = _state.Fm;
            IEnumerable<FmPlayer> source = AllPlayersCheck.IsChecked == true ? (IEnumerable<FmPlayer>)fm.Players : _planPlayers;
            var query = (SearchBox.Text ?? string.Empty).Trim();
            var kept = PlayerList.SelectedItem as Row;
            var rows = new List<Row>();
            foreach (var p in source.OrderByDescending(x => x.CurrentAbility))
            {
                FmClub club;
                var clubName = p.ClubUid != null && fm.Clubs.TryGetValue(p.ClubUid, out club) ? club.Name : Loc.T(22006);
                if (query.Length > 0 &&
                    (p.DisplayName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    clubName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(new Row { Player = p, Text = p.DisplayName + "\n" + clubName + "  ·  CA " + p.CurrentAbility });
                if (rows.Count >= 300) break;
            }
            PlayerList.ItemsSource = rows;
            CountText.Text = rows.Count >= 300 ? Loc.T(25001) : Loc.T(25002, rows.Count);
            // show a result right away: the player that was selected before, else the first one of the list
            var again = kept == null ? null : rows.FirstOrDefault(r => r.Player == kept.Player);
            if (again != null) PlayerList.SelectedItem = again;
            else if (rows.Count > 0) PlayerList.SelectedIndex = 0;
        }

        private void PlayerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = PlayerList.SelectedItem as Row;
            if (row != null) Render(row.Player);
        }

        private void ShowEmpty()
        {
            FmPanel.Children.Clear();
            FmPanel.Children.Add(new TextBlock
            {
                Text = Loc.T(25003),
                Foreground = FmMuted, FontSize = 16, Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap
            });
            A2Notes.Text = string.Empty;
        }

        // ---- Football Manager side ----------------------------------------------------------

        private static Brush B(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static readonly Brush FmText = B("#F2F4FA"), FmMuted = B("#9AA4C4"), FmRow = B("#1A2140"), FmRowAlt = B("#151B36"),
            FmBadge = B("#0B0F1E"), FmGreen = B("#4CE06C"), FmGold = B("#F2B134"), FmChip = B("#252E55"), FmStar = B("#F2C14E");

        private static readonly string[] TechnicalOutfield =
            { "corners", "crossing", "dribbling", "finishing", "first_touch", "free_kick_taking", "heading", "long_shots", "long_throws", "marking", "passing", "penalty_taking", "tackling", "technique" };
        private static readonly string[] MentalAttributes =
            { "aggression", "anticipation", "bravery", "composure", "concentration", "decisions", "determination", "flair", "leadership", "off_the_ball", "positioning", "teamwork", "vision", "work_rate" };
        private static readonly string[] PhysicalAttributes =
            { "acceleration", "agility", "balance", "jumping_reach", "natural_fitness", "pace", "stamina", "strength" };
        private static readonly string[] GoalkeepingAttributes =
            { "aerial_reach", "command_of_area", "communication", "eccentricity", "first_touch", "handling", "kicking", "one_on_ones", "passing", "punching", "reflexes", "rushing_out", "throwing" };
        private static readonly string[] TechnicalGoalkeeper = { "free_kick_taking", "penalty_taking", "technique" };
        /// <summary>FM's "hidden" attributes (not shown on the attribute columns): injury proneness and the consistency group.</summary>
        private static readonly string[] HiddenAttributes =
            { "consistency", "dirtiness", "important_matches", "injury_proneness", "versatility" };
        private static readonly string[] PersonalityAttributes =
            { "adaptability", "ambition", "loyalty", "pressure", "professionalism", "sportsmanship", "temperament", "controversy" };

        private void Render(FmPlayer p)
        {
            RenderFm(p);
            RenderA2(p);
        }

        private void RenderFm(FmPlayer p)
        {
            var fm = _state.Fm;
            FmClub club = null;
            if (p.ClubUid != null) fm.Clubs.TryGetValue(p.ClubUid, out club);
            var nation = fm.NationBySaveId(p.NationId);
            var nationName = nation != null && nation.Name != null ? nation.Name : "Nation " + p.NationId;
            var nationCode = nation != null && nation.Code != null ? nation.Code : "???";

            FmPanel.Children.Clear();

            // header: name / club + nation / ability stars
            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = p.DisplayName, FontSize = 32, FontWeight = FontWeights.SemiBold, Foreground = FmText });
            left.Children.Add(FmChipText(PrimaryPositionName(p), new Thickness(0, 4, 0, 4)));
            left.Children.Add(new TextBlock
            {
                Text = string.Format("{0}   {1} years old{2}", nationCode, p.Age, string.IsNullOrEmpty(p.BirthDate) ? string.Empty : " (" + p.BirthDate + ")"),
                FontSize = 15, Foreground = FmText, Margin = new Thickness(0, 2, 0, 0)
            });
            header.Children.Add(left);

            var mid = new StackPanel { Margin = new Thickness(18, 4, 18, 0) };
            mid.Children.Add(FmChipText(club != null ? club.Name : "No club", new Thickness(0, 0, 0, 6)));
            mid.Children.Add(FmChipText(nationName, new Thickness(0, 0, 0, 0)));
            Grid.SetColumn(mid, 1);
            header.Children.Add(mid);

            var stars = new StackPanel { Margin = new Thickness(10, 4, 0, 0) };
            stars.Children.Add(StarRow("Current Ability", p.CurrentAbility));
            stars.Children.Add(StarRow("Potential Ability", p.PotentialAbility));
            Grid.SetColumn(stars, 2);
            header.Children.Add(stars);
            FmPanel.Children.Add(header);

            // natural / accomplished positions
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var kv in p.PositionRatings.Where(x => x.Value >= 12).OrderByDescending(x => x.Value))
                chips.Children.Add(PositionChip(kv.Key.ToUpperInvariant(), kv.Value));
            FmPanel.Children.Add(chips);

            // attribute columns + info
            var body = new Grid();
            for (var i = 0; i < 3; i++) body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });

            StackPanel c1, c2, c3;
            if (p.IsGoalkeeper)
            {
                c1 = AttributeColumn("Goalkeeping", GoalkeepingAttributes, p);
                c2 = AttributeColumn("Mental", MentalAttributes, p);
                c3 = AttributeColumn("Physical", PhysicalAttributes, p);
                AppendColumn(c3, "Technical", TechnicalGoalkeeper, p);
            }
            else
            {
                c1 = AttributeColumn("Technical", TechnicalOutfield, p);
                c2 = AttributeColumn("Mental", MentalAttributes, p);
                c3 = AttributeColumn("Physical", PhysicalAttributes, p);
            }
            Grid.SetColumn(c2, 1); Grid.SetColumn(c3, 2);
            body.Children.Add(c1); body.Children.Add(c2); body.Children.Add(c3);

            var info = InfoColumn(p);
            Grid.SetColumn(info, 3);
            body.Children.Add(info);
            FmPanel.Children.Add(body);
        }

        private static string PrimaryPositionName(FmPlayer p)
        {
            var best = p.PositionRatings.OrderByDescending(kv => kv.Value).First().Key;
            switch (best)
            {
                case "gk": return "Goalkeeper";
                case "sw": return "Sweeper";
                case "dl": return "Defender (Left)";
                case "dc": return "Defender (Centre)";
                case "dr": return "Defender (Right)";
                case "wbl": return "Wing-Back (Left)";
                case "wbr": return "Wing-Back (Right)";
                case "dm": return "Defensive Midfielder";
                case "mc": return "Midfielder (Centre)";
                case "ml": return "Midfielder (Left)";
                case "mr": return "Midfielder (Right)";
                case "aml": return "Attacking Midfielder (Left)";
                case "amr": return "Attacking Midfielder (Right)";
                case "amc": return "Attacking Midfielder (Centre)";
                default: return "Striker";
            }
        }

        private static UIElement FmChipText(string text, Thickness margin)
        {
            return new Border
            {
                Background = FmChip, CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = text, Foreground = FmText, FontSize = 14 }
            };
        }

        private static UIElement PositionChip(string text, int rating)
        {
            return new Border
            {
                Background = rating >= 18 ? B("#2F6B3E") : FmChip, CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 2, 9, 2),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = text + "  " + rating, Foreground = FmText, FontSize = 13 }
            };
        }

        private static UIElement StarRow(string label, int ability)
        {
            var stars = Math.Max(0, Math.Min(5, (int)Math.Round(ability / 40.0)));
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock { Text = label, Foreground = FmText, FontSize = 12, FontWeight = FontWeights.SemiBold });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = new string('★', stars) + new string('☆', 5 - stars), Foreground = FmStar, FontSize = 20 });
            row.Children.Add(new TextBlock { Text = "  " + ability, Foreground = FmMuted, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
            return panel;
        }

        private static StackPanel AttributeColumn(string title, string[] keys, FmPlayer p)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            AppendColumn(col, title, keys, p);
            return col;
        }

        private static void AppendColumn(StackPanel col, string title, string[] keys, FmPlayer p)
        {
            col.Children.Add(new TextBlock { Text = title, Foreground = FmText, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, col.Children.Count == 0 ? 0 : 14, 0, 6) });
            var alt = false;
            foreach (var key in keys)
            {
                var v = p.Attribute(key);
                var row = new Grid { Background = alt ? FmRowAlt : FmRow, Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
                row.Children.Add(new TextBlock { Text = Pretty(key), Foreground = FmText, FontSize = 14, Margin = new Thickness(10, 5, 4, 5), TextTrimming = TextTrimming.CharacterEllipsis });
                var badge = new Border
                {
                    Background = FmBadge, Margin = new Thickness(1),
                    Child = new TextBlock { Text = v.ToString(), Foreground = AttributeBrush(v), FontWeight = FontWeights.Bold, FontSize = 14, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                Grid.SetColumn(badge, 1);
                row.Children.Add(badge);
                col.Children.Add(row);
                alt = !alt;
            }
        }

        private static Brush AttributeBrush(int v)
        {
            return v >= 16 ? FmGreen : v >= 11 ? FmGold : FmText;
        }

        private static StackPanel InfoColumn(FmPlayer p)
        {
            var col = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            col.Children.Add(new TextBlock { Text = "Info", Foreground = FmText, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 8) });
            col.Children.Add(InfoItem("Height", p.HeightCm > 0 ? p.HeightCm + " cm" : "-"));
            col.Children.Add(InfoItem("Reputation", p.ReputationCurrent + " (world " + p.ReputationWorld + ")"));
            var feet = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            feet.ColumnDefinitions.Add(new ColumnDefinition()); feet.ColumnDefinitions.Add(new ColumnDefinition());
            var lf = InfoItem("Left Foot", FootLabel(p.Attribute("left_foot")));
            var rf = InfoItem("Right Foot", FootLabel(p.Attribute("right_foot")));
            Grid.SetColumn(rf, 1);
            feet.Children.Add(lf); feet.Children.Add(rf);
            col.Children.Add(feet);

            col.Children.Add(new TextBlock { Text = "Personality", Foreground = FmMuted, FontSize = 12, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var key in PersonalityAttributes.Where(k => p.Attributes.ContainsKey(k)))
                col.Children.Add(new TextBlock { Text = Pretty(key) + "  " + p.Attribute(key), Foreground = FmText, FontSize = 12.5 });

            col.Children.Add(new TextBlock { Text = "Hidden", Foreground = FmMuted, FontSize = 12, Margin = new Thickness(0, 10, 0, 2) });
            foreach (var key in HiddenAttributes.Where(k => p.Attributes.ContainsKey(k)))
                col.Children.Add(new TextBlock { Text = Pretty(key) + "  " + p.Attribute(key), Foreground = FmText, FontSize = 12.5 });

            col.Children.Add(new TextBlock { Text = p.Traits.Count + " traits", Foreground = FmText, FontSize = 13, Margin = new Thickness(0, 12, 0, 2) });
            foreach (var t in p.Traits.Take(6))
                col.Children.Add(new TextBlock { Text = t, Foreground = FmMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            return col;
        }

        private static UIElement InfoItem(string label, string value)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
            panel.Children.Add(new TextBlock { Text = label, Foreground = FmMuted, FontSize = 12 });
            panel.Children.Add(new TextBlock { Text = value, Foreground = FmText, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private static string FootLabel(int v)
        {
            return v >= 18 ? "Very Strong" : v >= 15 ? "Strong" : v >= 12 ? "Fairly Strong" : v >= 8 ? "Reasonable" : v >= 5 ? "Weak" : "Very Weak";
        }

        private static string Pretty(string key)
        {
            var words = key.Replace('_', ' ').Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1));
            return string.Join(" ", words);
        }

        // ---- Anstoss 2 side -----------------------------------------------------------------

        private void RenderA2(FmPlayer p)
        {
            var fm = _state.Fm;
            var mapper = _state.CreateMapper();
            var a = mapper.Map(p);
            FmClub club = null;
            if (p.ClubUid != null) fm.Clubs.TryGetValue(p.ClubUid, out club);

            _last.Text = a.LastName;
            _first.Text = a.FirstName;
            _club.Text = club != null ? (_state != null ? A2G.Convert.Writing.ClubTexts.Name(club, _state.Settings) : ClubNamer.ClubName(club)) : "vereinslos";
            _alter.Text = a.Age.ToString();
            _staerke.Text = a.Strength.ToString();
            _hp.Text = PositionNames[Math.Max(1, Math.Min(10, a.Position1))];
            var secondary = new[] { a.Position2, a.Position3 }.Where(x => x >= 1 && x <= 10).Select(x => PositionNames[x]).ToList();
            _np.Text = secondary.Count > 0 ? string.Join("\n", secondary) : "-";

            var abilities = _state.Settings.AbilityRules.Where(r => r.Goalkeeper == (a.Position1 == 1) && (a.PositiveSkills & r.Bit) != 0).ToList();
            var negatives = _state.Settings.AbilityRules.Where(r => r.Goalkeeper == (a.Position1 == 1) && (a.NegativeSkills & r.Bit) != 0).ToList();
            _abilityPanel.Children.Clear();
            foreach (var r in abilities) _abilityPanel.Children.Add(AbilityRow(r.Name, "pos"));
            foreach (var r in negatives) _abilityPanel.Children.Add(AbilityRow(r.Name, "neg"));

            _flag.Source = LoadFlag(a.Nationality);
            _face.Fill = new SolidColorBrush(SkinColor(a.Skin));
            _hair.Fill = new SolidColorBrush(HairColor(a.Hair));   // bald: transparent
            // players of the non-league clubs: the game changes some values when it loads them
            NonLeagueNote.Visibility = _state.Plan != null && _state.Plan.Avereine.Teams.Any(t => t.Squad.Contains(p)) ? Visibility.Visible : Visibility.Collapsed;
            _lookPlayer = p;
            _settingLook = true;
            SkinBox.Value = a.Skin;
            HairBox.Value = a.Hair;
            _settingLook = false;

            var notes = new List<string>();
            foreach (var r in abilities)
            {
                var vals = r.Attributes.Select(x => x + " " + p.Attribute(x));
                notes.Add(Loc.T(25004, AbilityNames.ContainsKey(r.Name) ? AbilityNames[r.Name] : r.Name, string.Join(", ", vals), r.PositiveAtLeast));
            }
            var earned = _state.Settings.TraitRules.Where(r => RuleApplies(r, a)).ToList();
            var sentences = earned.Select(r =>
                r.Name == "Talent" && p.Age >= 25 ? "Er gilt als ewiges Talent"
                : TraitSentences.ContainsKey(r.Name) ? TraitSentences[r.Name] : r.Name).ToList();
            _traitText.Text = string.Join("\n", sentences);
            foreach (var r in earned)
            {
                var vals = r.Conditions.Select(c => TraitEditor.Pretty(c.Attribute) + " " + TraitEngine.Value(p, c.Attribute));
                notes.Add(Loc.T(25007, TraitEngine.DisplayName(r.Name), string.Join(", ", vals)));
            }
            notes.Add(Loc.T(25005, a.Strength, p.CurrentAbility));
            notes.Add(Loc.T(25006));
            A2Notes.Text = string.Join("\n", notes);
        }

        /// <summary>A row of the ability list: the game's own symbol (green = positive, red = negative) and the name.</summary>
        private UIElement AbilityRow(string ruleName, string variant)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            var icon = LoadAsset("Icons/ability_" + ruleName.ToLowerInvariant() + "_" + variant + ".png");
            row.Children.Add(new Image { Source = icon, Width = 17, Height = 17, Margin = new Thickness(0, 0, 8, 0), SnapsToDevicePixels = true });
            var text = new TextBlock
            {
                Text = (AbilityNames.ContainsKey(ruleName) ? AbilityNames[ruleName] : ruleName) + (variant == "neg" ? " (schwach)" : string.Empty),
                Foreground = variant == "neg" ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xB4)) : Brushes.White,
                FontFamily = new FontFamily("Tahoma"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(text);
            return row;
        }

        /// <summary>
        /// The flag file of every nation in the order of LAENDER.SAV, as listed in anstoss2.exe (the game builds
        /// "FLAGGEN\GROSS\<name>.BMP" from this list; it is not the nation code: Germany is "Ger", Holland "Ni", Japan "Jpn",
        /// Slowakei "Swk", Slowenien "Slo", index 0 "Sonstige" is "Wel" = the white world flag with the ball).
        /// </summary>
        private static readonly string[] FlagFiles =
        {
            "Wel", "Egy", "Alb", "Alg", "Arg", "Aus", "Bel", "Bol", "Bra", "Bul", "Chl", "Chi", "Cos", "Dan", "Ger", "Ecu", "Eng", "Est", "Far", "Fin",
            "Fra", "Geo", "Gha", "Gri", "Ni", "Irl", "Isl", "Isr", "Ita", "Jam", "Jpn", "Kam", "Kan", "Kol", "Kro", "Let", "Lib", "Lit", "Lux", "Mal",
            "Mar", "Mex", "Mol", "Nz", "Nig", "Nir", "Nor", "A", "Par", "Per", "Pol", "Por", "Pue", "Rum", "Rus", "San", "Sau", "Sco", "Swe", "Ch",
            "Sen", "Swk", "Slo", "Spa", "Sko", "Tsc", "Tun", "Tur", "Ukr", "Hun", "Uru", "Usa", "Ven", "Wal", "Zai", "Zyp"
        };

        private BitmapImage LoadFlag(int nationIndex)
        {
            var laender = _state.Laender;
            if (laender == null || nationIndex < 0 || nationIndex >= laender.Nations.Count) return null;
            var name = nationIndex < FlagFiles.Length ? FlagFiles[nationIndex] : laender.Nations[nationIndex].Code;
            return LoadAsset("Flags/" + name.ToUpperInvariant() + ".png");
        }

        /// <summary>Loads an image that was extracted from the original game and compiled into the application.</summary>
        private static BitmapImage LoadAsset(string relativePath)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri("pack://application:,,,/Assets/" + relativePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception) { return null; }
        }

        private static Color SkinColor(int skin)
        {
            return skin == 2 ? Color.FromRgb(0x7A, 0x4B, 0x2A) : skin == 1 ? Color.FromRgb(0xC6, 0x8E, 0x5B) : Color.FromRgb(0xF2, 0xC9, 0xA5);
        }

        private static Color HairColor(int hair)
        {
            // game order: 0 very light blond, 1 golden blond, 2 brown, 3 red, 4 black, 5 bald (the preview draws no hair)
            var colors = new[] { "#EBDDA5", "#D9A441", "#74574A", "#C2501E", "#17100A", "#00000000" };
            return (Color)ColorConverter.ConvertFromString(colors[Math.Abs(hair) % colors.Length]);
        }

        // ---- the drawn window ---------------------------------------------------------------

        private Canvas BuildA2Canvas()
        {
            var c = new Canvas { Width = 596, Height = 480 };
            c.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x0B, 0x36, 0x1C), 0), new GradientStop(Color.FromRgb(0x1B, 0x55, 0x30), 0.45),
                    new GradientStop(Color.FromRgb(0x0E, 0x3F, 0x24), 0.7), new GradientStop(Color.FromRgb(0x06, 0x1E, 0x0E), 1)
                }, new Point(0, 0), new Point(1, 1));

            var title = new Border { Width = 596, Height = 24, Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFA)) };
            title.Child = new TextBlock { Text = "ANSTOSS 2 - Spielerinfo", Foreground = Brushes.Black, FontFamily = new FontFamily("Tahoma"), FontSize = 12, Margin = new Thickness(10, 4, 0, 0) };
            c.Children.Add(title);

            _last = T(c, "", 13, 36, 15, true);
            _first = T(c, "", 13, 62, 15, true);
            _club = T(c, "", 13, 90, 13);
            _flag = new Image { Width = 28, Height = 18, Stretch = Stretch.Fill };
            Canvas.SetLeft(_flag, 13); Canvas.SetTop(_flag, 108);
            c.Children.Add(_flag);

            T(c, "HP:", 86, 110, 12);
            _hp = T(c, "", 122, 110, 12);
            T(c, "NPs:", 86, 130, 12);
            _np = T(c, "", 122, 130, 12);

            _abilityPanel = new StackPanel();
            Canvas.SetLeft(_abilityPanel, 258); Canvas.SetTop(_abilityPanel, 34);
            c.Children.Add(_abilityPanel);

            // the sentences the game prints for traits, character and health state
            _traitText = T(c, "", 72, 176, 11.5, false, new SolidColorBrush(Color.FromRgb(0xE2, 0xEE, 0xE2)), 300);
            _traitText.TextWrapping = TextWrapping.Wrap;

            T(c, "Alter", 505, 36, 13);
            _alter = T(c, "", 536, 36, 13, false, null, 36, TextAlignment.Right);
            T(c, "Stärke", 505, 66, 13);
            _staerke = T(c, "", 536, 66, 13, false, null, 36, TextAlignment.Right);

            var portrait = new Border { Width = 36, Height = 48, Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x4B, 0x3F)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
            var pg = new Canvas();
            _face = new Ellipse { Width = 22, Height = 26 };
            Canvas.SetLeft(_face, 6); Canvas.SetTop(_face, 14);
            _hair = new Rectangle { Width = 24, Height = 12, RadiusX = 8, RadiusY = 8 };
            Canvas.SetLeft(_hair, 5); Canvas.SetTop(_hair, 8);
            pg.Children.Add(_face); pg.Children.Add(_hair);
            portrait.Child = pg;
            Canvas.SetLeft(portrait, 464); Canvas.SetTop(portrait, 34);
            c.Children.Add(portrait);

            T(c, "Entwicklung", 470, 96, 12);
            var dev = new Border { Width = 112, Height = 30, Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0xA3, 0x7A)), BorderThickness = new Thickness(1) };
            Canvas.SetLeft(dev, 470); Canvas.SetTop(dev, 114);
            c.Children.Add(dev);

            // gauges and runtime text
            for (var i = 0; i < 2; i++)
            {
                var bar = new Rectangle { Width = 14, Height = 62, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x1E, 0x18)), Stroke = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(bar, 20 + i * 22); Canvas.SetTop(bar, 168);
                c.Children.Add(bar);
            }
            T(c, "Ko", 18, 148, 11);
            T(c, "Fr", 42, 148, 11);
            T(c, "Er ist gesund.", 18, 238, 12);
            T(c, "Er ist zufrieden.", 244, 236, 12);

            var rows = new[] { "Tore total (1. Liga)", "Einsätze total (1. Liga)", "Platzverweise total (1. Liga)", "", "Freistöße / Saison", "Elfmeter / Saison", "Pokaltore (nat./int.)", "Joker (Tore/Einsätze)" };
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].Length == 0) continue;
                T(c, rows[i], 378, 154 + i * 14.5, 11);
                T(c, "0", 548, 154 + i * 14.5, 11, false, null, 24, TextAlignment.Right);
            }

            T(c, "Formverlauf", 18, 290, 11);
            foreach (var y in new[] { 283.0, 325.0 })
            {
                var box = new Border { Width = 296, Height = 34, Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0xA3, 0x7A)), BorderThickness = new Thickness(1) };
                Canvas.SetLeft(box, 132); Canvas.SetTop(box, y);
                c.Children.Add(box);
            }
            var stats = new[] { "Durchschnittsform", "Beste Form", "Schlechteste Form", "Einsätze / Saison", "Karten / Saison", "Durchschnittsnote", "Tore / Saison", "Vorlagen / Saison" };
            for (var i = 0; i < stats.Length; i++)
            {
                T(c, stats[i], 446, 284 + i * 14.5, 11);
                if (i >= 3) T(c, "-", 560, 284 + i * 14.5, 11, false, null, 22, TextAlignment.Right);
            }

            var money = new[] { "Ablöse bezahlt", "Marktwert", "Fixe Ablöse vereinbart", "Festgehalt", "Auflaufprämie", "Torprämie", "Stammplatzgarantie" };
            for (var i = 0; i < money.Length; i++)
            {
                T(c, money[i], 18, 372 + i * 14, 10.5);
                T(c, "im Spiel", 190, 372 + i * 14, 10.5, false, new SolidColorBrush(Color.FromRgb(0xB5, 0xD6, 0xB5)), 70, TextAlignment.Right);
            }
            T(c, "Vertrag und Marktwert werden", 290, 392, 11, false, new SolidColorBrush(Color.FromRgb(0xE2, 0xEE, 0xE2)));
            T(c, "beim Spielstart festgelegt.", 290, 406, 11, false, new SolidColorBrush(Color.FromRgb(0xE2, 0xEE, 0xE2)));
            return c;
        }

        private static TextBlock T(Canvas canvas, string text, double x, double y, double size, bool bold = false, Brush fg = null,
            double width = 0, TextAlignment align = TextAlignment.Left)
        {
            var tb = new TextBlock
            {
                Text = text, FontFamily = new FontFamily("Tahoma"), FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = fg ?? Brushes.White, TextAlignment = align
            };
            if (width > 0) tb.Width = width;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
            return tb;
        }
    }
}

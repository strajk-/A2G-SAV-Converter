using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// Finds FM players to add to a team without typing: click positions on a vertical pitch (like the FM position picker),
    /// set CA / PA ranges, optionally only players without a club. The list fills as soon as a filter is set and stays empty
    /// before that (the full player list is far too large to show).
    /// </summary>
    public partial class PlayerPickerWindow : Window
    {
        private sealed class Spot
        {
            public string Code, Name;
            public double X, Y;
            public Ellipse Dot;
        }

        private sealed class ResultRow
        {
            public FmPlayer Player;
            public string Name { get; set; }
            public int Age { get; set; }
            public string Club { get; set; }
            public string PositionText { get; set; }
            public string InPlan { get; set; }
            public int CA { get; set; }
            public int PA { get; set; }
        }

        private const int MaxRows = 200;

        /// <summary>FM positions on a vertical pitch, attack at the top (names as FM calls them).</summary>
        private static readonly Spot[] Spots =
        {
            new Spot { Code = "stc", Name = "Striker (Centre)", X = 100, Y = 50 },
            new Spot { Code = "aml", Name = "Attacking Midfielder (Left)", X = 38, Y = 86 },
            new Spot { Code = "amc", Name = "Attacking Midfielder (Centre)", X = 100, Y = 86 },
            new Spot { Code = "amr", Name = "Attacking Midfielder (Right)", X = 162, Y = 86 },
            new Spot { Code = "ml", Name = "Midfielder (Left)", X = 28, Y = 126 },
            new Spot { Code = "mc", Name = "Midfielder (Centre)", X = 100, Y = 126 },
            new Spot { Code = "mr", Name = "Midfielder (Right)", X = 172, Y = 126 },
            new Spot { Code = "dm", Name = "Defensive Midfielder", X = 100, Y = 164 },
            new Spot { Code = "wbl", Name = "Wing-Back (Left)", X = 28, Y = 182 },
            new Spot { Code = "wbr", Name = "Wing-Back (Right)", X = 172, Y = 182 },
            new Spot { Code = "dl", Name = "Defender (Left)", X = 42, Y = 212 },
            new Spot { Code = "dc", Name = "Defender (Centre)", X = 100, Y = 212 },
            new Spot { Code = "dr", Name = "Defender (Right)", X = 158, Y = 212 },
            new Spot { Code = "sw", Name = "Sweeper", X = 100, Y = 240 },
            new Spot { Code = "gk", Name = "Goalkeeper", X = 100, Y = 268 }
        };

        private static readonly Brush DotOff = Frozen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush DotHover = Frozen(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush DotOn = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)));

        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

        private readonly FmDataSet _fm;
        private readonly string _teamName;
        private readonly Func<FmPlayer, string> _teamOf;
        private readonly Action<Window, FmPlayer> _preview;
        private readonly Func<FmPlayer, Tuple<string, bool>> _add;
        private readonly HashSet<string> _picked = new HashSet<string>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        private bool _loading = true;
        private readonly bool _closeAfterAdd;

        /// <param name="teamOf">The name of the team that has the player in the plan, or null.</param>
        /// <param name="add">Adds a player to the team; returns the text to show and whether it is a problem.</param>
        public PlayerPickerWindow(Window owner, FmDataSet fm, string teamName, Func<FmPlayer, string> teamOf, Action<Window, FmPlayer> preview, Func<FmPlayer, Tuple<string, bool>> add,
            string title = null, string addText = null, bool closeAfterAdd = false)
        {
            InitializeComponent();
            _closeAfterAdd = closeAfterAdd;
            if (addText != null) AddButton.Content = addText;
            Owner = owner;
            _fm = fm;
            _teamName = teamName;
            _teamOf = teamOf;
            _preview = preview;
            _add = add;
            Title = title ?? Loc.T(22051, teamName);
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);

            DrawPitch();
            MinRatingBox.Text = "15";
            ResetRanges();
            var descriptor = DependencyPropertyDescriptor.FromProperty(Stepper.ValueProperty, typeof(Stepper));
            foreach (var box in new[] { MinRatingBox, CaMinBox, CaMaxBox, PaMinBox, PaMaxBox })
                descriptor.AddValueChanged(box, (o, e) => Filter_Changed(o, null));
            _timer.Tick += (s, e) => { _timer.Stop(); RefreshResults(); };
            _loading = false;
            RefreshResults();
        }

        private void ResetRanges()
        {
            CaMinBox.Text = "0"; CaMaxBox.Text = "200";
            PaMinBox.Text = "0"; PaMaxBox.Text = "200";
        }

        // ---- pitch --------------------------------------------------------------------------

        private void DrawPitch()
        {
            PitchDrawing.Draw(Pitch);

            foreach (var spot in Spots)
            {
                var dot = new Ellipse { Width = 20, Height = 20, Fill = DotOff, Stroke = Brushes.White, StrokeThickness = 1, Cursor = Cursors.Hand, ToolTip = spot.Name };
                Canvas.SetLeft(dot, spot.X - 10); Canvas.SetTop(dot, spot.Y - 10);
                var captured = spot;
                dot.MouseEnter += (s, e) => { if (!_picked.Contains(captured.Code)) dot.Fill = DotHover; };
                dot.MouseLeave += (s, e) => { if (!_picked.Contains(captured.Code)) dot.Fill = DotOff; };
                dot.MouseLeftButtonUp += (s, e) => Toggle(captured);
                spot.Dot = dot;
                Pitch.Children.Add(dot);
            }
            UpdatePickedText();
        }

        private void Toggle(Spot spot)
        {
            if (!_picked.Add(spot.Code)) _picked.Remove(spot.Code);
            spot.Dot.Fill = _picked.Contains(spot.Code) ? DotOn : DotOff;
            UpdatePickedText();
            Filter_Changed(null, null);
        }

        private void UpdatePickedText()
        {
            PickedText.Text = _picked.Count == 0 ? Loc.T(22070)
                : string.Join(", ", Spots.Where(s => _picked.Contains(s.Code)).Select(s => s.Code.ToUpperInvariant()));
        }

        // ---- filtering ----------------------------------------------------------------------

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            _timer.Stop();
            _timer.Start();   // wait a moment so holding a stepper does not filter 36,000 players at every step
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e) { Filter_Changed(sender, null); }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _loading = true;
            _picked.Clear();
            foreach (var s in Spots) s.Dot.Fill = DotOff;
            UpdatePickedText();
            MinRatingBox.Text = "15";
            ResetRanges();
            FreeOnlyCheck.IsChecked = false;
            NameBox.Text = string.Empty;
            _loading = false;
            RefreshResults();
        }

        private bool AnyFilter(string query)
        {
            return _picked.Count > 0 || query.Length >= 2 || FreeOnlyCheck.IsChecked == true
                || CaMinBox.IntValue > 0 || CaMaxBox.IntValue < 200 || PaMinBox.IntValue > 0 || PaMaxBox.IntValue < 200;
        }

        private void RefreshResults()
        {
            var query = (NameBox.Text ?? string.Empty).Trim();
            if (_fm == null || !AnyFilter(query))
            {
                ResultList.ItemsSource = null;
                CountText.Text = Loc.T(22060);
                return;
            }
            var minRating = MinRatingBox.IntValue;
            int caMin = CaMinBox.IntValue, caMax = CaMaxBox.IntValue, paMin = PaMinBox.IntValue, paMax = PaMaxBox.IntValue;
            var picked = _picked.ToList();
            var free = FreeOnlyCheck.IsChecked == true;

            var matches = _fm.Players.Where(p =>
                SquadSelector.Eligible(p)
                && p.CurrentAbility >= caMin && p.CurrentAbility <= caMax
                && p.PotentialAbility >= paMin && p.PotentialAbility <= paMax
                && (!free || p.ClubUid == null)
                && (picked.Count == 0 || picked.Any(code => { int v; return p.PositionRatings != null && p.PositionRatings.TryGetValue(code, out v) && v >= minRating; }))
                && (query.Length < 2 || (p.DisplayName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(p => p.CurrentAbility).ThenByDescending(p => p.PotentialAbility).ToList();

            var rows = matches.Take(MaxRows).Select(p =>
            {
                FmClub club = null;
                if (p.ClubUid != null) _fm.Clubs.TryGetValue(p.ClubUid, out club);
                return new ResultRow
                {
                    Player = p, Name = p.DisplayName, Age = p.Age, Club = club != null ? club.Name : Loc.T(22006),
                    PositionText = PositionText(p, picked), InPlan = _teamOf(p) ?? "-", CA = p.CurrentAbility, PA = p.PotentialAbility
                };
            }).ToList();
            ResultList.ItemsSource = rows;
            CountText.Text = matches.Count > MaxRows ? Loc.T(22062, MaxRows, matches.Count) : Loc.T(22061, matches.Count);
        }

        /// <summary>The picked positions with the player's rating, or his two best positions.</summary>
        private static string PositionText(FmPlayer p, List<string> picked)
        {
            if (p.PositionRatings == null || p.PositionRatings.Count == 0) return "-";
            var source = picked.Count > 0
                ? p.PositionRatings.Where(kv => picked.Contains(kv.Key))
                : p.PositionRatings.Where(kv => kv.Value > 0);
            return string.Join(" · ", source.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key.ToUpperInvariant() + " " + kv.Value));
        }

        // ---- adding -------------------------------------------------------------------------

        private void ResultList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UiHelpers.IsOnItem<ListViewItem>(e.OriginalSource)) Preview_Click(sender, e);
        }

        private void MagnifyRow_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as ResultRow;
            if (row != null) _preview(this, row.Player);
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var row = ResultList.SelectedItem as ResultRow;
            if (row == null) { Show(Loc.T(22010), true); return; }
            _preview(this, row.Player);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var row = ResultList.SelectedItem as ResultRow;
            if (row == null) { Show(Loc.T(22010), true); return; }
            var result = _add(row.Player);
            Show(result.Item1, result.Item2);
            if (!result.Item2 && _closeAfterAdd) { Close(); return; }
            if (!result.Item2) RefreshResults();   // the "in plan" column changes
        }

        private void Show(string text, bool problem)
        {
            StatusText.Text = text;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, problem ? "DangerBrush" : "SuccessBrush");
        }
    }
}

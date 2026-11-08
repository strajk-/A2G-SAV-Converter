using A2G.SavConverter.Localization;
using Svc = A2G.SavConverter.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Engine;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>One line of the ability rule grid; edits go straight to the underlying rule.</summary>
    public sealed class RuleRow
    {
        private readonly AbilityRule _rule;
        public RuleRow(AbilityRule rule) { _rule = rule; }
        public AbilityRule Rule { get { return _rule; } }

        public bool Enabled { get { return _rule.Enabled; } set { _rule.Enabled = value; } }
        public string Name { get { return _rule.Goalkeeper ? Loc.T(21014, _rule.Name) : _rule.Name; } }

        public string AttributesText
        {
            get { return string.Join(", ", _rule.Attributes ?? new string[0]); }
            set
            {
                _rule.Attributes = (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant().Replace(' ', '_')).Where(a => a.Length > 0).ToArray();
            }
        }

        public string Mode { get { return _rule.Mode; } set { _rule.Mode = value; } }
        public double Positive { get { return _rule.PositiveAtLeast; } set { _rule.PositiveAtLeast = Math.Max(0, Math.Min(20, value)); } }
        public double Negative { get { return _rule.NegativeAtMost; } set { _rule.NegativeAtMost = Math.Max(0, Math.Min(20, value)); } }
    }

    /// <summary>One line of the trait grid: a trait, character or health rule and how many FM players meet it.</summary>
    public sealed class TraitRow
    {
        private readonly TraitRule _rule;
        public TraitRow(TraitRule rule) { _rule = rule; }
        public TraitRule Rule { get { return _rule; } }
        /// <summary>Players meeting the conditions, or -1 while no FM data is loaded.</summary>
        public int Count { get; set; }

        public bool Enabled { get { return _rule.Enabled; } set { _rule.Enabled = value; } }
        public string Name { get { return TraitEngine.DisplayName(_rule.Name); } }
        public string ConditionsText { get { return TraitEditor.Describe(_rule.Conditions); } }
        public string CountText { get { return Count < 0 ? "–" : Count.ToString("N0", CultureInfo.CurrentCulture); } }

        public string KindText
        {
            get
            {
                return Loc.T(_rule.Kind == TraitRule.KindCharacter ? 21021 : _rule.Kind == TraitRule.KindHealth ? 21022
                    : _rule.Kind == TraitRule.KindTalent ? 21023 : 21020);
            }
        }
    }

    /// <summary>Editor for the conversion settings (mapping\settings.json of the project).</summary>
    public partial class OptionsTab : UserControl
    {
        private AppState _state;
        private Svc.Stepper[] _bands;
        private Svc.Stepper[] _quota;
        private Svc.Stepper[] _avQuota;

        /// <summary>Raised after settings were applied; the preview and plan can refresh.</summary>
        public event Action<bool> Applied;

        public OptionsTab()
        {
            InitializeComponent();
            _bands = new[] { Band0, Band1, Band2, Band3, Band4, Band5 };
            _quota = new[] { QuotaBox0, QuotaBox1, QuotaBox2, QuotaBox3, QuotaBox4, QuotaBox5, QuotaBox6, QuotaBox7, QuotaBox8, QuotaBox9 };
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Svc.Stepper.ValueProperty, typeof(Svc.Stepper));
            foreach (var box in _quota) descriptor.AddValueChanged(box, (o, e) => UpdateQuotaTotal());
            _avQuota = new[] { AvQuotaBox0, AvQuotaBox1, AvQuotaBox2, AvQuotaBox3, AvQuotaBox4, AvQuotaBox5, AvQuotaBox6, AvQuotaBox7, AvQuotaBox8, AvQuotaBox9 };
            foreach (var box in _avQuota) descriptor.AddValueChanged(box, (o, e) => UpdateQuotaTotal());
        }

        private bool _fillingPriority;

        /// <summary>The three presets of the potential weight (current ability only, balanced, potential first).</summary>
        private static readonly int[] PriorityPresets = { 0, 50, 100 };

        private void FillPriorityBox(int weight)
        {
            _fillingPriority = true;
            PriorityBox.ItemsSource = new[] { Loc.T(21054), Loc.T(21055), Loc.T(21056), Loc.T(21059) };
            var index = Array.IndexOf(PriorityPresets, weight);
            PriorityBox.SelectedIndex = index >= 0 ? index : 3;
            _fillingPriority = false;
        }

        private void PriorityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingPriority || PriorityBox.SelectedIndex < 0 || PriorityBox.SelectedIndex >= PriorityPresets.Length) return;
            PotentialBox.Text = PriorityPresets[PriorityBox.SelectedIndex].ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateQuotaTotal()
        {
            QuotaTotalText.Text = Loc.T(21052, _quota.Sum(b => b.IntValue));
            AvQuotaTotalText.Text = Loc.T(21052, _avQuota.Sum(b => b.IntValue));
        }

        /// <summary>Re-reads the localized rule names after a language change.</summary>
        public void RefreshLanguage()
        {
            RuleGrid.Items.Refresh();
            TraitGrid.Items.Refresh();
            if (_state != null && _state.Settings != null) FillPriorityBox(PotentialBox.IntValue);
        }

        /// <summary>Counts how many players of the loaded FM save meet each trait rule (shown as "–" until a plan was built).</summary>
        public void RefreshCounts()
        {
            var rows = TraitGrid.ItemsSource as List<TraitRow>;
            if (rows == null) return;
            var players = _state != null && _state.Fm != null ? _state.Fm.Players : null;
            foreach (var row in rows) row.Count = players == null ? -1 : TraitEngine.Count(row.Rule, players);
            TraitGrid.Items.Refresh();
        }

        private void EditConditions_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as TraitRow;
            if (row == null) return;
            var result = TraitEditor.Edit(Window.GetWindow(this), row.Name, row.Rule.Conditions);
            if (result == null) return;
            row.Rule.Conditions = result;
            RefreshCounts();
            StatusText.Text = Loc.T(21000);
        }

        public void Attach(AppState state)
        {
            _state = state;
        }

        /// <summary>Shows the settings of the open project.</summary>
        public void Load()
        {
            if (_state == null) return;
            var s = _state.Settings;
            SquadSizeBox.Text = s.SquadSize.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < 10; i++) _quota[i].Text = s.SquadQuota[i].ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < 10; i++) _avQuota[i].Text = s.AvereineQuota[i].ToString(CultureInfo.InvariantCulture);
            PotentialBox.Text = s.PotentialWeight.ToString(CultureInfo.InvariantCulture);
            FillPriorityBox(s.PotentialWeight);
            UpdateQuotaTotal();
            KeepersBox.Text = s.MinGoalkeepers.ToString(CultureInfo.InvariantCulture);
            MinPlayersBox.Text = s.MinPlayersForTeam.ToString(CultureInfo.InvariantCulture);
            TeamMinBox.Text = s.TeamMinimum.ToString(CultureInfo.InvariantCulture);
            CapBox.Text = s.AvereinePerNationCap.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < 6; i++) _bands[i].Text = s.StrengthBands[i].ToString(CultureInfo.InvariantCulture);
            SecondaryBox.Text = s.SecondaryPositionMinRating.ToString(CultureInfo.InvariantCulture);
            MaxPosBox.Text = s.MaxPositiveAbilities.ToString(CultureInfo.InvariantCulture);
            MaxNegBox.Text = s.MaxNegativeAbilities.ToString(CultureInfo.InvariantCulture);
            RuleGrid.ItemsSource = s.AbilityRules.Select(r => new RuleRow(r)).ToList();
            MaxTraitsBox.Text = s.MaxTraits.ToString(CultureInfo.InvariantCulture);
            TraitGrid.ItemsSource = s.TraitRules.Select(r => new TraitRow(r)).ToList();
            RefreshCounts();
            StatusText.Text = string.Empty;
        }

        private void EditAttributes_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as RuleRow;
            if (row == null) return;
            var result = A2G.SavConverter.Services.AttributePicker.Pick(Window.GetWindow(this), row.Rule.Name, row.Rule.Attributes);
            if (result == null) return;
            row.Rule.Attributes = result;
            RuleGrid.Items.Refresh();
        }

        private void RuleGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            StatusText.Text = Loc.T(21000);
        }

        // ---- reading the fields -------------------------------------------------------------

        /// <summary>Validates the fields and copies them into the settings; returns a message on the first problem.</summary>
        private string ReadFields()
        {
            var s = _state.Settings;
            int size, keepers, minPlayers, teamMin, cap, secondary, maxPos, maxNeg, maxTraits;
            if (!Int(MaxTraitsBox, 1, 3, out maxTraits)) maxTraits = 1;
            if (!Int(SquadSizeBox, 16, 30, out size)) return Loc.T(21001);
            if (!Int(KeepersBox, 1, 4, out keepers)) return Loc.T(21002);
            if (!Int(MinPlayersBox, 1, 30, out minPlayers)) return Loc.T(21003);
            if (!Int(TeamMinBox, 16, 30, out teamMin)) return Loc.T(21004);
            if (!Int(CapBox, 1, 40, out cap)) return Loc.T(21005);
            if (!Int(SecondaryBox, 1, 20, out secondary)) return Loc.T(21006);
            if (!Int(MaxPosBox, 1, 2, out maxPos)) return Loc.T(21007);
            if (!Int(MaxNegBox, 0, 2, out maxNeg)) return Loc.T(21008);

            var bands = new int[6];
            for (var i = 0; i < 6; i++)
            {
                if (!Int(_bands[i], 1, 200, out bands[i])) return Loc.T(21009);
                if (i > 0 && bands[i] <= bands[i - 1]) return Loc.T(21010);
            }

            s.SquadQuota = _quota.Select(b => b.IntValue).ToArray();
            s.AvereineQuota = _avQuota.Select(b => b.IntValue).ToArray();
            s.AvereineSquadSize = Math.Max(1, Math.Min(30, s.AvereineQuota.Sum()));
            s.PotentialWeight = Math.Max(0, Math.Min(100, PotentialBox.IntValue));
            s.SquadSize = size; s.MinGoalkeepers = keepers; s.MinPlayersForTeam = minPlayers; s.TeamMinimum = teamMin;
            s.AvereinePerNationCap = cap; s.StrengthBands = bands; s.SecondaryPositionMinRating = secondary;
            s.MaxPositiveAbilities = maxPos; s.MaxNegativeAbilities = maxNeg; s.MaxTraits = maxTraits;
            return null;
        }

        private static bool Int(Svc.Stepper box, int min, int max, out int value)
        {
            return int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }

        // ---- buttons ------------------------------------------------------------------------

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Apply();
        }

        private bool Apply()
        {
            if (_state == null || _state.Project == null) return false;
            RuleGrid.CommitEdit(DataGridEditingUnit.Row, true);
            TraitGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var problem = ReadFields();
            if (problem != null)
            {
                StatusText.Text = problem;
                return false;
            }
            StatusText.Text = Loc.T(21011);
            if (Applied != null) Applied(true);
            return true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Apply()) return;
            _state.SaveSettings();
            StatusText.Text = Loc.T(21012, _state.SettingsPath);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null || _state.Project == null) return;
            var clubInfo = _state.Settings.ClubInfo;   // the user's city / nickname / stadium texts are not conversion settings: keep them
            var looks = _state.Settings.PlayerLooks;   // skin and hair the user set for single players are kept as well
            var seasonSlots = _state.Settings.SeasonSlots;   // and the results of the last season (champion, UEFA places, ...)
            var startYear = _state.Settings.StartYear;       // and the start year chosen on the Write & Install tab
            _state.Settings = new ConversionSettings();
            if (clubInfo != null) _state.Settings.ClubInfo = clubInfo;
            if (looks != null) _state.Settings.PlayerLooks = looks;
            if (seasonSlots != null) _state.Settings.SeasonSlots = seasonSlots;
            _state.Settings.StartYear = startYear;
            Load();
            if (Applied != null) Applied(true);
            StatusText.Text = Loc.T(21013);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Engine;
using A2G.Convert.Fm;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// The winners of last season's European cups (Champions League, Pokal der Pokalsieger, UEFA-Pokal). FM saves have no trophy data, so the user
    /// chooses a team of the five main nations for each; "Default" lets the rule of <see cref="TitleHolders"/> pick one. Each list can be typed into
    /// to find a club quickly.
    /// </summary>
    public sealed class EuropeanCupsWindow : Window
    {
        private sealed class Entry
        {
            public FmClub Club;      // null = the default
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly AppState _state;
        private readonly SearchPicker[] _pickers = new SearchPicker[3];

        public EuropeanCupsWindow(Window owner, AppState state)
        {
            _state = state;
            Title = Loc.T(28141);
            Owner = owner;
            Width = 680; SizeToContent = SizeToContent.Height; MinWidth = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, "BgBrush");
            SetResourceReference(ForegroundProperty, "TextBrush");
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);

            var root = new StackPanel { Margin = new Thickness(16) };
            var info = new TextBlock { Text = Loc.T(28142), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            info.SetResourceReference(StyleProperty, "Muted");
            root.Children.Add(info);

            var candidates = TitleHolders.Candidates(state.Plan)
                .Select(c => new { Club = c, Country = CountryOf(c) }).OrderBy(x => x.Country, StringComparer.Ordinal).ThenBy(x => x.Club.Name, StringComparer.Ordinal).ToList();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labels = new[] { Loc.T(28143), Loc.T(28144), Loc.T(28145) };
            foreach (var competition in TitleHolders.Competitions)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = labels[competition], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
                Grid.SetRow(label, competition);
                grid.Children.Add(label);

                var items = new List<object>();
                var rule = TitleHolders.Default(state.Plan, competition);
                items.Add(new Entry { Club = null, Text = Loc.T(28146, rule == null ? "-" : rule.Name) });
                items.AddRange(candidates.Select(x => (object)new Entry { Club = x.Club, Text = x.Country + "  " + x.Club.Name }));
                var picker = new SearchPicker { Margin = new Thickness(0, 5, 0, 5), Placeholder = Loc.T(22198), AlwaysShown = i => ((Entry)i).Club == null };
                picker.Items = items;
                var chosen = TitleHolders.Chosen(state.Settings, competition);
                picker.SelectedItem = items.Cast<Entry>().FirstOrDefault(i => i.Club != null && i.Club.Uid == chosen) ?? items[0];
                Grid.SetRow(picker, competition); Grid.SetColumn(picker, 1);
                grid.Children.Add(picker);
                _pickers[competition] = picker;
            }
            root.Children.Add(grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var save = new Button { Content = Loc.T(28147), MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            save.SetResourceReference(StyleProperty, "PrimaryButton");
            save.Click += (s, e) => Save();
            var cancel = new Button { Content = Loc.T(24004), MinWidth = 100, IsCancel = true };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        private string CountryOf(FmClub club)
        {
            var country = _state.Plan.Countries.FirstOrDefault(c => c.AllTeams.Any(t => t.Club == club));
            return country == null ? string.Empty : country.Spec.Key;
        }

        private void Save()
        {
            foreach (var competition in TitleHolders.Competitions)
            {
                var entry = _pickers[competition].SelectedItem as Entry;
                TitleHolders.Choose(_state.Settings, competition, entry == null || entry.Club == null ? null : entry.Club.Uid);
            }
            _state.SaveSettings();
            DialogResult = true;
        }
    }
}

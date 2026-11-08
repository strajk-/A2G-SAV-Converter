using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using A2G.Convert.Engine;
using A2G.SavConverter.Localization;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// Dialog for the conditions of one trait rule: pick a value with the drop-down and "+ Add", set "at least" / "at most"
    /// with the steppers (0 = no limit) and remove a condition with "−". No typing.
    /// </summary>
    public static class TraitEditor
    {
        /// <summary>All values a condition can test: FM attributes, FM personality and the pseudo values (age, CA, PA, growth room).</summary>
        public static IEnumerable<string> AllValues()
        {
            return AttributePicker.AllAttributes.Concat(TraitEngine.PersonalityValues).Concat(TraitEngine.PseudoValues).Distinct();
        }

        public static string Pretty(string value)
        {
            switch (value)
            {
                case TraitEngine.Age: return Loc.T(21032);
                case TraitEngine.CurrentAbility: return Loc.T(21033);
                case TraitEngine.PotentialAbility: return Loc.T(21034);
                case TraitEngine.GrowthRoom: return Loc.T(21035);
                default: return AttributePicker.Pretty(value);
            }
        }

        private static int Maximum(string value)
        {
            return value == TraitEngine.CurrentAbility || value == TraitEngine.PotentialAbility || value == TraitEngine.GrowthRoom ? 200
                : value == TraitEngine.Age ? 50 : 20;
        }

        private static int StepFor(string value)
        {
            return value == TraitEngine.CurrentAbility || value == TraitEngine.PotentialAbility || value == TraitEngine.GrowthRoom ? 5 : 1;
        }

        /// <summary>A short text such as "Leadership ≥ 17 · Work rate 14–16".</summary>
        public static string Describe(IEnumerable<TraitCondition> conditions)
        {
            var parts = (conditions ?? new TraitCondition[0]).Select(c =>
            {
                var name = Pretty(c.Attribute);
                if (c.Min > 0 && c.Max > 0) return name + " " + c.Min + "–" + c.Max;
                if (c.Min > 0) return name + " ≥ " + c.Min;
                if (c.Max > 0) return name + " ≤ " + c.Max;
                return name;
            }).ToList();
            return parts.Count == 0 ? Loc.T(21029) : string.Join("  ·  ", parts);
        }

        private sealed class Line
        {
            public TraitCondition Condition;
            public Stepper Min;
            public Stepper Max;
        }

        /// <summary>Returns the edited conditions, or null when cancelled.</summary>
        public static List<TraitCondition> Edit(Window owner, string ruleName, IEnumerable<TraitCondition> current)
        {
            var lines = (current ?? new TraitCondition[0]).Select(c => new Line { Condition = new TraitCondition(c.Attribute, c.Min, c.Max) }).ToList();
            var window = new Window
            {
                Title = Loc.T(21028, ruleName),
                Width = 600, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgBrush"),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush")
            };
            var combo = new ComboBox { Width = 270, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var add = new Button { Content = Loc.T(24001), Width = 110, Height = 30, Padding = new Thickness(0) };
            var list = new StackPanel();
            var scroll = new ScrollViewer { Content = list, Height = 270, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) };

            Action sync = () =>
            {
                foreach (var l in lines)
                {
                    if (l.Min != null) l.Condition.Min = l.Min.IntValue;
                    if (l.Max != null) l.Condition.Max = l.Max.IntValue;
                }
            };

            Action refresh = null;
            refresh = () =>
            {
                combo.ItemsSource = AllValues().Where(a => !lines.Any(l => l.Condition.Attribute == a))
                    .Select(a => new ComboBoxItem { Content = Pretty(a), Tag = a }).OrderBy(i => (string)i.Content).ToList();
                combo.SelectedIndex = -1;
                list.Children.Clear();
                if (lines.Count > 0)
                {
                    var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    SetColumns(header);
                    var atLeast = new TextBlock { Text = Loc.T(10068), Style = (Style)Application.Current.FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center };
                    var atMost = new TextBlock { Text = Loc.T(21036), Style = (Style)Application.Current.FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center };
                    Grid.SetColumn(atLeast, 1); Grid.SetColumn(atMost, 2);
                    header.Children.Add(atLeast); header.Children.Add(atMost);
                    list.Children.Add(header);
                }
                foreach (var l in lines.ToList())
                {
                    var attribute = l.Condition.Attribute;
                    var max = Maximum(attribute);
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    SetColumns(row);
                    var name = new TextBlock { Text = Pretty(attribute), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    l.Min = new Stepper { Min = 0, Max = max, Step = StepFor(attribute), Width = 110, Value = l.Condition.Min };
                    l.Max = new Stepper { Min = 0, Max = max, Step = StepFor(attribute), Width = 110, Value = l.Condition.Max };
                    var remove = new Button { Content = "−", Width = 30, Height = 26, Padding = new Thickness(0), ToolTip = Loc.T(24002) };
                    var captured = l;
                    remove.Click += (s, e) => { sync(); lines.Remove(captured); refresh(); };
                    Grid.SetColumn(l.Min, 1); Grid.SetColumn(l.Max, 2); Grid.SetColumn(remove, 3);
                    row.Children.Add(name); row.Children.Add(l.Min); row.Children.Add(l.Max); row.Children.Add(remove);
                    list.Children.Add(row);
                }
                if (lines.Count == 0) list.Children.Add(new TextBlock { Text = Loc.T(21029), Opacity = 0.6 });
            };
            add.Click += (s, e) =>
            {
                var item = combo.SelectedItem as ComboBoxItem;
                if (item == null) return;
                sync();
                lines.Add(new Line { Condition = new TraitCondition((string)item.Tag, 0, 0) });
                refresh();
            };
            refresh();

            List<TraitCondition> result = null;
            var ok = new Button { Content = Loc.T(24006), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc.T(24004), Width = 90, Height = 32, Padding = new Thickness(0), IsCancel = true };
            ok.Click += (s, e) => { sync(); result = lines.Select(l => l.Condition).ToList(); window.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(combo); top.Children.Add(add);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = Loc.T(21030), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(top); panel.Children.Add(scroll); panel.Children.Add(buttons);
            window.Content = panel;
            return window.ShowDialog() == true ? result : null;
        }

        private static void SetColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        }
    }
}

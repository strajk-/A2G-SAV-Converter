using A2G.SavConverter.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace A2G.SavConverter.Services
{
    /// <summary>Dialog to pick the FM attributes of an ability rule: a drop-down with "+ Add" and a list with "-" per entry.</summary>
    public static class AttributePicker
    {
        /// <summary>All FM26 player attributes as exported by fmsave (without the "attributes_" prefix).</summary>
        public static readonly string[] AllAttributes =
        {
            "acceleration", "aggression", "agility", "anticipation", "balance", "bravery", "command_of_area", "composure",
            "concentration", "consistency", "corners", "crossing", "decisions", "determination", "dirtiness", "dribbling",
            "eccentricity", "finishing", "first_touch", "flair", "free_kick_taking", "handling", "heading", "important_matches",
            "injury_proneness", "jumping_reach", "aerial_reach", "kicking", "leadership", "long_shots", "long_throws", "marking",
            "natural_fitness", "off_the_ball", "one_on_ones", "pace", "passing", "penalty_taking", "positioning", "punching",
            "reflexes", "rushing_out", "stamina", "strength", "tackling", "teamwork", "technique", "throwing", "versatility",
            "vision", "work_rate", "communication", "left_foot", "right_foot"
        };

        public static string Pretty(string name)
        {
            var s = name.Replace('_', ' ');
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Returns the new attribute list, or null when cancelled.</summary>
        public static string[] Pick(Window owner, string abilityName, IEnumerable<string> current)
        {
            var selected = new List<string>(current ?? new string[0]);
            var window = new Window
            {
                Title = Loc.T(24000, abilityName),
                Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner,
                Background = (System.Windows.Media.Brush)Application.Current.FindResource("BgBrush"),
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush")
            };
            var combo = new ComboBox { Width = 240, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var add = new Button { Content = Loc.T(24001), Width = 110, Height = 30, Padding = new Thickness(0) };
            var list = new StackPanel();
            var scroll = new ScrollViewer { Content = list, Height = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) };

            Action refresh = null;
            refresh = () =>
            {
                combo.ItemsSource = AllAttributes.Where(a => !selected.Contains(a)).OrderBy(a => a).Select(a => new ComboBoxItem { Content = Pretty(a), Tag = a }).ToList();
                combo.SelectedIndex = -1;
                list.Children.Clear();
                foreach (var attr in selected.ToList())
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var remove = new Button { Content = "−", Width = 30, Height = 26, Padding = new Thickness(0), ToolTip = Loc.T(24002) };
                    DockPanel.SetDock(remove, Dock.Right);
                    var captured = attr;
                    remove.Click += (s, e) => { selected.Remove(captured); refresh(); };
                    row.Children.Add(remove);
                    row.Children.Add(new TextBlock { Text = Pretty(attr), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                    list.Children.Add(row);
                }
                if (selected.Count == 0) list.Children.Add(new TextBlock { Text = Loc.T(24003), Opacity = 0.6 });
            };
            add.Click += (s, e) =>
            {
                var item = combo.SelectedItem as ComboBoxItem;
                if (item == null) return;
                selected.Add((string)item.Tag);
                refresh();
            };
            refresh();

            string[] result = null;
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc.T(24004), Width = 90, Height = 32, Padding = new Thickness(0), IsCancel = true };
            ok.Click += (s, e) => { result = selected.ToArray(); window.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(combo); top.Children.Add(add);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = Loc.T(24005), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(top); panel.Children.Add(scroll); panel.Children.Add(buttons);
            window.Content = panel;
            return window.ShowDialog() == true ? result : null;
        }
    }
}

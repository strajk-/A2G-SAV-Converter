using A2G.SavConverter.Localization;
using System.Windows;
using System.Windows.Controls;

namespace A2G.SavConverter.Services
{
    /// <summary>A minimal "enter a text" dialog (WPF has no built-in input box).</summary>
    public static class InputDialog
    {
        /// <summary>Asks for a text; the optional <paramref name="info"/> is shown below the box (for example a preview of what the answer leads to).</summary>
        public static string Ask(Window owner, string title, string prompt, string defaultValue, string info = null)
        {
            var box = new TextBox { Text = defaultValue ?? string.Empty, Margin = new Thickness(0, 6, 0, 12), MinWidth = 420 };
            string result = null;
            var window = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("BgBrush"),
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("TextBrush")
            };
            var ok = new Button { Content = Loc.T(24006), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = Loc.T(24004), Width = 80, IsCancel = true };
            ok.Click += (s, e) => { result = box.Text; window.DialogResult = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            panel.Children.Add(box);
            if (!string.IsNullOrEmpty(info))
                panel.Children.Add(new TextBlock { Text = info, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 0, 12), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(buttons);
            window.Content = panel;
            box.Focus();
            box.SelectAll();
            return window.ShowDialog() == true ? result : null;
        }
    }
}

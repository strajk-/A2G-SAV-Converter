using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using A2G.SavConverter.Localization;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// A message box in the look of the application (theme colours, rounded card, the app's buttons) instead of the Windows one.
    /// Same use as <see cref="MessageBox.Show(Window, string, string, MessageBoxButton, MessageBoxImage)"/>.
    /// </summary>
    public static class ThemedMessageBox
    {
        public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var result = buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.None : MessageBoxResult.OK;
            var window = new Window
            {
                Title = title ?? string.Empty,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 380,
                MaxWidth = 640,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner != null && owner.IsVisible ? owner : null,
                Background = (Brush)Application.Current.FindResource("BgBrush"),
                Foreground = (Brush)Application.Current.FindResource("TextBrush")
            };
            window.SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(window);

            // icon (Segoe MDL2 Assets) and text
            var glyph = icon == MessageBoxImage.Warning ? "" : icon == MessageBoxImage.Error ? "" : icon == MessageBoxImage.Question ? "" : icon == MessageBoxImage.Information ? "" : null;
            var body = new DockPanel { Margin = new Thickness(20, 18, 20, 12) };
            if (glyph != null)
            {
                var symbol = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Margin = new Thickness(0, 2, 16, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                symbol.SetResourceReference(TextBlock.ForegroundProperty, icon == MessageBoxImage.Warning || icon == MessageBoxImage.Error ? "DangerBrush" : "AccentBrush");
                DockPanel.SetDock(symbol, Dock.Left);
                body.Children.Add(symbol);
            }
            body.Children.Add(new TextBlock { Text = message ?? string.Empty, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center });

            // buttons
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 4, 20, 18) };
            Action<string, MessageBoxResult, bool, bool> add = (text, value, isDefault, isCancel) =>
            {
                var button = new Button { Content = text, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
                if (isDefault) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
                button.Click += (s, e) => { result = value; window.DialogResult = true; };
                row.Children.Add(button);
            };
            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    add(Loc.T(24007), MessageBoxResult.Yes, true, false);
                    add(Loc.T(24008), MessageBoxResult.No, false, true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    add(Loc.T(24007), MessageBoxResult.Yes, true, false);
                    add(Loc.T(24008), MessageBoxResult.No, false, false);
                    add(Loc.T(24004), MessageBoxResult.Cancel, false, true);
                    break;
                case MessageBoxButton.OKCancel:
                    add(Loc.T(24006), MessageBoxResult.OK, true, false);
                    add(Loc.T(24004), MessageBoxResult.Cancel, false, true);
                    break;
                default:
                    add(Loc.T(24006), MessageBoxResult.OK, true, true);
                    break;
            }

            var panel = new StackPanel();
            panel.Children.Add(body);
            panel.Children.Add(row);
            window.Content = panel;
            // closing with the window's X counts as "Cancel" / "No" / "OK" like the Windows box
            window.Closing += (s, e) =>
            {
                if (result == MessageBoxResult.None)
                    result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
            };
            window.ShowDialog();
            return result;
        }
    }
}

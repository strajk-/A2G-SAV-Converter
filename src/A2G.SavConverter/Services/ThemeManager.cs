using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace A2G.SavConverter.Services
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>Loads the light or dark palette: follows the Windows app theme by default, the user can override it (remembered per user).</summary>
    public static class ThemeManager
    {
        private static readonly string PrefPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", "theme.txt");
        private static ThemeMode _mode = ThemeMode.System;

        public static ThemeMode Mode { get { return _mode; } }

        public static void Initialize()
        {
            try
            {
                ThemeMode saved;
                if (File.Exists(PrefPath) && Enum.TryParse(File.ReadAllText(PrefPath).Trim(), out saved)) _mode = saved;
            }
            catch { }
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_mode == ThemeMode.System && Application.Current != null) Application.Current.Dispatcher.BeginInvoke(new Action(Apply));
            };
            Apply();
        }

        public static void SetMode(ThemeMode mode)
        {
            _mode = mode;
            try { Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)); File.WriteAllText(PrefPath, mode.ToString()); } catch { }
            Apply();
        }

        public static bool SystemUsesDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static bool IsDark { get { return _mode == ThemeMode.Dark || (_mode == ThemeMode.System && SystemUsesDark()); } }

        private static void Apply()
        {
            var app = Application.Current;
            if (app == null) return;
            var dict = new ResourceDictionary { Source = new Uri("pack://application:,,,/A2G-SAV-Converter;component/Themes/" + (IsDark ? "Dark" : "Light") + ".xaml") };
            app.Resources.MergedDictionaries[0] = dict;
            ApplySystemBrushes(app, dict);
            foreach (Window w in app.Windows) ApplyTitleBar(w);
        }

        /// <summary>
        /// Controls that fall back to Windows' selection colors (DataGrid, ListBox, ...) would show the light system highlight
        /// even in dark mode, so those system brushes are replaced by the palette colors.
        /// </summary>
        private static void ApplySystemBrushes(Application app, ResourceDictionary palette)
        {
            Func<string, System.Windows.Media.Brush> brush = key => new System.Windows.Media.SolidColorBrush(((System.Windows.Media.SolidColorBrush)palette[key]).Color);
            app.Resources[SystemColors.HighlightBrushKey] = brush("AccentSoftBrush");
            app.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = brush("AccentSoftBrush");
            app.Resources[SystemColors.HighlightTextBrushKey] = brush("TextBrush");
            app.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = brush("TextBrush");
            app.Resources[SystemColors.ControlBrushKey] = brush("CardBrush");
            app.Resources[SystemColors.ControlTextBrushKey] = brush("TextBrush");
            app.Resources[SystemColors.WindowBrushKey] = brush("CardBrush");
            app.Resources[SystemColors.WindowTextBrushKey] = brush("TextBrush");
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark title bar for dark mode (Windows 10 1903+; ignored elsewhere).</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref dark, 4);
                DwmSetWindowAttribute(handle, 19, ref dark, 4);
            }
            catch { }
        }
    }
}

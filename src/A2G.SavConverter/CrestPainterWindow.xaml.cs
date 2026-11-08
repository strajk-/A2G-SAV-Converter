using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using A2G.Convert.Writing;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// A small pixel editor for one 32x32 crest. It starts from the crest it is given (generated or the club's current one); the result is
    /// the game's BMP format (256 colours). Colours come from a fixed palette plus the colours already in the picture; nothing is typed.
    /// </summary>
    public partial class CrestPainterWindow : Window
    {
        private const int N = 32;
        private static readonly Color Background32 = Color.FromRgb(251, 250, 249);

        private readonly byte[] _pixels = new byte[N * N * 4];   // BGRA
        private readonly WriteableBitmap _bitmap = new WriteableBitmap(N, N, 96, 96, PixelFormats.Bgra32, null);
        private readonly List<byte[]> _undo = new List<byte[]>();
        private Color _color = Colors.Black;
        private bool _drawing;

        /// <summary>The finished crest as game BMP bytes, or null when the window was closed without saving.</summary>
        public byte[] Result { get; private set; }

        public CrestPainterWindow(Window owner, string title, byte[] start)
        {
            InitializeComponent();
            Owner = owner;
            Title = title;
            TitleText.Text = title;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            Load(start);
            Canvas.Source = _bitmap;
            BuildPalette(start);
            SetColor(Colors.Black);
            Redraw();
        }

        private void Load(byte[] bmp)
        {
            var source = new FormatConvertedBitmap(CrestsTab.Decode(bmp), PixelFormats.Bgra32, null, 0);
            source.CopyPixels(_pixels, N * 4, 0);
        }

        private static readonly Color[] FixedColors =
        {
            Color.FromRgb(251, 250, 249), Colors.White, Color.FromRgb(200, 200, 200), Color.FromRgb(128, 128, 128), Color.FromRgb(64, 64, 64), Colors.Black, Color.FromRgb(120, 70, 30), Color.FromRgb(240, 200, 150),
            Color.FromRgb(200, 30, 40), Color.FromRgb(240, 90, 90), Color.FromRgb(230, 120, 0), Color.FromRgb(250, 210, 0), Color.FromRgb(250, 235, 130), Color.FromRgb(140, 190, 40), Color.FromRgb(0, 120, 0), Color.FromRgb(0, 80, 40),
            Color.FromRgb(0, 150, 160), Color.FromRgb(120, 180, 230), Color.FromRgb(20, 90, 200), Color.FromRgb(20, 60, 160), Color.FromRgb(30, 30, 100), Color.FromRgb(90, 40, 140), Color.FromRgb(200, 90, 180), Color.FromRgb(120, 20, 60)
        };

        private void BuildPalette(byte[] start)
        {
            var colors = new List<Color>(FixedColors);
            // the colours of the picture itself (up to 16 more), so a crest can be touched up with its own colours
            var counts = new Dictionary<uint, int>();
            for (var i = 0; i < N * N; i++)
            {
                var key = ((uint)_pixels[i * 4 + 2] << 16) | ((uint)_pixels[i * 4 + 1] << 8) | _pixels[i * 4];
                int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
            }
            var own = new List<KeyValuePair<uint, int>>(counts);
            own.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in own)
            {
                if (colors.Count >= FixedColors.Length + 16) break;
                var c = Color.FromRgb((byte)(kv.Key >> 16), (byte)(kv.Key >> 8), (byte)kv.Key);
                if (!colors.Contains(c)) colors.Add(c);
            }
            foreach (var color in colors)
            {
                var captured = color;
                var swatch = new Border { Height = 24, Margin = new Thickness(1), Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                swatch.MouseLeftButtonDown += (s, e) => { SetColor(captured); PencilIfPipette(); };
                PaletteGrid.Children.Add(swatch);
            }
        }

        private void GridCheck_Click(object sender, RoutedEventArgs e)
        {
            GridOverlay.Visibility = GridCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PencilIfPipette() { if (PipetteTool.IsChecked == true) PencilTool.IsChecked = true; }

        private void SetColor(Color color)
        {
            _color = color;
            CurrentColor.Background = new SolidColorBrush(color);
        }

        // ---- drawing ------------------------------------------------------------------------

        private void Redraw() { _bitmap.WritePixels(new Int32Rect(0, 0, N, N), _pixels, N * 4, 0); }

        private bool PixelAt(MouseEventArgs e, out int x, out int y)
        {
            var p = e.GetPosition(Canvas);
            x = (int)(p.X / (Canvas.ActualWidth / N));
            y = (int)(p.Y / (Canvas.ActualHeight / N));
            return x >= 0 && y >= 0 && x < N && y < N;
        }

        private void Canvas_Down(object sender, MouseButtonEventArgs e)
        {
            int x, y;
            if (!PixelAt(e, out x, out y)) return;
            if (PipetteTool.IsChecked == true)
            {
                var i = (y * N + x) * 4;
                SetColor(Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]));
                return;
            }
            PushUndo();
            if (FillTool.IsChecked == true) { Fill(x, y, EraserTool.IsChecked == true ? Background32 : _color); Redraw(); return; }
            _drawing = true;
            Canvas.CaptureMouse();
            Paint(x, y);
        }

        private void Canvas_Move(object sender, MouseEventArgs e)
        {
            int x, y;
            if (_drawing && e.LeftButton == MouseButtonState.Pressed && PixelAt(e, out x, out y)) Paint(x, y);
        }

        private void Canvas_Up(object sender, MouseButtonEventArgs e)
        {
            _drawing = false;
            Canvas.ReleaseMouseCapture();
        }

        private void Paint(int x, int y)
        {
            Set(x, y, EraserTool.IsChecked == true ? Background32 : _color);
            Redraw();
        }

        private void Set(int x, int y, Color c)
        {
            var i = (y * N + x) * 4;
            _pixels[i] = c.B; _pixels[i + 1] = c.G; _pixels[i + 2] = c.R; _pixels[i + 3] = 255;
        }

        private Color Get(int x, int y)
        {
            var i = (y * N + x) * 4;
            return Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i]);
        }

        private void Fill(int x, int y, Color with)
        {
            var target = Get(x, y);
            if (target == with) return;
            var stack = new Stack<int>();
            stack.Push(y * N + x);
            while (stack.Count > 0)
            {
                var v = stack.Pop();
                int px = v % N, py = v / N;
                if (px < 0 || py < 0 || px >= N || py >= N || Get(px, py) != target) continue;
                Set(px, py, with);
                if (px > 0) stack.Push(v - 1);
                if (px < N - 1) stack.Push(v + 1);
                if (py > 0) stack.Push(v - N);
                if (py < N - 1) stack.Push(v + N);
            }
        }

        private void PushUndo()
        {
            _undo.Add((byte[])_pixels.Clone());
            if (_undo.Count > 60) _undo.RemoveAt(0);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo.Count == 0) return;
            Array.Copy(_undo[_undo.Count - 1], _pixels, _pixels.Length);
            _undo.RemoveAt(_undo.Count - 1);
            Redraw();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            PushUndo();
            for (var y = 0; y < N; y++) for (var x = 0; x < N; x++) Set(x, y, Background32);
            Redraw();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var rgb = new int[N * N][];
            for (var i = 0; i < N * N; i++) rgb[i] = new[] { (int)_pixels[i * 4 + 2], _pixels[i * 4 + 1], _pixels[i * 4] };
            Result = CrestLibrary.FromRgb(rgb);
            Close();
        }
    }
}

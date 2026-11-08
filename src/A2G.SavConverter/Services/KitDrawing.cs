using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using A2G.Convert.Engine;

namespace A2G.SavConverter.Services
{
    /// <summary>Draws a kit (shirt with its stripe pattern, shorts, socks) from the game's colours and patterns.</summary>
    public static class KitDrawing
    {
        public const double Width = 110, Height = 158;

        public static Color ColourOf(int index)
        {
            var rgb = Kits.Rgb[Math.Max(0, Math.Min(Kits.ColourCount - 1, index))];
            return Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
        }

        private static Brush Solid(int colour) { var b = new SolidColorBrush(ColourOf(colour)); b.Freeze(); return b; }

        public static Canvas Draw(KitSet kit)
        {
            var canvas = new Canvas { Width = Width, Height = Height, Background = Brushes.Transparent, SnapsToDevicePixels = true };
            var outline = new SolidColorBrush(Color.FromArgb(0xAA, 0x80, 0x80, 0x80));

            // socks and legs first (the shorts cover the top of the legs)
            foreach (var x in new[] { 33.0, 61.0 })
            {
                canvas.Children.Add(new Rectangle { Width = 16, Height = 34, Fill = Solid(kit.Socks), Stroke = outline, StrokeThickness = 0.8 }.At(x, 122));
            }

            // shorts
            var shorts = new Polygon
            {
                Points = new PointCollection { new Point(28, 86), new Point(82, 86), new Point(86, 126), new Point(58, 126), new Point(55, 100), new Point(52, 126), new Point(24, 126) },
                Fill = Solid(kit.Shorts), Stroke = outline, StrokeThickness = 0.8
            };
            canvas.Children.Add(shorts);

            // shirt: body and sleeves
            var points = new PointCollection
            {
                new Point(32, 6), new Point(78, 6), new Point(106, 30), new Point(92, 50), new Point(82, 42),
                new Point(82, 88), new Point(28, 88), new Point(28, 42), new Point(18, 50), new Point(4, 30)
            };
            var shirt = new Polygon { Points = points, Fill = Solid(kit.Shirt), Stroke = outline, StrokeThickness = 0.8 };
            canvas.Children.Add(shirt);

            // the stripes use the colour of the shorts (EDITOR.HLP); drawn inside the shirt outline
            // every stripe is clipped to the shirt outline: a clip of its own, moved into the coordinates of the rectangle
            Func<double, double, PathGeometry> clipFor = (left, top) =>
            {
                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
                for (var i = 1; i < points.Count; i++) figure.Segments.Add(new LineSegment(points[i], true));
                geometry.Figures.Add(figure);
                geometry.Transform = new TranslateTransform(-left, -top);
                return geometry;
            };
            var stripe = Solid(kit.Shorts);   // the stripes use the colour of the shorts (EDITOR.HLP)
            Action<double, double, double, double> band = (x, y, w, h) =>
            {
                var rectangle = new Rectangle { Width = w, Height = h, Fill = stripe, Clip = clipFor(x, y) };
                Canvas.SetLeft(rectangle, x); Canvas.SetTop(rectangle, y);
                canvas.Children.Add(rectangle);
            };            switch (kit.Pattern)
            {
                case 1:   // laengs: vertical stripes
                    for (var x = 8.0; x < 104; x += 18) band(x, 0, 9, 90);
                    break;
                case 2:   // quer: horizontal stripes
                    for (var y = 6.0; y < 90; y += 16) band(0, y, 110, 8);
                    break;
                case 3:   // Halb/Halb
                    band(55, 0, 55, 90);
                    break;
                case 4:   // Schulter
                    band(0, 0, 110, 26);
                    break;
                case 5:   // Brustring
                    band(0, 40, 110, 18);
                    break;
                case 6:   // Aermel w. H.: the sleeves
                    band(0, 0, 28, 60);
                    band(82, 0, 28, 60);
                    break;
            }
            canvas.Children.Add(new Polygon { Points = points, Fill = Brushes.Transparent, Stroke = outline, StrokeThickness = 0.8 });
            return canvas;
        }

        private static T At<T>(this T element, double left, double top) where T : UIElement
        {
            Canvas.SetLeft(element, left); Canvas.SetTop(element, top);
            return element;
        }
    }
}

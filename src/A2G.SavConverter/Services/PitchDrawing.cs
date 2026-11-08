using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace A2G.SavConverter.Services
{
    /// <summary>Draws the vertical pitch (attack at the top, 200 x 290) used by the player picker and the team popup.</summary>
    internal static class PitchDrawing
    {
        public const double Width = 200, Height = 290;

        public static void Draw(Canvas canvas)
        {
            var grass = new LinearGradientBrush(Color.FromRgb(0x2F, 0x82, 0x4F), Color.FromRgb(0x25, 0x68, 0x40), 90);
            canvas.Children.Add(new Rectangle { Width = Width, Height = Height, RadiusX = 8, RadiusY = 8, Fill = grass });
            var line = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Action<double, double, double, double> rect = (x, y, w, h) =>
            {
                var r = new Rectangle { Width = w, Height = h, Stroke = line, StrokeThickness = 1.2 };
                Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r);
            };
            rect(8, 8, 184, 274);                     // touch lines
            rect(48, 8, 104, 46);                     // penalty areas
            rect(48, 236, 104, 46);
            rect(75, 8, 50, 18);                      // goal areas
            rect(75, 264, 50, 18);
            canvas.Children.Add(new Line { X1 = 8, X2 = 192, Y1 = 145, Y2 = 145, Stroke = line, StrokeThickness = 1.2 });
            var circle = new Ellipse { Width = 56, Height = 56, Stroke = line, StrokeThickness = 1.2 };
            Canvas.SetLeft(circle, 72); Canvas.SetTop(circle, 117);
            canvas.Children.Add(circle);
        }
    }
}

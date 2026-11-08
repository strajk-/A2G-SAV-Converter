using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace A2G.SavConverter.Services
{
    /// <summary>A number the user changes with − / + buttons (hold to repeat); no typing.</summary>
    public sealed class Stepper : Grid
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(Stepper),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((Stepper)d).Refresh(), (d, v) => ((Stepper)d).Clamp((double)v)));

        public double Min { get; set; }
        public double Max { get; set; }
        public double Step { get; set; }
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public int IntValue { get { return (int)System.Math.Round(Value); } }

        private readonly TextBlock _text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        public Stepper()
        {
            Min = 0; Max = 100; Step = 1;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 28 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            Height = 26;
            var minus = new RepeatButton { Content = "−", Padding = new Thickness(0), Interval = 80, Delay = 350 };
            var plus = new RepeatButton { Content = "+", Padding = new Thickness(0), Interval = 80, Delay = 350 };
            minus.Click += (s, e) => Value = Value - Step;
            plus.Click += (s, e) => Value = Value + Step;
            SetColumn(_text, 1); SetColumn(plus, 2);
            Children.Add(minus); Children.Add(_text); Children.Add(plus);
            Refresh();
        }

        private object Clamp(double v) { return v < Min ? Min : v > Max ? Max : v; }
        private void Refresh() { _text.Text = Value.ToString("0.##", CultureInfo.InvariantCulture); }

        /// <summary>Used by the code that fills / reads the settings.</summary>
        public string Text
        {
            get { return _text.Text; }
            set { double v; Value = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : Min; Refresh(); }
        }
    }
}

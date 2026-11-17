using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// A drop-down you can type into: the text box filters the list while you type (any part of an entry's text, case ignored), the list opens under
    /// it, Down / Enter / click choose an entry and Escape or leaving the box puts the chosen entry's text back. Entries are shown with ToString().
    /// Entries marked with <see cref="AlwaysShown"/> stay in the list whatever is typed.
    /// </summary>
    public sealed class SearchPicker : Grid
    {
        private readonly TextBox _box;
        private readonly Button _arrow;
        private readonly Popup _popup;
        private readonly ListBox _list;
        private IList<object> _items = new List<object>();
        private object _selected;
        private bool _setting;

        public event Action SelectionChanged;

        /// <summary>Entries that are never filtered out (for example a "Default" entry).</summary>
        public Func<object, bool> AlwaysShown { get; set; }

        public SearchPicker()
        {
            _box = new TextBox { Padding = new Thickness(8, 4, 30, 4) };
            _arrow = new Button { Content = "", Width = 26, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 3, 2), Focusable = false, Cursor = Cursors.Arrow };
            _arrow.SetResourceReference(StyleProperty, "IconButton");
            _list = new ListBox { MaxHeight = 280, BorderThickness = new Thickness(0) };
            var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Margin = new Thickness(0, 2, 0, 0), Child = _list };
            frame.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            _popup = new Popup { PlacementTarget = _box, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = frame };
            Children.Add(_box);
            Children.Add(_arrow);

            _box.TextChanged += (s, e) => { if (!_setting) { Filter(_box.Text); OpenIfAny(); } };
            _box.GotKeyboardFocus += (s, e) => _box.SelectAll();
            _box.PreviewMouseLeftButtonDown += (s, e) => { if (!_box.IsKeyboardFocusWithin) { _box.Focus(); e.Handled = true; } };
            _box.PreviewKeyDown += OnKeyDown;
            _box.LostKeyboardFocus += (s, e) => { if (!_popup.IsOpen) Restore(); };
            _popup.Closed += (s, e) => { if (!_box.IsKeyboardFocusWithin) Restore(); };
            _arrow.Click += (s, e) =>
            {
                if (_popup.IsOpen) { _popup.IsOpen = false; return; }
                Filter(string.Empty);
                OpenIfAny();
                _box.Focus();
            };
            _list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var item = ItemAt(e);
                if (item != null) Choose(item);
            };
        }

        public IList<object> Items
        {
            get { return _items; }
            set { _items = value ?? new List<object>(); Filter(string.Empty); }
        }

        public object SelectedItem
        {
            get { return _selected; }
            set { _selected = value; Restore(); }
        }

        public string Placeholder
        {
            get { return _box.Tag as string; }
            set { _box.Tag = value; }
        }

        // ---- behaviour ----------------------------------------------------------------------

        private void Filter(string text)
        {
            text = (text ?? string.Empty).Trim();
            var shown = text.Length == 0 || text == TextOf(_selected)
                ? _items.ToList()
                : _items.Where(i => (AlwaysShown != null && AlwaysShown(i)) || TextOf(i).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            _list.ItemsSource = shown;
        }

        private void OpenIfAny()
        {
            var any = _list.Items.Count > 0;
            if (any) { _popup.Width = Math.Max(120, _box.ActualWidth); _list.MinWidth = _popup.Width - 6; }
            _popup.IsOpen = any;
            if (any && _list.SelectedItem == null && _list.Items.Count > 0)
            {
                var firstReal = _list.Items.Cast<object>().FirstOrDefault(i => AlwaysShown == null || !AlwaysShown(i));
                _list.SelectedItem = firstReal ?? _list.Items[0];
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                if (!_popup.IsOpen) { Filter(_box.Text); OpenIfAny(); }
                var index = _list.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                if (index >= 0 && index < _list.Items.Count) { _list.SelectedIndex = index; _list.ScrollIntoView(_list.SelectedItem); }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (_popup.IsOpen && _list.SelectedItem != null) { Choose(_list.SelectedItem); e.Handled = true; }
            }
            else if (e.Key == Key.Escape)
            {
                if (_popup.IsOpen) { _popup.IsOpen = false; Restore(); e.Handled = true; }
            }
        }

        private object ItemAt(MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is ListBoxItem) && element != _list)
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            var item = element as ListBoxItem;
            return item == null ? null : item.DataContext;
        }

        private void Choose(object item)
        {
            var changed = !Equals(_selected, item);
            _selected = item;
            _popup.IsOpen = false;
            Restore();
            if (changed && SelectionChanged != null) SelectionChanged();
        }

        private static string TextOf(object item) { return item == null ? string.Empty : item.ToString(); }

        /// <summary>Shows the chosen entry's text again (after typing that was not turned into a choice).</summary>
        private void Restore()
        {
            _setting = true;
            _box.Text = TextOf(_selected);
            _setting = false;
        }
    }
}

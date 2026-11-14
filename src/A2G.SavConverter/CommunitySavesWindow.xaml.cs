using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>Lists the saves in the repository's CommunitySaves folder (grouped by folder name) and lets the user pick one.</summary>
    public partial class CommunitySavesWindow : Window
    {
        private sealed class Row
        {
            public CommunitySave Save;
            public string Folder { get { return Save.Folder; } }
            public string Name { get { return Save.Name; } }
            public string SizeText { get { return Save.SizeText; } }
            public string LocalText { get { return Save.Downloaded ? Loc.T(28096) : string.Empty; } }
        }

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>The address of the chosen save (what goes into the FM save field).</summary>
        public string SelectedUrl { get; private set; }
        /// <summary>The start year the save's folder asks for, or 0.</summary>
        public int StartYear { get; private set; }

        public CommunitySavesWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            Closed += (s, e) => _cts.Cancel();
            Loaded += (s, e) => Reload();
        }

        private async void Reload()
        {
            SaveList.ItemsSource = null;
            SelectButton.IsEnabled = false;
            ReloadButton.IsEnabled = false;
            StatusText.Text = Loc.T(28093);
            try
            {
                var list = await CommunitySaves.ListAsync(_cts.Token);
                SaveList.ItemsSource = list.Select(s => new Row { Save = s }).ToList();
                StatusText.Text = list.Count == 0 ? Loc.T(28097) : string.Empty;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { StatusText.Text = Loc.T(28094); }
            ReloadButton.IsEnabled = true;
        }

        private void Reload_Click(object sender, RoutedEventArgs e) { Reload(); }

        private void SaveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = SaveList.SelectedItem != null;
        }

        private void SaveList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SaveList.SelectedItem != null) Select_Click(sender, e);
        }

        private async void Select_Click(object sender, RoutedEventArgs e)
        {
            var row = SaveList.SelectedItem as Row;
            if (row == null) return;
            IsEnabled = false;
            StartYear = await CommunitySaves.ReadStartYearAsync(row.Save.Folder);
            SelectedUrl = row.Save.Url;
            DialogResult = true;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using A2G.Convert.Fm;
using A2G.SavConverter.Localization;
using A2G.SavConverter.Services;

namespace A2G.SavConverter
{
    /// <summary>
    /// Popup with the conversion preview of one player: the FM profile and the converted Anstoss 2 player info. Opened from a team
    /// it can cycle through the players of that team (arrow buttons or the left / right arrow keys).
    /// </summary>
    public partial class PlayerPreviewWindow : Window
    {
        private readonly AppState _state;
        private readonly IList<FmPlayer> _players;
        private readonly string _teamName;
        private int _index;

        /// <param name="players">The players of the team in list order to cycle through (null or one entry: no arrows).</param>
        public PlayerPreviewWindow(Window owner, AppState state, FmPlayer player, IList<FmPlayer> players = null, string teamName = null)
        {
            InitializeComponent();
            Owner = owner;
            _state = state;
            _teamName = teamName;
            _players = players != null && players.Count > 1 && players.Contains(player) ? players : new List<FmPlayer> { player };
            _index = _players.IndexOf(player);
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            Inspector.Attach(state);
            NavBar.Visibility = _players.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            var player = _players[_index];
            Title = Loc.T(22077, player.DisplayName);
            NavText.Text = _players.Count > 1 ? Loc.T(22128, _index + 1, _players.Count, _teamName ?? string.Empty) : string.Empty;
            Inspector.ShowOnly(player);
        }

        private void Step(int delta)
        {
            if (_players.Count < 2) return;
            _index = (_index + delta + _players.Count) % _players.Count;
            ShowCurrent();
        }

        private void Previous_Click(object sender, RoutedEventArgs e) { Step(-1); }
        private void Next_Click(object sender, RoutedEventArgs e) { Step(1); }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left) { Step(-1); e.Handled = true; }
            else if (e.Key == Key.Right) { Step(1); e.Handled = true; }
        }
    }
}

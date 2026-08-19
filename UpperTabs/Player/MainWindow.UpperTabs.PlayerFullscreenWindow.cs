using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LibVLCSharp.WPF;

namespace IndigoMovieManager
{
    internal sealed class PlayerFullscreenHostWindow : Window
    {
        private readonly Grid _playerHost;

        public PlayerFullscreenHostWindow()
        {
            Title = "IndigoMovieManager Player Fullscreen";
            Background = Brushes.Black;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = false;
            _playerHost = new Grid { Background = Brushes.Black };
            Content = _playerHost;
            PreviewKeyDown += PlayerFullscreenHostWindow_PreviewKeyDown;
        }

        public event EventHandler EscapeRequested;

        private void PlayerFullscreenHostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            EscapeRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowPlayer(VideoView player)
        {
            ArgumentNullException.ThrowIfNull(player);
            DetachFromCurrentParent(player);

            player.Visibility = Visibility.Visible;
            player.Width = double.NaN;
            player.Height = double.NaN;
            player.HorizontalAlignment = HorizontalAlignment.Stretch;
            player.VerticalAlignment = VerticalAlignment.Stretch;
            _playerHost.Children.Add(player);

            if (!IsVisible)
            {
                Show();
            }

            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            Activate();
            Focus();
        }

        public void ReturnPlayer(VideoView player, Panel targetParent, int targetRow)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(targetParent);
            DetachFromCurrentParent(player);

            if (targetParent is Grid)
            {
                Grid.SetRow(player, targetRow);
            }

            player.Visibility = Visibility.Visible;
            player.Width = double.NaN;
            player.Height = double.NaN;
            player.HorizontalAlignment = HorizontalAlignment.Stretch;
            player.VerticalAlignment = VerticalAlignment.Stretch;
            targetParent.Children.Add(player);
            Hide();
        }

        private static void DetachFromCurrentParent(FrameworkElement player)
        {
            switch (player.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(player);
                    break;
                case ContentControl contentControl
                    when ReferenceEquals(contentControl.Content, player):
                    contentControl.Content = null;
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, player):
                    decorator.Child = null;
                    break;
            }
        }
    }

    public partial class MainWindow
    {
        private const int PlayerSurfaceRowIndex = 0;
        private PlayerFullscreenHostWindow _playerFullscreenWindow;
        private bool _isDetachedPlayerFullscreenActive;

        private PlayerFullscreenHostWindow GetOrCreatePlayerFullscreenWindow()
        {
            if (_playerFullscreenWindow != null)
            {
                return _playerFullscreenWindow;
            }

            _playerFullscreenWindow = new PlayerFullscreenHostWindow { Owner = this };
            _playerFullscreenWindow.EscapeRequested += PlayerFullscreenWindow_EscapeRequested;
            return _playerFullscreenWindow;
        }

        private void PlayerFullscreenWindow_EscapeRequested(object sender, EventArgs e)
        {
            _ = CloseMainWindowPlayerFullscreenAsync();
        }

        private Task OpenMainWindowPlayerFullscreenAsync()
        {
            if (_isDetachedPlayerFullscreenActive)
            {
                return CloseMainWindowPlayerFullscreenAsync();
            }

            if (
                !_isLibVlcPlayerInitialized
                || _playerMediaPlayer == null
                || uxVideoPlayer == null
                || string.IsNullOrWhiteSpace(_currentPlayerMoviePath)
            )
            {
                return Task.CompletedTask;
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"player fullscreen open requested: surface=libvlc path='{_currentPlayerMoviePath}'"
            );

            try
            {
                PlayerFullscreenHostWindow window = GetOrCreatePlayerFullscreenWindow();
                window.ShowPlayer(uxVideoPlayer);
                _isDetachedPlayerFullscreenActive = true;
                UpdateManualPlayerViewport();
                DebugRuntimeLog.Write("ui-tempo", "player fullscreen window shown: surface=libvlc");
            }
            catch (Exception ex)
            {
                _isDetachedPlayerFullscreenActive = false;
                ReturnDetachedPlayerToPlayerTab();
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"player fullscreen open failed: surface=libvlc type={ex.GetType().Name} message='{ex.Message}'"
                );
            }

            return Task.CompletedTask;
        }

        private bool TryHandleMainWindowPlayerFullscreenShortcut(KeyEventArgs e)
        {
            if (e == null || e.Key != Key.Escape || !_isDetachedPlayerFullscreenActive)
            {
                return false;
            }

            e.Handled = true;
            _ = CloseMainWindowPlayerFullscreenAsync();
            return true;
        }

        private Task CloseMainWindowPlayerFullscreenAsync()
        {
            if (!_isDetachedPlayerFullscreenActive)
            {
                return Task.CompletedTask;
            }

            ReturnDetachedPlayerToPlayerTab();
            Activate();
            uxVideoPlayer?.Focus();
            DebugRuntimeLog.Write("ui-tempo", "player fullscreen window closed: surface=libvlc");
            return Task.CompletedTask;
        }

        private Task ForceCloseMainWindowPlayerFullscreenAsync()
        {
            return CloseMainWindowPlayerFullscreenAsync();
        }

        private void ReturnDetachedPlayerToPlayerTab()
        {
            if (
                _playerFullscreenWindow == null
                || uxVideoPlayer == null
                || PlayerArea == null
            )
            {
                _isDetachedPlayerFullscreenActive = false;
                return;
            }

            if (!ReferenceEquals(uxVideoPlayer.Parent, PlayerArea))
            {
                _playerFullscreenWindow.ReturnPlayer(
                    uxVideoPlayer,
                    PlayerArea,
                    PlayerSurfaceRowIndex
                );
            }
            else
            {
                _playerFullscreenWindow.Hide();
            }

            _isDetachedPlayerFullscreenActive = false;
            ShowPlayerSurface();
            UpdateManualPlayerViewport();
        }
    }
}

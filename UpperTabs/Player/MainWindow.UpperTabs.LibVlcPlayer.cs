using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private LibVLC _playerLibVlc;
        private MediaPlayer _playerMediaPlayer;
        private Task<(LibVLC LibVlc, MediaPlayer MediaPlayer)> _playerLibVlcInitializationTask;
        private bool _isLibVlcPlayerInitialized;
        private bool _isLibVlcPlayerDisposed;
        private bool _isPlayerLibVlcPrewarmQueued;

        private enum PlayerMediaOpenResult
        {
            Started,
            Failed,
            Stale,
        }

        // first-pageと入力受付を先に返し、ApplicationIdleでnative初期化だけを済ませる。
        private void QueueLibVlcPlayerPrewarmAfterInputReady()
        {
            if (
                _isPlayerLibVlcPrewarmQueued
                || _isLibVlcPlayerInitialized
                || _isLibVlcPlayerDisposed
                || Dispatcher == null
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
            )
            {
                return;
            }

            _isPlayerLibVlcPrewarmQueued = true;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => _ = RunLibVlcPlayerPrewarmAsync())
            );
        }

        private async Task RunLibVlcPlayerPrewarmAsync()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool ready = await EnsureLibVlcPlayerReadyAsync();
            DebugRuntimeLog.Write(
                "player",
                $"libvlc player prewarm completed: ready={ready} elapsed_ms={stopwatch.ElapsedMilliseconds}"
            );
        }

        // LibVLCはPlayerタブで一度だけ生成し、動画切替ごとのネイティブ初期化を避ける。
        private async Task<bool> EnsureLibVlcPlayerReadyAsync()
        {
            if (_isLibVlcPlayerDisposed || uxVideoPlayer == null)
            {
                return false;
            }

            if (_isLibVlcPlayerInitialized && _playerMediaPlayer != null)
            {
                return true;
            }

            Task<(LibVLC LibVlc, MediaPlayer MediaPlayer)> initializationTask =
                _playerLibVlcInitializationTask;
            if (initializationTask == null)
            {
                initializationTask = Task.Run(CreateLibVlcPlayerResources);
                _playerLibVlcInitializationTask = initializationTask;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                (LibVLC LibVlc, MediaPlayer MediaPlayer) resources =
                    await initializationTask;
                if (_isLibVlcPlayerDisposed || uxVideoPlayer == null)
                {
                    if (ReferenceEquals(_playerLibVlcInitializationTask, initializationTask))
                    {
                        _playerLibVlcInitializationTask = null;
                        resources.MediaPlayer.Dispose();
                        resources.LibVlc.Dispose();
                    }
                    return false;
                }

                // 同じ初期化Taskを待った後着要求は、先着が接続した実体をそのまま使う。
                if (_isLibVlcPlayerInitialized && _playerMediaPlayer != null)
                {
                    return true;
                }

                _playerLibVlc = resources.LibVlc;
                _playerMediaPlayer = resources.MediaPlayer;
                HookLibVlcPlayerEvents(_playerMediaPlayer);
                uxVideoPlayer.MediaPlayer = _playerMediaPlayer;
                _isLibVlcPlayerInitialized = true;
                _playerLibVlcInitializationTask = null;
                ApplyCurrentPlayerVolumeToEngine();
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc player initialized: surface=libvlc elapsed_ms={stopwatch.ElapsedMilliseconds}"
                );
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc player initialization failed: type={ex.GetType().Name} message='{ex.Message}' elapsed_ms={stopwatch.ElapsedMilliseconds}"
                );
                if (ReferenceEquals(_playerLibVlcInitializationTask, initializationTask))
                {
                    _playerLibVlcInitializationTask = null;
                }
                ReleaseLibVlcPlayerResources();
                return false;
            }
        }

        private static (LibVLC LibVlc, MediaPlayer MediaPlayer) CreateLibVlcPlayerResources()
        {
            string nativeDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "libvlc",
                "win-x64"
            );
            Core.Initialize(nativeDirectory);
            LibVLC libVlc = null;
            try
            {
                libVlc = new LibVLC("--no-video-title-show", "--quiet");
                return (libVlc, new MediaPlayer(libVlc));
            }
            catch
            {
                // MediaPlayer生成まで到達できなくても、先に確保したLibVLCを残さない。
                libVlc?.Dispose();
                throw;
            }
        }

        private void HookLibVlcPlayerEvents(MediaPlayer mediaPlayer)
        {
            mediaPlayer.Playing += PlayerMediaPlayer_Playing;
            mediaPlayer.Paused += PlayerMediaPlayer_Paused;
            mediaPlayer.Stopped += PlayerMediaPlayer_Stopped;
            mediaPlayer.EndReached += PlayerMediaPlayer_EndReached;
            mediaPlayer.EncounteredError += PlayerMediaPlayer_EncounteredError;
            mediaPlayer.LengthChanged += PlayerMediaPlayer_LengthChanged;
        }

        private void UnhookLibVlcPlayerEvents(MediaPlayer mediaPlayer)
        {
            mediaPlayer.Playing -= PlayerMediaPlayer_Playing;
            mediaPlayer.Paused -= PlayerMediaPlayer_Paused;
            mediaPlayer.Stopped -= PlayerMediaPlayer_Stopped;
            mediaPlayer.EndReached -= PlayerMediaPlayer_EndReached;
            mediaPlayer.EncounteredError -= PlayerMediaPlayer_EncounteredError;
            mediaPlayer.LengthChanged -= PlayerMediaPlayer_LengthChanged;
        }

        private async Task<PlayerMediaOpenResult> TryOpenLibVlcMovieAsync(
            string moviePath,
            int requestRevision,
            long requestStartedTimestamp
        )
        {
            if (
                string.IsNullOrWhiteSpace(moviePath)
                || !await EnsureLibVlcPlayerReadyAsync()
            )
            {
                return PlayerMediaOpenResult.Failed;
            }

            // 初期化待ちの間に後着選択が来た時は、古いAVIをMediaへ渡さない。
            if (!IsPlayerOpenRequestCurrent(requestRevision))
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc media open skipped stale: revision={requestRevision} current_revision={_playerOpenRequestRevision} stage=after-initialize"
                );
                return PlayerMediaOpenResult.Stale;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                using Media media = new(_playerLibVlc, moviePath, FromType.FromPath);
                _currentPlayerMediaRequestRevision = requestRevision;
                _currentPlayerMediaRequestStartedTimestamp = requestStartedTimestamp;
                _playerMediaPlayer.Media = media;
                ApplyCurrentPlayerVolumeToEngine();
                bool started = _playerMediaPlayer.Play();
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc media open requested: revision={requestRevision} started={started} elapsed_ms={stopwatch.ElapsedMilliseconds} path='{moviePath}'"
                );
                return started ? PlayerMediaOpenResult.Started : PlayerMediaOpenResult.Failed;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc media open failed: revision={requestRevision} type={ex.GetType().Name} message='{ex.Message}' elapsed_ms={stopwatch.ElapsedMilliseconds} path='{moviePath}'"
                );
                return PlayerMediaOpenResult.Failed;
            }
        }

        private void PlayerMediaPlayer_Playing(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(HandleLibVlcPlayerPlayingOnUi);
        }

        private async void HandleLibVlcPlayerPlayingOnUi()
        {
            try
            {
                if (
                    _isLibVlcPlayerDisposed
                    || _playerMediaPlayer?.State != VLCState.Playing
                )
                {
                    return;
                }

                ShowPlayerSurface();
                HidePlayerLoadingState(_currentPlayerMediaRequestRevision, "playing");
                UpdatePlayerDurationFromEngine();
                long requestElapsedMs = _currentPlayerMediaRequestStartedTimestamp > 0L
                    ? (long)
                        Stopwatch
                            .GetElapsedTime(_currentPlayerMediaRequestStartedTimestamp)
                            .TotalMilliseconds
                    : -1L;
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc playing ready: revision={_currentPlayerMediaRequestRevision} request_elapsed_ms={requestElapsedMs}"
                );
                SetPlayerPlaybackActive(true, "libvlc-playing");
                await ApplyPendingPlayerPlaybackRequestAsync();
            }
            catch (Exception ex)
            {
                ReleasePendingPlayerUserPriorityWork(
                    "libvlc-playing-handler-failed",
                    _pendingPlayerUserPriorityRevision
                );
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc playing handler failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
            }
        }

        private void PlayerMediaPlayer_Paused(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(() =>
            {
                if (_playerMediaPlayer?.State != VLCState.Paused)
                {
                    return;
                }

                SetPlayerPlaybackActive(false, "libvlc-paused");
                StopDispatcherTimerSafely(timer, nameof(timer));
                UpdatePlayerPositionUi(GetPlayerPosition());
            });
        }

        private void PlayerMediaPlayer_Stopped(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(() =>
            {
                if (_playerMediaPlayer?.State != VLCState.Stopped)
                {
                    return;
                }

                SetPlayerPlaybackActive(false, "libvlc-stopped");
                StopDispatcherTimerSafely(timer, nameof(timer));
            });
        }

        private void PlayerMediaPlayer_EndReached(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(() =>
            {
                if (_playerMediaPlayer?.State != VLCState.Ended)
                {
                    return;
                }

                SetPlayerPlaybackActive(false, "libvlc-ended");
                StopDispatcherTimerSafely(timer, nameof(timer));
                UpdatePlayerPositionUi(GetPlayerPosition());
            });
        }

        private void PlayerMediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(() =>
            {
                if (_playerMediaPlayer?.State != VLCState.Error)
                {
                    return;
                }

                _hasPendingPlayerPlaybackRequest = false;
                HidePlayerLoadingState(_currentPlayerMediaRequestRevision, "playback-error");
                SetPlayerPlaybackActive(false, "libvlc-error");
                StopDispatcherTimerSafely(timer, nameof(timer));
                ReleasePendingPlayerUserPriorityWork(
                    "libvlc-error",
                    _pendingPlayerUserPriorityRevision
                );
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc playback failed: path='{_currentPlayerMoviePath}'"
                );
            });
        }

        private void PlayerMediaPlayer_LengthChanged(object sender, EventArgs e)
        {
            QueueLibVlcPlayerUi(UpdatePlayerDurationFromEngine);
        }

        private void QueueLibVlcPlayerUi(Action action)
        {
            if (action == null || Dispatcher == null || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            try
            {
                if (Dispatcher.CheckAccess())
                {
                    action();
                    return;
                }

                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
            }
            catch (InvalidOperationException)
            {
                // 終了中のDispatcher競合では、後着した再生通知を安全に捨てる。
            }
        }

        private void ApplyCurrentPlayerVolumeToEngine()
        {
            if (_playerMediaPlayer == null)
            {
                return;
            }

            _playerMediaPlayer.Volume = (int)Math.Round(GetCurrentPlayerVolumeSetting() * 100d);
        }

        private bool IsPlayerEngineMuted()
        {
            return _playerMediaPlayer?.Mute == true;
        }

        private void SetPlayerEngineMuted(bool muted)
        {
            if (_playerMediaPlayer != null)
            {
                _playerMediaPlayer.Mute = muted;
            }
        }

        private TimeSpan GetPlayerPosition()
        {
            long milliseconds = Math.Max(0L, _playerMediaPlayer?.Time ?? 0L);
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        private void SetPlayerPosition(TimeSpan position)
        {
            if (_playerMediaPlayer == null)
            {
                return;
            }

            double requested = Math.Max(0d, position.TotalMilliseconds);
            long duration = Math.Max(0L, _playerMediaPlayer.Length);
            _playerMediaPlayer.Time = (long)(duration > 0L ? Math.Min(requested, duration) : requested);
        }

        private void UpdatePlayerDurationFromEngine()
        {
            if (uxTimeSlider == null)
            {
                return;
            }

            uxTimeSlider.Maximum = Math.Max(0L, _playerMediaPlayer?.Length ?? 0L);
            UpdatePlayerPositionUi(GetPlayerPosition());
        }

        private void StartLibVlcPlayback()
        {
            if (_playerMediaPlayer == null)
            {
                return;
            }

            if (
                _playerMediaPlayer.State == VLCState.Ended
                || _playerMediaPlayer.State == VLCState.Stopped
                || _playerMediaPlayer.State == VLCState.Error
                || _playerMediaPlayer.State == VLCState.NothingSpecial
            )
            {
                if (!_playerMediaPlayer.Play())
                {
                    DebugRuntimeLog.Write("player", "libvlc playback start rejected");
                    return;
                }
            }
            else
            {
                _playerMediaPlayer.SetPause(false);
            }

            SetPlayerPlaybackActive(true, "libvlc-start");
            TryStartDispatcherTimer(timer, nameof(timer));
        }

        private void PauseLibVlcPlayback()
        {
            if (_playerMediaPlayer == null)
            {
                return;
            }

            _playerMediaPlayer.SetPause(true);
            SetPlayerPlaybackActive(false, "libvlc-pause");
            StopDispatcherTimerSafely(timer, nameof(timer));
        }

        private void StopLibVlcPlayback()
        {
            try
            {
                _playerMediaPlayer?.Stop();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc stop failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
            }
        }

        // 再生中ファイルの削除前に、MediaPlayerが保持するnativeファイル参照を明示的に外す。
        private bool TryReleaseCurrentLibVlcMediaForDelete(out string failureReason)
        {
            failureReason = "";
            if (_playerMediaPlayer == null)
            {
                return true;
            }

            try
            {
                _playerMediaPlayer.Media = null;
                DebugRuntimeLog.Write("player", "libvlc media released for movie delete");
                return true;
            }
            catch (Exception ex)
            {
                failureReason = string.IsNullOrWhiteSpace(ex.Message)
                    ? ex.GetType().Name
                    : ex.Message;
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc media release for movie delete failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
                return false;
            }
        }

        private void DisposeLibVlcPlayer()
        {
            if (_isLibVlcPlayerDisposed)
            {
                return;
            }

            _isLibVlcPlayerDisposed = true;
            ReleaseLibVlcPlayerResources();
        }

        private void ReleaseLibVlcPlayerResources()
        {
            MediaPlayer mediaPlayer = _playerMediaPlayer;
            LibVLC libVlc = _playerLibVlc;
            _playerMediaPlayer = null;
            _playerLibVlc = null;
            _isLibVlcPlayerInitialized = false;

            try
            {
                mediaPlayer?.Stop();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc stop during dispose failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
            }

            try
            {
                if (uxVideoPlayer != null)
                {
                    uxVideoPlayer.MediaPlayer = null;
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc surface detach failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
            }

            if (mediaPlayer != null)
            {
                try
                {
                    UnhookLibVlcPlayerEvents(mediaPlayer);
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "player",
                        $"libvlc event unhook failed: type={ex.GetType().Name} message='{ex.Message}'"
                    );
                }

                try
                {
                    mediaPlayer.Dispose();
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "player",
                        $"libvlc media player dispose failed: type={ex.GetType().Name} message='{ex.Message}'"
                    );
                }
            }

            try
            {
                libVlc?.Dispose();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "player",
                    $"libvlc core dispose failed: type={ex.GetType().Name} message='{ex.Message}'"
                );
            }

            DebugRuntimeLog.Write("player", "libvlc player resources released");
        }
    }
}

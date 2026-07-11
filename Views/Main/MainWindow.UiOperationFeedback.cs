using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private DispatcherTimer _uiOperationFeedbackTimer;
        private long _uiOperationFeedbackRevision;
        private long _pendingUiOperationFeedbackRevision;
        private string _pendingUiOperationFeedbackReason;

        // 操作開始は呼出し時点でrevisionを進め、Dispatcherへ遅れて届く古い要求を失効させる。
        private void ScheduleUiOperationFeedback(string reason)
        {
            long revision = Interlocked.Increment(ref _uiOperationFeedbackRevision);
            RunUiOperationFeedbackOnDispatcher(() =>
                ScheduleUiOperationFeedbackOnUiThread(reason, revision)
            );
        }

        private void ScheduleUiOperationFeedbackOnUiThread(string reason, long revision)
        {
            if (revision != Volatile.Read(ref _uiOperationFeedbackRevision))
            {
                return;
            }

            if (
                UiOperationFeedbackPanel == null
                || UiOperationFeedbackStatusText == null
                || lbDbFullPath == null
            )
            {
                return;
            }

            if (_uiOperationFeedbackTimer == null)
            {
                _uiOperationFeedbackTimer = new DispatcherTimer(
                    DispatcherPriority.Background,
                    Dispatcher
                )
                {
                    Interval = TimeSpan.FromMilliseconds(UiOperationFeedbackPolicy.DelayMs),
                };
                _uiOperationFeedbackTimer.Tick += UiOperationFeedbackTimer_Tick;
            }

            StopDispatcherTimerSafely(_uiOperationFeedbackTimer, nameof(_uiOperationFeedbackTimer));
            _pendingUiOperationFeedbackRevision = revision;
            _pendingUiOperationFeedbackReason = reason;
            HideUiOperationFeedback();
            TryStartDispatcherTimer(_uiOperationFeedbackTimer, nameof(_uiOperationFeedbackTimer));
        }

        private void UiOperationFeedbackTimer_Tick(object sender, EventArgs e)
        {
            StopDispatcherTimerSafely(_uiOperationFeedbackTimer, nameof(_uiOperationFeedbackTimer));

            long pendingRevision = _pendingUiOperationFeedbackRevision;
            long currentRevision = Volatile.Read(ref _uiOperationFeedbackRevision);
            if (
                !UiOperationFeedbackPolicy.ShouldShow(
                    pendingRevision,
                    currentRevision,
                    IsUserPriorityWorkActive()
                )
            )
            {
                return;
            }

            if (
                UiOperationFeedbackPanel == null
                || UiOperationFeedbackStatusText == null
                || lbDbFullPath == null
            )
            {
                return;
            }

            UiOperationFeedbackStatusText.Text = UiOperationFeedbackPolicy.ResolveStatusText(
                _pendingUiOperationFeedbackReason
            );
            lbDbFullPath.Visibility = Visibility.Collapsed;
            UiOperationFeedbackPanel.Visibility = Visibility.Visible;
        }

        // 操作終了は待機中tickを無効化し、表示済みの場合もその場で通常ヘッダーへ戻す。
        private void CompleteUiOperationFeedback()
        {
            long revision = Interlocked.Increment(ref _uiOperationFeedbackRevision);
            RunUiOperationFeedbackOnDispatcher(() =>
                CompleteUiOperationFeedbackOnUiThread(revision)
            );
        }

        private void CompleteUiOperationFeedbackOnUiThread(long revision)
        {
            if (revision != Volatile.Read(ref _uiOperationFeedbackRevision))
            {
                return;
            }

            StopDispatcherTimerSafely(_uiOperationFeedbackTimer, nameof(_uiOperationFeedbackTimer));
            _pendingUiOperationFeedbackRevision = 0;
            _pendingUiOperationFeedbackReason = null;
            HideUiOperationFeedback();
        }

        private void HideUiOperationFeedback()
        {
            if (UiOperationFeedbackPanel == null || lbDbFullPath == null)
            {
                return;
            }

            UiOperationFeedbackPanel.Visibility = Visibility.Collapsed;
            lbDbFullPath.Visibility = Visibility.Visible;
        }

        // 背景完了から呼ばれてもUI状態はDispatcher上だけで更新し、終了中は静かに破棄する。
        private void RunUiOperationFeedbackOnDispatcher(Action action)
        {
            Dispatcher dispatcher = Dispatcher;
            if (
                dispatcher == null
                || dispatcher.HasShutdownStarted
                || dispatcher.HasShutdownFinished
            )
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(action, DispatcherPriority.Background);
            }
            catch (InvalidOperationException)
                when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                // 終了競合では補助表示だけを諦め、操作本体へ影響させない。
            }
        }
    }
}

using System;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 検索のような明示的ユーザー要求が走っている間は、背後処理を後ろへ逃がして完了を優先する。
        private readonly object _userPriorityWorkSync = new();
        private int _userPriorityWorkCount;
        private DateTime? _userPriorityWorkStartedUtc;
        private string _userPriorityWorkBeginReason;

        private void BeginUserPriorityWork(string reason)
        {
            bool activated = false;
            DateTime startedUtc = DateTime.UtcNow;
            if (_userPriorityWorkSync == null)
            {
                return;
            }

            lock (_userPriorityWorkSync)
            {
                _userPriorityWorkCount++;
                activated = _userPriorityWorkCount == 1;
                if (activated)
                {
                    _userPriorityWorkStartedUtc = startedUtc;
                    _userPriorityWorkBeginReason = reason;
                }
            }

            if (activated)
            {
                UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                    isUserPriorityActive: true,
                    isManualMode: false
                );
                DebugRuntimeLog.Write(
                    "ui-priority",
                    BuildUserPriorityBeginLogMessage(reason, snapshot)
                );
                ScheduleUiOperationFeedback(reason);
            }
        }

        private void EndUserPriorityWork(string reason)
        {
            bool wasActive;
            bool isStillActive;
            bool hasDeferredWatchWork;
            string beginReason = null;
            DateTime? startedUtc = null;
            if (_userPriorityWorkSync == null)
            {
                return;
            }

            lock (_userPriorityWorkSync)
            {
                wasActive = _userPriorityWorkCount > 0;
                if (_userPriorityWorkCount > 0)
                {
                    _userPriorityWorkCount--;
                }

                isStillActive = _userPriorityWorkCount > 0;
                hasDeferredWatchWork =
                    !isStillActive && ConsumeWatchWorkDeferredForUserPriorityCatchUp();
                if (!isStillActive)
                {
                    beginReason = _userPriorityWorkBeginReason;
                    startedUtc = _userPriorityWorkStartedUtc;
                    _userPriorityWorkBeginReason = null;
                    _userPriorityWorkStartedUtc = null;
                }
            }

            if (!wasActive)
            {
                return;
            }

            if (!isStillActive)
            {
                DateTime endedUtc = DateTime.UtcNow;
                long elapsedMilliseconds = ResolveUserPriorityElapsedMilliseconds(
                    startedUtc,
                    endedUtc
                );
                string releaseReason = ResolveUserPriorityReleaseReason(
                    startedUtc,
                    endedUtc,
                    ResolveUserPriorityTimeout()
                );
                UiOperationSnapshot snapshot = CaptureUserPriorityOperationSnapshot(
                    isUserPriorityActive: false,
                    isManualMode: false
                );
                DebugRuntimeLog.Write(
                    "ui-priority",
                    BuildUserPriorityReleaseLogMessage(
                        beginReason ?? reason,
                        reason,
                        elapsedMilliseconds,
                        releaseReason,
                        hasDeferredWatchWork,
                        snapshot
                    )
                );
            }

            if (ShouldQueueBackgroundCatchUpAfterUserPriority(isStillActive, hasDeferredWatchWork))
            {
                DebugRuntimeLog.Write(
                    "ui-priority",
                    $"user priority catch-up queued: reason={reason}"
                );
                _ = QueueCheckFolderAsync(CheckMode.Watch, $"user-priority-resume:{reason}");
            }

            if (!isStillActive)
            {
                CompleteUiOperationFeedback();
            }
        }

        private bool IsUserPriorityWorkActive()
        {
            if (_userPriorityWorkSync == null)
            {
                return false;
            }

            lock (_userPriorityWorkSync)
            {
                return _userPriorityWorkCount > 0;
            }
        }

        // ログ用の軽い状態を snapshot に畳み、user-priority 入口の語彙を UI Shell 側へ寄せる。
        private UiOperationSnapshot CaptureUserPriorityOperationSnapshot(
            bool isUserPriorityActive,
            bool isManualMode
        )
        {
            return CreateUserPriorityOperationSnapshot(
                isUserPriorityActive,
                isManualMode,
                isWatchUiSuppressed: _watchUiSuppressionSync != null && IsWatchSuppressedByUi(),
                isRecentViewportInteractionActive: IsRecentViewportInteractionActive(),
                isPlayerPlaybackActive: IsPlayerPlaybackActive()
            );
        }

        // user-priority の解除と defer 記録を同じロック順に寄せ、解除境界の catch-up 取りこぼしを防ぐ。
        private bool TryMarkWatchWorkDeferredForUserPriorityCatchUp(string trigger)
        {
            if (_userPriorityWorkSync == null || _watchUiSuppressionSync == null)
            {
                return false;
            }

            bool shouldLog = false;
            lock (_userPriorityWorkSync)
            {
                if (_userPriorityWorkCount <= 0)
                {
                    return false;
                }

                lock (_watchUiSuppressionSync)
                {
                    shouldLog = !_watchWorkDeferredWhileSuppressed;
                    _watchWorkDeferredWhileSuppressed = true;
                }
            }

            if (shouldLog)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch work deferred for background catch-up: trigger={trigger}"
                );
            }

            return true;
        }

        private bool ConsumeWatchWorkDeferredForUserPriorityCatchUp()
        {
            if (_watchUiSuppressionSync == null)
            {
                return false;
            }

            lock (_watchUiSuppressionSync)
            {
                bool hasDeferredWatchWork = _watchWorkDeferredWhileSuppressed;
                if (hasDeferredWatchWork)
                {
                    _watchWorkDeferredWhileSuppressed = false;
                }

                return hasDeferredWatchWork;
            }
        }

        // 現在の mode で背後処理を後ろへ逃がすべきかを、runtime 状態込みでまとめる。
        private bool ShouldDeferCurrentBackgroundWork(CheckMode mode)
        {
            return UiOperationPriorityPolicy.ShouldDeferBackgroundWork(
                CaptureUserPriorityOperationSnapshot(
                    IsUserPriorityWorkActive(),
                    mode == CheckMode.Manual
                )
            );
        }
    }
}

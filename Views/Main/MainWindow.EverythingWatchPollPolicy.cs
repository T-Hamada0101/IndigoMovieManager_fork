using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int EverythingWatchPollCalmStartupGraceMs = 15000;
        private long _everythingWatchPollLoopStartedTick64;
        private int _everythingWatchPollPlanRevision;

        private readonly record struct EverythingWatchPollPlanRequest(
            int Revision,
            bool IsStartupFeedPartialActive,
            bool IsIntegrationConfigured,
            bool CanUseAvailability,
            bool KeepPollingForFallback,
            string DbPath
        );

        private readonly record struct EverythingWatchPollPlanResult(
            int Revision,
            string DbPath,
            bool ShouldRun,
            int EligibleWatchFolderCount
        );

        // Everything poll を走らせるかどうかの pure 判定を、UI本体から切り離してまとめる。
        internal static bool ShouldRunEverythingWatchPollPolicy(
            bool isStartupFeedPartialActive,
            bool isIntegrationConfigured,
            bool canUseAvailability,
            bool keepPollingForFallback,
            string dbPath,
            IEnumerable<string> watchFolders,
            Func<string, bool> pathExists,
            Func<string, bool> isEverythingEligiblePath
        )
        {
            if (isStartupFeedPartialActive)
            {
                return false;
            }

            if (!isIntegrationConfigured)
            {
                return false;
            }

            if (!canUseAvailability && !keepPollingForFallback)
            {
                return false;
            }

            Func<string, bool> exists = pathExists ?? Path.Exists;
            if (string.IsNullOrWhiteSpace(dbPath) || !exists(dbPath))
            {
                return false;
            }

            if (watchFolders == null)
            {
                return false;
            }

            foreach (string watchFolder in watchFolders)
            {
                if (!exists(watchFolder))
                {
                    continue;
                }

                if (isEverythingEligiblePath?.Invoke(watchFolder) == true)
                {
                    return true;
                }
            }

            return false;
        }

        // 明示操作中の Everything poll は入口で止め、重い eligible 判定へ入る前に catch-up へ逃がす。
        internal static bool ShouldDeferEverythingWatchPollForUserPriority(
            bool isUserPriorityActive
        )
        {
            return UiOperationPriorityPolicy.ShouldDeferBackgroundWork(
                CreateEverythingWatchPollOperationSnapshot(
                    isUserPriorityActive,
                    isManualMode: false,
                    isWatchUiSuppressed: false,
                    isRecentViewportInteractionActive: false,
                    isPlayerPlaybackActive: false
                )
            );
        }

        internal static bool ShouldDeferEverythingWatchPollForRecentViewport(
            bool isRecentViewportInteractionActive
        )
        {
            string deferReason = ResolveEverythingWatchPollDeferReason(
                isDeferredByUiSuppression: false,
                isDeferredByUserPriority: false,
                isRecentViewportInteractionActive
            );
            return string.Equals(
                deferReason,
                UiOperationPriorityPolicy.DeferReasonRecentViewport,
                StringComparison.Ordinal
            );
        }

        internal static string ResolveEverythingWatchPollDeferReason(
            bool isDeferredByUiSuppression,
            bool isDeferredByUserPriority,
            bool isRecentViewportInteractionActive
        )
        {
            return UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(
                CreateEverythingWatchPollOperationSnapshot(
                    isUserPriorityActive: isDeferredByUserPriority,
                    isManualMode: false,
                    isWatchUiSuppressed: isDeferredByUiSuppression,
                    isRecentViewportInteractionActive: isRecentViewportInteractionActive,
                    isPlayerPlaybackActive: false
                )
            );
        }

        internal static bool ShouldQueueEverythingWatchPollCatchUp(string deferReason)
        {
            return UiOperationPriorityPolicy.ShouldQueueCatchUpForEverythingPollDefer(
                deferReason
            );
        }

        internal static string ResolveEverythingWatchPollOperationReason(
            bool isDeferredByUiSuppression,
            bool isDeferredByUserPriority,
            bool isRecentViewportInteractionActive,
            bool isPlayerPlaybackActive
        )
        {
            UiOperationSnapshot snapshot = CreateEverythingWatchPollOperationSnapshot(
                isUserPriorityActive: isDeferredByUserPriority,
                isManualMode: false,
                isWatchUiSuppressed: isDeferredByUiSuppression,
                isRecentViewportInteractionActive: isRecentViewportInteractionActive,
                isPlayerPlaybackActive: isPlayerPlaybackActive
            );
            string deferReason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(
                snapshot
            );
            return UiOperationPriorityPolicy.ResolveEverythingPollOperationReason(
                snapshot,
                deferReason
            );
        }

        internal static string BuildEverythingWatchPollDeferredLogMessage(
            string operationReason,
            string deferReason,
            bool isRecentViewportInteractionActive,
            bool shouldQueueCatchUp,
            UiWorkRequest request
        )
        {
            return
                $"everything poll deferred: {UiWorkRequestPolicy.BuildRequestAdmissionLogFields(request, UiWorkRequestPolicy.ReleaseReasonDeferred)} operation_reason={operationReason} defer_reason={deferReason} recent_viewport={FormatLogBool(isRecentViewportInteractionActive)} catch_up={FormatLogBool(shouldQueueCatchUp)}";
        }

        // poll 自体は定期処理なので、検索などの明示操作中は1周見送り、解除後のwatchで追いつく。
        private bool TryDeferEverythingWatchPollForUserPriority()
        {
            return TryDeferEverythingWatchPollForUserPriorityCore(
                isRecentViewportInteractionActive: false
            );
        }

        private bool TryDeferEverythingWatchPollForUserPriorityCore(
            bool isRecentViewportInteractionActive
        )
        {
            if (!ShouldDeferEverythingWatchPollForUserPriority(IsUserPriorityWorkActive()))
            {
                return false;
            }

            if (!TryMarkWatchWorkDeferredForUserPriorityCatchUp("user-priority:everything-poll"))
            {
                return false;
            }

            UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();
            DebugRuntimeLog.Write(
                "watch-check",
                BuildEverythingWatchPollDeferredLogMessage(
                    UiOperationPriorityPolicy.DeferReasonUserPriority,
                    UiOperationPriorityPolicy.DeferReasonUserPriority,
                    isRecentViewportInteractionActive,
                    shouldQueueCatchUp: true,
                    request
                )
            );
            return true;
        }

        // スクロール直後は一周だけ poll を見送り、catch-up は積まず操作感だけ守る。
        private bool TryDeferEverythingWatchPollForRecentViewport(
            bool isRecentViewportInteractionActive
        )
        {
            if (
                !ShouldDeferEverythingWatchPollForRecentViewport(
                    isRecentViewportInteractionActive
                )
            )
            {
                return false;
            }

            UiWorkRequest request = UiWorkRequestPolicy.CreateEverythingWatchPollRequest();
            DebugRuntimeLog.Write(
                "watch-check",
                BuildEverythingWatchPollDeferredLogMessage(
                    UiOperationPriorityPolicy.DeferReasonRecentViewport,
                    UiOperationPriorityPolicy.DeferReasonRecentViewport,
                    isRecentViewportInteractionActive,
                    shouldQueueCatchUp: false,
                    request
                )
            );
            return true;
        }

        // UI抑止や明示操作でpoll本体を逃がした周回では、待機間隔のためだけにDBへ寄らない。
        internal static bool ShouldProbeEverythingWatchPollQueueLoad(
            bool isDeferredByUiSuppression,
            bool isDeferredByUserPriority,
            bool isRecentViewportInteractionActive = false
        )
        {
            string deferReason = ResolveEverythingWatchPollDeferReason(
                isDeferredByUiSuppression,
                isDeferredByUserPriority,
                isRecentViewportInteractionActive
            );
            return UiOperationPriorityPolicy.ShouldProbeEverythingPollQueueLoad(deferReason);
        }

        // 明示操作中や再生中は、poll を細かく刻まず calm 間隔へ寄せて背後 wake-up を減らす。
        internal static int ApplyEverythingWatchPollInteractionDelayPolicy(
            int delayMs,
            bool isDeferredByUiSuppression,
            bool isDeferredByUserPriority,
            bool isPlayerPlaybackActive,
            bool isRecentViewportInteractionActive = false
        )
        {
            if (delayMs <= 0)
            {
                delayMs = EverythingWatchPollIntervalMs;
            }

            UiOperationSnapshot snapshot = CreateEverythingWatchPollOperationSnapshot(
                isUserPriorityActive: isDeferredByUserPriority,
                isManualMode: false,
                isWatchUiSuppressed: isDeferredByUiSuppression,
                isRecentViewportInteractionActive: isRecentViewportInteractionActive,
                isPlayerPlaybackActive: isPlayerPlaybackActive
            );
            string deferReason = UiOperationPriorityPolicy.ResolveEverythingPollDeferReason(
                snapshot
            );
            if (!UiOperationPriorityPolicy.ShouldExtendEverythingPollDelay(snapshot, deferReason))
            {
                return delayMs;
            }

            return Math.Max(delayMs, EverythingWatchPollIntervalCalmMs);
        }

        // Everything poll の入力状態は UI Shell 共通 snapshot を正本にして、旧名 DTO へ戻さない。
        private static UiOperationSnapshot CreateEverythingWatchPollOperationSnapshot(
            bool isUserPriorityActive,
            bool isManualMode,
            bool isWatchUiSuppressed,
            bool isRecentViewportInteractionActive,
            bool isPlayerPlaybackActive
        )
        {
            return new UiOperationSnapshot(
                isUserPriorityActive,
                isManualMode,
                isWatchUiSuppressed,
                isRecentViewportInteractionActive,
                isPlayerPlaybackActive
            );
        }

        // Everything 対象が無い周回では、queue DB 参照や短周期 wake-up を避ける。
        internal static int ApplyEverythingWatchPollEligibilityDelayPolicy(
            int delayMs,
            bool hasEligibleWatchFolders
        )
        {
            if (delayMs <= 0)
            {
                delayMs = EverythingWatchPollIntervalMs;
            }

            return hasEligibleWatchFolders
                ? delayMs
                : Math.Max(delayMs, EverythingWatchPollIntervalBusyMs);
        }

        // queue 負荷を読まない周回では、直前の既知遅延を使って短周期化を避ける。
        internal static int ResolveEverythingWatchPollBaseDelayWhenQueueProbeSkipped(int lastDelayMs)
        {
            return lastDelayMs switch
            {
                EverythingWatchPollIntervalMs => EverythingWatchPollIntervalMs,
                EverythingWatchPollIntervalMediumMs => EverythingWatchPollIntervalMediumMs,
                EverythingWatchPollIntervalCalmMs => EverythingWatchPollIntervalCalmMs,
                EverythingWatchPollIntervalBusyMs => EverythingWatchPollIntervalBusyMs,
                _ => EverythingWatchPollIntervalMs,
            };
        }

        // 混雑度と直近の静かさを見て、Everything poll の待機間隔を決める。
        private int ResolveEverythingWatchPollDelayFromState(int queueActiveCount)
        {
            int delayMs = EverythingWatchPollIntervalMs;

            if (queueActiveCount >= EverythingWatchPollBusyThreshold)
            {
                delayMs = EverythingWatchPollIntervalBusyMs;
            }
            else if (queueActiveCount >= EverythingWatchPollMediumThreshold)
            {
                delayMs = EverythingWatchPollIntervalMediumMs;
            }

            // 起動直後を抜け、更新が静かな周期が続く時だけ少し疎にする。
            if (
                delayMs == EverythingWatchPollIntervalMs
                && !IsStartupFeedPartialActive
                && HasEverythingWatchPollCalmDelayWarmupElapsed()
                && Volatile.Read(ref _consecutiveCalmEverythingPollCount)
                    >= EverythingWatchPollCalmCyclesThreshold
            )
            {
                delayMs = EverythingWatchPollIntervalCalmMs;
            }

            return delayMs;
        }

        // watch ポーリング1周の静かさを記録し、次回の待機間隔判断に使う。
        private void RecordEverythingWatchPollResult(int updateCount)
        {
            Volatile.Write(ref _lastEverythingPollUpdateCount, updateCount);

            if (IsStartupFeedPartialActive)
            {
                Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
                return;
            }

            if (updateCount <= EverythingWatchPollLowUpdateThreshold)
            {
                Interlocked.Increment(ref _consecutiveCalmEverythingPollCount);
                return;
            }

            Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
        }

        // DB切替や監視設定変更後は、別スコープの静穏判定を持ち越さない。
        private void ResetEverythingWatchPollAdaptiveDelayState()
        {
            Volatile.Write(ref _lastEverythingPollUpdateCount, 0);
            Volatile.Write(ref _consecutiveCalmEverythingPollCount, 0);
            Volatile.Write(ref _lastEverythingPollDelayMs, EverythingWatchPollIntervalMs);
            Volatile.Write(ref _lastEverythingPollEligibleWatchFolderCount, 0);
            Volatile.Write(ref _everythingWatchPollLoopStartedTick64, Environment.TickCount64);
            Interlocked.Increment(ref _everythingWatchPollPlanRevision);
        }

        private bool HasEverythingWatchPollEligibleFolders()
        {
            return Volatile.Read(ref _lastEverythingPollEligibleWatchFolderCount) > 0;
        }

        // poll 起動直後は初期処理とぶつかりやすいため、calm 延長は一定時間後だけ許可する。
        private bool HasEverythingWatchPollCalmDelayWarmupElapsed()
        {
            long startedTick = Volatile.Read(ref _everythingWatchPollLoopStartedTick64);
            if (startedTick <= 0)
            {
                // 初回判定時に開始時刻を掴み、初期処理と競合しやすい時間帯は calm 延長を見送る。
                startedTick = Environment.TickCount64;
                Volatile.Write(ref _everythingWatchPollLoopStartedTick64, startedTick);
                return false;
            }

            long elapsedMs = Environment.TickCount64 - startedTick;
            if (elapsedMs < 0)
            {
                return false;
            }

            return elapsedMs >= EverythingWatchPollCalmStartupGraceMs;
        }

        // poll入口では軽い状態だけを固定し、DB/フォルダ存在確認とwatch snapshot取得は背景計画へ渡す。
        private EverythingWatchPollPlanRequest CaptureEverythingWatchPollPlanRequest()
        {
            var mode = GetEverythingIntegrationMode();
            bool isIntegrationConfigured = _indexProviderFacade.IsIntegrationConfigured(mode);
            var availability = _indexProviderFacade.CheckAvailability(mode);
            // OnモードはEverything停止中でも、filesystem fallback走査のためポーリングを止めない。
            bool keepPollingForFallback = (int)mode == 2;
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int revision = Interlocked.Increment(ref _everythingWatchPollPlanRevision);

            return new EverythingWatchPollPlanRequest(
                revision,
                IsStartupFeedPartialActive,
                isIntegrationConfigured,
                availability.CanUse,
                keepPollingForFallback,
                dbPath
            );
        }

        // 重いprobeを背景側でまとめ、戻った結果は revision / DB path / shutdown guard 後だけ採用する。
        private async Task<bool> ShouldRunEverythingWatchPollPolicyAsync(CancellationToken cancellationToken)
        {
            EverythingWatchPollPlanRequest request = CaptureEverythingWatchPollPlanRequest();
            try
            {
                EverythingWatchPollPlanResult result = await Task.Run(
                        () => BuildEverythingWatchPollPlan(request),
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (!IsCurrentEverythingWatchPollPlan(result))
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"everything poll plan skipped: reason=stale revision={result.Revision} db='{result.DbPath}'"
                    );
                    return false;
                }

                Volatile.Write(
                    ref _lastEverythingPollEligibleWatchFolderCount,
                    result.EligibleWatchFolderCount
                );
                return result.ShouldRun;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll eligibility failed: {ex.Message}"
                );
                Volatile.Write(ref _lastEverythingPollEligibleWatchFolderCount, 0);
                return false;
            }
        }

        // DB path の存在確認、watch snapshot取得、watch folder存在確認を同じ背景計画内で直列に閉じる。
        private EverythingWatchPollPlanResult BuildEverythingWatchPollPlan(
            EverythingWatchPollPlanRequest request
        )
        {
            if (
                request.IsStartupFeedPartialActive
                || !request.IsIntegrationConfigured
                || (!request.CanUseAvailability && !request.KeepPollingForFallback)
                || string.IsNullOrWhiteSpace(request.DbPath)
            )
            {
                return new EverythingWatchPollPlanResult(request.Revision, request.DbPath, false, 0);
            }

            bool dbPathExists = Path.Exists(request.DbPath);
            if (!dbPathExists)
            {
                return new EverythingWatchPollPlanResult(request.Revision, request.DbPath, false, 0);
            }

            string[] watchFolders = GetEverythingPollEligibleWatchFoldersSnapshot(
                request.DbPath,
                isDbPathKnownToExist: true
            );

            bool PollPathExists(string path)
            {
                // DB本体はこの計画内で確認済みなので、policy のDB判定で同じprobeを重ねない。
                if (AreSameMainDbPath(path, request.DbPath))
                {
                    return true;
                }

                return Path.Exists(path);
            }

            bool shouldRun = ShouldRunEverythingWatchPollPolicy(
                request.IsStartupFeedPartialActive,
                request.IsIntegrationConfigured,
                request.CanUseAvailability,
                request.KeepPollingForFallback,
                request.DbPath,
                watchFolders,
                PollPathExists,
                _ => true
            );

            return new EverythingWatchPollPlanResult(
                request.Revision,
                request.DbPath,
                shouldRun,
                watchFolders?.Length ?? 0
            );
        }

        private bool IsCurrentEverythingWatchPollPlan(EverythingWatchPollPlanResult result)
        {
            if (result.Revision != Volatile.Read(ref _everythingWatchPollPlanRevision))
            {
                return false;
            }

            if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return false;
            }

            string currentDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(result.DbPath) && string.IsNullOrWhiteSpace(currentDbPath))
            {
                return true;
            }

            return AreSameMainDbPath(result.DbPath, currentDbPath);
        }
    }
}

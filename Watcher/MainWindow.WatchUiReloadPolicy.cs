using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        internal Action<string, bool> FilterAndSortForTesting { get; set; }
        internal Action<string, string, IReadOnlyList<WatchChangedMovie>> RefreshMovieViewFromCurrentSourceForTesting { get; set; }

        // watch 起点の全件再読込は即時連打せず、短い遅延で最新1回へ圧縮する。
        private const int WatchDeferredUiReloadDelayMs = 350;
        private readonly object _watchDeferredUiReloadSync = new();
        private CancellationTokenSource _watchDeferredUiReloadCts = new();
        private int _watchDeferredUiReloadRevision;
        private bool _watchDeferredUiReloadPending;
        private bool _watchDeferredUiReloadQueryOnly;
        private string _watchDeferredUiReloadPlanReason = "";
        private List<WatchChangedMovie> _watchDeferredUiReloadChangedMovies = [];

        // watch 常時監視中だけ、終端のフル reload を短時間 debounce して UI テンポを守る。
        internal static bool ShouldUseDeferredWatchUiReload(bool hasChanges, bool isWatchMode)
        {
            return hasChanges && isWatchMode;
        }

        // watch の変更が in-memory に反映済みなら、DB再読込を飛ばして query-only 再計算へ寄せる。
        internal static bool ShouldUseQueryOnlyWatchUiReload(
            bool hasChanges,
            bool isWatchMode,
            bool canUseQueryOnlyReload
        )
        {
            return hasChanges && isWatchMode && canUseQueryOnlyReload;
        }

        // changedMovies が具体化されている時だけ no-op 札を除き、終端 reload の実効差分を見る。
        internal static bool HasEffectiveWatchUiReloadChanges(
            bool hasChanges,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            if (!hasChanges)
            {
                return false;
            }

            if (changedMovies == null || changedMovies.Count < 1)
            {
                return hasChanges;
            }

            foreach (WatchChangedMovie changedMovie in changedMovies)
            {
                if (
                    changedMovie.ChangeKind != WatchMovieChangeKind.None
                    || changedMovie.DirtyFields != WatchMovieDirtyFields.None
                )
                {
                    return true;
                }
            }

            return false;
        }

        // bulk 候補で一度 full 候補へ落ちても、既存行の軽い属性差分だけなら最終段で query-only へ戻す。
        internal static bool CanRecoverBulkExistingDirtyOnlyQueryReload(
            bool allowBulkExistingDirtyOnlyQueryReload,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            return IsBulkQueryReloadRecoveryReason(
                ResolveBulkQueryReloadRecoveryReason(
                    allowBulkExistingDirtyOnlyQueryReload,
                    changedMovies
                )
            );
        }

        // bulk 降格後でも、既存行だけの安全な差分なら DB 再読込へ戻さない。
        internal static string ResolveBulkQueryReloadRecoveryReason(
            bool allowBulkExistingDirtyOnlyQueryReload,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            if (!allowBulkExistingDirtyOnlyQueryReload)
            {
                return "not-bulk-downgrade";
            }

            WatchMovieDirtyFields safeExistingDirtyFields =
                WatchMovieDirtyFields.FileDate
                | WatchMovieDirtyFields.MovieSize
                | WatchMovieDirtyFields.MovieLength;
            WatchMovieChangeKind safeExistingChangeKinds =
                WatchMovieChangeKind.ViewRepaired
                | WatchMovieChangeKind.DisplayedViewRefresh;
            int changedCount = 0;
            int effectiveChangedCount = 0;
            bool hasViewOnlyChange = false;
            bool hasDirtyChange = false;
            foreach (WatchChangedMovie changedMovie in changedMovies ?? [])
            {
                changedCount++;
                if (
                    changedMovie.ChangeKind == WatchMovieChangeKind.None
                    && changedMovie.DirtyFields == WatchMovieDirtyFields.None
                )
                {
                    // no-op は圧縮後に混ざるだけの札として扱い、有効な差分判定から外す。
                    continue;
                }

                effectiveChangedCount++;
                if ((changedMovie.ChangeKind & WatchMovieChangeKind.SourceInserted) != 0)
                {
                    return "source-inserted";
                }

                if (
                    (changedMovie.ChangeKind & ~safeExistingChangeKinds)
                    != WatchMovieChangeKind.None
                )
                {
                    WatchMovieChangeKind unsafeChangeKinds =
                        changedMovie.ChangeKind & ~safeExistingChangeKinds;
                    return $"change-kind-unsafe:{FormatWatchChangeKindsForReason(unsafeChangeKinds)}";
                }

                if (
                    (changedMovie.DirtyFields & ~safeExistingDirtyFields)
                        != WatchMovieDirtyFields.None
                )
                {
                    WatchMovieDirtyFields unsafeDirtyFields =
                        changedMovie.DirtyFields & ~safeExistingDirtyFields;
                    return $"dirty-fields-unsafe:{FormatWatchDirtyFieldsForReason(unsafeDirtyFields)}";
                }

                hasViewOnlyChange |= changedMovie.ChangeKind != WatchMovieChangeKind.None;
                hasDirtyChange |= changedMovie.DirtyFields != WatchMovieDirtyFields.None;
            }

            if (changedCount < 1)
            {
                return "no-changed-movies";
            }

            if (effectiveChangedCount < 1)
            {
                return "no-effective-change";
            }

            if (hasViewOnlyChange && hasDirtyChange)
            {
                return "bulk-existing-view-dirty-only";
            }

            return hasViewOnlyChange
                ? "bulk-existing-view-only"
                : "bulk-existing-dirty-only";
        }

        private static bool IsBulkQueryReloadRecoveryReason(string reason)
        {
            return string.Equals(reason, "bulk-existing-dirty-only", StringComparison.Ordinal)
                || string.Equals(reason, "bulk-existing-view-only", StringComparison.Ordinal)
                || string.Equals(
                    reason,
                    "bulk-existing-view-dirty-only",
                    StringComparison.Ordinal
                );
        }

        // plan_reason と並べて常時出す札を固定し、full fallback の次候補を実機ログで選べるようにする。
        internal static string ResolveWatchUiReloadRecoveryReason(
            bool canUseQueryOnlyReload,
            bool allowBulkExistingDirtyOnlyQueryReload,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            return canUseQueryOnlyReload
                ? "already-query-only"
                : ResolveBulkQueryReloadRecoveryReason(
                    allowBulkExistingDirtyOnlyQueryReload,
                    changedMovies
                );
        }

        internal static string BuildWatchUiReloadPlanLogFields(
            string planReason,
            string recoveryReason
        )
        {
            string resolvedPlanReason = string.IsNullOrWhiteSpace(planReason)
                ? "unknown"
                : planReason;
            string resolvedRecoveryReason = string.IsNullOrWhiteSpace(recoveryReason)
                ? "none"
                : recoveryReason;

            return $"plan_reason={resolvedPlanReason} recovery_reason={resolvedRecoveryReason}";
        }

        // 安全外の change kind だけを列挙し、full fallback の次候補をログで選びやすくする。
        private static string FormatWatchChangeKindsForReason(WatchMovieChangeKind changeKinds)
        {
            if (changeKinds == WatchMovieChangeKind.None)
            {
                return "None";
            }

            List<string> names = [];
            int remaining = (int)changeKinds;
            foreach (WatchMovieChangeKind kind in Enum.GetValues<WatchMovieChangeKind>())
            {
                if (kind == WatchMovieChangeKind.None)
                {
                    continue;
                }

                if ((changeKinds & kind) != WatchMovieChangeKind.None)
                {
                    names.Add(kind.ToString());
                    remaining &= ~(int)kind;
                }
            }

            // 将来 enum 名がまだ無い bit が来ても、bit 単位の札としてログへ残す。
            for (int bit = 1; remaining != 0 && bit > 0; bit <<= 1)
            {
                if ((remaining & bit) == 0)
                {
                    continue;
                }

                names.Add(bit.ToString());
                remaining &= ~bit;
            }

            return names.Count > 0 ? string.Join(",", names) : changeKinds.ToString();
        }

        // unsafe 側だけを短い札へ畳み、実機ログから次の削減候補を選びやすくする。
        private static string FormatWatchDirtyFieldsForReason(WatchMovieDirtyFields dirtyFields)
        {
            if (dirtyFields == WatchMovieDirtyFields.None)
            {
                return "None";
            }

            List<string> names = [];
            int remaining = (int)dirtyFields;
            foreach (WatchMovieDirtyFields field in Enum.GetValues<WatchMovieDirtyFields>())
            {
                if (field == WatchMovieDirtyFields.None)
                {
                    continue;
                }

                if ((dirtyFields & field) != WatchMovieDirtyFields.None)
                {
                    names.Add(field.ToString());
                    remaining &= ~(int)field;
                }
            }

            // enum にまだ名前が無い dirty bit も、落とさず数値の札として残す。
            for (int bit = 1; remaining != 0 && bit > 0; bit <<= 1)
            {
                if ((remaining & bit) == 0)
                {
                    continue;
                }

                names.Add(bit.ToString());
                remaining &= ~bit;
            }

            return names.Count > 0 ? string.Join(",", names) : dirtyFields.ToString();
        }

        // 遅延実行時には、まだ同じDB向けの最新要求かを確認して stale reload を止める。
        internal static bool CanApplyDeferredWatchUiReload(
            string currentDbFullPath,
            string scheduledDbFullPath,
            bool isWatchSuppressedByUi,
            int requestRevision,
            int currentRevision
        )
        {
            return string.Equals(
                ResolveDeferredWatchUiReloadApplyState(
                    currentDbFullPath,
                    scheduledDbFullPath,
                    isWatchSuppressedByUi,
                    requestRevision,
                    currentRevision
                ),
                "apply",
                StringComparison.Ordinal
            );
        }

        // deferred reload が適用されない理由を短い札へ落とし、DB切替や古い要求をログから切り分ける。
        internal static string ResolveDeferredWatchUiReloadApplyState(
            string currentDbFullPath,
            string scheduledDbFullPath,
            bool isWatchSuppressedByUi,
            int requestRevision,
            int currentRevision
        )
        {
            if (isWatchSuppressedByUi)
            {
                return "ui-suppressed";
            }

            if (requestRevision != currentRevision)
            {
                return "revision-stale";
            }

            if (
                string.IsNullOrWhiteSpace(currentDbFullPath)
                || string.IsNullOrWhiteSpace(scheduledDbFullPath)
            )
            {
                return "db-empty";
            }

            return string.Equals(
                currentDbFullPath,
                scheduledDbFullPath,
                StringComparison.OrdinalIgnoreCase
            )
                ? "apply"
                : "db-changed";
        }

        // 終端reloadの分岐を純粋値へ落とし、呼び出し側は plan の実行だけに寄せる。
        internal static WatchUiReloadPlan EvaluateWatchUiReloadPlan(
            bool hasChanges,
            bool isWatchMode,
            bool isSuppressed,
            bool canUseQueryOnlyReload
        )
        {
            if (!hasChanges)
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.SkipNoChanges, false);
            }

            if (isSuppressed)
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.DeferBySuppression, false);
            }

            bool useQueryOnlyReload = ShouldUseQueryOnlyWatchUiReload(
                hasChanges,
                isWatchMode,
                canUseQueryOnlyReload
            );
            if (ShouldUseDeferredWatchUiReload(hasChanges, isWatchMode))
            {
                return new WatchUiReloadPlan(WatchUiReloadAction.ScheduleDeferred, useQueryOnlyReload);
            }

            return new WatchUiReloadPlan(WatchUiReloadAction.ApplyImmediate, useQueryOnlyReload);
        }

        // watch 終端 reload の判断理由を短い札へ落とし、full 戻りの次候補をログで拾いやすくする。
        internal static string ResolveWatchUiReloadPlanReason(
            bool hasChanges,
            bool isWatchMode,
            bool isSuppressed,
            bool canUseQueryOnlyReload
        )
        {
            if (!hasChanges)
            {
                return "no-changes";
            }

            if (isSuppressed)
            {
                return "ui-suppressed";
            }

            if (!isWatchMode)
            {
                return "not-watch";
            }

            return canUseQueryOnlyReload ? "watch-query-only" : "watch-full-fallback";
        }

        // watch 起点の reload 要求を最新1回へ圧縮し、連続通知時の UI 全面再読込を抑える。
        private void RequestDeferredWatchUiReload(
            string snapshotDbFullPath,
            string reason,
            string planReason,
            string recoveryReason,
            bool useQueryOnlyReload,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return;
            }

            CancellationTokenSource nextCts = new();
            CancellationTokenSource previousCts;
            int requestRevision;
            int changedMovieCount;
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                previousCts = _watchDeferredUiReloadCts ?? new CancellationTokenSource();
                _watchDeferredUiReloadCts = nextCts;
                requestRevision = Interlocked.Increment(ref _watchDeferredUiReloadRevision);
                _watchDeferredUiReloadPending = true;
                _watchDeferredUiReloadQueryOnly = useQueryOnlyReload;
                _watchDeferredUiReloadPlanReason = planReason ?? "";
                _watchDeferredUiReloadChangedMovies = useQueryOnlyReload
                    ? MergeChangedMovies(_watchDeferredUiReloadChangedMovies, changedMovies)
                    : [];
                changedMovieCount = _watchDeferredUiReloadChangedMovies.Count;
            }

            try
            {
                previousCts.Cancel();
            }
            catch
            {
                // 旧要求の停止失敗でも最新要求は継続させる。
            }
            finally
            {
                previousCts.Dispose();
            }

            UiWorkRequest workRequest = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
                useQueryOnlyReload
            );
            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload scheduled: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} reload={(useQueryOnlyReload ? "query-only" : "full")} changed_paths={changedMovieCount} {BuildWatchUiReloadPlanLogFields(planReason, recoveryReason)} {BuildWatchUiWorkRequestLogFields(workRequest, UiWorkRequestPolicy.ReleaseReasonDeferred)} delay_ms={WatchDeferredUiReloadDelayMs}"
            );
            _ = RunDeferredWatchUiReloadAsync(
                snapshotDbFullPath,
                requestRevision,
                reason,
                recoveryReason,
                nextCts.Token
            );
        }

        // DB切替や手動即時更新時は、残っている watch 用遅延 reload を取り消す。
        private bool CancelDeferredWatchUiReload(string reason)
        {
            CancellationTokenSource nextCts = new();
            CancellationTokenSource previousCts;
            int requestRevision;
            bool hadPendingRequest;
            bool canceledUseQueryOnlyReload;
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                previousCts = _watchDeferredUiReloadCts ?? new CancellationTokenSource();
                _watchDeferredUiReloadCts = nextCts;
                requestRevision = Interlocked.Increment(ref _watchDeferredUiReloadRevision);
                hadPendingRequest = _watchDeferredUiReloadPending;
                canceledUseQueryOnlyReload = _watchDeferredUiReloadQueryOnly;
                _watchDeferredUiReloadPending = false;
                _watchDeferredUiReloadQueryOnly = false;
                _watchDeferredUiReloadPlanReason = "";
                _watchDeferredUiReloadChangedMovies = [];
            }

            try
            {
                previousCts.Cancel();
            }
            catch
            {
                // 取消失敗でも後続処理を止めない。
            }
            finally
            {
                previousCts.Dispose();
            }

            string workRequestLogFields = "";
            if (hadPendingRequest)
            {
                UiWorkRequest workRequest = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
                    canceledUseQueryOnlyReload
                );
                workRequestLogFields =
                    " "
                    + BuildWatchUiWorkRequestLogFields(
                        workRequest,
                        UiWorkRequestPolicy.ReleaseReasonCanceled
                    );
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload canceled: revision={requestRevision} reason={reason} had_pending={FormatLogBool(hadPendingRequest)}{workRequestLogFields}"
            );
            return hadPendingRequest;
        }

        // apply直前に現在要求を消費し、旧reloadが新しい要求のpendingを奪わないようにする。
        private bool TryConsumeDeferredWatchUiReload(
            int requestRevision,
            out bool useQueryOnlyReload,
            out string planReason,
            out string consumeState,
            out List<WatchChangedMovie> changedMovies
        )
        {
            lock (GetWatchDeferredUiReloadSyncRoot())
            {
                consumeState = ResolveDeferredWatchUiReloadConsumeState(
                    _watchDeferredUiReloadPending,
                    requestRevision,
                    _watchDeferredUiReloadRevision
                );
                if (!string.Equals(consumeState, "consumed", StringComparison.Ordinal))
                {
                    useQueryOnlyReload = false;
                    planReason = "";
                    changedMovies = [];
                    return false;
                }

                _watchDeferredUiReloadPending = false;
                useQueryOnlyReload = _watchDeferredUiReloadQueryOnly;
                planReason = _watchDeferredUiReloadPlanReason ?? "";
                changedMovies = _watchDeferredUiReloadChangedMovies?.ToList() ?? [];
                _watchDeferredUiReloadPlanReason = "";
                _watchDeferredUiReloadChangedMovies = [];
                return true;
            }
        }

        // pending consume の失敗理由を固定し、old reload と二重消費をログで切り分ける。
        internal static string ResolveDeferredWatchUiReloadConsumeState(
            bool hasPendingRequest,
            int requestRevision,
            int currentRevision
        )
        {
            if (!hasPendingRequest)
            {
                return "not-pending";
            }

            return requestRevision == currentRevision ? "consumed" : "revision-stale";
        }

        // テストの未初期化インスタンスでもlock先が null にならないように退避する。
        private object GetWatchDeferredUiReloadSyncRoot()
        {
            return _watchDeferredUiReloadSync ?? _watchUiSuppressionSync ?? this;
        }

        // 遅延窓の後で、まだ最新要求なら現在の sort / search 条件で MainDB を引き直す。
        private async Task RunDeferredWatchUiReloadAsync(
            string snapshotDbFullPath,
            int requestRevision,
            string reason,
            string recoveryReason,
            CancellationToken cancellationToken
        )
        {
            try
            {
                await Task.Delay(WatchDeferredUiReloadDelayMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => ApplyDeferredWatchUiReloadOnUiThreadCore(
                    snapshotDbFullPath,
                    requestRevision,
                    reason,
                    recoveryReason
                ),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        // 遅延reloadの本体を分け、UIスレッド実行時もテスト時も同じ分岐を通す。
        private void ApplyDeferredWatchUiReloadOnUiThread(
            string snapshotDbFullPath,
            int requestRevision,
            string reason
        )
        {
            ApplyDeferredWatchUiReloadOnUiThreadCore(
                snapshotDbFullPath,
                requestRevision,
                reason,
                recoveryReason: ""
            );
        }

        private void ApplyDeferredWatchUiReloadOnUiThreadCore(
            string snapshotDbFullPath,
            int requestRevision,
            string reason,
            string recoveryReason
        )
        {
            if (
                !TryConsumeDeferredWatchUiReload(
                    requestRevision,
                    out bool useQueryOnlyReload,
                    out string planReason,
                    out string consumeState,
                    out List<WatchChangedMovie> changedMovies
                )
            )
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred ui reload skipped stale: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} skip_reason={consumeState}"
                );
                return;
            }

            bool isWatchSuppressed = IsWatchSuppressedByUi();
            string applyState = ResolveDeferredWatchUiReloadApplyState(
                MainVM?.DbInfo?.DBFullPath ?? "",
                snapshotDbFullPath,
                isWatchSuppressed,
                requestRevision,
                Volatile.Read(ref _watchDeferredUiReloadRevision)
            );
            if (!string.Equals(applyState, "apply", StringComparison.Ordinal))
            {
                if (ShouldSuppressWatchWorkByUi(isWatchSuppressed, true))
                {
                    MarkWatchWorkDeferredWhileSuppressed($"deferred-ui-reload:{reason}");
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"deferred ui reload suppressed: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} skip_reason={applyState}"
                    );
                    return;
                }

                DebugRuntimeLog.Write(
                    "watch-check",
                    $"deferred ui reload skipped stale: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} skip_reason={applyState}"
                );
                return;
            }

            string currentSort = MainVM?.DbInfo?.Sort ?? "";
            UiWorkRequest workRequest = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
                useQueryOnlyReload
            );
            DebugRuntimeLog.Write(
                "watch-check",
                $"deferred ui reload apply: db='{snapshotDbFullPath}' revision={requestRevision} reason={reason} reload={(useQueryOnlyReload ? "query-only" : "full")} sort={currentSort} changed_paths={changedMovies.Count} {BuildWatchUiReloadPlanLogFields(planReason, recoveryReason)} {BuildWatchUiWorkRequestLogFields(workRequest, UiWorkRequestPolicy.ReleaseReasonReleased)}"
            );
            InvokeWatchUiReload(
                currentSort,
                useQueryOnlyReload,
                $"deferred:{reason}",
                changedMovies,
                recoveryReason
            );
        }

        // watch本流の reload 方針はここだけで決め、scan 本体から分岐密度を追い出す。
        private void HandleFolderCheckUiReloadAfterChanges(
            bool hasChanges,
            CheckMode mode,
            string snapshotDbFullPath,
            bool canUseQueryOnlyReload,
            bool allowBulkExistingDirtyOnlyQueryReload,
            IReadOnlyList<WatchChangedMovie> changedMovies
        )
        {
            HandleFolderCheckUiReloadAfterChangesWithSort(
                hasChanges,
                mode,
                snapshotDbFullPath,
                canUseQueryOnlyReload,
                allowBulkExistingDirtyOnlyQueryReload,
                changedMovies,
                MainVM?.DbInfo?.Sort ?? ""
            );
        }

        // 走査完了時の UI 再読込は、呼び出し側で確定した sort を使って MainWindow 依存を薄める。
        private void HandleFolderCheckUiReloadAfterChangesWithSort(
            bool hasChanges,
            CheckMode mode,
            string snapshotDbFullPath,
            bool canUseQueryOnlyReload,
            bool allowBulkExistingDirtyOnlyQueryReload,
            IReadOnlyList<WatchChangedMovie> changedMovies,
            string currentSort
        )
        {
            bool isWatchMode = mode == CheckMode.Watch;
            bool isSuppressed = ShouldSuppressWatchWorkByUi(
                IsWatchSuppressedByUi(),
                isWatchMode
            );
            bool hasEffectiveChanges = HasEffectiveWatchUiReloadChanges(
                hasChanges,
                changedMovies
            );
            string queryOnlyRecoveryReason = ResolveWatchUiReloadRecoveryReason(
                canUseQueryOnlyReload,
                allowBulkExistingDirtyOnlyQueryReload,
                changedMovies
            );
            bool recoveredQueryOnlyReload =
                !canUseQueryOnlyReload
                && IsBulkQueryReloadRecoveryReason(queryOnlyRecoveryReason);
            bool resolvedCanUseQueryOnlyReload =
                canUseQueryOnlyReload || recoveredQueryOnlyReload;
            if (recoveredQueryOnlyReload)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"watch query-only recovered: reason={queryOnlyRecoveryReason} changed_paths={changedMovies?.Count ?? 0}"
                );
            }

            string planReason = ResolveWatchUiReloadPlanReason(
                hasEffectiveChanges,
                isWatchMode,
                isSuppressed,
                resolvedCanUseQueryOnlyReload
            );
            WatchUiReloadPlan reloadPlan = EvaluateWatchUiReloadPlan(
                hasEffectiveChanges,
                isWatchMode,
                isSuppressed,
                resolvedCanUseQueryOnlyReload
            );
            if (reloadPlan.Action == WatchUiReloadAction.SkipNoChanges)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip final watch ui reload no changes: mode={mode} db='{snapshotDbFullPath}' can_query_only={resolvedCanUseQueryOnlyReload} {BuildWatchUiReloadPlanLogFields(planReason, queryOnlyRecoveryReason)}"
                );
                return;
            }

            if (reloadPlan.Action == WatchUiReloadAction.DeferBySuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed($"final-reload:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip final watch ui reload by suppression: mode={mode} db='{snapshotDbFullPath}' can_query_only={resolvedCanUseQueryOnlyReload} {BuildWatchUiReloadPlanLogFields(planReason, queryOnlyRecoveryReason)}"
                );
                return;
            }

            if (reloadPlan.Action == WatchUiReloadAction.ScheduleDeferred)
            {
                RequestDeferredWatchUiReload(
                    snapshotDbFullPath,
                    $"check-folder:{mode}",
                    planReason,
                    queryOnlyRecoveryReason,
                    reloadPlan.UseQueryOnlyReload,
                    changedMovies
                );
                return;
            }

            CancelDeferredWatchUiReload($"immediate-reload:{mode}");
            UiWorkRequest workRequest = UiWorkRequestPolicy.CreateWatchUiReloadRequest(
                reloadPlan.UseQueryOnlyReload
            );
            DebugRuntimeLog.Write(
                "watch-check",
                $"final folder check ui reload apply: mode={mode} db='{snapshotDbFullPath}' reload={(reloadPlan.UseQueryOnlyReload ? "query-only" : "full")} changed_paths={changedMovies?.Count ?? 0} can_query_only={resolvedCanUseQueryOnlyReload} {BuildWatchUiReloadPlanLogFields(planReason, queryOnlyRecoveryReason)} {BuildWatchUiWorkRequestLogFields(workRequest, UiWorkRequestPolicy.ReleaseReasonReleased)}"
            );
            InvokeWatchUiReload(
                currentSort,
                reloadPlan.UseQueryOnlyReload,
                $"final:{mode}",
                changedMovies,
                queryOnlyRecoveryReason
            );
        }

        // watch の変更setを直接実行せず、reload policy / ReadModel 入口の request へ畳む。
        internal static WatchUiApplyRequest BuildWatchUiApplyRequest(
            string sort,
            bool useQueryOnlyReload,
            string reason,
            IReadOnlyList<WatchChangedMovie> changedMovies,
            string fullFallbackReason = ""
        )
        {
            int changedMovieCount = changedMovies?.Count ?? 0;
            string resolvedFullFallbackReason = useQueryOnlyReload
                ? MovieViewDiffFactory.FallbackReasonNone
                : (
                    string.IsNullOrWhiteSpace(fullFallbackReason)
                        ? "watch-full-fallback"
                        : fullFallbackReason
                );

            return new WatchUiApplyRequest(
                string.IsNullOrWhiteSpace(sort) ? "" : sort,
                string.IsNullOrWhiteSpace(reason) ? "watch" : reason,
                useQueryOnlyReload
                    ? WatchUiApplyRequestKind.InMemoryReadModelRefresh
                    : WatchUiApplyRequestKind.FullFallbackReload,
                UiWorkRequestPolicy.CreateWatchUiReloadRequest(useQueryOnlyReload),
                useQueryOnlyReload ? (changedMovies ?? []) : [],
                changedMovieCount,
                MovieViewDiffApplyPolicy.ResolveWatchUiApplyCandidate(
                    useQueryOnlyReload,
                    changedMovieCount,
                    resolvedFullFallbackReason
                )
            );
        }

        // scheduler 本体は作らず、watch reload を既存ログ上で同じ作業要求語彙として読めるようにする。
        internal static string BuildWatchUiWorkRequestLogFields(
            UiWorkRequest request,
            string releaseReason
        )
        {
            return $"{UiWorkRequestPolicy.BuildRequestSchedulerLogFields(request, releaseReason)} operation_reason={request.LogReason}";
        }

        // watch の query-only は、DB再読込へ戻さず in-memory 一覧から再計算する。
        private void InvokeWatchUiReload(
            string sort,
            bool useQueryOnlyReload,
            string reason,
            IReadOnlyList<WatchChangedMovie> changedMovies,
            string fullFallbackReason = ""
        )
        {
            WatchUiApplyRequest request = BuildWatchUiApplyRequest(
                sort,
                useQueryOnlyReload,
                reason,
                changedMovies,
                fullFallbackReason
            );
            ApplyWatchUiApplyRequest(request);
        }

        // request の実行先をここだけに閉じ、次段で Scheduler / ReadModel へ差し替えやすくする。
        private void ApplyWatchUiApplyRequest(WatchUiApplyRequest request)
        {
            if (request.Kind == WatchUiApplyRequestKind.FullFallbackReload)
            {
                InvokeFilterAndSortForWatch(request.Sort, true);
                return;
            }

            Action<string, string, IReadOnlyList<WatchChangedMovie>> refreshTestHook =
                RefreshMovieViewFromCurrentSourceForTesting;
            if (refreshTestHook != null)
            {
                refreshTestHook(request.Sort, request.Reason, request.ChangedMovies);
                return;
            }

            _ = RefreshMovieViewFromCurrentSourceAsync(
                request.Sort,
                "watch-query-only",
                UiHangActivityKind.Watch,
                request.ChangedMovies
            );
        }

        // テスト時だけ差し替え可能にし、本番では既存の FilterAndSort をそのまま使う。
        private void InvokeFilterAndSortForWatch(string sort, bool isGetNew)
        {
            Action<string, bool> testHook = FilterAndSortForTesting;
            if (testHook != null)
            {
                testHook(sort, isGetNew);
                return;
            }

            FilterAndSort(sort, isGetNew);
        }

        internal enum WatchUiReloadAction
        {
            SkipNoChanges,
            DeferBySuppression,
            ScheduleDeferred,
            ApplyImmediate,
        }

        internal readonly record struct WatchUiReloadPlan(
            WatchUiReloadAction Action,
            bool UseQueryOnlyReload
        );

        internal enum WatchUiApplyRequestKind
        {
            FullFallbackReload,
            InMemoryReadModelRefresh,
        }

        internal readonly record struct WatchUiApplyRequest(
            string Sort,
            string Reason,
            WatchUiApplyRequestKind Kind,
            UiWorkRequest WorkRequest,
            IReadOnlyList<WatchChangedMovie> ChangedMovies,
            int ChangedMovieCount,
            MovieViewDiffApplyPlan DiffApplyPlan
        );
    }
}

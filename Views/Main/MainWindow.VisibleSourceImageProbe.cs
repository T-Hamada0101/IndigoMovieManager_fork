using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly record struct VisibleSourceImageProbeTarget(
            string MoviePathKey,
            int FilteredIndex,
            MovieRecords Record
        );

        private readonly record struct VisibleSourceImageProbeResolution(
            VisibleSourceImageProbeTarget Target,
            string SourceImagePath
        );

        private sealed record VisibleSourceImageProbeRequest(
            string Reason,
            int ProbeRevision,
            int FilterRevision,
            string DbFullPath,
            VisibleSourceImageProbeTarget[] Targets
        );

        private int _visibleSourceImageProbeRevision;
        private string _visibleSourceImageProbePendingRequest;
        private int _visibleSourceImageProbeWorkerRunning;

        // 最新viewportだけを対象にし、user-priority中は探索開始そのものを後ろへ送る。
        private void QueueVisibleSourceImageProbe(string reason)
        {
            if (
                Dispatcher == null
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || Volatile.Read(ref _mainWindowClosingStarted) != 0
            )
            {
                return;
            }

            if (IsUserPriorityWorkActive())
            {
                Interlocked.Exchange(ref _visibleSourceImageProbePendingRequest, reason);
                if (
                    Interlocked.CompareExchange(
                        ref _visibleSourceImageProbeWorkerRunning,
                        1,
                        0
                    ) == 0
                )
                {
                    _ = RunVisibleSourceImageProbeWorkerAsync();
                }
                return;
            }

            int probeRevision = Interlocked.Increment(ref _visibleSourceImageProbeRevision);
            int filterRevision = _filterAndSortRequestRevision;
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            VisibleSourceImageProbeTarget[] targets = CaptureVisibleSourceImageProbeTargets();
            if (targets.Length == 0)
            {
                return;
            }

            VisibleSourceImageProbeRequest request = new(
                reason,
                probeRevision,
                filterRevision,
                dbFullPath,
                targets
            );
            _ = ExecuteVisibleSourceImageProbeAsync(request);
        }

        // 現在のnear-visible範囲だけを読む。非filtered全件やMovieRecs全件は走査しない。
        private VisibleSourceImageProbeTarget[] CaptureVisibleSourceImageProbeTargets()
        {
            if (
                !_activeUpperTabVisibleRange.HasVisibleItems
                || MainVM?.FilteredMovieRecs == null
                || _preferredVisibleMoviePathKeysSnapshot.Count == 0
            )
            {
                return [];
            }

            HashSet<string> preferredKeys = new(
                _preferredVisibleMoviePathKeysSnapshot,
                StringComparer.OrdinalIgnoreCase
            );
            List<VisibleSourceImageProbeTarget> targets = [];
            int first = Math.Max(0, _activeUpperTabVisibleRange.FirstNearVisibleIndex);
            int last = Math.Min(
                MainVM.FilteredMovieRecs.Count - 1,
                _activeUpperTabVisibleRange.LastNearVisibleIndex
            );
            for (int index = first; index <= last; index++)
            {
                MovieRecords record = MainVM.FilteredMovieRecs[index];
                string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(
                    record?.Movie_Path ?? ""
                );
                if (
                    record != null
                    && ThumbnailErrorPlaceholderHelper.CountPlaceholders(record) > 0
                    && preferredKeys.Remove(moviePathKey)
                )
                {
                    targets.Add(new VisibleSourceImageProbeTarget(moviePathKey, index, record));
                }
            }

            return targets.ToArray();
        }

        // 操作中は最新reasonだけを保持し、解除後に通常Queueへ一度だけ戻す。
        private async Task RunVisibleSourceImageProbeWorkerAsync()
        {
            while (true)
            {
                while (IsUserPriorityWorkActive())
                {
                    await Task.Delay(120);
                    if (
                        Dispatcher == null
                        || Dispatcher.HasShutdownStarted
                        || Dispatcher.HasShutdownFinished
                        || Volatile.Read(ref _mainWindowClosingStarted) != 0
                    )
                    {
                        Interlocked.Exchange(ref _visibleSourceImageProbePendingRequest, null);
                        Interlocked.Exchange(ref _visibleSourceImageProbeWorkerRunning, 0);
                        return;
                    }
                }

                try
                {
                    await Dispatcher.InvokeAsync(
                        () =>
                        {
                            string latestReason = Interlocked.Exchange(
                                ref _visibleSourceImageProbePendingRequest,
                                null
                            );
                            if (!string.IsNullOrEmpty(latestReason))
                            {
                                QueueVisibleSourceImageProbe(latestReason);
                            }
                        },
                        DispatcherPriority.Background
                    );
                }
                catch (TaskCanceledException)
                {
                    Interlocked.Exchange(ref _visibleSourceImageProbePendingRequest, null);
                    Interlocked.Exchange(ref _visibleSourceImageProbeWorkerRunning, 0);
                    return;
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _visibleSourceImageProbePendingRequest, null);
                    Interlocked.Exchange(ref _visibleSourceImageProbeWorkerRunning, 0);
                    return;
                }

                Interlocked.Exchange(ref _visibleSourceImageProbeWorkerRunning, 0);
                if (Volatile.Read(ref _visibleSourceImageProbePendingRequest) == null)
                {
                    return;
                }

                if (
                    Interlocked.CompareExchange(
                        ref _visibleSourceImageProbeWorkerRunning,
                        1,
                        0
                    ) != 0
                )
                {
                    return;
                }
            }
        }

        private async Task ExecuteVisibleSourceImageProbeAsync(
            VisibleSourceImageProbeRequest request
        )
        {
            string reason = request.Reason;
            int probeRevision = request.ProbeRevision;
            int filterRevision = request.FilterRevision;
            string dbFullPath = request.DbFullPath;
            VisibleSourceImageProbeTarget[] targets = request.Targets;
            try
            {
                while (IsUserPriorityWorkActive())
                {
                    await Task.Delay(120);
                    if (
                        !IsVisibleSourceImageProbeRequestCurrent(
                            probeRevision,
                            filterRevision,
                            dbFullPath
                        )
                    )
                    {
                        WriteVisibleSourceImageProbeResult(
                            reason,
                            probeRevision,
                            filterRevision,
                            targets.Length,
                            0,
                            true,
                            0
                        );
                        return;
                    }
                }

                VisibleSourceImageProbeResolution[] resolved = await Task.Run(() =>
                {
                    List<VisibleSourceImageProbeResolution> results = [];
                    foreach (VisibleSourceImageProbeTarget target in targets)
                    {
                        if (probeRevision != Volatile.Read(ref _visibleSourceImageProbeRevision))
                        {
                            break;
                        }

                        if (
                            ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                                target.Record.Movie_Path,
                                out string sourceImagePath
                            )
                        )
                        {
                            results.Add(
                                new VisibleSourceImageProbeResolution(target, sourceImagePath)
                            );
                        }
                    }

                    return results.ToArray();
                });

                if (
                    !IsVisibleSourceImageProbeRequestCurrent(
                        probeRevision,
                        filterRevision,
                        dbFullPath
                    )
                )
                {
                    WriteVisibleSourceImageProbeResult(
                        reason,
                        probeRevision,
                        filterRevision,
                        targets.Length,
                        resolved.Length,
                        true,
                        0
                    );
                    return;
                }

                Stopwatch applyStopwatch = Stopwatch.StartNew();
                int applied = 0;
                foreach (VisibleSourceImageProbeResolution resolution in resolved)
                {
                    if (!IsSameVisibleMovieRecordReference(resolution.Target))
                    {
                        continue;
                    }

                    MovieRecords record = resolution.Target.Record;
                    bool changed = false;
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbPathSmall,
                        value => record.ThumbPathSmall = value,
                        resolution.SourceImagePath
                    );
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbPathBig,
                        value => record.ThumbPathBig = value,
                        resolution.SourceImagePath
                    );
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbPathGrid,
                        value => record.ThumbPathGrid = value,
                        resolution.SourceImagePath
                    );
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbPathList,
                        value => record.ThumbPathList = value,
                        resolution.SourceImagePath
                    );
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbPathBig10,
                        value => record.ThumbPathBig10 = value,
                        resolution.SourceImagePath
                    );
                    changed |= ApplySourceImageToPlaceholder(
                        record.ThumbDetail,
                        value => record.ThumbDetail = value,
                        resolution.SourceImagePath
                    );
                    if (changed)
                    {
                        applied++;
                    }
                }
                applyStopwatch.Stop();
                if (applied > 0)
                {
                    RefreshSharedUpperTabImageRevision();
                    RefreshPlayerRightRailImageRevision();
                }
                WriteVisibleSourceImageProbeResult(
                    reason,
                    probeRevision,
                    filterRevision,
                    targets.Length,
                    applied,
                    false,
                    applyStopwatch.ElapsedMilliseconds
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"source image probe failed: reason={reason} revision={probeRevision} filter_revision={filterRevision} requested={targets.Length} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 背景探索の完了後も、その間に生成された管理サムネイルは上書きしない。
        private static bool ApplySourceImageToPlaceholder(
            string currentPath,
            Action<string> apply,
            string sourceImagePath
        )
        {
            if (!ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(currentPath))
            {
                return false;
            }

            apply(sourceImagePath);
            return true;
        }

        private bool IsVisibleSourceImageProbeRequestCurrent(
            int probeRevision,
            int filterRevision,
            string dbFullPath
        )
        {
            return probeRevision == Volatile.Read(ref _visibleSourceImageProbeRevision)
                && filterRevision == Volatile.Read(ref _filterAndSortRequestRevision)
                && AreSameMainDbPath(dbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
                && Volatile.Read(ref _mainWindowClosingStarted) == 0
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished;
        }

        private bool IsSameVisibleMovieRecordReference(VisibleSourceImageProbeTarget target)
        {
            if (
                MainVM?.FilteredMovieRecs == null
                || target.FilteredIndex < 0
                || target.FilteredIndex >= MainVM.FilteredMovieRecs.Count
            )
            {
                return false;
            }

            MovieRecords current = MainVM.FilteredMovieRecs[target.FilteredIndex];
            return ReferenceEquals(current, target.Record)
                && string.Equals(
                    QueueDbPathResolver.CreateMoviePathKey(current?.Movie_Path ?? ""),
                    target.MoviePathKey,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static void WriteVisibleSourceImageProbeResult(
            string reason,
            int probeRevision,
            int filterRevision,
            int requested,
            int resolved,
            bool stale,
            long applyMilliseconds
        )
        {
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"source image probe: reason={reason} revision={probeRevision} filter_revision={filterRevision} requested={requested} resolved={resolved} stale={stale} apply_ms={applyMilliseconds} stage=completed"
            );
        }
    }
}

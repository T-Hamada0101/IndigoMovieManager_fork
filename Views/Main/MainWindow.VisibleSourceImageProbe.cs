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

        private int _visibleSourceImageProbeRevision;

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

            int probeRevision = Interlocked.Increment(ref _visibleSourceImageProbeRevision);
            int filterRevision = _filterAndSortRequestRevision;
            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            VisibleSourceImageProbeTarget[] targets = CaptureVisibleSourceImageProbeTargets();
            if (targets.Length == 0)
            {
                return;
            }

            _ = RunVisibleSourceImageProbeAsync(
                reason,
                probeRevision,
                filterRevision,
                dbFullPath,
                targets
            );
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
                if (record != null && preferredKeys.Remove(moviePathKey))
                {
                    targets.Add(new VisibleSourceImageProbeTarget(moviePathKey, index, record));
                }
            }

            return targets.ToArray();
        }

        private async Task RunVisibleSourceImageProbeAsync(
            string reason,
            int probeRevision,
            int filterRevision,
            string dbFullPath,
            VisibleSourceImageProbeTarget[] targets
        )
        {
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
                    record.ThumbPathSmall = resolution.SourceImagePath;
                    record.ThumbPathBig = resolution.SourceImagePath;
                    record.ThumbPathGrid = resolution.SourceImagePath;
                    record.ThumbPathList = resolution.SourceImagePath;
                    record.ThumbPathBig10 = resolution.SourceImagePath;
                    record.ThumbDetail = resolution.SourceImagePath;
                    applied++;
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

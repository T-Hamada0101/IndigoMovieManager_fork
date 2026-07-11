using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int StartupAsciiSearchPrewarmLimit = 200;
        private const int StartupAsciiSearchPrewarmBatchSize = 16;

        private readonly object _startupAsciiSearchPrewarmSync = new();
        private StartupAsciiSearchPrewarmRequest _startupAsciiSearchPrewarmPending;
        private Task _startupAsciiSearchPrewarmTask;
        private int _startupAsciiSearchPrewarmRevision;

        private void QueueStartupAsciiSearchPrewarm(
            int startupSessionRevision,
            string dbFullPath
        )
        {
            if (
                !IsStartupFeedPartialActive
                || MainVM?.MovieRecs == null
                || MainVM.MovieRecs.Count == 0
                || startupSessionRevision <= 0
                || string.IsNullOrWhiteSpace(dbFullPath)
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
            )
            {
                return;
            }

            int count = Math.Min(MainVM.MovieRecs.Count, StartupAsciiSearchPrewarmLimit);
            // UI側では先頭ページの参照だけを確定し、投影生成は背景へ渡す。
            var snapshot = new MovieRecords[count];
            for (int index = 0; index < count; index++)
            {
                snapshot[index] = MainVM.MovieRecs[index];
            }
            int revision = Interlocked.Increment(ref _startupAsciiSearchPrewarmRevision);

            lock (_startupAsciiSearchPrewarmSync)
            {
                _startupAsciiSearchPrewarmPending = new StartupAsciiSearchPrewarmRequest(
                    revision,
                    startupSessionRevision,
                    dbFullPath,
                    snapshot
                );
                if (_startupAsciiSearchPrewarmTask is { IsCompleted: false })
                {
                    return;
                }

                _startupAsciiSearchPrewarmTask = Task.Run(RunStartupAsciiSearchPrewarmWorker);
            }
        }

        private void RunStartupAsciiSearchPrewarmWorker()
        {
            while (true)
            {
                StartupAsciiSearchPrewarmRequest request;
                lock (_startupAsciiSearchPrewarmSync)
                {
                    request = _startupAsciiSearchPrewarmPending;
                    _startupAsciiSearchPrewarmPending = null;
                    if (request == null)
                    {
                        _startupAsciiSearchPrewarmTask = null;
                        return;
                    }
                }

                RunStartupAsciiSearchPrewarm(request);
            }
        }

        private void RunStartupAsciiSearchPrewarm(StartupAsciiSearchPrewarmRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            int completedCount = 0;
            string interruptedReason = "";

            try
            {
                // スクロールや入力が始まったら、次の小区切りで準備を打ち切る。
                for (int offset = 0; offset < request.Items.Length; offset += StartupAsciiSearchPrewarmBatchSize)
                {
                    interruptedReason = ResolveStartupAsciiSearchPrewarmInterruption(request);
                    if (!string.IsNullOrEmpty(interruptedReason))
                    {
                        break;
                    }

                    int end = Math.Min(
                        offset + StartupAsciiSearchPrewarmBatchSize,
                        request.Items.Length
                    );
                    for (int index = offset; index < end; index++)
                    {
                        request.Items[index]?.GetAsciiSearchFieldsForFilter(
                            allowExpensivePhoneticFallback: false
                        );
                        completedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                interruptedReason = $"failed:{ex.GetType().Name}";
            }

            string state = string.IsNullOrEmpty(interruptedReason) ? "completed" : "interrupted";
            string reasonField = string.IsNullOrEmpty(interruptedReason)
                ? ""
                : $" reason={interruptedReason}";
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"startup ascii search prewarm {state}: revision={request.Revision} startup_revision={request.StartupSessionRevision} completed={completedCount} requested={request.Items.Length}{reasonField} elapsed_ms={stopwatch.ElapsedMilliseconds}"
            );
        }

        private string ResolveStartupAsciiSearchPrewarmInterruption(
            StartupAsciiSearchPrewarmRequest request
        )
        {
            if (request.Revision != Volatile.Read(ref _startupAsciiSearchPrewarmRevision))
            {
                return "superseded";
            }

            if (
                request.StartupSessionRevision != Volatile.Read(ref _startupSessionRevision)
                || !_startupLoadCoordinator.IsCurrent(request.StartupSessionRevision)
            )
            {
                return "startup-session-changed";
            }

            if (
                Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || Volatile.Read(ref _mainWindowClosingStarted) != 0
            )
            {
                return "shutdown";
            }

            if (!AreSameMainDbPath(request.DbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                return "db-changed";
            }

            return IsUserPriorityWorkActive() ? "user-priority" : "";
        }

        private sealed record StartupAsciiSearchPrewarmRequest(
            int Revision,
            int StartupSessionRevision,
            string DbFullPath,
            MovieRecords[] Items
        );
    }
}

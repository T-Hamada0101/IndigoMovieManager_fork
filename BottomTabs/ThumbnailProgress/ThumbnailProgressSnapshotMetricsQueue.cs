using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.BottomTabs.ThumbnailProgress
{
    internal readonly record struct ThumbnailProgressSnapshotApplyMetric(
        long SnapshotVersion,
        int DbPendingCount,
        int DbTotalCount,
        int WorkerCount,
        double ApplyDurationMs
    );

    /// <summary>
    /// 進捗UIの計測値を受付順に背景保存し、UIスレッドからディスクI/Oを外す。
    /// </summary>
    internal sealed class ThumbnailProgressSnapshotMetricsQueue
    {
        private readonly ConcurrentQueue<ThumbnailProgressSnapshotApplyMetric> pending = new();
        private int drainRunning;

        public void Enqueue(ThumbnailProgressSnapshotApplyMetric metric)
        {
            pending.Enqueue(metric);
            StartDrainIfNeeded();
        }

        private void StartDrainIfNeeded()
        {
            if (Interlocked.CompareExchange(ref drainRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(Drain);
        }

        private void Drain()
        {
            try
            {
                while (pending.TryDequeue(out ThumbnailProgressSnapshotApplyMetric metric))
                {
                    ThumbnailProgressUiMetricsLogger.RecordSnapshotApply(
                        metric.SnapshotVersion,
                        metric.DbPendingCount,
                        metric.DbTotalCount,
                        metric.WorkerCount,
                        metric.ApplyDurationMs
                    );
                }
            }
            finally
            {
                Interlocked.Exchange(ref drainRunning, 0);
                if (!pending.IsEmpty)
                {
                    StartDrainIfNeeded();
                }
            }
        }
    }
}

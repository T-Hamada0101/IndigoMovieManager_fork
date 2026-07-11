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
    /// 進捗UIの最新計測値だけを背景保存し、遅い診断I/Oでメモリを増やさない。
    /// </summary>
    internal sealed class ThumbnailProgressSnapshotMetricsQueue
    {
        private readonly object syncRoot = new();
        private readonly Action<ThumbnailProgressSnapshotApplyMetric> writeMetric;
        private ThumbnailProgressSnapshotApplyMetric? latestPending;
        private bool drainRunning;

        public ThumbnailProgressSnapshotMetricsQueue()
            : this(WriteMetric) { }

        internal ThumbnailProgressSnapshotMetricsQueue(
            Action<ThumbnailProgressSnapshotApplyMetric> writeMetric
        )
        {
            this.writeMetric = writeMetric ?? throw new ArgumentNullException(nameof(writeMetric));
        }

        public void Enqueue(ThumbnailProgressSnapshotApplyMetric metric)
        {
            lock (syncRoot)
            {
                // 保存が遅れている間は未保存の旧値を捨て、最新1件だけを保持する。
                latestPending = metric;
                if (drainRunning)
                {
                    return;
                }

                drainRunning = true;
            }

            _ = Task.Run(Drain);
        }

        private void Drain()
        {
            while (true)
            {
                ThumbnailProgressSnapshotApplyMetric metric;
                lock (syncRoot)
                {
                    if (!latestPending.HasValue)
                    {
                        drainRunning = false;
                        return;
                    }

                    metric = latestPending.Value;
                    latestPending = null;
                }

                try
                {
                    writeMetric(metric);
                }
                catch
                {
                    // 診断ログ失敗は本体へ伝播させず、次の最新値を処理する。
                }
            }
        }

        private static void WriteMetric(ThumbnailProgressSnapshotApplyMetric metric)
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
}

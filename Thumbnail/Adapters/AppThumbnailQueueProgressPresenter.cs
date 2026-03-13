using System.ComponentModel;
using System.Threading;
using Notification.Wpf;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Queue通知ポートを既存の Notification.Wpf へ橋渡しする。
    /// </summary>
    internal sealed class AppThumbnailQueueProgressPresenter : IThumbnailQueueProgressPresenter
    {
        // Notification.Wpf は内部でWPF Window資源を握るため、使い回しで増殖を抑える。
        private static readonly NotificationManager SharedNotificationManager = new();
        private static int _disabledByResourceExhaustion;

        public IThumbnailQueueProgressHandle Show(string title)
        {
            if (Volatile.Read(ref _disabledByResourceExhaustion) != 0)
            {
                return NoOpThumbnailQueueProgressHandle.Instance;
            }

            try
            {
                var progress = SharedNotificationManager.ShowProgressBar(
                    title,
                    false,
                    true,
                    "ProgressArea",
                    false,
                    2,
                    ""
                );
                return new AppThumbnailQueueProgressHandle(progress);
            }
            catch (Win32Exception ex)
            {
                // ハンドル枯渇時は通知UIだけ諦め、本体処理は継続する。
                Interlocked.Exchange(ref _disabledByResourceExhaustion, 1);
                DebugRuntimeLog.Write(
                    "queue-consumer",
                    $"progress presenter disabled by Win32 resource exhaustion: {ex.Message}"
                );
                return NoOpThumbnailQueueProgressHandle.Instance;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "queue-consumer",
                    $"progress presenter open failed: {ex.Message}"
                );
                return NoOpThumbnailQueueProgressHandle.Instance;
            }
        }

        private sealed class AppThumbnailQueueProgressHandle : IThumbnailQueueProgressHandle
        {
            private readonly dynamic progress;
            private bool disposed;

            public AppThumbnailQueueProgressHandle(object progress)
            {
                this.progress = progress;
            }

            public void Report(
                double progressPercent,
                string message,
                string title,
                bool isIndeterminate
            )
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    progress.Report((progressPercent, message, title, isIndeterminate));
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-consumer",
                        $"progress presenter report failed: {ex.Message}"
                    );
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;

                try
                {
                    progress.Dispose();
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-consumer",
                        $"progress presenter dispose failed: {ex.Message}"
                    );
                }
            }
        }
    }
}

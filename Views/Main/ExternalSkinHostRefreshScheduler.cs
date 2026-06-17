using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    /// <summary>
    /// 外部 skin host の refresh 要求を 1 本の列へ畳み、古い途中経過より最新要求を優先する。
    /// MainWindow 側は「何を refresh するか」だけに集中し、直列化の責務をここへ寄せる。
    /// </summary>
    internal sealed class ExternalSkinHostRefreshScheduler
    {
        private readonly Dispatcher dispatcher;
        private readonly Func<int, string, string, Task> refreshAsync;
        private readonly Action<Exception> onDrainFailed;
        private readonly Func<string, string, string> selectPreferredReason;
        private bool isRefreshRunning;
        private bool isRefreshPending;
        private int currentGeneration;
        private string pendingReason = "";
        private string pendingRequestTraceId = "";

        internal ExternalSkinHostRefreshScheduler(
            Dispatcher dispatcher,
            Func<int, string, string, Task> refreshAsync,
            Action<Exception> onDrainFailed,
            Func<string, string, string> selectPreferredReason = null
        )
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
            this.onDrainFailed = onDrainFailed ?? (_ => { });
            this.selectPreferredReason = selectPreferredReason ?? SelectLatestReason;
        }

        internal int CurrentGeneration => currentGeneration;
        internal bool CanAcceptQueueRequests =>
            !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished;

        internal bool Queue(string reason, string requestTraceId = "")
        {
            // 終了シーケンス中は新規 refresh を受け付けず、dispatcher shutdown 競合の例外を避ける。
            if (!CanAcceptQueueRequests)
            {
                ResetPendingStateForShutdown();
                return false;
            }

            string normalizedReason = reason ?? "";
            string normalizedRequestTraceId = requestTraceId ?? "";
            string selectedReason = isRefreshPending
                ? selectPreferredReason(pendingReason, normalizedReason) ?? ""
                : normalizedReason;

            isRefreshPending = true;
            if (string.Equals(selectedReason, normalizedReason, StringComparison.Ordinal))
            {
                // 採用した reason と trace を対にして残し、CatalogRefresh の要求元を後続の軽い要求で潰さない。
                pendingReason = normalizedReason;
                pendingRequestTraceId = normalizedRequestTraceId;
            }
            else
            {
                pendingReason = selectedReason;
            }

            currentGeneration++;
            if (isRefreshRunning)
            {
                return true;
            }

            isRefreshRunning = true;
            try
            {
                _ = dispatcher.BeginInvoke(
                    new Action(async () => await DrainAsync()),
                    DispatcherPriority.Background
                );
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                ResetPendingStateForShutdown();
                return false;
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                while (isRefreshPending)
                {
                    isRefreshPending = false;

                    int generation = currentGeneration;
                    string reason = pendingReason;
                    string requestTraceId = pendingRequestTraceId;
                    await refreshAsync(generation, reason, requestTraceId);
                }
            }
            catch (Exception ex)
            {
                onDrainFailed(ex);
            }
            finally
            {
                isRefreshRunning = false;
                if (isRefreshPending)
                {
                    _ = Queue(pendingReason, pendingRequestTraceId);
                }
            }
        }

        private void ResetPendingStateForShutdown()
        {
            isRefreshRunning = false;
            isRefreshPending = false;
            pendingReason = "";
            pendingRequestTraceId = "";
        }

        private static string SelectLatestReason(string currentReason, string candidateReason)
        {
            return !string.IsNullOrWhiteSpace(candidateReason) ? candidateReason : currentReason ?? "";
        }
    }
}

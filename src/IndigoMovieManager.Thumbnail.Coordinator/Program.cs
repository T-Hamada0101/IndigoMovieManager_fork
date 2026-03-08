using System.Diagnostics;

namespace IndigoMovieManager.Thumbnail
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                ThumbnailCoordinatorRuntimeOptions options =
                    ThumbnailCoordinatorRuntimeOptions.Parse(args);
                using CancellationTokenSource cts = new();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                using CancellationTokenSource linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                Task parentWatcherTask = StartParentWatcherIfNeeded(options.ParentProcessId, linkedCts);

                ThumbnailCoordinatorHostService hostService = new();
                await hostService.RunAsync(options, linkedCts.Token).ConfigureAwait(false);
                await parentWatcherTask.ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static Task StartParentWatcherIfNeeded(
            int parentProcessId,
            CancellationTokenSource linkedCts
        )
        {
            if (parentProcessId <= 0 || linkedCts == null)
            {
                return Task.CompletedTask;
            }

            return Task.Run(
                async () =>
                {
                    while (!linkedCts.IsCancellationRequested)
                    {
                        if (!IsParentProcessAlive(parentProcessId))
                        {
                            linkedCts.Cancel();
                            break;
                        }

                        try
                        {
                            await Task.Delay(2000, linkedCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                },
                linkedCts.Token
            );
        }

        private static bool IsParentProcessAlive(int parentProcessId)
        {
            try
            {
                using Process parent = Process.GetProcessById(parentProcessId);
                return !parent.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}


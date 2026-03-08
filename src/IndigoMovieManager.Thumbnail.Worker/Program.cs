using System.Globalization;
using System.Diagnostics;

namespace IndigoMovieManager.Thumbnail
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                ThumbnailWorkerRuntimeOptions options = ParseArguments(args);
                using CancellationTokenSource cts = new();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                using CancellationTokenSource linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                Task parentWatcherTask = StartParentWatcherIfNeeded(options.ParentProcessId, linkedCts);

                ThumbnailWorkerHostService hostService = new();
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

        private static ThumbnailWorkerRuntimeOptions ParseArguments(string[] args)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i] ?? "";
                if (!current.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = current[2..];
                string value = "";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i] ?? "";
                }

                values[key] = value;
            }

            return new ThumbnailWorkerRuntimeOptions
            {
                MainDbFullPath = GetRequired(values, "main-db"),
                OwnerInstanceId = GetRequired(values, "owner"),
                SettingsSnapshotPath = GetRequired(values, "settings-snapshot"),
                WorkerRole = ParseRole(GetRequired(values, "role")),
                ParentProcessId = ParseInt(GetOptional(values, "parent-pid", "0"), 0),
            };
        }

        private static string GetRequired(
            IReadOnlyDictionary<string, string> values,
            string key
        )
        {
            if (values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            throw new ArgumentException($"missing required argument: --{key}");
        }

        private static string GetOptional(
            IReadOnlyDictionary<string, string> values,
            string key,
            string defaultValue
        )
        {
            return values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : defaultValue;
        }

        private static ThumbnailQueueWorkerRole ParseRole(string raw)
        {
            return raw.Trim().ToLowerInvariant() switch
            {
                "normal" => ThumbnailQueueWorkerRole.Normal,
                "idle" => ThumbnailQueueWorkerRole.Idle,
                _ => throw new ArgumentException($"unknown role: {raw}"),
            };
        }

        private static int ParseInt(string raw, int defaultValue)
        {
            return int.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed
            )
                ? parsed
                : defaultValue;
        }

        // 親プロセスが消えた孤児Workerを残さないよう、定期監視で自動終了へ寄せる。
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

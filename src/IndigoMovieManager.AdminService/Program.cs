using System.Diagnostics;

namespace IndigoMovieManager
{
    internal static class Program
    {
        [STAThread]
        private static async Task Main(string[] args)
        {
            AdminTelemetryServiceRuntimeOptions options =
                AdminTelemetryServiceRuntimeOptions.Parse(args);
            using CancellationTokenSource appCts = new();
            Task parentMonitorTask = RunParentMonitorAsync(options.ParentProcessId, appCts);

            try
            {
                AdminTelemetryServiceHost host = new();
                await host.RunAsync(appCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                appCts.Cancel();
                try
                {
                    await parentMonitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        // 親プロセスが死んだら孤児化させず、管理者サービスも静かに止める。
        private static async Task RunParentMonitorAsync(
            int parentProcessId,
            CancellationTokenSource appCts
        )
        {
            if (appCts == null || parentProcessId <= 0)
            {
                return;
            }

            while (!appCts.IsCancellationRequested)
            {
                bool exists = true;
                try
                {
                    using Process process = Process.GetProcessById(parentProcessId);
                    exists = !process.HasExited;
                }
                catch
                {
                    exists = false;
                }

                if (!exists)
                {
                    appCts.Cancel();
                    return;
                }

                await Task.Delay(2000, appCts.Token).ConfigureAwait(false);
            }
        }
    }

    internal sealed class AdminTelemetryServiceRuntimeOptions
    {
        public int ParentProcessId { get; init; }

        public static AdminTelemetryServiceRuntimeOptions Parse(string[] args)
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
                string value = i + 1 < args.Length ? args[i + 1] ?? "" : "";
                if (value.StartsWith("--", StringComparison.Ordinal))
                {
                    value = "";
                }
                else
                {
                    i++;
                }

                values[key] = value;
            }

            int parentProcessId = 0;
            if (
                values.TryGetValue("parent-pid", out string rawParentPid)
                && !string.IsNullOrWhiteSpace(rawParentPid)
            )
            {
                _ = int.TryParse(rawParentPid, out parentProcessId);
            }

            return new AdminTelemetryServiceRuntimeOptions { ParentProcessId = parentProcessId };
        }
    }
}

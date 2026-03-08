using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル進捗Viewerの別プロセス起動と再起動を面倒みる。
    /// </summary>
    internal sealed class ThumbnailProgressViewerProcessManager
    {
        private readonly object syncRoot = new();
        private ManagedViewerProcess currentProcess;

        public bool IsViewerAvailable()
        {
            return File.Exists(ResolveViewerExecutablePath());
        }

        public bool IsViewerRunning()
        {
            lock (syncRoot)
            {
                return currentProcess != null && !currentProcess.Process.HasExited;
            }
        }

        public async Task RunSupervisorAsync(
            Func<ThumbnailProgressViewerLaunchConfig> resolveLaunchConfig,
            Action<string> log,
            CancellationToken cts
        )
        {
            Action<string> safeLog = log ?? (_ => { });
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ThumbnailProgressViewerLaunchConfig launchConfig =
                        resolveLaunchConfig?.Invoke();
                    if (launchConfig == null)
                    {
                        StopViewer(safeLog, "config-empty");
                    }
                    else
                    {
                        EnsureViewerRunning(launchConfig, safeLog);
                    }

                    await Task.Delay(1000, cts).ConfigureAwait(false);
                }
            }
            finally
            {
                StopViewer(safeLog, "supervisor-stop");
            }
        }

        public void StopViewerNow(Action<string> log = null)
        {
            StopViewer(log ?? (_ => { }), "manual-stop");
        }

        private void EnsureViewerRunning(
            ThumbnailProgressViewerLaunchConfig launchConfig,
            Action<string> log
        )
        {
            string viewerExePath = ResolveViewerExecutablePath();
            if (!File.Exists(viewerExePath))
            {
                log($"progress viewer start skipped: file missing '{viewerExePath}'");
                return;
            }

            string signature = launchConfig.CreateSignature();
            ManagedViewerProcess existing = null;
            lock (syncRoot)
            {
                existing = currentProcess;
            }

            if (existing != null)
            {
                if (
                    !existing.Process.HasExited
                    && string.Equals(existing.Signature, signature, StringComparison.Ordinal)
                )
                {
                    return;
                }

                StopViewer(log, "config-changed-or-exited");
            }

            ProcessStartInfo psi = new()
            {
                FileName = viewerExePath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(viewerExePath) ?? AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add("--main-db");
            psi.ArgumentList.Add(launchConfig.MainDbFullPath);
            psi.ArgumentList.Add("--db-name");
            psi.ArgumentList.Add(launchConfig.DbName);
            psi.ArgumentList.Add("--normal-owner");
            psi.ArgumentList.Add(launchConfig.NormalOwnerInstanceId);
            psi.ArgumentList.Add("--idle-owner");
            psi.ArgumentList.Add(launchConfig.IdleOwnerInstanceId);
            psi.ArgumentList.Add("--coordinator-owner");
            psi.ArgumentList.Add(launchConfig.CoordinatorOwnerInstanceId);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());

            Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                log($"progress viewer exited: pid={process.Id} code={TryGetExitCode(process)}");
            };

            if (!process.Start())
            {
                process.Dispose();
                log("progress viewer start failed.");
                return;
            }

            lock (syncRoot)
            {
                currentProcess = new ManagedViewerProcess(signature, process);
            }
            log($"progress viewer started: pid={process.Id}");
        }

        private void StopViewer(Action<string> log, string reason)
        {
            ManagedViewerProcess managed = null;
            lock (syncRoot)
            {
                if (currentProcess == null)
                {
                    return;
                }

                managed = currentProcess;
                currentProcess = null;
            }
            try
            {
                if (!managed.Process.HasExited)
                {
                    managed.Process.Kill(entireProcessTree: true);
                    managed.Process.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                log($"progress viewer stop warning: reason={reason} err={ex.Message}");
            }
            finally
            {
                log($"progress viewer stopped: reason={reason}");
                managed.Process.Dispose();
            }
        }

        private static string ResolveViewerExecutablePath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "thumbnail-progress-viewer",
                "IndigoMovieManager.Thumbnail.ProgressViewer.exe"
            );
        }

        private static string TryGetExitCode(Process process)
        {
            try
            {
                return process.ExitCode.ToString();
            }
            catch
            {
                return "unknown";
            }
        }

        internal sealed class ThumbnailProgressViewerLaunchConfig
        {
            public string MainDbFullPath { get; init; } = "";
            public string DbName { get; init; } = "";
            public string NormalOwnerInstanceId { get; init; } = "";
            public string IdleOwnerInstanceId { get; init; } = "";
            public string CoordinatorOwnerInstanceId { get; init; } = "";

            public string CreateSignature()
            {
                return string.Join(
                    "|",
                    MainDbFullPath,
                    DbName,
                    NormalOwnerInstanceId,
                    IdleOwnerInstanceId,
                    CoordinatorOwnerInstanceId
                );
            }
        }

        private sealed class ManagedViewerProcess
        {
            public ManagedViewerProcess(string signature, Process process)
            {
                Signature = signature ?? "";
                Process = process ?? throw new ArgumentNullException(nameof(process));
            }

            public string Signature { get; }

            public Process Process { get; }
        }
    }
}

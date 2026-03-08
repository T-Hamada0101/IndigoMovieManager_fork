using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル Coordinator の別プロセス起動と再起動を面倒みる。
    /// 接続点を先に固定し、UIと外部運転席の並列開発をしやすくする。
    /// </summary>
    internal sealed class ThumbnailCoordinatorProcessManager
    {
        private readonly object syncRoot = new();
        private ManagedCoordinatorProcess currentProcess;

        public bool IsCoordinatorAvailable()
        {
            return File.Exists(ResolveCoordinatorExecutablePath());
        }

        public bool IsCoordinatorRunning()
        {
            lock (syncRoot)
            {
                return currentProcess != null && !currentProcess.Process.HasExited;
            }
        }

        public async Task RunSupervisorAsync(
            Func<ThumbnailCoordinatorLaunchConfig> resolveLaunchConfig,
            Action<string> log,
            CancellationToken cts
        )
        {
            Action<string> safeLog = log ?? (_ => { });
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    ThumbnailCoordinatorLaunchConfig launchConfig = resolveLaunchConfig?.Invoke();
                    if (launchConfig == null)
                    {
                        StopCoordinator(safeLog, "config-empty");
                    }
                    else
                    {
                        EnsureCoordinatorRunning(launchConfig, safeLog);
                    }

                    await Task.Delay(1000, cts).ConfigureAwait(false);
                }
            }
            finally
            {
                StopCoordinator(safeLog, "supervisor-stop");
            }
        }

        public void StopCoordinatorNow(Action<string> log = null)
        {
            StopCoordinator(log ?? (_ => { }), "manual-stop");
        }

        private void EnsureCoordinatorRunning(
            ThumbnailCoordinatorLaunchConfig launchConfig,
            Action<string> log
        )
        {
            string coordinatorExePath = ResolveCoordinatorExecutablePath();
            if (!File.Exists(coordinatorExePath))
            {
                log($"thumbnail coordinator start skipped: file missing '{coordinatorExePath}'");
                return;
            }

            string signature = launchConfig.CreateSignature();
            ManagedCoordinatorProcess existing = null;
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

                StopCoordinator(log, "config-changed-or-exited");
            }

            ProcessStartInfo psi = new()
            {
                FileName = coordinatorExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(coordinatorExePath) ?? AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add("--main-db");
            psi.ArgumentList.Add(launchConfig.MainDbFullPath);
            psi.ArgumentList.Add("--db-name");
            psi.ArgumentList.Add(launchConfig.DbName);
            psi.ArgumentList.Add("--owner");
            psi.ArgumentList.Add(launchConfig.OwnerInstanceId);
            psi.ArgumentList.Add("--normal-owner");
            psi.ArgumentList.Add(launchConfig.NormalWorkerOwnerInstanceId);
            psi.ArgumentList.Add("--idle-owner");
            psi.ArgumentList.Add(launchConfig.IdleWorkerOwnerInstanceId);
            psi.ArgumentList.Add("--initial-settings-snapshot");
            psi.ArgumentList.Add(launchConfig.InitialSettingsSnapshotPath);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());

            Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
            AttachProcessLogForwarding(process, log);
            process.Exited += (_, _) =>
            {
                log($"thumbnail coordinator exited: pid={process.Id} code={TryGetExitCode(process)}");
            };

            if (!process.Start())
            {
                process.Dispose();
                log("thumbnail coordinator start failed.");
                return;
            }

            lock (syncRoot)
            {
                currentProcess = new ManagedCoordinatorProcess(signature, process);
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            log($"thumbnail coordinator started: pid={process.Id}");
        }

        private void StopCoordinator(Action<string> log, string reason)
        {
            ManagedCoordinatorProcess managed = null;
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
                log($"thumbnail coordinator stop warning: reason={reason} err={ex.Message}");
            }
            finally
            {
                log($"thumbnail coordinator stopped: reason={reason}");
                managed.Process.Dispose();
            }
        }

        private static string ResolveCoordinatorExecutablePath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                "thumbnail-coordinator",
                "IndigoMovieManager.Thumbnail.Coordinator.exe"
            );
        }

        // 即死時の例外を本体ログへ流し、黒窓を開かずに原因を追えるようにする。
        private static void AttachProcessLogForwarding(Process process, Action<string> log)
        {
            if (process == null)
            {
                return;
            }

            Action<string> safeLog = log ?? (_ => { });
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    safeLog($"thumbnail coordinator stdout: {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    safeLog($"thumbnail coordinator stderr: {e.Data}");
                }
            };
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

        internal sealed class ThumbnailCoordinatorLaunchConfig
        {
            public string MainDbFullPath { get; init; } = "";
            public string DbName { get; init; } = "";
            public string OwnerInstanceId { get; init; } = "";
            public string NormalWorkerOwnerInstanceId { get; init; } = "";
            public string IdleWorkerOwnerInstanceId { get; init; } = "";
            public string InitialSettingsSnapshotPath { get; init; } = "";

            public string CreateSignature()
            {
                return string.Join(
                    "|",
                    MainDbFullPath,
                    DbName,
                    OwnerInstanceId,
                    NormalWorkerOwnerInstanceId,
                    IdleWorkerOwnerInstanceId,
                    InitialSettingsSnapshotPath
                );
            }
        }

        private sealed class ManagedCoordinatorProcess
        {
            public ManagedCoordinatorProcess(string signature, Process process)
            {
                Signature = signature ?? "";
                Process = process ?? throw new ArgumentNullException(nameof(process));
            }

            public string Signature { get; }

            public Process Process { get; }
        }
    }
}

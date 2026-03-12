using System.Diagnostics;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Coordinator から子 Worker を起動・監視し、設定変更に追従させる。
    /// </summary>
    internal sealed class ThumbnailCoordinatorWorkerProcessManager
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<ThumbnailQueueWorkerRole, ManagedWorkerProcess> workers = [];
        private readonly Dictionary<ThumbnailQueueWorkerRole, string> lastHealthStateByRole = [];

        public bool IsWorkerAvailable()
        {
            return File.Exists(ResolveWorkerExecutablePath());
        }

        public void ReconcileWorkers(
            IReadOnlyList<ThumbnailWorkerLaunchConfig> launchConfigs,
            Action<string> log
        )
        {
            Action<string> safeLog = log ?? (_ => { });
            IReadOnlyList<ThumbnailWorkerLaunchConfig> safeLaunchConfigs = launchConfigs ?? [];
            Dictionary<ThumbnailQueueWorkerRole, ThumbnailWorkerLaunchConfig> desiredByRole =
                safeLaunchConfigs.ToDictionary(x => x.WorkerRole);

            foreach (ThumbnailQueueWorkerRole existingRole in GetManagedRolesSnapshot())
            {
                if (!desiredByRole.ContainsKey(existingRole))
                {
                    StopWorker(existingRole, safeLog, "config-removed");
                }
            }

            foreach (ThumbnailWorkerLaunchConfig launchConfig in safeLaunchConfigs)
            {
                EnsureWorkerRunning(launchConfig, safeLog);
            }
        }

        public void StopAllWorkers(Action<string> log = null)
        {
            foreach (ThumbnailQueueWorkerRole role in GetManagedRolesSnapshot())
            {
                StopWorker(role, log ?? (_ => { }), "manual-stop");
            }
        }

        private void EnsureWorkerRunning(ThumbnailWorkerLaunchConfig launchConfig, Action<string> log)
        {
            string workerExePath = ResolveWorkerExecutablePath();
            if (!File.Exists(workerExePath))
            {
                PublishSupervisorHealth(
                    launchConfig,
                    ThumbnailWorkerHealthState.Missing,
                    processId: 0,
                    currentPriority: "",
                    message: $"worker file missing: {workerExePath}",
                    reasonCode: ThumbnailWorkerHealthReasonCode.WorkerMissing
                );
                log($"coordinator worker start skipped: file missing '{workerExePath}'");
                return;
            }

            string signature = launchConfig.CreateSignature();
            ManagedWorkerProcess existing = null;
            lock (syncRoot)
            {
                workers.TryGetValue(launchConfig.WorkerRole, out existing);
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

                if (existing.Process.HasExited)
                {
                    PublishSupervisorHealth(
                        existing.LaunchConfig,
                        ThumbnailWorkerHealthState.Exited,
                        existing.Process.Id,
                        "",
                        message: "worker exited before restart",
                        reasonCode: ThumbnailWorkerHealthReasonCode.Exception,
                        exitCode: TryGetNullableExitCode(existing.Process)
                    );
                }

                if (
                    !existing.Process.HasExited
                    && ShouldDeferRestartForActiveWork(existing.LaunchConfig)
                )
                {
                    return;
                }

                StopWorker(launchConfig.WorkerRole, log, "config-changed-or-exited");
            }

            ProcessStartInfo psi = new()
            {
                FileName = workerExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(workerExePath) ?? AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add("--role");
            psi.ArgumentList.Add(
                launchConfig.WorkerRole == ThumbnailQueueWorkerRole.Normal ? "normal" : "idle"
            );
            psi.ArgumentList.Add("--main-db");
            psi.ArgumentList.Add(launchConfig.MainDbFullPath);
            psi.ArgumentList.Add("--owner");
            psi.ArgumentList.Add(launchConfig.OwnerInstanceId);
            psi.ArgumentList.Add("--settings-snapshot");
            psi.ArgumentList.Add(launchConfig.SettingsSnapshotPath);
            psi.ArgumentList.Add("--parent-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());

            Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                PublishSupervisorHealth(
                    launchConfig,
                    ThumbnailWorkerHealthState.Exited,
                    process.Id,
                    "",
                    message: "worker process exited",
                    reasonCode: ThumbnailWorkerHealthReasonCode.Exception,
                    exitCode: TryGetNullableExitCode(process)
                );
                log(
                    $"coordinator worker exited: role={launchConfig.WorkerRole} pid={process.Id} code={TryGetExitCode(process)}"
                );
            };

            if (!process.Start())
            {
                PublishSupervisorHealth(
                    launchConfig,
                    ThumbnailWorkerHealthState.StartFailed,
                    processId: 0,
                    currentPriority: "",
                    message: "process.Start returned false",
                    reasonCode: ThumbnailWorkerHealthReasonCode.ProcessStartFailed
                );
                process.Dispose();
                log($"coordinator worker start failed: role={launchConfig.WorkerRole}");
                return;
            }

            lock (syncRoot)
            {
                workers[launchConfig.WorkerRole] = new ManagedWorkerProcess(
                    launchConfig.WorkerRole,
                    signature,
                    launchConfig,
                    process
                );
            }
            log(
                $"coordinator worker started: role={launchConfig.WorkerRole} pid={process.Id} settings={launchConfig.SettingsVersionToken}"
            );
        }

        internal static bool ShouldDeferRestartForActiveWork(ThumbnailWorkerLaunchConfig launchConfig)
        {
            if (
                launchConfig == null
                || string.IsNullOrWhiteSpace(launchConfig.MainDbFullPath)
                || string.IsNullOrWhiteSpace(launchConfig.OwnerInstanceId)
            )
            {
                return false;
            }

            try
            {
                QueueDbService queueDbService = new(launchConfig.MainDbFullPath);
                return queueDbService.HasOwnedActiveWork(launchConfig.OwnerInstanceId, DateTime.UtcNow);
            }
            catch
            {
                // 判定不能時は従来どおり再起動を優先する。
                return false;
            }
        }

        private void StopWorker(ThumbnailQueueWorkerRole role, Action<string> log, string reason)
        {
            ManagedWorkerProcess managed = null;
            lock (syncRoot)
            {
                if (!workers.TryGetValue(role, out managed))
                {
                    return;
                }

                workers.Remove(role);
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
                log($"coordinator worker stop warning: role={role} reason={reason} err={ex.Message}");
            }
            finally
            {
                bool exited = false;
                try
                {
                    exited = managed.Process.HasExited;
                }
                catch
                {
                    exited = false;
                }

                string finalState =
                    exited && string.Equals(reason, "config-changed-or-exited", StringComparison.Ordinal)
                        ? ThumbnailWorkerHealthState.Exited
                        : ThumbnailWorkerHealthState.Stopped;
                PublishSupervisorHealth(
                    managed.LaunchConfig,
                    finalState,
                    managed.Process.Id,
                    "",
                    message: $"coordinator stop: {reason}",
                    reasonCode: ThumbnailWorkerHealthReasonResolver.Resolve(
                        finalState,
                        $"coordinator stop: {reason}"
                    ),
                    exitCode: TryGetNullableExitCode(managed.Process)
                );
                managed.Process.Dispose();
            }
        }

        private void PublishSupervisorHealth(
            ThumbnailWorkerLaunchConfig launchConfig,
            string state,
            int processId,
            string currentPriority,
            string message,
            string reasonCode,
            int? exitCode = null
        )
        {
            if (launchConfig == null)
            {
                return;
            }

            string dedupeKey = string.Join(
                "|",
                state ?? "",
                reasonCode ?? "",
                processId,
                exitCode?.ToString() ?? "",
                launchConfig.SettingsVersionToken ?? "",
                message ?? ""
            );
            if (TryShouldSkipHealthPublish(launchConfig.WorkerRole, dedupeKey))
            {
                return;
            }

            new ThumbnailWorkerHealthPublisher(
                launchConfig.MainDbFullPath,
                launchConfig.OwnerInstanceId,
                launchConfig.WorkerRole,
                launchConfig.SettingsVersionToken
            ).Publish(
                state,
                processId,
                currentPriority,
                message,
                reasonCode,
                exitCode
            );
        }

        private ThumbnailQueueWorkerRole[] GetManagedRolesSnapshot()
        {
            lock (syncRoot)
            {
                return [.. workers.Keys];
            }
        }

        private bool TryShouldSkipHealthPublish(
            ThumbnailQueueWorkerRole workerRole,
            string dedupeKey
        )
        {
            lock (syncRoot)
            {
                if (
                    lastHealthStateByRole.TryGetValue(workerRole, out string currentKey)
                    && string.Equals(currentKey, dedupeKey, StringComparison.Ordinal)
                )
                {
                    return true;
                }

                lastHealthStateByRole[workerRole] = dedupeKey;
                return false;
            }
        }

        private static string ResolveWorkerExecutablePath()
        {
            string coordinatorBaseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string parentDir = Directory.GetParent(coordinatorBaseDir)?.FullName ?? coordinatorBaseDir;
            return Path.Combine(
                parentDir,
                "thumbnail-worker",
                "IndigoMovieManager.Thumbnail.Worker.exe"
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

        private static int? TryGetNullableExitCode(Process process)
        {
            try
            {
                return process.ExitCode;
            }
            catch
            {
                return null;
            }
        }

        internal sealed class ThumbnailWorkerLaunchConfig
        {
            public ThumbnailQueueWorkerRole WorkerRole { get; init; }
            public string MainDbFullPath { get; init; } = "";
            public string OwnerInstanceId { get; init; } = "";
            public string SettingsSnapshotPath { get; init; } = "";
            public string SettingsVersionToken { get; init; } = "";

            public string CreateSignature()
            {
                return string.Join(
                    "|",
                    WorkerRole,
                    MainDbFullPath,
                    OwnerInstanceId,
                    SettingsSnapshotPath,
                    SettingsVersionToken
                );
            }
        }

        private sealed class ManagedWorkerProcess
        {
            public ManagedWorkerProcess(
                ThumbnailQueueWorkerRole workerRole,
                string signature,
                ThumbnailWorkerLaunchConfig launchConfig,
                Process process
            )
            {
                WorkerRole = workerRole;
                Signature = signature ?? "";
                LaunchConfig = launchConfig ?? throw new ArgumentNullException(nameof(launchConfig));
                Process = process ?? throw new ArgumentNullException(nameof(process));
            }

            public ThumbnailQueueWorkerRole WorkerRole { get; }
            public string Signature { get; }
            public ThumbnailWorkerLaunchConfig LaunchConfig { get; }
            public Process Process { get; }
        }
    }
}

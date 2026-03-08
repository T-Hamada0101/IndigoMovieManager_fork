using System.Diagnostics;
using System.Globalization;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker.exe から呼ばれる、サムネイル専用実行ホスト。
    /// </summary>
    public sealed class ThumbnailWorkerHostService
    {
        public async Task RunAsync(
            ThumbnailWorkerRuntimeOptions options,
            CancellationToken cts = default
        )
        {
            ThumbnailWorkerHealthPublisher healthPublisher = null;
            ProcessPriorityClass targetPriority = ProcessPriorityClass.BelowNormal;
            try
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }
                if (string.IsNullOrWhiteSpace(options.MainDbFullPath))
                {
                    throw new ArgumentException(
                        "MainDbFullPath is required.",
                        nameof(options.MainDbFullPath)
                    );
                }
                if (string.IsNullOrWhiteSpace(options.OwnerInstanceId))
                {
                    throw new ArgumentException(
                        "OwnerInstanceId is required.",
                        nameof(options.OwnerInstanceId)
                    );
                }
                if (string.IsNullOrWhiteSpace(options.SettingsSnapshotPath))
                {
                    throw new ArgumentException(
                        "SettingsSnapshotPath is required.",
                        nameof(options.SettingsSnapshotPath)
                    );
                }

                healthPublisher = new ThumbnailWorkerHealthPublisher(
                    options.MainDbFullPath,
                    options.OwnerInstanceId,
                    options.WorkerRole,
                    ""
                );

                ThumbnailWorkerSettingsSnapshot snapshot = ThumbnailWorkerSettingsStore.LoadSnapshot(
                    options.SettingsSnapshotPath
                );
                if (
                    !string.Equals(
                        snapshot.MainDbFullPath,
                        options.MainDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"worker settings db mismatch: options='{options.MainDbFullPath}' snapshot='{snapshot.MainDbFullPath}'"
                    );
                }

                ThumbnailWorkerResolvedSettings resolvedSettings = ThumbnailWorkerSettingsResolver.Resolve(
                    snapshot,
                    options.WorkerRole,
                    Environment.ProcessorCount
                );

                ThumbnailWorkerRuntimeLog runtimeLog = new(options.OwnerInstanceId);
                runtimeLog.Write(
                    "worker",
                    $"start role={options.WorkerRole} db='{options.MainDbFullPath}' settings={resolvedSettings.SettingsVersionToken} parallel={resolvedSettings.MaxParallelism} preset={resolvedSettings.Preset}"
                );
                healthPublisher = new ThumbnailWorkerHealthPublisher(
                    options.MainDbFullPath,
                    options.OwnerInstanceId,
                    options.WorkerRole,
                    resolvedSettings.SettingsVersionToken
                );
                targetPriority = ResolveProcessPriority(options.WorkerRole);
                healthPublisher.Publish(
                    ThumbnailWorkerHealthState.Starting,
                    Environment.ProcessId,
                    targetPriority.ToString(),
                    message: "worker starting",
                    reasonCode: ThumbnailWorkerHealthReasonCode.None
                );

                ApplyWorkerEnvironment(resolvedSettings, runtimeLog);
                ApplyWorkerProcessPriority(targetPriority, runtimeLog);

                ThumbnailCreationRuntime creationRuntime = ThumbnailCreationRuntimeFactory.CreateDefault(
                    new WorkerVideoMetadataProvider(),
                    new WorkerThumbnailLogger(runtimeLog)
                );
                QueueDbService queueDbService = new(options.MainDbFullPath);
                ThumbnailQueueProcessor queueProcessor = new();
                ThumbnailProgressRuntime progressRuntime = new();
                progressRuntime.SetPersistentMainDbFullPath(options.MainDbFullPath);
                using ThumbnailProgressExternalSnapshotPublisher progressPublisher = new(
                    options.MainDbFullPath,
                    options.OwnerInstanceId
                );

                progressRuntime.UpdateSessionProgress(
                    completedCount: 0,
                    totalCount: 0,
                    currentParallel: 0,
                    configuredParallel: Math.Max(0, resolvedSettings.MaxParallelism)
                );
                PublishProgress(progressRuntime, progressPublisher);
                using CancellationTokenSource healthHeartbeatCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts);
                Task healthHeartbeatTask = RunHealthHeartbeatAsync(
                    healthPublisher,
                    targetPriority,
                    healthHeartbeatCts.Token
                );

                try
                {
                    await queueProcessor
                        .RunAsync(
                            queueDbServiceResolver: () => queueDbService,
                            ownerInstanceId: options.OwnerInstanceId,
                            createThumbAsync: (queueObj, token) =>
                                CreateThumbAsync(
                                    creationRuntime,
                                    resolvedSettings,
                                    queueObj,
                                    runtimeLog,
                                    progressRuntime,
                                    progressPublisher,
                                    token
                                ),
                            maxParallelism: Math.Max(1, resolvedSettings.MaxParallelism),
                            maxParallelismResolver: () => resolvedSettings.MaxParallelism,
                            dynamicMinimumParallelismResolver: () =>
                                resolvedSettings.DynamicMinimumParallelism,
                            allowScaleUpResolver: () => resolvedSettings.AllowDynamicScaleUp,
                            scaleUpDemandFactorResolver: () => resolvedSettings.ScaleUpDemandFactor,
                            pollIntervalMs: Math.Max(100, resolvedSettings.PollIntervalMs),
                            batchCooldownMs: Math.Max(0, resolvedSettings.BatchCooldownMs),
                            leaseMinutes: Math.Max(1, resolvedSettings.LeaseMinutes),
                            leaseBatchSize: Math.Max(1, resolvedSettings.LeaseBatchSize),
                            preferredTabIndexResolver: null,
                            thermalDiskIdResolver: () =>
                                ResolveAdminTelemetryDiskId(resolvedSettings.MainDbFullPath),
                            usnMftVolumeResolver: () =>
                                ResolveAdminTelemetryVolumeName(resolvedSettings.MainDbFullPath),
                            log: message => runtimeLog.Write("queue", message),
                            progressSnapshot: (completed, total, currentParallel, configuredParallel) =>
                            {
                                progressRuntime.UpdateSessionProgress(
                                    completed,
                                    total,
                                    currentParallel,
                                    configuredParallel
                                );
                                PublishProgress(progressRuntime, progressPublisher);
                            },
                            onJobStarted: queueObj =>
                            {
                                progressRuntime.MarkJobStarted(queueObj);
                                PublishProgress(progressRuntime, progressPublisher);
                            },
                            onJobCompleted: queueObj =>
                            {
                                progressRuntime.MarkJobCompleted(queueObj);
                                PublishProgress(progressRuntime, progressPublisher);
                            },
                            progressPresenter: NoOpThumbnailQueueProgressPresenter.Instance,
                            adminTelemetryClient: CreateAdminTelemetryClient(),
                            workerRole: options.WorkerRole,
                            cts: cts
                        )
                        .ConfigureAwait(false);

                    healthPublisher.Publish(
                        ThumbnailWorkerHealthState.Stopped,
                        Environment.ProcessId,
                        targetPriority.ToString(),
                        message: "worker stopped gracefully",
                        reasonCode: ThumbnailWorkerHealthReasonCode.GracefulStop,
                        lastHeartbeatUtc: DateTime.UtcNow
                    );
                }
                catch (OperationCanceledException)
                {
                    healthPublisher.Publish(
                        ThumbnailWorkerHealthState.Stopped,
                        Environment.ProcessId,
                        targetPriority.ToString(),
                        message: "worker canceled",
                        reasonCode: ThumbnailWorkerHealthReasonCode.Canceled,
                        lastHeartbeatUtc: DateTime.UtcNow
                    );
                    throw;
                }
                catch (Exception ex)
                {
                    healthPublisher.Publish(
                        ThumbnailWorkerHealthState.Exited,
                        Environment.ProcessId,
                        targetPriority.ToString(),
                        message: ex.Message,
                        reasonCode: ThumbnailWorkerHealthReasonResolver.Resolve(
                            ThumbnailWorkerHealthState.Exited,
                            ex
                        ),
                        exitCode: 1,
                        lastHeartbeatUtc: DateTime.UtcNow
                    );
                    throw;
                }
                finally
                {
                    healthHeartbeatCts.Cancel();
                    await AwaitBackgroundTaskAsync(healthHeartbeatTask).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (healthPublisher != null)
                {
                    healthPublisher.Publish(
                        ThumbnailWorkerHealthState.Exited,
                        Environment.ProcessId,
                        targetPriority.ToString(),
                        message: ex.Message,
                        reasonCode: ThumbnailWorkerHealthReasonResolver.Resolve(
                            ThumbnailWorkerHealthState.Exited,
                            ex
                        ),
                        exitCode: 1,
                        lastHeartbeatUtc: DateTime.UtcNow
                    );
                }

                throw;
            }
        }

        private static async Task CreateThumbAsync(
            ThumbnailCreationRuntime creationRuntime,
            ThumbnailWorkerResolvedSettings resolvedSettings,
            QueueObj queueObj,
            ThumbnailWorkerRuntimeLog runtimeLog,
            ThumbnailProgressRuntime progressRuntime,
            ThumbnailProgressExternalSnapshotPublisher progressPublisher,
            CancellationToken cts
        )
        {
            ThumbnailCreateResult result = await creationRuntime
                .CreateThumbAsync(
                    queueObj,
                    resolvedSettings.DbName,
                    resolvedSettings.ThumbFolder,
                    resolvedSettings.ResizeThumb,
                    isManual: false,
                    cts
                )
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                if (!string.IsNullOrWhiteSpace(result.SaveThumbFileName))
                {
                    progressRuntime.MarkThumbnailSaved(
                        queueObj,
                        result.SaveThumbFileName,
                        previewCacheKey: "",
                        previewRevision: DateTime.UtcNow.Ticks
                    );
                    PublishProgress(progressRuntime, progressPublisher);
                }

                runtimeLog.Write(
                    "thumbnail",
                    $"done role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' output='{result.SaveThumbFileName}'"
                );
                return;
            }

            string message =
                $"thumbnail create failed: role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' reason='{result.ErrorMessage}'";
            progressRuntime.MarkJobFailed(queueObj);
            PublishProgress(progressRuntime, progressPublisher);
            runtimeLog.Write("thumbnail", message);
            throw new InvalidOperationException(message);
        }

        private static IAdminTelemetryClient CreateAdminTelemetryClient()
        {
            return new NamedPipeAdminTelemetryClient();
        }

        private static string ResolveAdminTelemetryDiskId(string mainDbFullPath)
        {
            return ResolveDriveRootPath(mainDbFullPath);
        }

        private static string ResolveAdminTelemetryVolumeName(string mainDbFullPath)
        {
            return ResolveDriveRootPath(mainDbFullPath);
        }

        private static string ResolveDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void ApplyWorkerEnvironment(
            ThumbnailWorkerResolvedSettings resolvedSettings,
            ThumbnailWorkerRuntimeLog runtimeLog
        )
        {
            ThumbnailWorkerExecutionEnvironment.Apply(
                resolvedSettings,
                message => runtimeLog.Write("worker", message)
            );
        }

        private static void ApplyWorkerProcessPriority(
            ProcessPriorityClass targetPriority,
            ThumbnailWorkerRuntimeLog runtimeLog
        )
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = targetPriority;
                runtimeLog.Write("worker", $"priority applied: {targetPriority}");
            }
            catch (Exception ex)
            {
                runtimeLog.Write("worker", $"priority apply failed: {ex.Message}");
            }
        }

        private static ProcessPriorityClass ResolveProcessPriority(
            ThumbnailQueueWorkerRole workerRole
        )
        {
            return workerRole switch
            {
                ThumbnailQueueWorkerRole.Normal => ProcessPriorityClass.BelowNormal,
                ThumbnailQueueWorkerRole.Idle => ProcessPriorityClass.Idle,
                _ => ProcessPriorityClass.BelowNormal,
            };
        }

        private static ProcessPriorityClass ResolveFfmpegPriority(
            ThumbnailQueueWorkerRole workerRole
        )
        {
            return workerRole switch
            {
                ThumbnailQueueWorkerRole.Normal => ProcessPriorityClass.BelowNormal,
                ThumbnailQueueWorkerRole.Idle => ProcessPriorityClass.Idle,
                _ => ProcessPriorityClass.Idle,
            };
        }
        private static void PublishProgress(
            ThumbnailProgressRuntime progressRuntime,
            ThumbnailProgressExternalSnapshotPublisher progressPublisher
        )
        {
            if (progressRuntime == null || progressPublisher == null)
            {
                return;
            }

            progressPublisher.Publish(progressRuntime.CreateSnapshot());
        }

        private static async Task RunHealthHeartbeatAsync(
            ThumbnailWorkerHealthPublisher healthPublisher,
            ProcessPriorityClass priority,
            CancellationToken cts
        )
        {
            if (healthPublisher == null)
            {
                return;
            }

            while (!cts.IsCancellationRequested)
            {
                DateTime heartbeatUtc = DateTime.UtcNow;
                healthPublisher.Publish(
                    ThumbnailWorkerHealthState.Running,
                    Environment.ProcessId,
                    priority.ToString(),
                    message: "heartbeat",
                    reasonCode: ThumbnailWorkerHealthReasonCode.None,
                    lastHeartbeatUtc: heartbeatUtc
                );

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cts).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task AwaitBackgroundTaskAsync(Task task)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}

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
        private const int ThumbnailNormalLaneTimeoutDefaultSec = 10;
        private const string ThumbnailNormalLaneTimeoutSecEnvName =
            "IMM_THUMB_NORMAL_LANE_TIMEOUT_SEC";

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
                PublishProgress(
                    progressRuntime,
                    progressPublisher,
                    queueDbService,
                    options.OwnerInstanceId,
                    resolvedSettings.SlowLaneMinGb
                );
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
                                    queueDbService,
                                    options.OwnerInstanceId,
                                    resolvedSettings.SlowLaneMinGb,
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
                                PublishProgress(
                                    progressRuntime,
                                    progressPublisher,
                                    queueDbService,
                                    options.OwnerInstanceId,
                                    resolvedSettings.SlowLaneMinGb
                                );
                            },
                            onJobStarted: queueObj =>
                            {
                                progressRuntime.MarkJobStarted(queueObj);
                                PublishProgress(
                                    progressRuntime,
                                    progressPublisher,
                                    queueDbService,
                                    options.OwnerInstanceId,
                                    resolvedSettings.SlowLaneMinGb
                                );
                            },
                            onJobCompleted: queueObj =>
                            {
                                progressRuntime.MarkJobCompleted(queueObj);
                                PublishProgress(
                                    progressRuntime,
                                    progressPublisher,
                                    queueDbService,
                                    options.OwnerInstanceId,
                                    resolvedSettings.SlowLaneMinGb
                                );
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
            QueueDbService queueDbService,
            string ownerInstanceId,
            int slowLaneMinGb,
            CancellationToken cts
        )
        {
            bool useNormalLaneTimeout = ShouldUseThumbnailNormalLaneTimeout(queueObj);
            TimeSpan normalLaneTimeout = ResolveThumbnailNormalLaneTimeout();
            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;
            ThumbnailCreateResult result;
            runtimeLog.Write(
                "thumbnail",
                $"enter role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} rescue={queueObj?.IsRescueRequest} attempt={queueObj?.AttemptCount}"
            );
            try
            {
                if (useNormalLaneTimeout)
                {
                    // 通常レーンだけ短い予算を掛け、重い仕事は recovery へ逃がす。
                    timeoutCts = new CancellationTokenSource(normalLaneTimeout);
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cts,
                        timeoutCts.Token
                    );
                }

                runtimeLog.Write(
                    "thumbnail",
                    $"invoke create role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' timeout_enabled={useNormalLaneTimeout} timeout_sec={(useNormalLaneTimeout ? normalLaneTimeout.TotalSeconds : 0):0}"
                );
                result = await creationRuntime
                    .CreateThumbAsync(
                        queueObj,
                        resolvedSettings.DbName,
                        resolvedSettings.ThumbFolder,
                        resolvedSettings.ResizeThumb,
                        isManual: false,
                        linkedCts?.Token ?? cts
                    )
                    .ConfigureAwait(false);
                runtimeLog.Write(
                    "thumbnail",
                    $"create returned role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' success={result?.IsSuccess} err='{result?.ErrorMessage}' output='{result?.SaveThumbFileName}'"
                );
            }
            catch (OperationCanceledException)
                when (
                    useNormalLaneTimeout
                    && timeoutCts?.IsCancellationRequested == true
                    && !cts.IsCancellationRequested
                )
            {
                if (
                    TryPromoteThumbnailJobToRescueLane(
                        queueDbService,
                        queueObj,
                        runtimeLog,
                        $"normal-timeout:{normalLaneTimeout.TotalSeconds:0}"
                    )
                )
                {
                    runtimeLog.Write(
                        "thumbnail-timeout",
                        $"normal lane timeout handoff: role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} timeout_sec={normalLaneTimeout.TotalSeconds:0}"
                    );
                    return;
                }

                throw new TimeoutException(
                    $"thumbnail normal lane timeout: role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} timeout_sec={normalLaneTimeout.TotalSeconds:0}"
                );
            }
            finally
            {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }

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
                    PublishProgress(
                        progressRuntime,
                        progressPublisher,
                        queueDbService,
                        ownerInstanceId,
                        slowLaneMinGb
                    );
                }

                runtimeLog.Write(
                    "thumbnail",
                    $"done role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' output='{result.SaveThumbFileName}'"
                );
                return;
            }

            if (
                ShouldPromoteThumbnailFailureToRescueLane(queueObj)
                && TryPromoteThumbnailJobToRescueLane(
                    queueDbService,
                    queueObj,
                    runtimeLog,
                    $"normal-failed:{result.ErrorMessage}"
                )
            )
            {
                runtimeLog.Write(
                    "thumbnail-recovery",
                    $"normal lane failure handoff: role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} reason='{result.ErrorMessage}'"
                );
                return;
            }

            string message =
                $"thumbnail create failed: role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}' reason='{result.ErrorMessage}'";
            runtimeLog.Write(
                "thumbnail",
                $"failure finalize begin role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}'"
            );
            progressRuntime.MarkJobFailed(queueObj);
            PublishProgress(
                progressRuntime,
                progressPublisher,
                queueDbService,
                ownerInstanceId,
                slowLaneMinGb
            );
            runtimeLog.Write("thumbnail", message);
            runtimeLog.Write(
                "thumbnail",
                $"failure finalize end role={resolvedSettings.WorkerRole} movie='{queueObj?.MovieFullPath}'"
            );
            throw new ThumbnailCreateFailedException(message, result);
        }

        // 通常レーンだけ短い予算を掛け、recovery は長居を許可する。
        private static bool ShouldUseThumbnailNormalLaneTimeout(QueueObj queueObj)
        {
            if (queueObj?.IsRescueRequest == true)
            {
                return false;
            }

            return queueObj != null;
        }

        // 通常レーン失敗だけ recovery へ昇格し、recovery 内の再帰的横流しは避ける。
        private static bool ShouldPromoteThumbnailFailureToRescueLane(QueueObj queueObj)
        {
            if (queueObj?.IsRescueRequest == true)
            {
                return false;
            }

            return queueObj != null;
        }

        // 実機で秒数を詰めやすいよう、normal lane timeout は環境変数で上書きできる。
        private static TimeSpan ResolveThumbnailNormalLaneTimeout()
        {
            string raw =
                Environment.GetEnvironmentVariable(ThumbnailNormalLaneTimeoutSecEnvName)?.Trim()
                ?? "";
            if (
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec)
                && sec > 0
            )
            {
                return TimeSpan.FromSeconds(sec);
            }

            return TimeSpan.FromSeconds(ThumbnailNormalLaneTimeoutDefaultSec);
        }

        // QueueDB 行を recovery へ昇格し、同じ重い仕事が normal を塞ぎ続けるのを防ぐ。
        private static bool TryPromoteThumbnailJobToRescueLane(
            QueueDbService queueDbService,
            QueueObj queueObj,
            ThumbnailWorkerRuntimeLog runtimeLog,
            string reason
        )
        {
            if (!ShouldPromoteThumbnailFailureToRescueLane(queueObj))
            {
                return false;
            }

            if (queueDbService == null || string.IsNullOrWhiteSpace(queueObj?.MovieFullPath))
            {
                return false;
            }

            int updated = queueDbService.ForceRetryMovieToPending(
                queueObj.MovieFullPath,
                queueObj.Tabindex,
                DateTime.UtcNow,
                promoteToRecovery: true
            );
            if (updated > 0)
            {
                queueObj.IsRescueRequest = true;
                runtimeLog.Write(
                    "thumbnail-recovery",
                    $"recovery scheduled by force-reset: movie='{queueObj?.MovieFullPath}' tab={queueObj?.Tabindex} reason='{reason}'"
                );
                return true;
            }

            return false;
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
            ThumbnailProgressExternalSnapshotPublisher progressPublisher,
            QueueDbService queueDbService,
            string ownerInstanceId,
            int slowLaneMinGb
        )
        {
            if (progressRuntime == null || progressPublisher == null)
            {
                return;
            }

            if (queueDbService != null && !string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                long slowLaneMinMovieSizeBytes = Math.Max(1, slowLaneMinGb) * 1024L * 1024L * 1024L;
                QueueDbDemandSnapshot demandSnapshot = queueDbService.GetDemandSnapshot(
                    [ownerInstanceId],
                    slowLaneMinMovieSizeBytes,
                    DateTime.UtcNow
                );
                progressRuntime.UpdateQueueObservation(
                    demandSnapshot.LeasedTotalCount,
                    demandSnapshot.RunningTotalCount,
                    demandSnapshot.HangSuspectedCount
                );
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

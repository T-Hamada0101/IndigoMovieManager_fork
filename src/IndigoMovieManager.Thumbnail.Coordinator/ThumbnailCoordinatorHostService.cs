using System.Globalization;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 最小 Coordinator ホスト。
    /// まずは settings と command を束ねて control を返す契約面だけを担当する。
    /// </summary>
    public sealed class ThumbnailCoordinatorHostService
    {
        private static readonly TimeSpan CommandMaxAge = TimeSpan.FromDays(1);
        private static readonly TimeSpan HealthMaxAge = TimeSpan.FromSeconds(15);
        private const int MinExternalCoordinatorParallelism = 2;
        private const int MaxDecisionHistoryCount = 10;

        public async Task RunAsync(
            ThumbnailCoordinatorRuntimeOptions options,
            CancellationToken cts = default
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ThumbnailWorkerSettingsSnapshot initialSettings = ThumbnailWorkerSettingsStore.LoadSnapshot(
                options.InitialSettingsSnapshotPath
            );
            if (
                !string.Equals(
                    initialSettings.MainDbFullPath,
                    options.MainDbFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException(
                    $"coordinator settings db mismatch: options='{options.MainDbFullPath}' snapshot='{initialSettings.MainDbFullPath}'"
                );
            }

            ThumbnailCoordinatorWorkerProcessManager workerProcessManager = new();
            QueueDbService queueDbService = new(options.MainDbFullPath);
            List<ThumbnailCoordinatorDecisionHistoryEntry> decisionHistory = LoadDecisionHistorySeed(
                options
            );
            try
            {
                ThumbnailCoordinatorCommandSnapshot latestCommand =
                    ThumbnailCoordinatorCommandStore.LoadLatest(
                        options.MainDbFullPath,
                        options.OwnerInstanceId,
                        CommandMaxAge
                    );
                CoordinatorExecutionPlan executionPlan = ResolveExecutionPlan(
                    initialSettings,
                    latestCommand,
                    queueDbService,
                    options
                );
                ThumbnailWorkerSettingsSaveResult settingsRef = SaveEffectiveSettingsSnapshot(
                    initialSettings,
                    executionPlan
                );
                DateTime publishedAtUtc = DateTime.UtcNow;
                decisionHistory = AppendDecisionHistory(
                    decisionHistory,
                    executionPlan,
                    publishedAtUtc
                );

                PublishControl(
                    options,
                    executionPlan,
                    settingsRef,
                    ThumbnailCoordinatorState.Starting,
                    decisionHistory,
                    publishedAtUtc
                );

                while (!cts.IsCancellationRequested)
                {
                    latestCommand =
                        ThumbnailCoordinatorCommandStore.LoadLatest(
                            options.MainDbFullPath,
                            options.OwnerInstanceId,
                            CommandMaxAge
                        );
                    executionPlan = ResolveExecutionPlan(
                        initialSettings,
                        latestCommand,
                        queueDbService,
                        options
                    );
                    settingsRef = SaveEffectiveSettingsSnapshot(initialSettings, executionPlan);
                    workerProcessManager.ReconcileWorkers(
                        BuildWorkerLaunchConfigs(options, settingsRef),
                        _ => { }
                    );
                    publishedAtUtc = DateTime.UtcNow;
                    decisionHistory = AppendDecisionHistory(
                        decisionHistory,
                        executionPlan,
                        publishedAtUtc
                    );

                    PublishControl(
                        options,
                        executionPlan,
                        settingsRef,
                        ThumbnailCoordinatorState.Running,
                        decisionHistory,
                        publishedAtUtc
                    );

                    await Task.Delay(1000, cts).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                PublishControl(
                    options,
                    ResolveExecutionPlan(initialSettings, null, queueDbService, options),
                    null,
                    ThumbnailCoordinatorState.Stopped,
                    decisionHistory,
                    DateTime.UtcNow
                );
                throw;
            }
            catch
            {
                PublishControl(
                    options,
                    ResolveExecutionPlan(initialSettings, null, queueDbService, options),
                    null,
                    ThumbnailCoordinatorState.StartFailed,
                    decisionHistory,
                    DateTime.UtcNow
                );
                throw;
            }
            finally
            {
                workerProcessManager.StopAllWorkers();
            }
        }

        private static void PublishControl(
            ThumbnailCoordinatorRuntimeOptions options,
            CoordinatorExecutionPlan executionPlan,
            ThumbnailWorkerSettingsSaveResult settingsRef,
            string coordinatorState,
            IReadOnlyList<ThumbnailCoordinatorDecisionHistoryEntry> decisionHistory,
            DateTime publishedAtUtc
        )
        {
            IReadOnlyList<ThumbnailWorkerHealthSnapshot> healthSnapshots =
                ThumbnailWorkerHealthStore.LoadSnapshots(
                    options.MainDbFullPath,
                    [options.NormalWorkerOwnerInstanceId, options.IdleWorkerOwnerInstanceId],
                    HealthMaxAge
                );
            int activeWorkerCount = healthSnapshots.Count(x =>
                string.Equals(x.State, ThumbnailWorkerHealthState.Running, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.State, ThumbnailWorkerHealthState.Starting, StringComparison.OrdinalIgnoreCase)
            );
            string resolvedState = ResolveCoordinatorState(
                coordinatorState,
                healthSnapshots,
                activeWorkerCount
            );
            string reason = ResolveReason(executionPlan, settingsRef, healthSnapshots);

            ThumbnailCoordinatorControlStore.Save(
                new ThumbnailCoordinatorControlSnapshot
                {
                    MainDbFullPath = options.MainDbFullPath,
                    DbName = options.DbName,
                    OwnerInstanceId = options.OwnerInstanceId,
                    CoordinatorState = resolvedState,
                    RequestedParallelism = executionPlan.RequestedParallelism,
                    TemporaryParallelismDelta = executionPlan.TemporaryParallelismDelta,
                    EffectiveParallelism = executionPlan.EffectiveParallelism,
                    LargeMovieThresholdGb = executionPlan.LargeMovieThresholdGb,
                    GpuDecodeEnabled = executionPlan.GpuDecodeEnabled,
                    OperationMode = executionPlan.OperationMode,
                    FastSlotCount = executionPlan.FastSlotCount,
                    SlowSlotCount = executionPlan.SlowSlotCount,
                    ActiveWorkerCount = activeWorkerCount,
                    ActiveFfmpegCount = 0,
                    QueuedNormalCount = executionPlan.DemandSnapshot.QueuedNormalCount,
                    QueuedSlowCount = executionPlan.DemandSnapshot.QueuedSlowCount,
                    QueuedRecoveryCount = executionPlan.DemandSnapshot.QueuedRecoveryCount,
                    RunningNormalCount = executionPlan.DemandSnapshot.RunningNormalCount,
                    RunningSlowCount = executionPlan.DemandSnapshot.RunningSlowCount,
                    RunningRecoveryCount = executionPlan.DemandSnapshot.RunningRecoveryCount,
                    DemandNormalCount = executionPlan.SlotDecision.NormalDemand,
                    DemandSlowCount = executionPlan.SlotDecision.SlowDemand,
                    DemandRecoveryCount = executionPlan.SlotDecision.RecoveryDemand,
                    WeightedNormalDemand = executionPlan.SlotDecision.WeightedNormalDemand,
                    WeightedSlowDemand = executionPlan.SlotDecision.WeightedSlowDemand,
                    SlowSlotMinimum = executionPlan.SlotDecision.MinimumSlowSlots,
                    SlowSlotMaximum = executionPlan.SlotDecision.MaximumSlowSlots,
                    DecisionCategory = executionPlan.SlotDecision.DecisionCategory,
                    DecisionSummary = executionPlan.SlotDecision.DecisionSummary,
                    Reason = reason,
                    DecisionHistory = decisionHistory ?? [],
                    UpdatedAtUtc = publishedAtUtc,
                }
            );
        }

        private static List<ThumbnailCoordinatorDecisionHistoryEntry> LoadDecisionHistorySeed(
            ThumbnailCoordinatorRuntimeOptions options
        )
        {
            ThumbnailCoordinatorControlSnapshot latestSnapshot =
                ThumbnailCoordinatorControlStore.LoadLatest(
                    options.MainDbFullPath,
                    options.OwnerInstanceId,
                    TimeSpan.FromDays(30)
                );

            if (latestSnapshot?.DecisionHistory == null || latestSnapshot.DecisionHistory.Count < 1)
            {
                return [];
            }

            return latestSnapshot
                .DecisionHistory.Where(x => x != null)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(MaxDecisionHistoryCount)
                .Select(x => new ThumbnailCoordinatorDecisionHistoryEntry
                {
                    UpdatedAtUtc = x.UpdatedAtUtc,
                    OperationMode = x.OperationMode ?? "",
                    DecisionCategory = x.DecisionCategory ?? "",
                    DecisionSummary = x.DecisionSummary ?? "",
                    FastSlotCount = x.FastSlotCount,
                    SlowSlotCount = x.SlowSlotCount,
                })
                .ToList();
        }

        // 同じ配分判断が続く間は履歴を増やさず、変化した時だけ先頭へ積む。
        private static List<ThumbnailCoordinatorDecisionHistoryEntry> AppendDecisionHistory(
            IReadOnlyList<ThumbnailCoordinatorDecisionHistoryEntry> currentHistory,
            CoordinatorExecutionPlan executionPlan,
            DateTime updatedAtUtc
        )
        {
            List<ThumbnailCoordinatorDecisionHistoryEntry> nextHistory = currentHistory?
                .Where(x => x != null)
                .Take(MaxDecisionHistoryCount)
                .Select(x => new ThumbnailCoordinatorDecisionHistoryEntry
                {
                    UpdatedAtUtc = x.UpdatedAtUtc,
                    OperationMode = x.OperationMode ?? "",
                    DecisionCategory = x.DecisionCategory ?? "",
                    DecisionSummary = x.DecisionSummary ?? "",
                    FastSlotCount = x.FastSlotCount,
                    SlowSlotCount = x.SlowSlotCount,
                })
                .ToList() ?? [];

            ThumbnailCoordinatorDecisionHistoryEntry latestEntry = new()
            {
                UpdatedAtUtc = updatedAtUtc,
                OperationMode = executionPlan?.OperationMode ?? "",
                DecisionCategory = executionPlan?.SlotDecision?.DecisionCategory ?? "",
                DecisionSummary = executionPlan?.SlotDecision?.DecisionSummary ?? "",
                FastSlotCount = executionPlan?.FastSlotCount ?? 0,
                SlowSlotCount = executionPlan?.SlowSlotCount ?? 0,
            };

            if (nextHistory.Count > 0 && IsSameDecision(nextHistory[0], latestEntry))
            {
                return nextHistory;
            }

            nextHistory.Insert(0, latestEntry);
            while (nextHistory.Count > MaxDecisionHistoryCount)
            {
                nextHistory.RemoveAt(nextHistory.Count - 1);
            }

            return nextHistory;
        }

        private static bool IsSameDecision(
            ThumbnailCoordinatorDecisionHistoryEntry left,
            ThumbnailCoordinatorDecisionHistoryEntry right
        )
        {
            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.OperationMode, right.OperationMode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.DecisionCategory,
                    right.DecisionCategory,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(left.DecisionSummary, right.DecisionSummary, StringComparison.Ordinal)
                && left.FastSlotCount == right.FastSlotCount
                && left.SlowSlotCount == right.SlowSlotCount;
        }

        private static CoordinatorExecutionPlan ResolveExecutionPlan(
            ThumbnailWorkerSettingsSnapshot initialSettings,
            ThumbnailCoordinatorCommandSnapshot latestCommand,
            QueueDbService queueDbService,
            ThumbnailCoordinatorRuntimeOptions options
        )
        {
            int requestedParallelism = ClampRequestedParallelism(
                latestCommand?.RequestedParallelism ?? initialSettings?.RequestedParallelism ?? 8
            );
            int temporaryParallelismDelta = ClampTemporaryParallelismDelta(
                latestCommand?.TemporaryParallelismDelta ?? 0
            );
            int effectiveParallelism = ClampAppliedParallelism(
                requestedParallelism + temporaryParallelismDelta
            );
            int largeMovieThresholdGb = Math.Max(
                1,
                latestCommand?.LargeMovieThresholdGb ?? initialSettings?.SlowLaneMinGb ?? 50
            );
            string operationMode = NormalizeOperationMode(
                latestCommand?.OperationMode,
                initialSettings?.Preset
            );
            QueueDbDemandSnapshot demandSnapshot = queueDbService.GetDemandSnapshot(
                [options.NormalWorkerOwnerInstanceId, options.IdleWorkerOwnerInstanceId],
                (long)largeMovieThresholdGb * 1024L * 1024L * 1024L,
                DateTime.UtcNow
            );
            CoordinatorSlotDecision slotDecision = ResolveSlotCounts(
                effectiveParallelism,
                operationMode,
                demandSnapshot
            );

            return new CoordinatorExecutionPlan
            {
                RequestedParallelism = requestedParallelism,
                TemporaryParallelismDelta = temporaryParallelismDelta,
                EffectiveParallelism = effectiveParallelism,
                LargeMovieThresholdGb = largeMovieThresholdGb,
                GpuDecodeEnabled =
                    latestCommand?.GpuDecodeEnabled ?? initialSettings?.GpuDecodeEnabled ?? false,
                OperationMode = operationMode,
                FastSlotCount = slotDecision.FastSlotCount,
                SlowSlotCount = slotDecision.SlowSlotCount,
                SlotDecision = slotDecision,
                DemandSnapshot = demandSnapshot,
                LatestCommand = latestCommand,
            };
        }

        private static string ResolveReason(
            CoordinatorExecutionPlan executionPlan,
            ThumbnailWorkerSettingsSaveResult settingsRef,
            IReadOnlyList<ThumbnailWorkerHealthSnapshot> healthSnapshots
        )
        {
            string healthSummary = healthSnapshots == null || healthSnapshots.Count < 1
                ? "health:none"
                : string.Join(
                    ",",
                    healthSnapshots.Select(x =>
                        $"{x.WorkerRole}:{x.State}:{ThumbnailWorkerHealthReasonResolver.ToDisplayText(x.ReasonCode)}"
                    )
                );

            QueueDbDemandSnapshot demandSnapshot = executionPlan?.DemandSnapshot ?? new();
            string queueSummary =
                $"q={demandSnapshot.QueuedNormalCount}/{demandSnapshot.QueuedSlowCount}/{demandSnapshot.QueuedRecoveryCount}"
                + $" run={demandSnapshot.RunningNormalCount}/{demandSnapshot.RunningSlowCount}/{demandSnapshot.RunningRecoveryCount}"
                + $" demand={executionPlan?.SlotDecision?.NormalDemand ?? 0}/{executionPlan?.SlotDecision?.SlowDemand ?? 0}/{executionPlan?.SlotDecision?.RecoveryDemand ?? 0}"
                + $" weight={executionPlan?.SlotDecision?.WeightedNormalDemand ?? 0}/{executionPlan?.SlotDecision?.WeightedSlowDemand ?? 0}"
                + $" slot={executionPlan?.FastSlotCount ?? 0}/{executionPlan?.SlowSlotCount ?? 0}"
                + $" range={executionPlan?.SlotDecision?.MinimumSlowSlots ?? 0}-{executionPlan?.SlotDecision?.MaximumSlowSlots ?? 0}";

            if (executionPlan?.LatestCommand == null)
            {
                return $"initial-settings:{settingsRef?.VersionToken ?? ""} / {queueSummary} / {healthSummary}";
            }

            string issuer = string.IsNullOrWhiteSpace(executionPlan.LatestCommand.IssuedBy)
                ? "unknown"
                : executionPlan.LatestCommand.IssuedBy;
            return $"command:{issuer}:{settingsRef?.VersionToken ?? ""} / {queueSummary} / {healthSummary}";
        }

        private static ThumbnailWorkerSettingsSaveResult SaveEffectiveSettingsSnapshot(
            ThumbnailWorkerSettingsSnapshot initialSettings,
            CoordinatorExecutionPlan executionPlan
        )
        {
            ThumbnailWorkerSettingsSnapshot nextSnapshot = new()
            {
                MainDbFullPath = initialSettings.MainDbFullPath,
                DbName = initialSettings.DbName,
                ThumbFolder = initialSettings.ThumbFolder,
                Preset = initialSettings.Preset,
                RequestedParallelism = executionPlan.EffectiveParallelism,
                SlowLaneMinGb = executionPlan.LargeMovieThresholdGb,
                GpuDecodeEnabled = executionPlan.GpuDecodeEnabled,
                ResizeThumb = initialSettings.ResizeThumb,
                AllowFallbackInProcess = initialSettings.AllowFallbackInProcess,
                BasePollIntervalMs = initialSettings.BasePollIntervalMs,
                LeaseMinutes = initialSettings.LeaseMinutes,
                CoordinatorNormalParallelismOverride = executionPlan.FastSlotCount,
                CoordinatorIdleParallelismOverride = executionPlan.SlowSlotCount,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            return ThumbnailWorkerSettingsStore.SaveSnapshot(nextSnapshot);
        }

        private static IReadOnlyList<ThumbnailCoordinatorWorkerProcessManager.ThumbnailWorkerLaunchConfig> BuildWorkerLaunchConfigs(
            ThumbnailCoordinatorRuntimeOptions options,
            ThumbnailWorkerSettingsSaveResult settingsRef
        )
        {
            if (settingsRef == null || string.IsNullOrWhiteSpace(settingsRef.SnapshotFilePath))
            {
                return [];
            }

            return
            [
                new ThumbnailCoordinatorWorkerProcessManager.ThumbnailWorkerLaunchConfig
                {
                    WorkerRole = ThumbnailQueueWorkerRole.Normal,
                    MainDbFullPath = options.MainDbFullPath,
                    OwnerInstanceId = options.NormalWorkerOwnerInstanceId,
                    SettingsSnapshotPath = settingsRef.SnapshotFilePath,
                    SettingsVersionToken = settingsRef.VersionToken,
                },
                new ThumbnailCoordinatorWorkerProcessManager.ThumbnailWorkerLaunchConfig
                {
                    WorkerRole = ThumbnailQueueWorkerRole.Idle,
                    MainDbFullPath = options.MainDbFullPath,
                    OwnerInstanceId = options.IdleWorkerOwnerInstanceId,
                    SettingsSnapshotPath = settingsRef.SnapshotFilePath,
                    SettingsVersionToken = settingsRef.VersionToken,
                },
            ];
        }

        private static CoordinatorSlotDecision ResolveSlotCounts(
            int effectiveParallelism,
            string operationMode,
            QueueDbDemandSnapshot demandSnapshot
        )
        {
            int safeParallelism = ClampAppliedParallelism(effectiveParallelism);
            int normalDemand = Math.Max(
                0,
                demandSnapshot?.QueuedNormalCount ?? 0
                    + demandSnapshot?.RunningNormalCount ?? 0
            );
            int slowDemand = Math.Max(
                0,
                demandSnapshot?.QueuedSlowCount ?? 0
                    + demandSnapshot?.RunningSlowCount ?? 0
            );
            int recoveryDemand = Math.Max(
                0,
                demandSnapshot?.QueuedRecoveryCount ?? 0
                    + demandSnapshot?.RunningRecoveryCount ?? 0
            );
            CoordinatorSlotDecision slotDecision = ResolveDemandWeightedSlowSlots(
                safeParallelism,
                operationMode,
                normalDemand,
                slowDemand,
                recoveryDemand
            );
            int fastSlotCount = Math.Max(0, safeParallelism - slotDecision.SlowSlotCount);
            if (fastSlotCount < 1)
            {
                fastSlotCount = 1;
                slotDecision = slotDecision with
                {
                    FastSlotCount = fastSlotCount,
                    SlowSlotCount = Math.Max(0, safeParallelism - fastSlotCount),
                };
            }

            if (slotDecision.FastSlotCount != fastSlotCount)
            {
                slotDecision = slotDecision with { FastSlotCount = fastSlotCount };
            }

            return slotDecision;
        }

        private static string ResolveCoordinatorState(
            string requestedState,
            IReadOnlyList<ThumbnailWorkerHealthSnapshot> healthSnapshots,
            int activeWorkerCount
        )
        {
            if (
                !string.Equals(requestedState, ThumbnailCoordinatorState.Running, StringComparison.Ordinal)
            )
            {
                return requestedState;
            }

            if (activeWorkerCount < 2)
            {
                return ThumbnailCoordinatorState.Degraded;
            }

            if (
                healthSnapshots.Any(x =>
                    !string.Equals(
                        x.State,
                        ThumbnailWorkerHealthState.Running,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.Equals(
                        x.State,
                        ThumbnailWorkerHealthState.Starting,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                return ThumbnailCoordinatorState.Degraded;
            }

            return ThumbnailCoordinatorState.Running;
        }

        private static CoordinatorSlotDecision ResolveDemandWeightedSlowSlots(
            int effectiveParallelism,
            string operationMode,
            int normalDemand,
            int slowDemand,
            int recoveryDemand
        )
        {
            if (slowDemand < 1 && recoveryDemand < 1)
            {
                int defaultSlowSlots = 1;
                return new CoordinatorSlotDecision
                {
                    FastSlotCount = Math.Max(1, effectiveParallelism - defaultSlowSlots),
                    SlowSlotCount = defaultSlowSlots,
                    NormalDemand = normalDemand,
                    SlowDemand = slowDemand,
                    RecoveryDemand = recoveryDemand,
                    WeightedNormalDemand = Math.Max(0, normalDemand),
                    WeightedSlowDemand = 0,
                    MinimumSlowSlots = 1,
                    MaximumSlowSlots = Math.Max(1, effectiveParallelism - 1),
                    DecisionCategory = ThumbnailCoordinatorDecisionCategory.Steady,
                    DecisionSummary = BuildDecisionSummary(
                        operationMode,
                        ThumbnailCoordinatorDecisionCategory.Steady,
                        normalDemand,
                        slowDemand,
                        recoveryDemand,
                        Math.Max(0, normalDemand),
                        0,
                        ratioSlowSlots: defaultSlowSlots,
                        defaultSlowSlots,
                        minimumSlowSlots: 1,
                        maximumSlowSlots: Math.Max(1, effectiveParallelism - 1)
                    ),
                };
            }

            int minimumSlowSlots = ResolveMinimumSlowSlots(
                effectiveParallelism,
                operationMode,
                recoveryDemand
            );
            int weightedNormalDemand = Math.Max(0, normalDemand);
            int weightedSlowDemand =
                Math.Max(0, slowDemand) + ResolveRecoveryWeight(operationMode) * Math.Max(0, recoveryDemand);
            int totalWeightedDemand = weightedNormalDemand + weightedSlowDemand;
            int ratioSlowSlots = totalWeightedDemand < 1
                ? minimumSlowSlots
                : (int)Math.Ceiling(
                    effectiveParallelism * (double)weightedSlowDemand / totalWeightedDemand
                );

            // normal が空でも fast 側は slow 初回を代行できるため、slow へ寄せ切らない。
            int maximumSlowSlots = ResolveMaximumSlowSlots(
                effectiveParallelism,
                operationMode,
                normalDemand,
                slowDemand,
                recoveryDemand
            );
            int boundedSlowSlots = Math.Max(minimumSlowSlots, ratioSlowSlots);
            if (maximumSlowSlots >= minimumSlowSlots)
            {
                boundedSlowSlots = Math.Min(boundedSlowSlots, maximumSlowSlots);
            }

            int resolvedSlowSlots = Math.Min(effectiveParallelism - 1, Math.Max(1, boundedSlowSlots));
            string decisionCategory = ResolveDecisionCategory(
                normalDemand,
                slowDemand,
                recoveryDemand,
                ratioSlowSlots,
                resolvedSlowSlots,
                minimumSlowSlots,
                maximumSlowSlots
            );
            return new CoordinatorSlotDecision
            {
                FastSlotCount = Math.Max(1, effectiveParallelism - resolvedSlowSlots),
                SlowSlotCount = resolvedSlowSlots,
                NormalDemand = normalDemand,
                SlowDemand = slowDemand,
                RecoveryDemand = recoveryDemand,
                WeightedNormalDemand = weightedNormalDemand,
                WeightedSlowDemand = weightedSlowDemand,
                MinimumSlowSlots = minimumSlowSlots,
                MaximumSlowSlots = maximumSlowSlots,
                DecisionCategory = decisionCategory,
                DecisionSummary = BuildDecisionSummary(
                    operationMode,
                    decisionCategory,
                    normalDemand,
                    slowDemand,
                    recoveryDemand,
                    weightedNormalDemand,
                    weightedSlowDemand,
                    ratioSlowSlots,
                    resolvedSlowSlots,
                    minimumSlowSlots,
                    maximumSlowSlots
                ),
            };
        }

        private static string ResolveDecisionCategory(
            int normalDemand,
            int slowDemand,
            int recoveryDemand,
            int ratioSlowSlots,
            int resolvedSlowSlots,
            int minimumSlowSlots,
            int maximumSlowSlots
        )
        {
            if (slowDemand < 1 && recoveryDemand < 1)
            {
                return ThumbnailCoordinatorDecisionCategory.Steady;
            }

            if (resolvedSlowSlots == minimumSlowSlots && ratioSlowSlots <= minimumSlowSlots)
            {
                return ThumbnailCoordinatorDecisionCategory.Minimum;
            }

            if (resolvedSlowSlots == maximumSlowSlots && ratioSlowSlots >= maximumSlowSlots)
            {
                if (recoveryDemand > 0)
                {
                    return ThumbnailCoordinatorDecisionCategory.RecoveryBiased;
                }

                if (normalDemand < 1)
                {
                    return ThumbnailCoordinatorDecisionCategory.DelegationCapped;
                }
            }

            return ThumbnailCoordinatorDecisionCategory.DemandBiased;
        }

        private static string BuildDecisionSummary(
            string operationMode,
            string decisionCategory,
            int normalDemand,
            int slowDemand,
            int recoveryDemand,
            int weightedNormalDemand,
            int weightedSlowDemand,
            int ratioSlowSlots,
            int resolvedSlowSlots,
            int minimumSlowSlots,
            int maximumSlowSlots
        )
        {
            string modeText = ThumbnailCoordinatorOperationModeResolver.ToDisplayText(operationMode);
            string categoryText = ThumbnailCoordinatorDecisionCategoryResolver.ToDisplayText(
                decisionCategory
            );
            string demandText = $"需要 n/s/r={normalDemand}/{slowDemand}/{recoveryDemand}";
            string weightText = $"重み n/s={weightedNormalDemand}/{weightedSlowDemand}";
            string slotText = $"slow={resolvedSlowSlots} (比率={ratioSlowSlots}, 範囲={minimumSlowSlots}-{maximumSlowSlots})";

            if (slowDemand < 1 && recoveryDemand < 1)
            {
                return $"{modeText}/{categoryText}: {demandText}。slow/recovery 需要がないため slow=1 を維持";
            }

            if (resolvedSlowSlots == minimumSlowSlots && ratioSlowSlots <= minimumSlowSlots)
            {
                return $"{modeText}/{categoryText}: {demandText}。{weightText}。slow 需要が軽いため最小 slow={minimumSlowSlots} を維持";
            }

            if (resolvedSlowSlots == maximumSlowSlots && ratioSlowSlots >= maximumSlowSlots)
            {
                string capReason = recoveryDemand > 0
                    ? "回復需要を優先して slow 側へ寄せた"
                    : normalDemand < 1
                        ? "通常キューが空でも fast 代行余地を残すため slow を抑制した"
                        : "slow 側へ寄せた";
                return $"{modeText}/{categoryText}: {demandText}。{weightText}。{capReason}。{slotText}";
            }

            return $"{modeText}/{categoryText}: {demandText}。{weightText}。{slotText}";
        }

        private static int ResolveMinimumSlowSlots(
            int effectiveParallelism,
            string operationMode,
            int recoveryDemand
        )
        {
            if (effectiveParallelism <= 2)
            {
                return 1;
            }

            if (
                string.Equals(
                    operationMode,
                    ThumbnailCoordinatorOperationMode.PowerSave,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Math.Min(
                    effectiveParallelism - 1,
                    Math.Max(1, (int)Math.Ceiling(effectiveParallelism / 3d))
                );
            }

            if (
                string.Equals(
                    operationMode,
                    ThumbnailCoordinatorOperationMode.RecoveryFirst,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                int baseSlots = recoveryDemand > 0 ? 2 : 1;
                return Math.Min(effectiveParallelism - 1, Math.Max(1, baseSlots));
            }

            return 1;
        }

        private static int ResolveMaximumSlowSlots(
            int effectiveParallelism,
            string operationMode,
            int normalDemand,
            int slowDemand,
            int recoveryDemand
        )
        {
            if (recoveryDemand > 0)
            {
                return effectiveParallelism - 1;
            }

            if (
                normalDemand < 1
                && slowDemand > 0
                && string.Equals(
                    operationMode,
                    ThumbnailCoordinatorOperationMode.PowerSave,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Math.Max(1, effectiveParallelism - 2);
            }

            if (normalDemand < 1 && slowDemand > 0)
            {
                return Math.Max(1, effectiveParallelism - 3);
            }

            return effectiveParallelism - 1;
        }

        private static int ResolveRecoveryWeight(string operationMode)
        {
            if (
                string.Equals(
                    operationMode,
                    ThumbnailCoordinatorOperationMode.RecoveryFirst,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return 4;
            }

            if (
                string.Equals(
                    operationMode,
                    ThumbnailCoordinatorOperationMode.PowerSave,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return 3;
            }

            return 2;
        }

        private static int ClampRequestedParallelism(int parallelism)
        {
            return Math.Min(24, Math.Max(1, parallelism));
        }

        private static int ClampAppliedParallelism(int parallelism)
        {
            return Math.Min(24, Math.Max(MinExternalCoordinatorParallelism, parallelism));
        }

        private static int ClampTemporaryParallelismDelta(int delta)
        {
            return Math.Min(8, Math.Max(-8, delta));
        }

        private static string NormalizeOperationMode(string operationMode, string preset)
        {
            return (operationMode ?? "").ToLowerInvariant() switch
            {
                ThumbnailCoordinatorOperationMode.PowerSave => ThumbnailCoordinatorOperationMode.PowerSave,
                ThumbnailCoordinatorOperationMode.RecoveryFirst => ThumbnailCoordinatorOperationMode.RecoveryFirst,
                _ => string.Equals((preset ?? "").Trim(), "slow", StringComparison.OrdinalIgnoreCase)
                    ? ThumbnailCoordinatorOperationMode.PowerSave
                    : ThumbnailCoordinatorOperationMode.NormalFirst,
            };
        }
    }

    internal sealed class CoordinatorExecutionPlan
    {
        public int RequestedParallelism { get; init; }
        public int TemporaryParallelismDelta { get; init; }
        public int EffectiveParallelism { get; init; }
        public int LargeMovieThresholdGb { get; init; }
        public bool GpuDecodeEnabled { get; init; }
        public string OperationMode { get; init; } = ThumbnailCoordinatorOperationMode.NormalFirst;
        public int FastSlotCount { get; init; }
        public int SlowSlotCount { get; init; }
        public CoordinatorSlotDecision SlotDecision { get; init; } = new();
        public QueueDbDemandSnapshot DemandSnapshot { get; init; } = new();
        public ThumbnailCoordinatorCommandSnapshot LatestCommand { get; init; }
    }

    internal sealed record CoordinatorSlotDecision
    {
        public int FastSlotCount { get; init; }
        public int SlowSlotCount { get; init; }
        public int NormalDemand { get; init; }
        public int SlowDemand { get; init; }
        public int RecoveryDemand { get; init; }
        public int WeightedNormalDemand { get; init; }
        public int WeightedSlowDemand { get; init; }
        public int MinimumSlowSlots { get; init; }
        public int MaximumSlowSlots { get; init; }
        public string DecisionCategory { get; init; } = "";
        public string DecisionSummary { get; init; } = "";
    }

    public sealed class ThumbnailCoordinatorRuntimeOptions
    {
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string OwnerInstanceId { get; init; } = "";
        public string InitialSettingsSnapshotPath { get; init; } = "";
        public string NormalWorkerOwnerInstanceId { get; init; } = "";
        public string IdleWorkerOwnerInstanceId { get; init; } = "";
        public int ParentProcessId { get; init; }

        public static ThumbnailCoordinatorRuntimeOptions Parse(string[] args)
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

            return new ThumbnailCoordinatorRuntimeOptions
            {
                MainDbFullPath = GetRequired(values, "main-db"),
                DbName = GetRequired(values, "db-name"),
                OwnerInstanceId = GetRequired(values, "owner"),
                InitialSettingsSnapshotPath =
                    GetRequired(values, "initial-settings-snapshot"),
                NormalWorkerOwnerInstanceId = GetRequired(values, "normal-owner"),
                IdleWorkerOwnerInstanceId = GetRequired(values, "idle-owner"),
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
    }
}

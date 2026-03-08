namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker が snapshot から role ごとの実効設定を決める。
    /// </summary>
    public static class ThumbnailWorkerSettingsResolver
    {
        private const int HardMinParallelism = 2;
        private const int HardMaxParallelism = 24;
        private const int DefaultDynamicMinimumParallelism = 4;
        private const int BallenceDynamicMinimumParallelism = 3;
        private const int FastDemandScaleUpFactor = 2;
        private const int NormalDemandScaleUpFactor = 3;
        private const int BallenceDemandScaleUpFactor = 4;
        private const int SlowPollIntervalMultiplier = 2;
        private const int SlowBatchCooldownMs = 750;
        private const int IdleMinimumPollIntervalMs = 3000;

        public static ThumbnailWorkerResolvedSettings Resolve(
            ThumbnailWorkerSettingsSnapshot snapshot,
            ThumbnailQueueWorkerRole workerRole,
            int logicalCoreCount
        )
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            int safeLogicalCoreCount = logicalCoreCount < 1 ? 1 : logicalCoreCount;
            string normalizedPreset = NormalizePresetKey(snapshot.Preset);
            int presetConfiguredParallelism = ResolveParallelism(
                normalizedPreset,
                snapshot.RequestedParallelism,
                safeLogicalCoreCount
            );
            bool hasCoordinatorOverride = TryResolveCoordinatorParallelismOverride(
                snapshot,
                presetConfiguredParallelism,
                out int configuredParallelism,
                out int coordinatorNormalParallelism,
                out int coordinatorIdleParallelism
            );

            int resolvedParallelism = workerRole switch
            {
                ThumbnailQueueWorkerRole.Idle => hasCoordinatorOverride
                    ? coordinatorIdleParallelism
                    : 1,
                ThumbnailQueueWorkerRole.Normal => hasCoordinatorOverride
                    ? coordinatorNormalParallelism
                    : Math.Max(1, configuredParallelism - 1),
                _ => configuredParallelism,
            };

            int normalPollIntervalMs = ResolveQueuePollIntervalMs(
                normalizedPreset,
                snapshot.BasePollIntervalMs
            );
            int normalBatchCooldownMs = ResolveBatchCooldownMs(normalizedPreset);
            int pollIntervalMs = workerRole == ThumbnailQueueWorkerRole.Idle
                ? Math.Max(normalPollIntervalMs, IdleMinimumPollIntervalMs)
                : normalPollIntervalMs;
            int batchCooldownMs = workerRole == ThumbnailQueueWorkerRole.Idle
                ? Math.Max(normalBatchCooldownMs, SlowBatchCooldownMs)
                : normalBatchCooldownMs;

            int dynamicMinimumParallelism = Math.Min(
                resolvedParallelism,
                ResolveDynamicMinimumParallelism(normalizedPreset, resolvedParallelism)
            );

            return new ThumbnailWorkerResolvedSettings
            {
                MainDbFullPath = snapshot.MainDbFullPath ?? "",
                DbName = snapshot.DbName ?? "",
                ThumbFolder = snapshot.ThumbFolder ?? "",
                WorkerRole = workerRole,
                ConfiguredTotalParallelism = configuredParallelism,
                MaxParallelism = resolvedParallelism,
                DynamicMinimumParallelism = Math.Max(1, dynamicMinimumParallelism),
                AllowDynamicScaleUp = workerRole != ThumbnailQueueWorkerRole.Idle
                    && ResolveAllowDynamicScaleUp(normalizedPreset),
                ScaleUpDemandFactor = ResolveScaleUpDemandFactor(normalizedPreset),
                PollIntervalMs = Math.Max(100, pollIntervalMs),
                BatchCooldownMs = Math.Max(0, batchCooldownMs),
                LeaseMinutes = Math.Max(1, snapshot.LeaseMinutes),
                LeaseBatchSize = workerRole == ThumbnailQueueWorkerRole.Idle
                    ? 1
                    : Math.Max(4, resolvedParallelism),
                SlowLaneMinGb = Math.Max(1, snapshot.SlowLaneMinGb),
                ResizeThumb = snapshot.ResizeThumb,
                GpuDecodeEnabled = snapshot.GpuDecodeEnabled,
                ProcessPriorityName = workerRole == ThumbnailQueueWorkerRole.Idle
                    ? "Idle"
                    : "BelowNormal",
                FfmpegPriorityName = workerRole == ThumbnailQueueWorkerRole.Idle
                    ? "Idle"
                    : "BelowNormal",
                Preset = normalizedPreset,
                SettingsVersionToken = snapshot.VersionToken ?? "",
            };
        }

        private static string NormalizePresetKey(string preset)
        {
            string normalized = (preset ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "slow" => "slow",
                "normal" => "normal",
                "ballence" => "ballence",
                "fast" => "fast",
                "max" => "max",
                "custum" => "custum",
                _ => "custum",
            };
        }

        private static bool IsLowLoadPreset(string preset)
        {
            return string.Equals(preset, "slow", StringComparison.Ordinal);
        }

        private static int ResolveParallelism(string preset, int manualParallelism, int logicalCoreCount)
        {
            return preset switch
            {
                "slow" => HardMinParallelism,
                "normal" => ResolveDividedParallelism(logicalCoreCount, 3),
                "ballence" => ResolveDividedParallelism(logicalCoreCount, 4),
                "fast" => ResolveDividedParallelism(logicalCoreCount, 2),
                "max" => ClampParallelism(logicalCoreCount),
                _ => ClampParallelism(manualParallelism),
            };
        }

        private static int ResolveQueuePollIntervalMs(string preset, int basePollIntervalMs)
        {
            int safeBasePollIntervalMs = basePollIntervalMs < 100 ? 100 : basePollIntervalMs;
            if (!IsLowLoadPreset(preset))
            {
                return safeBasePollIntervalMs;
            }

            long resolved = (long)safeBasePollIntervalMs * SlowPollIntervalMultiplier;
            return resolved > int.MaxValue ? int.MaxValue : (int)resolved;
        }

        private static int ResolveBatchCooldownMs(string preset)
        {
            return IsLowLoadPreset(preset) ? SlowBatchCooldownMs : 0;
        }

        private static int ResolveDynamicMinimumParallelism(string preset, int configuredParallelism)
        {
            int boundedConfigured = ClampParallelism(configuredParallelism);
            int resolved = preset switch
            {
                "slow" => HardMinParallelism,
                "ballence" => BallenceDynamicMinimumParallelism,
                _ => DefaultDynamicMinimumParallelism,
            };

            if (resolved > boundedConfigured)
            {
                resolved = boundedConfigured;
            }

            return ClampParallelism(resolved);
        }

        private static bool ResolveAllowDynamicScaleUp(string preset)
        {
            return !IsLowLoadPreset(preset);
        }

        private static int ResolveScaleUpDemandFactor(string preset)
        {
            return preset switch
            {
                "ballence" => BallenceDemandScaleUpFactor,
                "normal" => NormalDemandScaleUpFactor,
                _ => FastDemandScaleUpFactor,
            };
        }

        private static int ResolveDividedParallelism(int logicalCoreCount, int divisor)
        {
            int safeDivisor = divisor < 1 ? 1 : divisor;
            return ClampParallelism(logicalCoreCount / safeDivisor);
        }

        private static bool TryResolveCoordinatorParallelismOverride(
            ThumbnailWorkerSettingsSnapshot snapshot,
            int presetConfiguredParallelism,
            out int configuredParallelism,
            out int coordinatorNormalParallelism,
            out int coordinatorIdleParallelism
        )
        {
            configuredParallelism = presetConfiguredParallelism;
            coordinatorNormalParallelism = 0;
            coordinatorIdleParallelism = 0;
            if (snapshot == null)
            {
                return false;
            }

            int normalOverride = snapshot.CoordinatorNormalParallelismOverride;
            int idleOverride = snapshot.CoordinatorIdleParallelismOverride;
            if (normalOverride < 1 || idleOverride < 1)
            {
                return false;
            }

            int overrideTotal = normalOverride + idleOverride;
            if (overrideTotal < HardMinParallelism || overrideTotal > HardMaxParallelism)
            {
                return false;
            }

            configuredParallelism = overrideTotal;
            coordinatorNormalParallelism = normalOverride;
            coordinatorIdleParallelism = idleOverride;
            return true;
        }

        private static int ClampParallelism(int parallelism)
        {
            if (parallelism < HardMinParallelism)
            {
                return HardMinParallelism;
            }

            if (parallelism > HardMaxParallelism)
            {
                return HardMaxParallelism;
            }

            return parallelism;
        }
    }

    /// <summary>
    /// role ごとに Worker が実行へ使う値。
    /// </summary>
    public sealed class ThumbnailWorkerResolvedSettings
    {
        public string MainDbFullPath { get; init; } = "";
        public string DbName { get; init; } = "";
        public string ThumbFolder { get; init; } = "";
        public ThumbnailQueueWorkerRole WorkerRole { get; init; }
        public int ConfiguredTotalParallelism { get; init; } = 1;
        public int MaxParallelism { get; init; } = 1;
        public int DynamicMinimumParallelism { get; init; } = 1;
        public bool AllowDynamicScaleUp { get; init; }
        public int ScaleUpDemandFactor { get; init; } = 2;
        public int PollIntervalMs { get; init; } = 3000;
        public int BatchCooldownMs { get; init; }
        public int LeaseMinutes { get; init; } = 5;
        public int LeaseBatchSize { get; init; } = 1;
        public int SlowLaneMinGb { get; init; } = 50;
        public bool ResizeThumb { get; init; }
        public bool GpuDecodeEnabled { get; init; }
        public string ProcessPriorityName { get; init; } = "BelowNormal";
        public string FfmpegPriorityName { get; init; } = "Idle";
        public string Preset { get; init; } = "";
        public string SettingsVersionToken { get; init; } = "";
    }
}

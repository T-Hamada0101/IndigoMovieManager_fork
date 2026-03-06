namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル負荷プリセットから、現在使う並列数を解決する。
    /// 低負荷モードそのものは別段で実装するため、ここでは数値解決だけを担当する。
    /// </summary>
    public static class ThumbnailThreadPresetResolver
    {
        public const string PresetSlow = "slow";
        public const string PresetNormal = "normal";
        public const string PresetBallence = "ballence";
        public const string PresetFast = "fast";
        public const string PresetMax = "max";
        public const string PresetCustum = "custum";

        private const int HardMinParallelism = 2;
        private const int HardMaxParallelism = 24;
        private const int DefaultDynamicMinimumParallelism = 4;
        private const int BallenceDynamicMinimumParallelism = 3;
        private const int FastDemandScaleUpFactor = 2;
        private const int NormalDemandScaleUpFactor = 3;
        private const int BallenceDemandScaleUpFactor = 4;
        private const int SlowPollIntervalMultiplier = 2;
        private const int SlowBatchCooldownMs = 750;

        // UIで扱う既知の6値へ丸める。未知値は手動設定へ逃がす。
        public static string NormalizePresetKey(string preset)
        {
            string normalized = (preset ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                PresetSlow => PresetSlow,
                PresetNormal => PresetNormal,
                PresetBallence => PresetBallence,
                PresetFast => PresetFast,
                PresetMax => PresetMax,
                PresetCustum => PresetCustum,
                _ => PresetCustum,
            };
        }

        // slow は内部1本固定ではなく、低負荷でゆっくり回すモードとして扱う。
        public static bool IsLowLoadPreset(string preset)
        {
            return string.Equals(
                NormalizePresetKey(preset),
                PresetSlow,
                StringComparison.Ordinal
            );
        }

        // 現時点のプリセットから並列数を解決する。
        public static int ResolveParallelism(string preset, int manualParallelism, int logicalCoreCount)
        {
            string normalizedPreset = NormalizePresetKey(preset);
            int safeLogicalCoreCount = logicalCoreCount < 1 ? 1 : logicalCoreCount;

            return normalizedPreset switch
            {
                PresetSlow => HardMinParallelism,
                PresetNormal => ResolveDividedParallelism(safeLogicalCoreCount, 3),
                PresetBallence => ResolveDividedParallelism(safeLogicalCoreCount, 4),
                PresetFast => ResolveDividedParallelism(safeLogicalCoreCount, 2),
                PresetMax => ClampParallelism(safeLogicalCoreCount),
                _ => ClampParallelism(manualParallelism),
            };
        }

        public static int ClampParallelism(int parallelism)
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

        // slow 時はDBポーリング間隔を伸ばして、常時張り付きの強さを落とす。
        public static int ResolveQueuePollIntervalMs(string preset, int basePollIntervalMs)
        {
            int safeBasePollIntervalMs = basePollIntervalMs < 100 ? 100 : basePollIntervalMs;
            if (!IsLowLoadPreset(preset))
            {
                return safeBasePollIntervalMs;
            }

            long resolved = (long)safeBasePollIntervalMs * SlowPollIntervalMultiplier;
            if (resolved > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)resolved;
        }

        // slow 時だけバッチ完了後に少し待機し、連続高負荷になりにくくする。
        public static int ResolveBatchCooldownMs(string preset)
        {
            return IsLowLoadPreset(preset) ? SlowBatchCooldownMs : 0;
        }

        // 動的並列制御が下げてもよい下限値を、プリセット意図込みで返す。
        public static int ResolveDynamicMinimumParallelism(string preset, int configuredParallelism)
        {
            int boundedConfigured = ClampParallelism(configuredParallelism);
            int resolved = NormalizePresetKey(preset) switch
            {
                PresetSlow => HardMinParallelism,
                PresetBallence => BallenceDynamicMinimumParallelism,
                _ => DefaultDynamicMinimumParallelism,
            };

            if (resolved > boundedConfigured)
            {
                resolved = boundedConfigured;
            }

            return ClampParallelism(resolved);
        }

        // slow は低負荷優先のため、動的復帰で積極的に上げない。
        public static bool ResolveAllowDynamicScaleUp(string preset)
        {
            return !IsLowLoadPreset(preset);
        }

        // 需要がどれだけ溜まったら復帰候補にするかを、プリセットごとに変える。
        public static int ResolveScaleUpDemandFactor(string preset)
        {
            return NormalizePresetKey(preset) switch
            {
                PresetBallence => BallenceDemandScaleUpFactor,
                PresetNormal => NormalDemandScaleUpFactor,
                _ => FastDemandScaleUpFactor,
            };
        }

        // 論理コア数を分母で割った値を、安全範囲へ丸めて返す。
        private static int ResolveDividedParallelism(int logicalCoreCount, int divisor)
        {
            int safeDivisor = divisor < 1 ? 1 : divisor;
            int resolved = logicalCoreCount / safeDivisor;
            return ClampParallelism(resolved);
        }
    }
}

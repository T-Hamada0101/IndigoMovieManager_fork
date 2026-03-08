namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Worker と fallback consumer が同じ環境変数で実行設定を共有する。
    /// </summary>
    public static class ThumbnailWorkerExecutionEnvironment
    {
        public const string ProcessPriorityEnvName = "IMM_THUMB_PROCESS_PRIORITY";
        public const string FfmpegPriorityEnvName = "IMM_THUMB_FFMPEG_PRIORITY";
        public const string SlowLaneMinGbEnvName = "IMM_THUMB_SLOW_LANE_MIN_GB";
        public const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";

        public static void Apply(ThumbnailWorkerResolvedSettings resolvedSettings, Action<string> log = null)
        {
            if (resolvedSettings == null)
            {
                throw new ArgumentNullException(nameof(resolvedSettings));
            }

            Environment.SetEnvironmentVariable(
                ProcessPriorityEnvName,
                string.IsNullOrWhiteSpace(resolvedSettings.ProcessPriorityName)
                    ? "BelowNormal"
                    : resolvedSettings.ProcessPriorityName
            );
            Environment.SetEnvironmentVariable(
                FfmpegPriorityEnvName,
                string.IsNullOrWhiteSpace(resolvedSettings.FfmpegPriorityName)
                    ? "Idle"
                    : resolvedSettings.FfmpegPriorityName
            );
            Environment.SetEnvironmentVariable(
                SlowLaneMinGbEnvName,
                Math.Max(1, resolvedSettings.SlowLaneMinGb).ToString()
            );

            string gpuMode = ResolveGpuDecodeMode(resolvedSettings.GpuDecodeEnabled);
            Environment.SetEnvironmentVariable(GpuDecodeModeEnvName, gpuMode);
            log?.Invoke($"worker environment applied: gpu={gpuMode} slow_lane_gb={resolvedSettings.SlowLaneMinGb} process={resolvedSettings.ProcessPriorityName} ffmpeg={resolvedSettings.FfmpegPriorityName}");
        }

        // UI が事前に固定したGPUモードを尊重しつつ、OFFだけは必ず強制する。
        internal static string ResolveGpuDecodeMode(bool gpuDecodeEnabled)
        {
            if (!gpuDecodeEnabled)
            {
                return "off";
            }

            string inherited = NormalizeGpuDecodeMode(
                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim()
            );
            return inherited is "cuda" or "qsv" or "amd" ? inherited : "auto";
        }

        private static string NormalizeGpuDecodeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "";
            }

            return mode.Trim().ToLowerInvariant() switch
            {
                "cuda" => "cuda",
                "qsv" => "qsv",
                "qvc" => "qsv",
                "amd" => "amd",
                "amf" => "amd",
                "off" => "off",
                "auto" => "auto",
                _ => "",
            };
        }
    }
}

using System.Diagnostics;
using System.Text.RegularExpressions;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class ThumbnailEngineBenchTests
{
    private const string BenchInputEnvName = "IMM_BENCH_INPUT";
    private const string BenchEnginesEnvName = "IMM_BENCH_ENGINES";
    private const string BenchIterationEnvName = "IMM_BENCH_ITER";
    private const string BenchWarmupEnvName = "IMM_BENCH_WARMUP";
    private const string BenchTabIndexEnvName = "IMM_BENCH_TAB_INDEX";
    private const string BenchPriorityEnvName = "IMM_BENCH_PRIORITY";
    private const string BenchRecoveryEnvName = "IMM_BENCH_RECOVERY";
    private const string ThumbEngineEnvName = "IMM_THUMB_ENGINE";

    [Test]
    public async Task Bench_同一入力でエンジン別比較を実行する()
    {
        string moviePath = Environment.GetEnvironmentVariable(BenchInputEnvName)?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
        {
            Assert.Ignore($"{BenchInputEnvName} に存在する動画ファイルを設定してください。");
            return;
        }

        List<string> engines = ResolveEngineList();
        if (engines.Count < 1)
        {
            Assert.Ignore("比較対象エンジンが0件です。");
            return;
        }
        List<string> canonicalEngines = engines
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int iterationCount = ResolveIterationCount();
        int warmupCount = ResolveWarmupCount();
        int tabIndex = ResolveTabIndex();
        int panelCount = ResolvePanelCount(tabIndex);
        bool isRecovery = ResolveRecoveryMode();
        string requestedPriority = ResolveRequestedPriority();
        string benchRunId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string benchRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_bench",
            benchRunId
        );
        Directory.CreateDirectory(benchRoot);

        List<BenchRow> rows = [];
        var service = new ThumbnailCreationService();
        string oldEngine = Environment.GetEnvironmentVariable(ThumbEngineEnvName) ?? "";
        ProcessPriorityClass? oldPriorityClass = null;
        string priorityApplyError = "";

        try
        {
            oldPriorityClass = TryApplyBenchPriority(out priorityApplyError);

            // 公平比較のため、先に各エンジンをウォームアップして初回初期化コストを計測から除外する。
            for (int w = 1; w <= warmupCount; w++)
            {
                foreach (string engine in canonicalEngines)
                {
                    Environment.SetEnvironmentVariable(ThumbEngineEnvName, engine);
                    _ = await ExecuteSingleBenchAsync(
                        service,
                        moviePath,
                        benchRoot,
                        engine,
                        tabIndex,
                        isRecovery,
                        requestedPriority,
                        priorityApplyError,
                        runId: -w,
                        folderPrefix: "warmup"
                    );
                }
            }

            // 計測順の偏りを減らすため、反復ごとにエンジン順をローテーションする。
            int baseOffset = ResolveBaseOffset(moviePath, canonicalEngines.Count);
            for (int i = 1; i <= iterationCount; i++)
            {
                List<string> orderedEngines = RotateEngineOrder(canonicalEngines, baseOffset + i - 1);
                foreach (string engine in orderedEngines)
                {
                    Environment.SetEnvironmentVariable(ThumbEngineEnvName, engine);
                    rows.Add(
                        await ExecuteSingleBenchAsync(
                            service,
                            moviePath,
                            benchRoot,
                            engine,
                            tabIndex,
                            isRecovery,
                            requestedPriority,
                            priorityApplyError,
                            runId: i,
                            folderPrefix: "measure"
                        )
                    );
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThumbEngineEnvName, oldEngine);
            RestoreBenchPriority(oldPriorityClass);
        }

        string csvPath = WriteCsv(rows, benchRunId, moviePath);
        TestContext.Out.WriteLine($"bench input={moviePath}");
        TestContext.Out.WriteLine($"bench csv={csvPath}");
        TestContext.Out.WriteLine($"bench rows={rows.Count}");
        TestContext.Out.WriteLine($"bench tab_index={tabIndex} panel_count={panelCount}");
        TestContext.Out.WriteLine(
            $"bench requested_priority={requestedPriority} actual_priority={ResolveCurrentPriorityLabel()} apply_error={priorityApplyError} recovery={isRecovery}"
        );
        TestContext.Out.WriteLine(
            $"bench fairness=canonical_order+offset_rotation warmup={warmupCount} canonical={string.Join(",", canonicalEngines)}"
        );

        Assert.That(rows.Count, Is.EqualTo(canonicalEngines.Count * iterationCount));
    }

    private static async Task<BenchRow> ExecuteSingleBenchAsync(
        ThumbnailCreationService service,
        string moviePath,
        string benchRoot,
        string engine,
        int tabIndex,
        bool isRecovery,
        string requestedPriority,
        string priorityApplyError,
        int runId,
        string folderPrefix
    )
    {
        string runLabel = runId >= 0 ? $"run{runId:00}" : $"warm{Math.Abs(runId):00}";
        string thumbRoot = Path.Combine(benchRoot, engine, folderPrefix, runLabel);
        Directory.CreateDirectory(thumbRoot);

        QueueObj queue = new()
        {
            MovieId = runId,
            Tabindex = tabIndex,
            MovieFullPath = moviePath,
            AttemptCount = isRecovery ? 1 : 0,
        };

        Stopwatch sw = Stopwatch.StartNew();
        ThumbnailCreateResult result = await service.CreateThumbAsync(
            queue,
            dbName: "bench",
            thumbFolder: thumbRoot,
            isResizeThumb: true,
            isManual: false
        );
        sw.Stop();

        long outputBytes = 0;
        if (!string.IsNullOrWhiteSpace(result.SaveThumbFileName))
        {
            try
            {
                if (File.Exists(result.SaveThumbFileName))
                {
                    outputBytes = new FileInfo(result.SaveThumbFileName).Length;
                }
            }
            catch
            {
                outputBytes = 0;
            }
        }

        long inputSizeBytes = 0;
        try
        {
            inputSizeBytes = new FileInfo(moviePath).Length;
        }
        catch
        {
            inputSizeBytes = 0;
        }

        double? bitrateMbps = null;
        if (inputSizeBytes > 0 && result.DurationSec.HasValue && result.DurationSec.Value > 0)
        {
            bitrateMbps = (inputSizeBytes * 8d) / (result.DurationSec.Value * 1_000_000d);
        }

        return new BenchRow(
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            Path.GetFileName(moviePath) ?? "",
            inputSizeBytes,
            bitrateMbps,
            result.DurationSec,
            requestedPriority,
            ResolveCurrentPriorityLabel(),
            priorityApplyError,
            isRecovery,
            ResolveDriveLetter(moviePath),
            engine,
            runId,
            tabIndex,
            ResolvePanelCount(tabIndex),
            sw.ElapsedMilliseconds,
            result.IsSuccess,
            outputBytes,
            result.SaveThumbFileName ?? "",
            result.ErrorMessage ?? ""
        );
    }

    private static List<string> RotateEngineOrder(IReadOnlyList<string> engines, int offset)
    {
        List<string> ordered = [];
        if (engines == null || engines.Count < 1)
        {
            return ordered;
        }

        int count = engines.Count;
        int normalizedOffset = ((offset % count) + count) % count;
        for (int i = 0; i < count; i++)
        {
            ordered.Add(engines[(normalizedOffset + i) % count]);
        }
        return ordered;
    }

    private static int ResolveBaseOffset(string moviePath, int engineCount)
    {
        if (engineCount < 1)
        {
            return 0;
        }

        string key = Path.GetFileName(moviePath)?.ToLowerInvariant() ?? moviePath?.ToLowerInvariant() ?? "";
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < key.Length; i++)
            {
                hash = (hash * 31) + key[i];
            }
            if (hash < 0)
            {
                hash = -hash;
            }
            return hash % engineCount;
        }
    }

    private static List<string> ResolveEngineList()
    {
        string raw = Environment.GetEnvironmentVariable(BenchEnginesEnvName) ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ["autogen", "ffmediatoolkit", "ffmpeg1pass", "opencv"];
        }

        // カンマ区切り/空白区切り/セミコロン区切りを同一ルールで受け付ける。
        string[] parts = Regex.Split(raw, @"[\s,;]+", RegexOptions.CultureInvariant);
        List<string> engines = [];
        foreach (string p in parts)
        {
            string e = p.Trim();
            if (!string.IsNullOrWhiteSpace(e))
            {
                engines.Add(e);
            }
        }

        return engines;
    }

    private static int ResolveIterationCount()
    {
        string raw = Environment.GetEnvironmentVariable(BenchIterationEnvName) ?? "";
        if (
            int.TryParse(raw, out int parsed)
            && parsed >= 1
            && parsed <= 100
        )
        {
            return parsed;
        }
        return 3;
    }

    private static int ResolveWarmupCount()
    {
        // 公平比較の既定値として、各エンジン1回ウォームアップする。
        string raw = Environment.GetEnvironmentVariable(BenchWarmupEnvName) ?? "";
        if (
            int.TryParse(raw, out int parsed)
            && parsed >= 0
            && parsed <= 10
        )
        {
            return parsed;
        }
        return 1;
    }

    private static int ResolveTabIndex()
    {
        // 既定は通常一覧(3x1)。10パネル比較は tab=4 を指定する。
        string raw = Environment.GetEnvironmentVariable(BenchTabIndexEnvName) ?? "";
        if (!int.TryParse(raw, out int parsed))
        {
            return 0;
        }

        return parsed switch
        {
            0 or 1 or 2 or 3 or 4 or 99 => parsed,
            _ => 0,
        };
    }

    private static string ResolveRequestedPriority()
    {
        string raw = Environment.GetEnvironmentVariable(BenchPriorityEnvName) ?? "";
        return string.IsNullOrWhiteSpace(raw) ? "Normal" : raw.Trim();
    }

    private static ProcessPriorityClass? TryApplyBenchPriority(out string errorMessage)
    {
        errorMessage = "";
        string requested = ResolveRequestedPriority();
        if (!Enum.TryParse(requested, ignoreCase: true, out ProcessPriorityClass target))
        {
            errorMessage = $"unsupported:{requested}";
            return null;
        }

        try
        {
            Process current = Process.GetCurrentProcess();
            ProcessPriorityClass original = current.PriorityClass;
            if (original != target)
            {
                current.PriorityClass = target;
            }
            return original;
        }
        catch (Exception ex)
        {
            errorMessage = $"{ex.GetType().Name}:{ex.Message}";
            return null;
        }
    }

    private static void RestoreBenchPriority(ProcessPriorityClass? original)
    {
        if (!original.HasValue)
        {
            return;
        }

        try
        {
            Process current = Process.GetCurrentProcess();
            if (current.PriorityClass != original.Value)
            {
                current.PriorityClass = original.Value;
            }
        }
        catch
        {
            // 優先度復元失敗でもテスト結果を優先する。
        }
    }

    private static bool ResolveRecoveryMode()
    {
        string raw = Environment.GetEnvironmentVariable(BenchRecoveryEnvName) ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        return normalized is "1" or "true" or "on" or "yes";
    }

    private static string ResolveCurrentPriorityLabel()
    {
        try
        {
            return Process.GetCurrentProcess().PriorityClass.ToString();
        }
        catch
        {
            return ResolveRequestedPriority();
        }
    }

    private static string ResolveDriveLetter(string moviePath)
    {
        if (string.IsNullOrWhiteSpace(moviePath))
        {
            return "";
        }

        try
        {
            string root = Path.GetPathRoot(moviePath) ?? "";
            if (string.IsNullOrWhiteSpace(root))
            {
                return "";
            }

            string trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed;
        }
        catch
        {
            return "";
        }
    }

    private static int ResolvePanelCount(int tabIndex)
    {
        TabInfo tabInfo = new(tabIndex, "bench");
        return tabInfo.DivCount;
    }

    private static string WriteCsv(List<BenchRow> rows, string benchRunId, string moviePath)
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndigoMovieManager_fork",
            "logs"
        );
        Directory.CreateDirectory(logDir);

        string csvPath = Path.Combine(logDir, $"thumbnail-engine-bench-{benchRunId}.csv");
        using StreamWriter writer = new(csvPath, append: false, new System.Text.UTF8Encoding(false));
        writer.WriteLine(
            "datetime,input_file_name,input_size_bytes,bitrate_mbps,play_time_sec,requested_priority,priority,priority_apply_error,is_recovery,drive_letter,engine,iteration,tab_index,panel_count,elapsed_ms,success,output_bytes,output_path,error_message"
        );
        foreach (BenchRow row in rows)
        {
            writer.WriteLine(
                string.Join(
                    ",",
                    EscapeCsvValue(row.DateTimeText),
                    EscapeCsvValue(row.InputFileName),
                    EscapeCsvValue(row.InputSizeBytes.ToString()),
                    EscapeCsvValue(
                        row.BitrateMbps.HasValue
                            ? row.BitrateMbps.Value.ToString(
                                "0.###",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                            : ""
                    ),
                    EscapeCsvValue(
                        row.PlayTimeSec.HasValue
                            ? row.PlayTimeSec.Value.ToString(
                                "0.###",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                            : ""
                    ),
                    EscapeCsvValue(row.RequestedPriority),
                    EscapeCsvValue(row.Priority),
                    EscapeCsvValue(row.PriorityApplyError),
                    EscapeCsvValue(row.IsRecovery ? "1" : "0"),
                    EscapeCsvValue(row.DriveLetter),
                    EscapeCsvValue(row.Engine),
                    EscapeCsvValue(row.Iteration.ToString()),
                    EscapeCsvValue(row.TabIndex.ToString()),
                    EscapeCsvValue(row.PanelCount.ToString()),
                    EscapeCsvValue(row.ElapsedMs.ToString()),
                    EscapeCsvValue(row.IsSuccess ? "success" : "failed"),
                    EscapeCsvValue(row.OutputBytes.ToString()),
                    EscapeCsvValue(row.OutputPath),
                    EscapeCsvValue(row.ErrorMessage)
                )
            );
        }
        return csvPath;
    }

    private static string EscapeCsvValue(string value)
    {
        value ??= "";
        if (
            !value.Contains(',')
            && !value.Contains('"')
            && !value.Contains('\n')
            && !value.Contains('\r')
        )
        {
            return value;
        }
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private readonly record struct BenchRow(
        string DateTimeText,
        string InputFileName,
        long InputSizeBytes,
        double? BitrateMbps,
        double? PlayTimeSec,
        string RequestedPriority,
        string Priority,
        string PriorityApplyError,
        bool IsRecovery,
        string DriveLetter,
        string Engine,
        int Iteration,
        int TabIndex,
        int PanelCount,
        long ElapsedMs,
        bool IsSuccess,
        long OutputBytes,
        string OutputPath,
        string ErrorMessage
    );
}

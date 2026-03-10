using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class FfmpegShortClipRecoveryPlaygroundTests
{
    private static readonly HashSet<string> IndexRepairTargetExtensions = new(
        [".mp4", ".m4v", ".3gp", ".3g2", ".mov", ".avi", ".divx", ".mkv", ".flv", ".f4v", ".wmv", ".asf", ".mts", ".m2ts"],
        StringComparer.OrdinalIgnoreCase
    );

    private const string DefaultMoviePath = @"E:\_サムネイル作成困難動画\画像1枚ありページ.mkv";
    private const string MoviePathEnvName = "IMM_TEST_FFMPEG_SHORT_MOVIE_PATH";

    [Test]
    [Explicit("実動画パス依存。IMM_TEST_FFMPEG_SHORT_MOVIE_PATH で対象動画を差し替え可能。")]
    public async Task 実動画パスで_ffmpeg短尺救済_成功候補を順に試せる()
    {
        string moviePath = ResolveMoviePath();
        if (!File.Exists(moviePath))
        {
            Assert.Ignore($"対象動画が見つかりません: {moviePath}");
        }

        string tempRoot = CreateTempRoot();
        string mainDbPath = Path.Combine(tempRoot, "ffmpeg-short-clip-playground.wb");
        try
        {
            ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
            List<FfmpegAttemptResult> results = [];
            TestContext.Out.WriteLine($"movie={moviePath}");
            TestContext.Out.WriteLine($"work={tempRoot}");
            TestContext.Out.WriteLine($"failure_db={failureDbService.FailureDbFullPath}");
            TestContext.Out.WriteLine($"ffmpeg_exe={ResolveFfmpegExecutablePath()}");

            // まずはメタ情報差分を出し、短尺動画の duration 候補を把握する。
            MovieInfoMetadataProbeSet metadataProbe = MovieInfo.ProbeMetadataSources(moviePath);
            WriteMetadataProbe(metadataProbe);

            await RunOnePassAttemptSetAsync(
                moviePath,
                tempRoot,
                "original",
                durationOverrideSec: null,
                failureDbService,
                results
            );
            await RunDurationOverrideAttemptsAsync(
                moviePath,
                metadataProbe,
                tempRoot,
                failureDbService,
                results
            );
            await RunCliAttemptSetAsync(
                moviePath,
                tempRoot,
                "original",
                ResolveRepresentativeDurationSec(metadataProbe),
                failureDbService,
                results
            );

            // repair 後の入力でも同じ ffmpeg 系試行を流し、成功条件の差分を見えるようにする。
            ThumbnailRepairWorkflowCoordinator repairCoordinator = new(
                new AppVideoMetadataProvider(),
                new VideoIndexRepairService()
            );
            await RunPreparedRepairAttemptSetAsync(
                moviePath,
                tempRoot,
                repairCoordinator,
                failureDbService,
                results
            );

            int successCount = results.Count(x => x.IsSuccess);
            TestContext.Out.WriteLine(
                $"ffmpeg_results total={results.Count} success={successCount} failure_db_records={failureDbService.GetFailureRecords().Count}"
            );

            Assert.That(results.Count, Is.GreaterThan(0));
            Assert.That(successCount, Is.GreaterThan(0), "どの ffmpeg 試行でも成功しなかった");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string ResolveMoviePath()
    {
        string configuredPath = Environment.GetEnvironmentVariable(MoviePathEnvName)?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(configuredPath) ? DefaultMoviePath : configuredPath;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            "ffmpeg_short_clip_playground",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteMetadataProbe(MovieInfoMetadataProbeSet metadataProbe)
    {
        TestContext.Out.WriteLine("metadata probe:");
        foreach (string line in metadataProbe.ToDebugLines())
        {
            TestContext.Out.WriteLine($"  {line}");
        }
    }

    private async Task RunDurationOverrideAttemptsAsync(
        string moviePath,
        MovieInfoMetadataProbeSet metadataProbe,
        string tempRoot,
        ThumbnailFailureDebugDbService failureDbService,
        List<FfmpegAttemptResult> results
    )
    {
        foreach (DurationOverrideCase durationCase in BuildDurationOverrideCases(metadataProbe))
        {
            await RunOnePassAttemptSetAsync(
                moviePath,
                tempRoot,
                durationCase.Name,
                durationCase.DurationSec,
                failureDbService,
                results
            );
        }
    }

    private async Task RunPreparedRepairAttemptSetAsync(
        string moviePath,
        string tempRoot,
        ThumbnailRepairWorkflowCoordinator repairCoordinator,
        ThumbnailFailureDebugDbService failureDbService,
        List<FfmpegAttemptResult> results
    )
    {
        string repairedMovieTempPath = "";
        try
        {
            ThumbnailRepairPreparationResult preparationResult =
                await repairCoordinator.PrepareWorkingMovieAsync(
                    moviePath,
                    isRecoveryLane: true,
                    IsIndexRepairTargetMovie(moviePath),
                    CancellationToken.None
                );
            repairedMovieTempPath = preparationResult.RepairedMovieTempPath;
            TestContext.Out.WriteLine(
                "prepare repair: "
                    + $"repaired_by_probe={preparationResult.RepairedByProbe} "
                    + $"working='{preparationResult.WorkingMovieFullPath}' "
                    + $"temp='{preparationResult.RepairedMovieTempPath}'"
            );

            if (
                !preparationResult.RepairedByProbe
                || string.IsNullOrWhiteSpace(preparationResult.WorkingMovieFullPath)
            )
            {
                return;
            }

            MovieInfoMetadataProbeSet repairedProbe = MovieInfo.ProbeMetadataSources(
                preparationResult.WorkingMovieFullPath
            );
            WriteMetadataProbe(repairedProbe);

            await RunOnePassAttemptSetAsync(
                preparationResult.WorkingMovieFullPath,
                tempRoot,
                "prepared-repair",
                durationOverrideSec: null,
                failureDbService,
                results
            );
            await RunCliAttemptSetAsync(
                preparationResult.WorkingMovieFullPath,
                tempRoot,
                "prepared-repair",
                ResolveRepresentativeDurationSec(repairedProbe),
                failureDbService,
                results
            );
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine(
                $"prepare repair exception: {ex.GetType().Name}: {ex.Message}"
            );
        }
        finally
        {
            TryDeleteFileQuietly(repairedMovieTempPath);
        }
    }

    private async Task RunOnePassAttemptSetAsync(
        string moviePath,
        string tempRoot,
        string attemptGroupName,
        double? durationOverrideSec,
        ThumbnailFailureDebugDbService failureDbService,
        List<FfmpegAttemptResult> results
    )
    {
        foreach ((string suffix, int tabIndex) in new[] { ("1x1", 99), ("5x1", 3) })
        {
            FfmpegAttemptResult result = await RunOnePassAttemptAsync(
                moviePath,
                tempRoot,
                $"{attemptGroupName}-{suffix}",
                tabIndex,
                durationOverrideSec
            );
            WriteAttemptResult(result);
            AppendAttemptRecord(failureDbService, result);
            results.Add(result);
        }
    }

    private async Task RunCliAttemptSetAsync(
        string moviePath,
        string tempRoot,
        string attemptGroupName,
        double? durationSec,
        ThumbnailFailureDebugDbService failureDbService,
        List<FfmpegAttemptResult> results
    )
    {
        foreach (double seekSec in BuildShortSeekCandidates(durationSec))
        {
            foreach (bool isPostInputSeek in new[] { false, true })
            {
                string seekKind = isPostInputSeek ? "postseek" : "preseek";
                FfmpegAttemptResult result = await RunCliSingleFrameAttemptAsync(
                    moviePath,
                    tempRoot,
                    $"{attemptGroupName}-cli-{seekKind}-{seekSec.ToString("0.###", CultureInfo.InvariantCulture)}",
                    seekSec,
                    isPostInputSeek
                );
                WriteAttemptResult(result);
                AppendAttemptRecord(failureDbService, result);
                results.Add(result);
            }
        }
    }

    private async Task<FfmpegAttemptResult> RunOnePassAttemptAsync(
        string moviePath,
        string tempRoot,
        string attemptName,
        int tabIndex,
        double? durationOverrideSec
    )
    {
        string attemptDir = Path.Combine(tempRoot, SanitizeName(attemptName));
        Directory.CreateDirectory(attemptDir);
        string outputPath = Path.Combine(attemptDir, "ffmpeg1pass.jpg");

        try
        {
            FileInfo fileInfo = new(moviePath);
            QueueObj queueObj = new()
            {
                MovieId = 1,
                Tabindex = tabIndex,
                MovieFullPath = moviePath,
                AttemptCount = 1,
            };
            TabInfo tabInfo = new(tabIndex, "ffmpeg-short-playground", attemptDir);
            ThumbnailJobMaterialBuilder materialBuilder = new(new AppVideoMetadataProvider());
            ThumbnailJobMaterialBuildResult material = materialBuilder.Build(
                new ThumbnailJobMaterialBuildRequest
                {
                    QueueObj = queueObj,
                    TabInfo = tabInfo,
                    WorkingMovieFullPath = moviePath,
                    SaveThumbFileName = outputPath,
                    IsManual = false,
                    DurationSec = durationOverrideSec,
                    FileSizeBytes = fileInfo.Length,
                }
            );

            if (!material.IsSuccess || material.ThumbInfo == null)
            {
                return FfmpegAttemptResult.Failed(
                    attemptName,
                    "ffmpeg1pass",
                    moviePath,
                    outputPath,
                    durationOverrideSec,
                    material.DurationSec,
                    null,
                    $"material build failed: {material.ErrorMessage}"
                );
            }

            ThumbnailJobContext context = ThumbnailJobContextFactory.Create(
                queueObj,
                tabInfo,
                material.ThumbInfo,
                moviePath,
                outputPath,
                isResizeThumb: true,
                isManual: false,
                material.DurationSec,
                fileInfo.Length,
                material.AverageBitrateMbps,
                material.VideoCodec
            );

            FfmpegOnePassThumbnailGenerationEngine engine = new();
            ThumbnailCreateResult result = await engine.CreateAsync(context);

            return new FfmpegAttemptResult(
                attemptName,
                "ffmpeg1pass",
                moviePath,
                outputPath,
                durationOverrideSec,
                material.DurationSec,
                null,
                result.IsSuccess,
                result.ErrorMessage ?? "",
                Path.Exists(outputPath)
            );
        }
        catch (Exception ex)
        {
            return FfmpegAttemptResult.Failed(
                attemptName,
                "ffmpeg1pass",
                moviePath,
                outputPath,
                durationOverrideSec,
                null,
                null,
                $"{ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private static async Task<FfmpegAttemptResult> RunCliSingleFrameAttemptAsync(
        string moviePath,
        string tempRoot,
        string attemptName,
        double seekSec,
        bool isPostInputSeek
    )
    {
        string attemptDir = Path.Combine(tempRoot, SanitizeName(attemptName));
        Directory.CreateDirectory(attemptDir);
        string outputPath = Path.Combine(attemptDir, "ffmpeg-cli.jpg");

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = ResolveFfmpegExecutablePath(),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            string seekText = seekSec.ToString("0.###", CultureInfo.InvariantCulture);
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-sn");
            psi.ArgumentList.Add("-dn");

            if (isPostInputSeek)
            {
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(moviePath);
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(seekText);
            }
            else
            {
                FfmpegOnePassThumbnailGenerationEngine.AddInputArguments(
                    psi,
                    moviePath,
                    seekText,
                    useTolerantInput: false
                );
            }

            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-strict");
            psi.ArgumentList.Add("unofficial");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv420p");
            psi.ArgumentList.Add("-q:v");
            psi.ArgumentList.Add("5");
            psi.ArgumentList.Add(outputPath);

            (bool ok, string err) = await RunProcessAsync(psi, TimeSpan.FromSeconds(20));
            return new FfmpegAttemptResult(
                attemptName,
                isPostInputSeek ? "ffmpeg-cli-postseek" : "ffmpeg-cli-preseek",
                moviePath,
                outputPath,
                null,
                null,
                seekSec,
                ok && Path.Exists(outputPath),
                err,
                Path.Exists(outputPath)
            );
        }
        catch (Exception ex)
        {
            return FfmpegAttemptResult.Failed(
                attemptName,
                isPostInputSeek ? "ffmpeg-cli-postseek" : "ffmpeg-cli-preseek",
                moviePath,
                outputPath,
                null,
                null,
                seekSec,
                $"{ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private static IEnumerable<DurationOverrideCase> BuildDurationOverrideCases(
        MovieInfoMetadataProbeSet metadataProbe
    )
    {
        if (metadataProbe.FfMediaToolkit.IsSuccess && metadataProbe.FfMediaToolkit.DurationSec > 0)
        {
            yield return new DurationOverrideCase(
                "duration-ffmediatoolkit",
                metadataProbe.FfMediaToolkit.DurationSec
            );
        }

        if (metadataProbe.OpenCv.IsSuccess && metadataProbe.OpenCv.DurationSec > 0)
        {
            yield return new DurationOverrideCase(
                "duration-opencv",
                metadataProbe.OpenCv.DurationSec
            );
        }

        if (metadataProbe.AutoGen.IsSuccess && metadataProbe.AutoGen.DurationSec > 0)
        {
            yield return new DurationOverrideCase(
                "duration-autogen",
                metadataProbe.AutoGen.DurationSec
            );
        }
    }

    private static double? ResolveRepresentativeDurationSec(MovieInfoMetadataProbeSet metadataProbe)
    {
        if (metadataProbe.FfMediaToolkit.IsSuccess && metadataProbe.FfMediaToolkit.DurationSec > 0)
        {
            return metadataProbe.FfMediaToolkit.DurationSec;
        }

        if (metadataProbe.OpenCv.IsSuccess && metadataProbe.OpenCv.DurationSec > 0)
        {
            return metadataProbe.OpenCv.DurationSec;
        }

        if (metadataProbe.AutoGen.IsSuccess && metadataProbe.AutoGen.DurationSec > 0)
        {
            return metadataProbe.AutoGen.DurationSec;
        }

        return null;
    }

    private static IReadOnlyList<double> BuildShortSeekCandidates(double? durationSec)
    {
        List<double> raw =
        [
            0,
            0.001,
            0.005,
            0.01,
            0.016,
            0.033,
            0.05,
            0.069,
            0.1,
            0.25,
            0.5,
        ];

        if (durationSec.HasValue && durationSec.Value > 0)
        {
            raw.Add(durationSec.Value * 0.1d);
            raw.Add(durationSec.Value * 0.25d);
            raw.Add(durationSec.Value * 0.5d);
            raw.Add(Math.Max(0, durationSec.Value - 0.01d));
        }

        SortedDictionary<string, double> normalized = new(StringComparer.Ordinal);
        foreach (double candidate in raw)
        {
            double? clamped = ClampCandidate(candidate, durationSec);
            if (!clamped.HasValue)
            {
                continue;
            }

            string key = clamped.Value.ToString("0.###", CultureInfo.InvariantCulture);
            normalized[key] = clamped.Value;
        }

        return normalized.Values.ToList();
    }

    private static double? ClampCandidate(double candidate, double? durationSec)
    {
        if (candidate < 0)
        {
            return null;
        }

        if (!durationSec.HasValue || durationSec.Value <= 0)
        {
            return candidate;
        }

        if (candidate == 0)
        {
            return 0;
        }

        double maxSeek = Math.Max(0, durationSec.Value - 0.001d);
        if (maxSeek <= 0)
        {
            return 0;
        }

        if (candidate > maxSeek)
        {
            return null;
        }

        return candidate;
    }

    private static async Task<(bool ok, string err)> RunProcessAsync(
        ProcessStartInfo psi,
        TimeSpan timeout
    )
    {
        try
        {
            using Process process = new() { StartInfo = psi };
            if (!process.Start())
            {
                return (false, "process start returned false");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            Task waitForExitTask = process.WaitForExitAsync();
            Task completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout));
            if (completedTask != waitForExitTask)
            {
                TryKillProcess(process);
                _ = await stdoutTask;
                string timeoutErr = await SafeReadProcessErrorAsync(stderrTask);
                return (
                    false,
                    string.IsNullOrWhiteSpace(timeoutErr)
                        ? $"ffmpeg timeout after {timeout.TotalSeconds:0}s"
                        : $"ffmpeg timeout after {timeout.TotalSeconds:0}s, err={timeoutErr}"
                );
            }

            await waitForExitTask;
            _ = await stdoutTask;
            string stderr = await SafeReadProcessErrorAsync(stderrTask);
            if (process.ExitCode != 0)
            {
                return (false, $"exit={process.ExitCode}, err={stderr}");
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<string> SafeReadProcessErrorAsync(Task<string> stderrTask)
    {
        try
        {
            return await stderrTask;
        }
        catch
        {
            return "";
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 試験ハーネスなので、停止失敗では落とさない。
        }
    }

    private static string ResolveFfmpegExecutablePath()
    {
        string configuredPath = Environment.GetEnvironmentVariable("IMM_FFMPEG_EXE_PATH") ?? "";
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string normalizedPath = configuredPath.Trim().Trim('"');
            if (File.Exists(normalizedPath))
            {
                return normalizedPath;
            }

            if (Directory.Exists(normalizedPath))
            {
                string candidate = Path.Combine(normalizedPath, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string baseDir = AppContext.BaseDirectory;
        string[] bundledCandidates =
        [
            Path.Combine(baseDir, "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
            Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
        ];

        foreach (string candidate in bundledCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "ffmpeg";
    }

    private static void WriteAttemptResult(FfmpegAttemptResult result)
    {
        TestContext.Out.WriteLine(
            "ffmpeg attempt: "
                + $"name={result.AttemptName} "
                + $"engine={result.EngineId} "
                + $"success={result.IsSuccess} "
                + $"output_exists={result.OutputExists} "
                + $"override_sec={FormatNullable(result.DurationOverrideSec)} "
                + $"material_sec={FormatNullable(result.MaterialDurationSec)} "
                + $"seek_sec={FormatNullable(result.SeekSec)} "
                + $"output='{result.OutputPath}' "
                + $"error='{result.ErrorMessage}'"
        );
    }

    private static string FormatNullable(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "";
    }

    private static void AppendAttemptRecord(
        ThumbnailFailureDebugDbService failureDbService,
        FfmpegAttemptResult result
    )
    {
        if (failureDbService == null || result == null)
        {
            return;
        }

        _ = failureDbService.InsertFailureRecord(
            new ThumbnailFailureRecord
            {
                MoviePath = result.MoviePath,
                PanelType = "ffmpeg-short-playground",
                MovieSizeBytes = TryReadMovieSize(result.MoviePath),
                Duration = result.MaterialDurationSec,
                Reason = result.AttemptName,
                FailureKind = result.IsSuccess
                    ? ThumbnailFailureKind.None
                    : ResolveFailureKind(result.ErrorMessage, result.MaterialDurationSec),
                AttemptCount = 0,
                OccurredAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                TabIndex = 99,
                WorkerRole = "explicit-test",
                EngineId = result.EngineId,
                QueueStatus = result.IsSuccess ? "Done" : "Failed",
                LastError = result.ErrorMessage,
                ExtraJson = JsonSerializer.Serialize(
                    new
                    {
                        result.AttemptName,
                        result.EngineId,
                        result.IsSuccess,
                        result.DurationOverrideSec,
                        result.MaterialDurationSec,
                        result.SeekSec,
                        result.OutputPath,
                        failure_kind_source = "playground",
                        material_duration_sec = result.MaterialDurationSec,
                        thumb_sec = result.SeekSec,
                        engine_attempted = result.EngineId,
                        engine_succeeded = result.IsSuccess,
                        seek_strategy = ResolveSeekStrategy(result.AttemptName),
                        seek_sec = result.SeekSec,
                        repair_attempted =
                            result.AttemptName.Contains("repair", StringComparison.OrdinalIgnoreCase),
                        repair_succeeded =
                            result.IsSuccess
                            && result.AttemptName.Contains("repair", StringComparison.OrdinalIgnoreCase),
                        preflight_branch = "none",
                        result_signature = ResolveResultSignature(result.ErrorMessage),
                        repro_confirmed = false,
                        recovery_route = ResolveRecoveryRoute(result.AttemptName, result.EngineId),
                        decision_basis = result.AttemptName,
                    }
                ),
            }
        );
    }

    private static long TryReadMovieSize(string moviePath)
    {
        try
        {
            return File.Exists(moviePath) ? new FileInfo(moviePath).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static ThumbnailFailureKind ResolveFailureKind(
        string errorMessage,
        double? materialDurationSec
    )
    {
        string text = (errorMessage ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ThumbnailFailureKind.Unknown;
        }

        if (text.Contains("no frames decoded"))
        {
            if (
                materialDurationSec.HasValue
                && materialDurationSec.Value > 0
                && materialDurationSec.Value <= 1.0
            )
            {
                return ThumbnailFailureKind.ShortClipStillLike;
            }

            return ThumbnailFailureKind.TransientDecodeFailure;
        }

        if (text.Contains("invalid data") || text.Contains("stream info") || text.Contains("index"))
        {
            return ThumbnailFailureKind.IndexCorruption;
        }

        if (text.Contains("video stream") || text.Contains("no video"))
        {
            return ThumbnailFailureKind.NoVideoStream;
        }

        if (text.Contains("access denied") || text.Contains("being used"))
        {
            return ThumbnailFailureKind.FileLocked;
        }

        if (text.Contains("not found"))
        {
            return ThumbnailFailureKind.FileMissing;
        }

        if (text.Contains("eof") || text.Contains("end of file"))
        {
            return ThumbnailFailureKind.PhysicalCorruption;
        }

        return ThumbnailFailureKind.Unknown;
    }

    private static string ResolveResultSignature(string errorMessage)
    {
        string text = (errorMessage ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "success";
        }

        if (text.Contains("no frames decoded"))
        {
            return "no-frames-decoded";
        }

        if (text.Contains("near-black"))
        {
            return "near-black";
        }

        if (text.Contains("eof") || text.Contains("end of file"))
        {
            return "eof";
        }

        if (text.Contains("invalid data"))
        {
            return "invalid-data";
        }

        if (text.Contains("video stream") || text.Contains("no video"))
        {
            return "no-video-stream";
        }

        return "unknown";
    }

    private static string ResolveSeekStrategy(string attemptName)
    {
        string lower = (attemptName ?? "").ToLowerInvariant();
        if (lower.Contains("mid") || lower.Contains("1200"))
        {
            return "midpoint";
        }

        if (lower.Contains("preseek"))
        {
            return "preseek";
        }

        if (lower.Contains("postseek"))
        {
            return "postseek";
        }

        return "original";
    }

    private static string ResolveRecoveryRoute(string attemptName, string engineId)
    {
        if (attemptName.Contains("repair", StringComparison.OrdinalIgnoreCase))
        {
            return "repair";
        }

        return string.Equals(engineId, "ffmpeg1pass", StringComparison.OrdinalIgnoreCase)
            ? "ffmpeg1pass"
            : "retry";
    }

    private static string SanitizeName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = value;
        foreach (char invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized;
    }

    private static void TryDeleteFileQuietly(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 試験ハーネスなので、後片付け失敗では止めない。
        }
    }

    private static bool IsIndexRepairTargetMovie(string moviePath)
    {
        if (string.IsNullOrWhiteSpace(moviePath))
        {
            return false;
        }

        string ext = Path.GetExtension(moviePath)?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(ext) && IndexRepairTargetExtensions.Contains(ext);
    }

    private sealed record DurationOverrideCase(string Name, double DurationSec);

    private sealed record FfmpegAttemptResult(
        string AttemptName,
        string EngineId,
        string MoviePath,
        string OutputPath,
        double? DurationOverrideSec,
        double? MaterialDurationSec,
        double? SeekSec,
        bool IsSuccess,
        string ErrorMessage,
        bool OutputExists
    )
    {
        public static FfmpegAttemptResult Failed(
            string attemptName,
            string engineId,
            string moviePath,
            string outputPath,
            double? durationOverrideSec,
            double? materialDurationSec,
            double? seekSec,
            string errorMessage
        )
        {
            return new FfmpegAttemptResult(
                attemptName,
                engineId,
                moviePath,
                outputPath,
                durationOverrideSec,
                materialDurationSec,
                seekSec,
                false,
                errorMessage,
                Path.Exists(outputPath)
            );
        }
    }
}

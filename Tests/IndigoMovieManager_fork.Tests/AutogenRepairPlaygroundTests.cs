using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;
using System.Text.Json;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class AutogenRepairPlaygroundTests
{
    private static readonly HashSet<string> IndexRepairTargetExtensions = new(
        [".mp4", ".m4v", ".3gp", ".3g2", ".mov", ".avi", ".divx", ".mkv", ".flv", ".f4v", ".wmv", ".asf", ".mts", ".m2ts"],
        StringComparer.OrdinalIgnoreCase
    );
    private const string DefaultMoviePath = @"E:\_サムネイル作成困難動画\画像1枚あり顔.mkv";
    private const string MoviePathEnvName = "IMM_TEST_AUTOGEN_MOVIE_PATH";
    private const string DefaultSeekMoviePath = @"E:\_サムネイル作成困難動画\作成1ショットOK\35967.mp4";
    private const string SeekMoviePathEnvName = "IMM_TEST_AUTOGEN_SEEK_MOVIE_PATH";
    private const string SeekSecEnvName = "IMM_TEST_AUTOGEN_SEEK_SEC";

    [Test]
    [Explicit("実動画パス依存。IMM_TEST_AUTOGEN_MOVIE_PATH で対象動画を差し替え可能。")]
    public async Task 実動画パスで_autogen_各種修復処理を順に試せる()
    {
        string moviePath = ResolveMoviePath();
        if (!File.Exists(moviePath))
        {
            Assert.Ignore($"対象動画が見つかりません: {moviePath}");
        }

        string tempRoot = CreateTempRoot();
        string mainDbPath = Path.Combine(tempRoot, "autogen-playground.wb");
        try
        {
            ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
            TestContext.Out.WriteLine($"movie={moviePath}");
            TestContext.Out.WriteLine($"work={tempRoot}");
            TestContext.Out.WriteLine($"failure_db={failureDbService.FailureDbFullPath}");
            TestContext.Out.WriteLine($"index_repair_target={IsIndexRepairTargetMovie(moviePath)}");

            // まずは3系統のメタ情報差分を出し、duration混入源を切り分ける。
            MovieInfoMetadataProbeSet metadataProbe = MovieInfo.ProbeMetadataSources(moviePath);
            WriteMetadataProbe(metadataProbe);

            // 原本を autogen 1x1 で試す。短尺や先頭救済の確認をしやすくする。
            AutogenAttemptResult originalResult = await RunAutogenAttemptAsync(
                moviePath,
                tempRoot,
                attemptName: "original",
                durationOverrideSec: null
            );
            WriteAttemptResult(originalResult);
            AppendAttemptRecord(failureDbService, originalResult);

            // autogen 内部の先頭 fallback 候補を、その動画尺でどう組み立てるか確認できるようにする。
            await RunSeekObservationAttemptAsync(moviePath, metadataProbe, tempRoot, failureDbService);

            // 尺の候補を差し替えて、ThumbSecの作り方だけで結果が変わるか確認できるようにする。
            await RunDurationOverrideAttemptsAsync(moviePath, metadataProbe, tempRoot, failureDbService);

            // 直接repairに加え、recoveryレーン相当の入口でも同じ動画を試せるようにする。
            ThumbnailRepairWorkflowCoordinator repairCoordinator = new(
                new AppVideoMetadataProvider(),
                new VideoIndexRepairService()
            );
            await RunDirectRepairAttemptAsync(moviePath, tempRoot, failureDbService);
            await RunPrepareRepairAttemptAsync(moviePath, tempRoot, repairCoordinator, failureDbService);
            await RunForcedRepairAttemptAsync(
                moviePath,
                tempRoot,
                repairCoordinator,
                originalResult,
                failureDbService
            );

            TestContext.Out.WriteLine(
                $"failure_db_records={failureDbService.GetFailureRecords().Count}"
            );

            Assert.That(originalResult.Attempted, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    [Explicit("実動画パス依存。IMM_TEST_AUTOGEN_SEEK_MOVIE_PATH と IMM_TEST_AUTOGEN_SEEK_SEC で差し替え可能。")]
    public async Task 実動画パスで_autogen_指定秒seekを直接試せる()
    {
        string moviePath = ResolveSeekMoviePath();
        if (!File.Exists(moviePath))
        {
            Assert.Ignore($"対象動画が見つかりません: {moviePath}");
        }

        int seekSec = ResolveSeekSec();
        string tempRoot = CreateTempRoot();
        string mainDbPath = Path.Combine(tempRoot, "autogen-seek-playground.wb");
        try
        {
            ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
            TestContext.Out.WriteLine($"movie={moviePath}");
            TestContext.Out.WriteLine($"seek_sec={seekSec}");
            TestContext.Out.WriteLine($"work={tempRoot}");
            TestContext.Out.WriteLine($"failure_db={failureDbService.FailureDbFullPath}");

            AutogenAttemptResult result = await RunAutogenSeekAttemptAsync(
                moviePath,
                tempRoot,
                $"seek-{seekSec}",
                seekSec
            );
            WriteAttemptResult(result);
            AppendAttemptRecord(failureDbService, result);
            TestContext.Out.WriteLine(
                $"autogen_midseek_success={result.IsSuccess} output_exists={result.OutputExists}"
            );

            Assert.That(result.Attempted, Is.True);
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

    private static string ResolveSeekMoviePath()
    {
        string configuredPath =
            Environment.GetEnvironmentVariable(SeekMoviePathEnvName)?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(configuredPath) ? DefaultSeekMoviePath : configuredPath;
    }

    private static int ResolveSeekSec()
    {
        string configured = Environment.GetEnvironmentVariable(SeekSecEnvName)?.Trim() ?? "";
        if (int.TryParse(configured, out int parsed) && parsed >= 0)
        {
            return parsed;
        }

        return 1200;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            "autogen_repair_playground",
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
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        foreach (DurationOverrideCase durationCase in BuildDurationOverrideCases(metadataProbe))
        {
            AutogenAttemptResult result = await RunAutogenAttemptAsync(
                moviePath,
                tempRoot,
                durationCase.Name,
                durationCase.DurationSec
            );
            WriteAttemptResult(result);
            AppendAttemptRecord(failureDbService, result);
        }
    }

    private async Task RunSeekObservationAttemptAsync(
        string moviePath,
        MovieInfoMetadataProbeSet metadataProbe,
        string tempRoot,
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        double? observationDurationSec = ResolveSeekObservationDurationSec(metadataProbe);
        List<double> candidateSecs =
            FfmpegAutoGenThumbnailGenerationEngine.BuildHeaderFallbackCandidateSeconds(
                observationDurationSec
            );
        TestContext.Out.WriteLine(
            "autogen seek candidates: "
                + $"duration_sec={FormatNullable(observationDurationSec)} "
                + $"candidates=[{string.Join(",", candidateSecs.Select(x => x.ToString("0.###")))}]"
        );

        AutogenAttemptResult result = await RunAutogenAttemptAsync(
            moviePath,
            tempRoot,
            attemptName: "seek-observation",
            durationOverrideSec: observationDurationSec
        );
        WriteAttemptResult(result);
        AppendAttemptRecord(failureDbService, result);
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

    private static double? ResolveSeekObservationDurationSec(MovieInfoMetadataProbeSet metadataProbe)
    {
        double[] candidates =
        [
            metadataProbe.FfMediaToolkit.DurationSec,
            metadataProbe.OpenCv.DurationSec,
            metadataProbe.AutoGen.DurationSec,
        ];

        foreach (double candidate in candidates)
        {
            if (candidate > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task RunDirectRepairAttemptAsync(
        string moviePath,
        string tempRoot,
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        VideoIndexRepairService repairService = new();
        try
        {
            VideoIndexProbeResult probeResult = await repairService.ProbeAsync(moviePath);
            TestContext.Out.WriteLine(
                "repair probe: "
                    + $"detected={probeResult.IsIndexCorruptionDetected} "
                    + $"reason='{probeResult.DetectionReason}' "
                    + $"error='{probeResult.ErrorCode}' "
                    + $"format='{probeResult.ContainerFormat}'"
            );

            string repairedPath = Path.Combine(tempRoot, "repair-output.mkv");
            VideoIndexRepairResult repairResult = await repairService.RepairAsync(
                moviePath,
                repairedPath
            );
            TestContext.Out.WriteLine(
                "repair result: "
                    + $"success={repairResult.IsSuccess} "
                    + $"output='{repairResult.OutputPath}' "
                    + $"err='{repairResult.ErrorMessage}'"
            );

            if (repairResult.IsSuccess && !string.IsNullOrWhiteSpace(repairResult.OutputPath))
            {
                AutogenAttemptResult repairedResult = await RunAutogenAttemptAsync(
                    repairResult.OutputPath,
                    tempRoot,
                    "repaired-autogen",
                    durationOverrideSec: null
                );
                WriteAttemptResult(repairedResult);
                AppendAttemptRecord(failureDbService, repairedResult);
            }
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"repair exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task RunPrepareRepairAttemptAsync(
        string moviePath,
        string tempRoot,
        ThumbnailRepairWorkflowCoordinator repairCoordinator,
        ThumbnailFailureDebugDbService failureDbService
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
                preparationResult.RepairedByProbe
                && !string.IsNullOrWhiteSpace(preparationResult.WorkingMovieFullPath)
            )
            {
                AutogenAttemptResult repairedResult = await RunAutogenAttemptAsync(
                    preparationResult.WorkingMovieFullPath,
                    tempRoot,
                    "prepared-recovery-autogen",
                    durationOverrideSec: null
                );
                WriteAttemptResult(repairedResult);
                AppendAttemptRecord(failureDbService, repairedResult);
            }
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

    private async Task RunForcedRepairAttemptAsync(
        string moviePath,
        string tempRoot,
        ThumbnailRepairWorkflowCoordinator repairCoordinator,
        AutogenAttemptResult originalResult,
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        string repairedMovieTempPath = "";
        try
        {
            ThumbnailForcedRepairRequest request = new()
            {
                IsManual = false,
                ResultIsSuccess = originalResult.IsSuccess,
                IsRecoveryLane = true,
                IsIndexRepairTargetMovie = IsIndexRepairTargetMovie(moviePath),
                RepairedByProbe = false,
                MovieFullPath = moviePath,
                // autogen失敗後の強制repair判定へ入るよう、実運用に近いラベルを付ける。
                EngineErrorMessages = string.IsNullOrWhiteSpace(originalResult.ErrorMessage)
                    ? []
                    : [$"[autogen] {originalResult.ErrorMessage}"],
            };

            ThumbnailForcedRepairResult forcedRepairResult = await repairCoordinator
                .TryRepairAfterFailureAsync(request, CancellationToken.None);
            repairedMovieTempPath = forcedRepairResult.RepairedMovieTempPath;
            TestContext.Out.WriteLine(
                "forced repair: "
                    + $"applied={forcedRepairResult.WasApplied} "
                    + $"working='{forcedRepairResult.WorkingMovieFullPath}' "
                    + $"temp='{forcedRepairResult.RepairedMovieTempPath}' "
                    + $"codec='{forcedRepairResult.VideoCodec}'"
            );

            if (
                forcedRepairResult.WasApplied
                && !string.IsNullOrWhiteSpace(forcedRepairResult.WorkingMovieFullPath)
            )
            {
                AutogenAttemptResult repairedResult = await RunAutogenAttemptAsync(
                    forcedRepairResult.WorkingMovieFullPath,
                    tempRoot,
                    "forced-recovery-autogen",
                    durationOverrideSec: null
                );
                WriteAttemptResult(repairedResult);
                AppendAttemptRecord(failureDbService, repairedResult);
            }
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine(
                $"forced repair exception: {ex.GetType().Name}: {ex.Message}"
            );
        }
        finally
        {
            TryDeleteFileQuietly(repairedMovieTempPath);
        }
    }

    private async Task<AutogenAttemptResult> RunAutogenAttemptAsync(
        string moviePath,
        string tempRoot,
        string attemptName,
        double? durationOverrideSec
    )
    {
        string attemptDir = Path.Combine(tempRoot, SanitizeName(attemptName));
        Directory.CreateDirectory(attemptDir);
        string outputPath = Path.Combine(attemptDir, "autogen.jpg");

        try
        {
            FileInfo fileInfo = new(moviePath);
            QueueObj queueObj = new()
            {
                MovieId = 1,
                Tabindex = 99,
                MovieFullPath = moviePath,
            };
            TabInfo tabInfo = new(99, "autogen-playground", attemptDir);
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
                return AutogenAttemptResult.Failed(
                    attemptName,
                    moviePath,
                    outputPath,
                    durationOverrideSec,
                    material.DurationSec,
                    [],
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

            FfmpegAutoGenThumbnailGenerationEngine engine = new();
            ThumbnailCreateResult result = await engine.CreateAsync(context);

            return new AutogenAttemptResult(
                attemptName,
                moviePath,
                outputPath,
                true,
                durationOverrideSec,
                material.DurationSec,
                material.ThumbInfo.ThumbSec?.Select(x => (double)x).ToList() ?? [],
                result.IsSuccess,
                result.ErrorMessage ?? "",
                Path.Exists(outputPath)
            );
        }
        catch (Exception ex)
        {
            return AutogenAttemptResult.Failed(
                attemptName,
                moviePath,
                outputPath,
                durationOverrideSec,
                null,
                [],
                $"{ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private async Task<AutogenAttemptResult> RunAutogenSeekAttemptAsync(
        string moviePath,
        string tempRoot,
        string attemptName,
        int seekSec
    )
    {
        string attemptDir = Path.Combine(tempRoot, SanitizeName(attemptName));
        Directory.CreateDirectory(attemptDir);
        string outputPath = Path.Combine(attemptDir, "autogen-seek.jpg");

        try
        {
            FileInfo fileInfo = new(moviePath);
            QueueObj queueObj = new()
            {
                MovieId = 1,
                Tabindex = 99,
                MovieFullPath = moviePath,
            };
            TabInfo tabInfo = new(99, "autogen-seek-playground", attemptDir);
            ThumbnailJobMaterialBuilder materialBuilder = new(new AppVideoMetadataProvider());
            ThumbnailJobMaterialBuildResult material = materialBuilder.Build(
                new ThumbnailJobMaterialBuildRequest
                {
                    QueueObj = queueObj,
                    TabInfo = tabInfo,
                    WorkingMovieFullPath = moviePath,
                    SaveThumbFileName = outputPath,
                    IsManual = false,
                    DurationSec = null,
                    FileSizeBytes = fileInfo.Length,
                }
            );

            if (!material.IsSuccess)
            {
                return AutogenAttemptResult.Failed(
                    attemptName,
                    moviePath,
                    outputPath,
                    null,
                    material.DurationSec,
                    [seekSec],
                    $"material build failed: {material.ErrorMessage}"
                );
            }

            // 1x1 で指定秒だけを明示し、autogen のデコード可否を切り分ける。
            ThumbInfo thumbInfo = new()
            {
                ThumbCounts = 1,
                ThumbWidth = tabInfo.Width,
                ThumbHeight = tabInfo.Height,
                ThumbColumns = 1,
                ThumbRows = 1,
                ThumbSec = [seekSec],
            };
            thumbInfo.NewThumbInfo();

            ThumbnailJobContext context = ThumbnailJobContextFactory.Create(
                queueObj,
                tabInfo,
                thumbInfo,
                moviePath,
                outputPath,
                isResizeThumb: true,
                isManual: false,
                material.DurationSec,
                fileInfo.Length,
                material.AverageBitrateMbps,
                material.VideoCodec
            );

            FfmpegAutoGenThumbnailGenerationEngine engine = new();
            ThumbnailCreateResult result = await engine.CreateAsync(context);

            return new AutogenAttemptResult(
                attemptName,
                moviePath,
                outputPath,
                true,
                null,
                material.DurationSec,
                [seekSec],
                result.IsSuccess,
                result.ErrorMessage ?? "",
                Path.Exists(outputPath)
            );
        }
        catch (Exception ex)
        {
            return AutogenAttemptResult.Failed(
                attemptName,
                moviePath,
                outputPath,
                null,
                null,
                [seekSec],
                $"{ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private static void WriteAttemptResult(AutogenAttemptResult result)
    {
        string thumbSecText = result.ThumbSec.Count < 1
            ? ""
            : string.Join(",", result.ThumbSec.Select(x => x.ToString("0.###")));
        TestContext.Out.WriteLine(
            "autogen attempt: "
                + $"name={result.AttemptName} "
                + $"success={result.IsSuccess} "
                + $"output_exists={result.OutputExists} "
                + $"override_sec={FormatNullable(result.DurationOverrideSec)} "
                + $"material_sec={FormatNullable(result.MaterialDurationSec)} "
                + $"thumb_sec=[{thumbSecText}] "
                + $"output='{result.OutputPath}' "
                + $"error='{result.ErrorMessage}'"
        );
    }

    private static string FormatNullable(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###") : "";
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

    private static void AppendAttemptRecord(
        ThumbnailFailureDebugDbService failureDbService,
        AutogenAttemptResult result
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
                PanelType = "autogen-playground",
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
                EngineId = "autogen",
                QueueStatus = result.IsSuccess ? "Done" : "Failed",
                LastError = result.ErrorMessage,
                ExtraJson = JsonSerializer.Serialize(
                    new
                    {
                        result.AttemptName,
                        result.IsSuccess,
                        result.DurationOverrideSec,
                        result.MaterialDurationSec,
                        result.ThumbSec,
                        result.OutputPath,
                        failure_kind_source = "playground",
                        material_duration_sec = result.MaterialDurationSec,
                        thumb_sec = result.ThumbSec.Count > 0 ? result.ThumbSec[0] : (double?)null,
                        engine_attempted = "autogen",
                        engine_succeeded = result.IsSuccess,
                        seek_strategy = result.AttemptName.StartsWith("seek-", StringComparison.OrdinalIgnoreCase)
                            ? "midpoint"
                            : "original",
                        seek_sec = result.ThumbSec.Count > 0 ? result.ThumbSec[0] : (double?)null,
                        repair_attempted =
                            result.AttemptName.Contains("repair", StringComparison.OrdinalIgnoreCase),
                        repair_succeeded =
                            result.IsSuccess
                            && result.AttemptName.Contains("repair", StringComparison.OrdinalIgnoreCase),
                        preflight_branch = "none",
                        result_signature = ResolveResultSignature(result.ErrorMessage),
                        repro_confirmed = false,
                        recovery_route = ResolveRecoveryRoute(result.AttemptName),
                        decision_basis = result.AttemptName,
                    }
                ),
            }
        );
    }

    [TestCase("No frames decoded", 0.069, ThumbnailFailureKind.ShortClipStillLike)]
    [TestCase("No frames decoded", 0.98, ThumbnailFailureKind.ShortClipStillLike)]
    [TestCase("No frames decoded", 2872.529, ThumbnailFailureKind.TransientDecodeFailure)]
    [TestCase("Failed to open input: End of file", 5.8, ThumbnailFailureKind.PhysicalCorruption)]
    public void ResolveFailureKind_短尺静止画系を補助分類できる(
        string errorMessage,
        double? materialDurationSec,
        ThumbnailFailureKind expected
    )
    {
        Assert.That(
            ResolveFailureKind(errorMessage, materialDurationSec),
            Is.EqualTo(expected)
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

        if (text.Contains("no frames decoded") || text.Contains("generic error occurred in gdi+"))
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

        if (text.Contains("access denied") || text.Contains("being used"))
        {
            return "file-locked";
        }

        return "unknown";
    }

    private static string ResolveRecoveryRoute(string attemptName)
    {
        if (attemptName.Contains("forced-recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "repair";
        }

        if (attemptName.Contains("prepared-recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "repair";
        }

        if (attemptName.Contains("repaired", StringComparison.OrdinalIgnoreCase))
        {
            return "repair";
        }

        return "retry";
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

    private sealed record AutogenAttemptResult(
        string AttemptName,
        string MoviePath,
        string OutputPath,
        bool Attempted,
        double? DurationOverrideSec,
        double? MaterialDurationSec,
        IReadOnlyList<double> ThumbSec,
        bool IsSuccess,
        string ErrorMessage,
        bool OutputExists
    )
    {
        public static AutogenAttemptResult Failed(
            string attemptName,
            string moviePath,
            string outputPath,
            double? durationOverrideSec,
            double? materialDurationSec,
            IReadOnlyList<double> thumbSec,
            string errorMessage
        )
        {
            return new AutogenAttemptResult(
                attemptName,
                moviePath,
                outputPath,
                true,
                durationOverrideSec,
                materialDurationSec,
                thumbSec,
                false,
                errorMessage,
                Path.Exists(outputPath)
            );
        }
    }
}

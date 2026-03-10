using System.Drawing;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class AutogenExecutionFlowTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";
    private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
    private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";

    [Test]
    public async Task CreateThumbAsync_AutogenSuccess_DoesNotFallback()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 1, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AutogenInitFailure_初回は再試行ルーティングのため失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (_, _) =>
                    throw new InvalidOperationException("simulated autogen init failure")
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 2, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                // 初回自動作成では autogen 単独で判定し、失敗は再試行ルートへ返す。
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_初回失敗はFallbackログではなく再試行誘導だけを記録する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (_, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            "unused.jpg",
                            10,
                            "simulated autogen init failure"
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            RecordingThumbnailLogger logger = new();
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                logger,
                new RecordingVideoIndexRepairService(
                    _ => new VideoIndexProbeResult(),
                    (_, __) => new VideoIndexRepairResult()
                )
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 200, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(
                    logger.DebugMessages.Any(x =>
                        x.Contains("failure placeholder skipped")
                        && (
                            x.Contains("initial-autogen-failure-retry")
                            || x.Contains("initial-index-repair-target")
                        )
                    ),
                    Is.True
                );
                Assert.That(
                    logger.DebugMessages.Any(x => x.Contains("engine failed: category=error")),
                    Is.False
                );
                Assert.That(
                    logger.DebugMessages.Any(x => x.Contains("engine fallback: category=fallback")),
                    Is.False
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AutogenTransientFailure_4回リトライ後に成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            int failureCount = 0;
            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                {
                    if (failureCount < 4)
                    {
                        failureCount++;
                        return Task.FromResult(
                            ThumbnailResultFactory.CreateFailed(
                                ctx.SaveThumbFileName,
                                ctx.DurationSec,
                                "timeout"
                            )
                        );
                    }

                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            string? oldAutogenRetry = Environment.GetEnvironmentVariable(AutogenRetryEnvName);
            string? oldAutogenRetryDelay = Environment.GetEnvironmentVariable(
                AutogenRetryDelayMsEnvName
            );
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, "on");
                Environment.SetEnvironmentVariable(AutogenRetryDelayMsEnvName, "0");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 3, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(5));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, oldAutogenRetry);
                Environment.SetEnvironmentVariable(
                    AutogenRetryDelayMsEnvName,
                    oldAutogenRetryDelay
                );
            }
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
    public async Task CreateThumbAsync_AttemptCount0_インデックスProbeRepairは実行しない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyFlvFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "test",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    OutputPath = Path.Combine(tempRoot, "fixed.mkv"),
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 6,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 0,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(0));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(0));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount0_Flv失敗はプレースホルダー成功化せず失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyFlvFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            const string unsupportedError = "invalid data found when processing input";
            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "test",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    OutputPath = Path.Combine(tempRoot, "fixed.mkv"),
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 60,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 0,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorMessage, Is.Not.Empty);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(0));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount0_Mp4失敗はプレースホルダー成功化せず失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            const string unsupportedError = "invalid data found when processing input";
            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            unsupportedError
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "test",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    OutputPath = Path.Combine(tempRoot, "fixed.mkv"),
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 62,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 0,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorMessage, Is.Not.Empty);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(0));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount1_ProbeOkかつNoFramesDecoded時は修復後も失敗なら失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyFlvFile(tempRoot);
            string repairedPath = Path.Combine(tempRoot, "forced-fixed.mkv");
            File.WriteAllBytes(repairedPath, [0x22, 0x33, 0x44, 0x55]);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            const string decodeError = "No frames decoded";
            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        string.Equals(
                            ctx.MovieFullPath,
                            repairedPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? ThumbnailResultFactory.CreateSuccess(
                                ctx.SaveThumbFileName,
                                ctx.DurationSec
                            )
                            : ThumbnailResultFactory.CreateFailed(
                                ctx.SaveThumbFileName,
                                ctx.DurationSec,
                                decodeError
                            )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            decodeError
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            decodeError
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            decodeError
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = false,
                    DetectionReason = "probe_ok",
                    ContainerFormat = "flv",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    InputPath = moviePath,
                    OutputPath = repairedPath,
                    UsedTemporaryRemux = true,
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            string? oldAutogenRetry = Environment.GetEnvironmentVariable(AutogenRetryEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, "off");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 61,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(1));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(1));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(2));
                Assert.That(Path.Exists(repairedPath), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, oldAutogenRetry);
            }
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
    public async Task CreateThumbAsync_AttemptCount1_ProbeHit時は修復後パスでOnePassを実行する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyFlvFile(tempRoot);
            string repairedPath = Path.Combine(tempRoot, "fixed.mkv");
            File.WriteAllBytes(repairedPath, [0x10, 0x20, 0x30, 0x40]);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                {
                    Assert.That(ctx.MovieFullPath, Is.EqualTo(repairedPath));
                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                {
                    Assert.That(ctx.MovieFullPath, Is.EqualTo(repairedPath));
                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "open_failed_but_ignidx_succeeded",
                    ContainerFormat = "flv",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    InputPath = moviePath,
                    OutputPath = repairedPath,
                    UsedTemporaryRemux = true,
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 7,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(1));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(1));
                Assert.That(Path.Exists(repairedPath), Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount1_Mp4でProbeHit時は修復後パスでOnePassを実行する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string repairedPath = Path.Combine(tempRoot, "fixed.mkv");
            File.WriteAllBytes(repairedPath, [0x10, 0x20, 0x30, 0x40]);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                {
                    Assert.That(ctx.MovieFullPath, Is.EqualTo(repairedPath));
                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                {
                    Assert.That(ctx.MovieFullPath, Is.EqualTo(repairedPath));
                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "open_failed_but_ignidx_succeeded",
                    ContainerFormat = "mp4",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    InputPath = moviePath,
                    OutputPath = repairedPath,
                    UsedTemporaryRemux = true,
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 8,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(1));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(1));
                Assert.That(Path.Exists(repairedPath), Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_手動かつAttemptCount1_Mp4でProbeHit時は修復後パスでエンジン実行する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string repairedPath = Path.Combine(tempRoot, "fixed-manual.mkv");
            File.WriteAllBytes(repairedPath, [0x10, 0x20, 0x30, 0x40]);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);
            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                new TabInfo(0, "testdb", thumbRoot),
                moviePath,
                Tools.GetHashCRC32(moviePath)
            );
            Directory.CreateDirectory(Path.GetDirectoryName(saveThumbFileName) ?? thumbRoot);
            ThumbInfo thumbInfo = new();
            thumbInfo.ThumbSec.Add(0);
            thumbInfo.ThumbSec.Add(1);
            thumbInfo.NewThumbInfo();
            using (var bitmap = new System.Drawing.Bitmap(1, 1))
            {
                bitmap.Save(
                    saveThumbFileName,
                    System.Drawing.Imaging.ImageFormat.Jpeg
                );
            }
            using (FileStream stream = new(saveThumbFileName, FileMode.Append, FileAccess.Write))
            {
                stream.Write(thumbInfo.SecBuffer, 0, thumbInfo.SecBuffer.Length);
                stream.Write(thumbInfo.InfoBuffer, 0, thumbInfo.InfoBuffer.Length);
            }

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                {
                    Assert.That(ctx.MovieFullPath, Is.EqualTo(repairedPath));
                    return Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    );
                }
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = true,
                    DetectionReason = "open_failed_but_ignidx_succeeded",
                    ContainerFormat = "mp4",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    InputPath = moviePath,
                    OutputPath = repairedPath,
                    UsedTemporaryRemux = true,
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 81,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: true
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(indexRepairService.ProbeCallCount, Is.EqualTo(1));
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(1));
                Assert.That(Path.Exists(repairedPath), Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount1_RecoveryはOnePass直行で処理する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "No frames decoded"
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "ffmedia failed"
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "opencv failed"
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 82,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_Autogen黒プレビュー成功時も初回は再試行ルーティングで終了する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            CreatePreviewFrame(16, 16, 0)
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "ffmedia failed"
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "opencv failed"
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 83, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_初回でも長尺NoFramesDecoded時はOnePass救済する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "No frames decoded"
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "ffmedia failed"
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "opencv failed"
                        )
                    )
            );
            RecordingThumbnailLogger logger = new();
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                new FixedVideoMetadataProvider(2872.529, "png"),
                logger,
                new RecordingVideoIndexRepairService(
                    _ => new VideoIndexProbeResult(),
                    (_, __) => new VideoIndexRepairResult()
                )
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            string? oldAutogenRetry = Environment.GetEnvironmentVariable(AutogenRetryEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, "off");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 84, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
                Assert.That(
                    logger.DebugMessages.Any(x =>
                        x.Contains("engine fallback: category=fallback from=autogen, to=ffmpeg1pass")
                        && x.Contains("recovery-no-frames-decoded")
                    ),
                    Is.True
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
                Environment.SetEnvironmentVariable(AutogenRetryEnvName, oldAutogenRetry);
            }
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
    public async Task CreateThumbAsync_RecoveryOnePass失敗時は追加修復せずそのまま失敗を返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string repairedPath = Path.Combine(tempRoot, "repair.mkv");
            File.WriteAllBytes(repairedPath, [1, 2, 3, 4]);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "No frames decoded"
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            $"ffmedia failed: {Path.GetExtension(ctx.MovieFullPath)}"
                        )
                    )
            );
            int originalMovieOnePassCallCount = 0;
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                {
                    if (string.Equals(ctx.MovieFullPath, moviePath, StringComparison.OrdinalIgnoreCase))
                    {
                        originalMovieOnePassCallCount++;
                        return Task.FromResult(
                            originalMovieOnePassCallCount >= 2
                                ? ThumbnailResultFactory.CreateSuccess(
                                    ctx.SaveThumbFileName,
                                    ctx.DurationSec
                                )
                                : ThumbnailResultFactory.CreateFailed(
                                    ctx.SaveThumbFileName,
                                    ctx.DurationSec,
                                    "original one-pass failed"
                                )
                        );
                    }

                    return Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "repair one-pass failed"
                        )
                    );
                }
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "opencv failed"
                        )
                    )
            );
            var indexRepairService = new RecordingVideoIndexRepairService(
                _ => new VideoIndexProbeResult
                {
                    IsIndexCorruptionDetected = false,
                    DetectionReason = "probe_ok",
                    ContainerFormat = "mp4",
                },
                (_, __) => new VideoIndexRepairResult
                {
                    IsSuccess = true,
                    InputPath = moviePath,
                    OutputPath = repairedPath,
                    UsedTemporaryRemux = true,
                }
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance,
                indexRepairService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 84,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(indexRepairService.RepairCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
                Assert.That(
                    ffmpeg1pass.CreateMoviePaths.Any(path =>
                        string.Equals(path, moviePath, StringComparison.OrdinalIgnoreCase)
                    ),
                    Is.True
                );
                Assert.That(
                    ffmpeg1pass.CreateMoviePaths.Any(path =>
                        string.Equals(path, repairedPath, StringComparison.OrdinalIgnoreCase)
                    ),
                    Is.False
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_AttemptCount1_Autogen強制時の黒プレビューでもRecoveryでFfmpegOnePassへフォールバックする()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyMp4File(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            CreatePreviewFrame(16, 16, 0)
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "ffmedia failed"
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                createAsync: (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateFailed(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec,
                            "opencv failed"
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "autogen");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj
                    {
                        MovieId = 85,
                        Tabindex = 0,
                        MovieFullPath = moviePath,
                        AttemptCount = 1,
                    },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(1));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_WmvDrmPrecheckHit_エンジン実行せずプレースホルダーで成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummyWmvWithDrmHeaderFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 4, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(Path.Exists(result.SaveThumbFileName), Is.True);
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
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
    public async Task CreateThumbAsync_SwfSignaturePrecheckHit_Swf専用サービスで成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = CreateDummySwfWithSignatureFile(tempRoot, "CWS");
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine(
                "ffmediatoolkit",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmpeg1pass = new RecordingEngine(
                "ffmpeg1pass",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var opencv = new RecordingEngine(
                "opencv",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailResultFactory.CreateSuccess(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var swfService = new StubSwfThumbnailGenerationService(outputPath =>
            {
                using Bitmap bitmap = new(120, 90);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.LightSkyBlue);
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                return SwfThumbnailCandidate.CreateAccepted(2d, outputPath);
            });
            var service = ThumbnailCreationServiceFactory.Create(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen,
                swfService
            );

            string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                ThumbnailCreateResult result = await service.CreateThumbAsync(
                    new QueueObj { MovieId = 5, Tabindex = 0, MovieFullPath = moviePath },
                    dbName: "testdb",
                    thumbFolder: thumbRoot,
                    isResizeThumb: true,
                    isManual: false
                );

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(Path.Exists(result.SaveThumbFileName), Is.True);
                Assert.That(swfService.CallCount, Is.EqualTo(1));
                Assert.That(autogen.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmedia.CreateCallCount, Is.EqualTo(0));
                Assert.That(ffmpeg1pass.CreateCallCount, Is.EqualTo(0));
                Assert.That(opencv.CreateCallCount, Is.EqualTo(0));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateDummyMovieFile(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "dummy.mp4");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);
        return path;
    }

    private static string CreateDummyFlvFile(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "dummy.flv");
        File.WriteAllBytes(path, [0x46, 0x4C, 0x56, 0x01]);
        return path;
    }

    private static string CreateDummyMp4File(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "dummy.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        return path;
    }

    private static string CreateDummyWmvWithDrmHeaderFile(string tempRoot)
    {
        string path = Path.Combine(tempRoot, "drm-sample.wmv");
        byte[] header = new byte[4096];
        byte[] drmGuid =
        [
            0xFB,
            0xB3,
            0x11,
            0x22,
            0x23,
            0xBD,
            0xD2,
            0x11,
            0xB4,
            0xB7,
            0x00,
            0xA0,
            0xC9,
            0x55,
            0xFC,
            0x6E,
        ];
        Array.Copy(drmGuid, 0, header, 256, drmGuid.Length);
        File.WriteAllBytes(path, header);
        return path;
    }

    private static string CreateDummySwfWithSignatureFile(string tempRoot, string signature)
    {
        string path = Path.Combine(tempRoot, "swf-sample.swf");
        byte[] header = signature.ToUpperInvariant() switch
        {
            "FWS" => [0x46, 0x57, 0x53, 0x09, 0x00, 0x00, 0x00, 0x00],
            "CWS" => [0x43, 0x57, 0x53, 0x09, 0x00, 0x00, 0x00, 0x00],
            "ZWS" => [0x5A, 0x57, 0x53, 0x09, 0x00, 0x00, 0x00, 0x00],
            _ => [0x00, 0x00, 0x00, 0x00],
        };
        File.WriteAllBytes(path, header);
        return path;
    }

    private static ThumbnailPreviewFrame CreatePreviewFrame(int width, int height, byte value)
    {
        byte[] pixels = new byte[width * height * 3];
        Array.Fill(pixels, value);
        return new ThumbnailPreviewFrame
        {
            PixelBytes = pixels,
            Width = width,
            Height = height,
            Stride = width * 3,
            PixelFormat = ThumbnailPreviewPixelFormat.Bgr24,
        };
    }

    private sealed class StubSwfThumbnailGenerationService : SwfThumbnailGenerationService
    {
        private readonly Func<string, SwfThumbnailCandidate> candidateFactory;

        public StubSwfThumbnailGenerationService(
            Func<string, SwfThumbnailCandidate> candidateFactory
        )
        {
            this.candidateFactory = candidateFactory;
        }

        public int CallCount { get; private set; }

        public override Task<SwfThumbnailCandidate> TryCaptureRepresentativeFrameAsync(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options,
            CancellationToken cts = default
        )
        {
            CallCount++;
            return Task.FromResult(candidateFactory(outputPath));
        }
    }

    private sealed class RecordingEngine : IThumbnailGenerationEngine
    {
        private readonly Func<ThumbnailJobContext, CancellationToken, Task<ThumbnailCreateResult>> createAsync;

        public RecordingEngine(
            string engineId,
            Func<ThumbnailJobContext, CancellationToken, Task<ThumbnailCreateResult>> createAsync
        )
        {
            EngineId = engineId;
            EngineName = engineId;
            this.createAsync = createAsync;
        }

        public string EngineId { get; }
        public string EngineName { get; }
        public int CreateCallCount { get; private set; }
        public List<string> CreateMoviePaths { get; } = [];

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            CreateCallCount++;
            CreateMoviePaths.Add(context.MovieFullPath ?? "");
            return createAsync(context, cts);
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(false);
        }
    }

    private sealed class RecordingVideoIndexRepairService : IVideoIndexRepairService
    {
        private readonly Func<string, VideoIndexProbeResult> probeFunc;
        private readonly Func<string, string, VideoIndexRepairResult> repairFunc;

        public RecordingVideoIndexRepairService(
            Func<string, VideoIndexProbeResult> probeFunc,
            Func<string, string, VideoIndexRepairResult> repairFunc
        )
        {
            this.probeFunc = probeFunc;
            this.repairFunc = repairFunc;
        }

        public int ProbeCallCount { get; private set; }
        public int RepairCallCount { get; private set; }

        public Task<VideoIndexProbeResult> ProbeAsync(
            string moviePath,
            CancellationToken cts = default
        )
        {
            ProbeCallCount++;
            return Task.FromResult(probeFunc(moviePath));
        }

        public Task<VideoIndexRepairResult> RepairAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        )
        {
            RepairCallCount++;
            return Task.FromResult(repairFunc(moviePath, outputPath));
        }
    }

    private sealed class RecordingThumbnailLogger : IThumbnailLogger
    {
        public List<string> DebugMessages { get; } = [];

        public void LogDebug(string category, string message)
        {
            DebugMessages.Add($"[{category}] {message}");
        }

        public void LogInfo(string category, string message) { }

        public void LogWarning(string category, string message) { }

        public void LogError(string category, string message) { }
    }

    private sealed class FixedVideoMetadataProvider : IVideoMetadataProvider
    {
        private readonly double durationSec;
        private readonly string videoCodec;

        public FixedVideoMetadataProvider(double durationSec, string videoCodec)
        {
            this.durationSec = durationSec;
            this.videoCodec = videoCodec;
        }

        public bool TryGetVideoCodec(string moviePath, out string codec)
        {
            codec = videoCodec;
            return !string.IsNullOrWhiteSpace(codec);
        }

        public bool TryGetDurationSec(string moviePath, out double durationSec)
        {
            durationSec = this.durationSec;
            return durationSec > 0;
        }
    }
}

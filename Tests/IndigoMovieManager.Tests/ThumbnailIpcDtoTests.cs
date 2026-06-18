using System.Reflection;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailIpcDtoTests
{
    [Test]
    public void EnumNames_仕様どおりに固定する()
    {
        Assert.That(
            Enum.GetNames<EngineJobLaneKind>(),
            Is.EqualTo(new[] { "Normal", "Slow" })
        );
        Assert.That(
            Enum.GetNames<EngineFailureKind>(),
            Is.EqualTo(new[] { "None", "Io", "Decode", "Index", "Timeout", "Unknown" })
        );
        Assert.That(
            Enum.GetNames<DiskThermalState>(),
            Is.EqualTo(new[] { "Normal", "Warning", "Critical", "Unavailable" })
        );
        Assert.That(
            Enum.GetNames<UsnMftStatusKind>(),
            Is.EqualTo(new[] { "Ready", "Busy", "Unavailable", "AccessDenied" })
        );
        Assert.That(
            Enum.GetNames<ThrottleDecisionKind>(),
            Is.EqualTo(new[] { "Keep", "ThrottleDown", "RecoverUp" })
        );
        Assert.That(
            Enum.GetNames<ThrottleReasonKind>(),
            Is.EqualTo(new[] { "Error", "HighLoad", "Thermal", "Manual", "Fallback" })
        );
    }

    [Test]
    public void DtoPropertyNames_仕様どおりに固定する()
    {
        Assert.That(
            GetPublicPropertyNames(typeof(EngineJobMetricsDto)),
            Is.EqualTo(
                new[]
                {
                    "JobId",
                    "SourcePath",
                    "LaneKind",
                    "ElapsedMs",
                    "Succeeded",
                    "FailureKind",
                    "AttemptCount",
                    "DecodedFrameCount",
                    "PeakWorkingSetMb",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(SystemLoadSnapshotDto)),
            Is.EqualTo(
                new[]
                {
                    "CpuUsageRate",
                    "IoBusyRate",
                    "MemoryPressureRate",
                    "QueueBacklogCount",
                    "SlowLaneBacklogCount",
                    "SampleWindowMs",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(DiskThermalSnapshotDto)),
            Is.EqualTo(
                new[]
                {
                    "DiskId",
                    "TemperatureCelsius",
                    "WarningThresholdCelsius",
                    "CriticalThresholdCelsius",
                    "ThermalState",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(UsnMftStatusDto)),
            Is.EqualTo(
                new[]
                {
                    "VolumeName",
                    "Available",
                    "LastScanLatencyMs",
                    "JournalBacklogCount",
                    "StatusKind",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(ThrottleDecisionDto)),
            Is.EqualTo(
                new[]
                {
                    "ConfiguredParallelism",
                    "EffectiveParallelism",
                    "DecisionKind",
                    "ReasonKind",
                    "ReasonDetail",
                    "CooldownUntilUtc",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(WorkerJobRequestDto)),
            Is.EqualTo(
                new[]
                {
                    "JobId",
                    "Kind",
                    "InputFiles",
                    "OutputArtifactPath",
                    "TimeoutMs",
                    "Capabilities",
                    "DiagnosticContext",
                    "RequestedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(WorkerJobArtifactDto)),
            Is.EqualTo(
                new[]
                {
                    "ArtifactKind",
                    "Path",
                    "ContentType",
                    "SizeBytes",
                    "Sha256",
                    "Metadata",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(WorkerJobProgressDto)),
            Is.EqualTo(
                new[]
                {
                    "JobId",
                    "Stage",
                    "CompletedCount",
                    "TotalCount",
                    "CurrentInputFile",
                    "Message",
                    "Metrics",
                    "CapturedAtUtc",
                }
            )
        );
        Assert.That(
            GetPublicPropertyNames(typeof(WorkerJobResultDto)),
            Is.EqualTo(
                new[]
                {
                    "JobId",
                    "Status",
                    "Artifact",
                    "FailureReason",
                    "ElapsedMs",
                    "Retryability",
                    "Logs",
                    "Metrics",
                    "FinishedAtUtc",
                }
            )
        );
    }

    [Test]
    public void CapturedAtUtc_ローカル時刻入力でもUtcへ正規化する()
    {
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 3, 6, 21, 15, 0),
            DateTimeKind.Local
        );

        EngineJobMetricsDto dto = new() { CapturedAtUtc = localTime };

        Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(dto.CapturedAtUtc, Is.EqualTo(localTime.ToUniversalTime()));
    }

    [Test]
    public void ThrottleDecisionDto_復帰待ち時刻もUtcへ正規化する()
    {
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 3, 6, 22, 30, 0),
            DateTimeKind.Local
        );

        ThrottleDecisionDto dto = new()
        {
            CooldownUntilUtc = localTime,
            CapturedAtUtc = localTime,
        };

        Assert.That(dto.CooldownUntilUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(dto.CooldownUntilUtc, Is.EqualTo(localTime.ToUniversalTime()));
        Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void 状態系Dto_未取得時はUnavailableを既定にする()
    {
        DiskThermalSnapshotDto thermal = new();
        UsnMftStatusDto usnMft = new();

        Assert.That(thermal.ThermalState, Is.EqualTo(DiskThermalState.Unavailable));
        Assert.That(usnMft.StatusKind, Is.EqualTo(UsnMftStatusKind.Unavailable));
    }

    [Test]
    public void Worker契約Dto_既定値はNullではなく空値で扱える()
    {
        WorkerJobRequestDto request = new();
        WorkerJobArtifactDto artifact = new();
        WorkerJobProgressDto progress = new();
        WorkerJobResultDto result = new();

        Assert.That(request.InputFiles, Is.Empty);
        Assert.That(request.Capabilities, Is.Empty);
        Assert.That(request.DiagnosticContext, Is.Empty);
        Assert.That(artifact.Metadata, Is.Empty);
        Assert.That(progress.Metrics, Is.Empty);
        Assert.That(result.Artifact, Is.Not.Null);
        Assert.That(result.Logs, Is.Empty);
        Assert.That(result.Metrics, Is.Empty);
    }

    [Test]
    public void Worker契約Dto_JSONでRequestProgressResultArtifactをRoundtripできる()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 9, 30, 0),
            DateTimeKind.Local
        );

        WorkerJobRequestDto request = new()
        {
            JobId = "worker-job-001",
            Kind = "rescue",
            InputFiles = ["movies/anime.mp4"],
            OutputArtifactPath = "thumbs/anime.jpg",
            TimeoutMs = 120000,
            Capabilities = ["rescue-job-json", "ffmpeg"],
            DiagnosticContext = new Dictionary<string, string>
            {
                ["caller"] = "IndigoMovieManager",
                ["operation"] = "thumbnail-rescue",
            },
            RequestedAtUtc = localTime,
        };
        WorkerJobProgressDto progress = new()
        {
            JobId = "worker-job-001",
            Stage = "decode",
            CompletedCount = 1,
            TotalCount = 3,
            CurrentInputFile = "movies/anime.mp4",
            Message = "decoding",
            Metrics = new Dictionary<string, string> { ["decodedFrames"] = "42" },
            CapturedAtUtc = localTime,
        };
        WorkerJobResultDto result = new()
        {
            JobId = "worker-job-001",
            Status = "succeeded",
            Artifact = new WorkerJobArtifactDto
            {
                ArtifactKind = "thumbnail-image",
                Path = "thumbs/anime.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 34567,
                Sha256 = "abc123",
                Metadata = new Dictionary<string, string> { ["role"] = "preferred" },
            },
            ElapsedMs = 3456,
            Retryability = "not-retryable",
            Logs = ["started", "completed"],
            Metrics = new Dictionary<string, string> { ["engine"] = "ffmpeg" },
            FinishedAtUtc = localTime,
        };

        string requestJson = JsonSerializer.Serialize(request, options);
        string progressJson = JsonSerializer.Serialize(progress, options);
        string resultJson = JsonSerializer.Serialize(result, options);

        Assert.That(requestJson, Does.Contain("\"jobId\""));
        Assert.That(requestJson, Does.Contain("\"inputFiles\""));
        Assert.That(requestJson, Does.Contain("\"outputArtifactPath\""));
        Assert.That(requestJson, Does.Contain("\"diagnosticContext\""));
        Assert.That(progressJson, Does.Contain("\"stage\""));
        Assert.That(progressJson, Does.Contain("\"completedCount\""));
        Assert.That(resultJson, Does.Contain("\"artifact\""));
        Assert.That(resultJson, Does.Contain("\"failureReason\""));
        Assert.That(resultJson, Does.Contain("\"retryability\""));

        WorkerJobRequestDto roundtripRequest =
            JsonSerializer.Deserialize<WorkerJobRequestDto>(requestJson, options) ?? new();
        WorkerJobProgressDto roundtripProgress =
            JsonSerializer.Deserialize<WorkerJobProgressDto>(progressJson, options) ?? new();
        WorkerJobResultDto roundtripResult =
            JsonSerializer.Deserialize<WorkerJobResultDto>(resultJson, options) ?? new();

        Assert.That(roundtripRequest.JobId, Is.EqualTo("worker-job-001"));
        Assert.That(roundtripRequest.Kind, Is.EqualTo("rescue"));
        Assert.That(roundtripRequest.InputFiles.Single(), Is.EqualTo("movies/anime.mp4"));
        Assert.That(roundtripRequest.Capabilities, Does.Contain("rescue-job-json"));
        Assert.That(roundtripRequest.DiagnosticContext["operation"], Is.EqualTo("thumbnail-rescue"));
        Assert.That(roundtripRequest.RequestedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(roundtripProgress.Stage, Is.EqualTo("decode"));
        Assert.That(roundtripProgress.Metrics["decodedFrames"], Is.EqualTo("42"));
        Assert.That(roundtripProgress.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(roundtripResult.Status, Is.EqualTo("succeeded"));
        Assert.That(roundtripResult.Artifact.ArtifactKind, Is.EqualTo("thumbnail-image"));
        Assert.That(roundtripResult.Artifact.Metadata["role"], Is.EqualTo("preferred"));
        Assert.That(roundtripResult.Logs, Does.Contain("completed"));
        Assert.That(roundtripResult.FinishedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    private static string[] GetPublicPropertyNames(Type type)
    {
        return
        [
            .. type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(x => x.Name),
        ];
    }
}

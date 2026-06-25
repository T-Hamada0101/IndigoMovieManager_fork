using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchMetadataProbeWorkerContractAdapterTests
{
    [Test]
    public void MetadataProbe入力をWorkerJobRequestDtoへ写せる()
    {
        DateTime requestedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 12, 13, 14, 567),
            DateTimeKind.Utc
        );
        WatchMetadataProbeRequest request =
            new()
            {
                MoviePath = "%USERPROFILE%/videos/sample.mp4",
                ExistingMovieLengthSeconds = 0,
                HasFileDateDirty = true,
                HasMovieSizeDirty = false,
                Source = "watch-existing-movie",
                RequestedAtUtc = requestedAt,
            };

        WorkerJobRequestDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobRequestDto(
                request,
                outputArtifactPath: "metadata/sample.json",
                timeoutMs: 30000
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Does.StartWith("metadata-probe-"));
            Assert.That(dto.JobId, Does.EndWith("-20260618121314567"));
            Assert.That(dto.Kind, Is.EqualTo("metadata-probe"));
            Assert.That(dto.InputFiles, Is.EqualTo(new[] { "%USERPROFILE%/videos/sample.mp4" }));
            Assert.That(dto.OutputArtifactPath, Is.EqualTo("metadata/sample.json"));
            Assert.That(dto.TimeoutMs, Is.EqualTo(30000));
            Assert.That(dto.Capabilities, Does.Contain("watch-metadata-probe"));
            Assert.That(dto.Capabilities, Does.Contain("metadata-probe"));
            Assert.That(dto.Capabilities, Does.Contain("cheap-dirty"));
            Assert.That(dto.Capabilities, Does.Contain("length-missing"));
            Assert.That(dto.RequestedAtUtc, Is.EqualTo(requestedAt));
            Assert.That(dto.RequestedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(dto.DiagnosticContext["contractSource"], Is.EqualTo("watch-scan"));
            Assert.That(dto.DiagnosticContext["source"], Is.EqualTo("watch-existing-movie"));
            Assert.That(dto.DiagnosticContext["moviePathKey"], Is.Not.Empty);
            Assert.That(dto.DiagnosticContext["existingMovieLengthSeconds"], Is.EqualTo("0"));
            Assert.That(dto.DiagnosticContext["hasFileDateDirty"], Is.EqualTo("true"));
            Assert.That(dto.DiagnosticContext["hasMovieSizeDirty"], Is.EqualTo("false"));
            Assert.That(dto.DiagnosticContext["hasCheapDirtyFields"], Is.EqualTo("true"));
        });
    }

    [Test]
    public void MetadataProbe入力が空でもNullを返さない()
    {
        WorkerJobRequestDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobRequestDto(
                null!,
                outputArtifactPath: null,
                timeoutMs: -1
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Does.StartWith("metadata-probe-empty-"));
            Assert.That(dto.Kind, Is.EqualTo("metadata-probe"));
            Assert.That(dto.InputFiles, Is.Empty);
            Assert.That(dto.OutputArtifactPath, Is.Empty);
            Assert.That(dto.TimeoutMs, Is.Zero);
            Assert.That(dto.Capabilities, Does.Contain("watch-metadata-probe"));
            Assert.That(dto.Capabilities, Does.Contain("metadata-probe"));
            Assert.That(dto.Capabilities, Does.Contain("length-missing"));
            Assert.That(dto.DiagnosticContext["moviePathKey"], Is.EqualTo("empty"));
            Assert.That(dto.DiagnosticContext["source"], Is.EqualTo("watch-scan"));
        });
    }

    [Test]
    public void MetadataProbe成功結果をWorkerJobResultDtoへ写せる()
    {
        WatchMetadataProbeRequest request =
            new()
            {
                MoviePath = "%USERPROFILE%/videos/sample.mp4",
                ExistingMovieLengthSeconds = 0,
                RequestedAtUtc = DateTime.SpecifyKind(
                    new DateTime(2026, 6, 18, 12, 30, 0),
                    DateTimeKind.Utc
                ),
            };
        WorkerJobRequestDto requestDto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobRequestDto(request);
        DateTime finishedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 12, 30, 1),
            DateTimeKind.Utc
        );

        WorkerJobResultDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobResultDto(
                new WatchMetadataProbeResult
                {
                    JobId = requestDto.JobId,
                    MoviePath = request.MoviePath,
                    MovieLengthSeconds = 123,
                    Succeeded = true,
                    ElapsedMs = 45,
                    FinishedAtUtc = finishedAt,
                },
                new Dictionary<string, string> { ["engine"] = "in-process" }
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo(requestDto.JobId));
            Assert.That(dto.Status, Is.EqualTo("succeeded"));
            Assert.That(dto.Artifact.ArtifactKind, Is.EqualTo("metadata-probe-state"));
            Assert.That(dto.Artifact.Path, Is.Empty);
            Assert.That(
                dto.Artifact.ContentType,
                Is.EqualTo("application/x.indigo.metadata-probe")
            );
            Assert.That(dto.Artifact.Metadata["moviePathKey"], Is.Not.Empty);
            Assert.That(dto.Artifact.Metadata["movieLengthSeconds"], Is.EqualTo("123"));
            Assert.That(dto.FailureReason, Is.Empty);
            Assert.That(dto.ElapsedMs, Is.EqualTo(45));
            Assert.That(dto.Retryability, Is.EqualTo("not-retryable"));
            Assert.That(dto.Metrics["movieLengthSeconds"], Is.EqualTo("123"));
            Assert.That(dto.Metrics["status"], Is.EqualTo("succeeded"));
            Assert.That(dto.Metrics["failureKind"], Is.Empty);
            Assert.That(dto.Metrics["retryable"], Is.EqualTo("false"));
            Assert.That(dto.Metrics["elapsedMs"], Is.EqualTo("45"));
            Assert.That(dto.Metrics["engine"], Is.EqualTo("in-process"));
            Assert.That(dto.Logs[0], Does.Contain("status=succeeded"));
            Assert.That(dto.FinishedAtUtc, Is.EqualTo(finishedAt));
        });
    }

    [Test]
    public void MetadataProbe進捗をWorkerJobProgressDtoへ写せる()
    {
        DateTime capturedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 13, 1, 2, 345),
            DateTimeKind.Utc
        );

        WorkerJobProgressDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobProgressDto(
                new WatchMetadataProbeProgress
                {
                    JobId = "metadata-probe-job-1",
                    MoviePath = "%USERPROFILE%/videos/sample.mp4",
                    Stage = WatchMetadataProbeWorkerContractAdapter.ProgressStageRunning,
                    CompletedCount = 0,
                    TotalCount = 1,
                    Message = "probing metadata",
                    CapturedAtUtc = capturedAt,
                },
                new Dictionary<string, string> { ["source"] = "watch-existing-movie" }
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("metadata-probe-job-1"));
            Assert.That(dto.Stage, Is.EqualTo("running"));
            Assert.That(dto.CompletedCount, Is.Zero);
            Assert.That(dto.TotalCount, Is.EqualTo(1));
            Assert.That(dto.CurrentInputFile, Is.EqualTo("%USERPROFILE%/videos/sample.mp4"));
            Assert.That(dto.Message, Is.EqualTo("probing metadata"));
            Assert.That(dto.Metrics["workerKind"], Is.EqualTo("metadata-probe"));
            Assert.That(dto.Metrics["moviePathKey"], Is.Not.Empty);
            Assert.That(dto.Metrics["stage"], Is.EqualTo("running"));
            Assert.That(dto.Metrics["completedCount"], Is.EqualTo("0"));
            Assert.That(dto.Metrics["totalCount"], Is.EqualTo("1"));
            Assert.That(dto.Metrics["source"], Is.EqualTo("watch-existing-movie"));
            Assert.That(dto.CapturedAtUtc, Is.EqualTo(capturedAt));
            Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void MetadataProbe進捗が空でも最小Progressを返す()
    {
        WorkerJobProgressDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobProgressDto(
                null!,
                new Dictionary<string, string> { ["note"] = null! }
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Does.StartWith("metadata-probe-empty-"));
            Assert.That(dto.Stage, Is.EqualTo("running"));
            Assert.That(dto.CompletedCount, Is.Zero);
            Assert.That(dto.TotalCount, Is.Zero);
            Assert.That(dto.CurrentInputFile, Is.Empty);
            Assert.That(dto.Message, Is.Empty);
            Assert.That(dto.Metrics["workerKind"], Is.EqualTo("metadata-probe"));
            Assert.That(dto.Metrics["moviePathKey"], Is.EqualTo("empty"));
            Assert.That(dto.Metrics["note"], Is.Empty);
            Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void MetadataProbe失敗結果はfailureKindとretryableをWorkerJobResultDtoへ写せる()
    {
        WorkerJobResultDto dto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobResultDto(
                new WatchMetadataProbeResult
                {
                    JobId = "metadata-probe-job-1",
                    MoviePath = "%USERPROFILE%/videos/sample.mp4",
                    Succeeded = false,
                    FailureKind = "ProbeFailed",
                    Retryable = true,
                    ElapsedMs = -1,
                }
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("metadata-probe-job-1"));
            Assert.That(dto.Status, Is.EqualTo("failed"));
            Assert.That(dto.Artifact.ArtifactKind, Is.Empty);
            Assert.That(dto.Artifact.ContentType, Is.Empty);
            Assert.That(dto.FailureReason, Is.EqualTo("ProbeFailed"));
            Assert.That(dto.ElapsedMs, Is.Zero);
            Assert.That(dto.Retryability, Is.EqualTo("retryable"));
            Assert.That(dto.Metrics["failureKind"], Is.EqualTo("ProbeFailed"));
            Assert.That(dto.Metrics["retryable"], Is.EqualTo("true"));
            Assert.That(dto.Metrics["status"], Is.EqualTo("failed"));
            Assert.That(dto.Logs, Does.Contain("failure_kind=ProbeFailed"));
            Assert.That(dto.Logs, Does.Contain("failure_reason=ProbeFailed"));
        });
    }

    [Test]
    public void MetadataProbeログFieldsはWorker契約Dtoから組み立てる()
    {
        WatchMetadataProbeRequest request =
            new()
            {
                MoviePath = "%USERPROFILE%/videos/sample.mp4",
                ExistingMovieLengthSeconds = 0,
                HasFileDateDirty = true,
                Source = "watch-existing-movie",
                RequestedAtUtc = DateTime.SpecifyKind(
                    new DateTime(2026, 6, 18, 14, 0, 0),
                    DateTimeKind.Utc
                ),
            };
        WorkerJobRequestDto requestDto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobRequestDto(request);
        WorkerJobProgressDto progressDto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobProgressDto(
                new WatchMetadataProbeProgress
                {
                    JobId = requestDto.JobId,
                    MoviePath = request.MoviePath,
                    Stage = WatchMetadataProbeWorkerContractAdapter.ProgressStageCompleted,
                    CompletedCount = 1,
                    TotalCount = 1,
                }
            );
        WorkerJobResultDto resultDto =
            WatchMetadataProbeWorkerContractAdapter.ToWorkerJobResultDto(
                new WatchMetadataProbeResult
                {
                    JobId = requestDto.JobId,
                    MoviePath = request.MoviePath,
                    MovieLengthSeconds = 120,
                    Succeeded = true,
                    ElapsedMs = 12,
                }
            );

        string requestFields =
            WatchMetadataProbeWorkerContractAdapter.BuildWorkerJobRequestLogFields(requestDto);
        string progressFields =
            WatchMetadataProbeWorkerContractAdapter.BuildWorkerJobProgressLogFields(progressDto);
        string resultFields =
            WatchMetadataProbeWorkerContractAdapter.BuildWorkerJobResultLogFields(resultDto);
        string combinedFields =
            WatchMetadataProbeWorkerContractAdapter.BuildWorkerProbeLogFields(
                requestDto,
                progressDto,
                resultDto
            );

        Assert.Multiple(() =>
        {
            Assert.That(requestFields, Does.Contain("worker_job_id="));
            Assert.That(requestFields, Does.Contain("worker_kind=metadata-probe"));
            Assert.That(requestFields, Does.Contain("input_count=1"));
            Assert.That(requestFields, Does.Contain("capability_count=4"));
            Assert.That(requestFields, Does.Contain("source=watch-existing-movie"));
            Assert.That(requestFields, Does.Contain("has_cheap_dirty_fields=true"));
            Assert.That(requestFields, Does.Contain("existing_movie_length_seconds=0"));
            Assert.That(progressFields, Does.Contain("worker_stage=completed"));
            Assert.That(progressFields, Does.Contain("progress_completed=1"));
            Assert.That(progressFields, Does.Contain("current_input_file="));
            Assert.That(progressFields, Does.Contain("movie_path_key="));
            Assert.That(resultFields, Does.Contain("worker_status=succeeded"));
            Assert.That(resultFields, Does.Contain("artifact_kind=metadata-probe-state"));
            Assert.That(resultFields, Does.Contain("retryable=false"));
            Assert.That(resultFields, Does.Contain("elapsed_ms=12"));
            Assert.That(resultFields, Does.Contain("movie_length_seconds=120"));
            Assert.That(combinedFields, Does.Contain("worker_job_id="));
            Assert.That(combinedFields, Does.Contain("worker_kind=metadata-probe"));
            Assert.That(combinedFields, Does.Contain("worker_status=succeeded"));
            Assert.That(combinedFields, Does.Contain("worker_stage=completed"));
            Assert.That(combinedFields, Does.Contain("artifact_kind=metadata-probe-state"));
            Assert.That(combinedFields, Does.Contain("retryable=false"));
            Assert.That(combinedFields, Does.Contain("elapsed_ms=12"));
            Assert.That(combinedFields, Does.Contain("progress_total=1"));
            Assert.That(combinedFields, Does.Contain("input_count=1"));
            Assert.That(combinedFields, Does.Contain("capability_count=4"));
            Assert.That(combinedFields, Does.Contain("source=watch-existing-movie"));
            Assert.That(combinedFields, Does.Contain("has_cheap_dirty_fields=true"));
            Assert.That(combinedFields, Does.Contain("existing_movie_length_seconds=0"));
            Assert.That(combinedFields, Does.Contain("movie_length_seconds=120"));
        });
    }
}

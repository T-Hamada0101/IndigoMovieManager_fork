using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailQueueWorkerContractAdapterTests
{
    [Test]
    public void QueueRequestをWorkerJobRequestDtoへ写せる()
    {
        DateTime requestedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 10, 20, 30, 456),
            DateTimeKind.Utc
        );
        QueueRequest request = new()
        {
            MainDbFullPath = "%USERPROFILE%/source/repos/sample/sample.wb",
            MainDbSessionStamp = 42,
            MoviePath = "movies/sample.mp4",
            MoviePathKey = "movie-key-001",
            TabIndex = 3,
            MovieSizeBytes = 123456,
            ThumbPanelPos = 7,
            ThumbTimePos = 123,
            Priority = ThumbnailQueuePriority.Preferred,
            RequestedAtUtc = requestedAt,
        };

        WorkerJobRequestDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobRequestDto(
                request,
                outputArtifactPath: "thumbs/sample.jpg",
                timeoutMs: 150000
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-movie-key-001-20260618102030456"));
            Assert.That(dto.Kind, Is.EqualTo("thumbnail-create"));
            Assert.That(dto.InputFiles, Is.EqualTo(new[] { "movies/sample.mp4" }));
            Assert.That(dto.OutputArtifactPath, Is.EqualTo("thumbs/sample.jpg"));
            Assert.That(dto.TimeoutMs, Is.EqualTo(150000));
            Assert.That(dto.Capabilities, Does.Contain("thumbnail-queue"));
            Assert.That(dto.Capabilities, Does.Contain("thumbnail-create"));
            Assert.That(dto.Capabilities, Does.Contain("Preferred"));
            Assert.That(dto.RequestedAtUtc, Is.EqualTo(requestedAt));
            Assert.That(dto.RequestedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(
                dto.DiagnosticContext["mainDbFullPath"],
                Is.EqualTo("%USERPROFILE%/source/repos/sample/sample.wb")
            );
            Assert.That(dto.DiagnosticContext["mainDbSessionStamp"], Is.EqualTo("42"));
            Assert.That(dto.DiagnosticContext["moviePathKey"], Is.EqualTo("movie-key-001"));
            Assert.That(dto.DiagnosticContext["tabIndex"], Is.EqualTo("3"));
            Assert.That(dto.DiagnosticContext["movieSizeBytes"], Is.EqualTo("123456"));
            Assert.That(dto.DiagnosticContext["thumbPanelPos"], Is.EqualTo("7"));
            Assert.That(dto.DiagnosticContext["thumbTimePos"], Is.EqualTo("123"));
            Assert.That(dto.DiagnosticContext["priority"], Is.EqualTo("Preferred"));
        });
    }

    [Test]
    public void QueueRequestが空でもNullを返さない()
    {
        WorkerJobRequestDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobRequestDto(
                (QueueRequest)null!,
                outputArtifactPath: null,
                timeoutMs: -1
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Does.StartWith("thumbnail-empty-"));
            Assert.That(dto.Kind, Is.EqualTo("thumbnail-create"));
            Assert.That(dto.InputFiles, Is.Empty);
            Assert.That(dto.OutputArtifactPath, Is.Empty);
            Assert.That(dto.TimeoutMs, Is.Zero);
            Assert.That(dto.Capabilities, Does.Contain("thumbnail-queue"));
            Assert.That(dto.DiagnosticContext, Is.Not.Empty);
            Assert.That(dto.DiagnosticContext["mainDbFullPath"], Is.Empty);
            Assert.That(dto.DiagnosticContext["mainDbSessionStamp"], Is.EqualTo("0"));
            Assert.That(dto.DiagnosticContext["moviePathKey"], Is.Empty);
            Assert.That(dto.DiagnosticContext["priority"], Is.EqualTo("Normal"));
        });
    }

    [Test]
    public void QueueLeaseItemをWorkerJobRequestDtoへ写せる()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();

        WorkerJobRequestDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobRequestDto(
                leasedItem,
                outputArtifactPath: "thumbs/sample.jpg",
                timeoutMs: 60000
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-movie-key-001-queue-77"));
            Assert.That(dto.Kind, Is.EqualTo("thumbnail-create"));
            Assert.That(dto.InputFiles, Is.EqualTo(new[] { "movies/sample.mp4" }));
            Assert.That(dto.OutputArtifactPath, Is.EqualTo("thumbs/sample.jpg"));
            Assert.That(dto.TimeoutMs, Is.EqualTo(60000));
            Assert.That(dto.Capabilities, Does.Contain("thumbnail-queue"));
            Assert.That(dto.Capabilities, Does.Contain("thumbnail-create"));
            Assert.That(dto.Capabilities, Does.Contain("Preferred"));
            Assert.That(dto.DiagnosticContext["queueId"], Is.EqualTo("77"));
            Assert.That(dto.DiagnosticContext["moviePathKey"], Is.EqualTo("movie-key-001"));
            Assert.That(dto.DiagnosticContext["priority"], Is.EqualTo("Preferred"));
            Assert.That(dto.DiagnosticContext["attemptCount"], Is.EqualTo("3"));
            Assert.That(dto.DiagnosticContext["ownerInstanceId"], Is.EqualTo("worker-a"));
        });
    }

    [Test]
    public void Queue進捗をWorkerJobProgressDtoへ写せる()
    {
        DateTime capturedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 12, 0, 0, 123),
            DateTimeKind.Utc
        );
        QueueDbLeaseItem leasedItem = CreateLeaseItem();

        WorkerJobProgressDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobProgressDto(
                leasedItem,
                completedCount: 2,
                totalCount: 10,
                currentParallelism: 3,
                configuredParallelism: 6,
                stage: "  running  ",
                message: "  working  ",
                capturedAtUtc: capturedAt
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-movie-key-001-queue-77"));
            Assert.That(dto.Stage, Is.EqualTo("running"));
            Assert.That(dto.CompletedCount, Is.EqualTo(2));
            Assert.That(dto.TotalCount, Is.EqualTo(10));
            Assert.That(dto.CurrentInputFile, Is.EqualTo("movies/sample.mp4"));
            Assert.That(dto.Message, Is.EqualTo("working"));
            Assert.That(dto.CapturedAtUtc, Is.EqualTo(capturedAt));
            Assert.That(dto.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(dto.Metrics["queueId"], Is.EqualTo("77"));
            Assert.That(dto.Metrics["moviePathKey"], Is.EqualTo("movie-key-001"));
            Assert.That(dto.Metrics["tabIndex"], Is.EqualTo("2"));
            Assert.That(dto.Metrics["priority"], Is.EqualTo("Preferred"));
            Assert.That(dto.Metrics["attemptCount"], Is.EqualTo("3"));
            Assert.That(dto.Metrics["ownerInstanceId"], Is.EqualTo("worker-a"));
            Assert.That(dto.Metrics["currentParallelism"], Is.EqualTo("3"));
            Assert.That(dto.Metrics["configuredParallelism"], Is.EqualTo("6"));
        });
    }

    [Test]
    public void QueueRuntimeSnapshotから最小ProgressDtoを作れる()
    {
        DateTime capturedAt = DateTime.SpecifyKind(
            new DateTime(2026, 6, 18, 12, 30, 0),
            DateTimeKind.Utc
        );
        ThumbnailProgressRuntimeSnapshot snapshot = new()
        {
            Version = 15,
            SessionCompletedCount = 4,
            SessionTotalCount = 3,
            TotalCreatedCount = 120,
            CurrentParallelism = 2,
            ConfiguredParallelism = 5,
            ActiveWorkers =
            [
                new ThumbnailProgressWorkerSnapshot
                {
                    WorkerId = 8,
                    WorkerLabel = "Thread 8",
                    MoviePath = "movies/current.mp4",
                    IsActive = true,
                },
            ],
        };

        WorkerJobProgressDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobProgressDto(
                snapshot,
                stage: "",
                message: "session",
                capturedAtUtc: capturedAt
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-progress-snapshot-15"));
            Assert.That(dto.Stage, Is.EqualTo("running"));
            Assert.That(dto.CompletedCount, Is.EqualTo(4));
            Assert.That(dto.TotalCount, Is.EqualTo(4));
            Assert.That(dto.CurrentInputFile, Is.EqualTo("movies/current.mp4"));
            Assert.That(dto.Message, Is.EqualTo("session"));
            Assert.That(dto.CapturedAtUtc, Is.EqualTo(capturedAt));
            Assert.That(dto.Metrics["version"], Is.EqualTo("15"));
            Assert.That(dto.Metrics["sessionCompletedCount"], Is.EqualTo("4"));
            Assert.That(dto.Metrics["sessionTotalCount"], Is.EqualTo("3"));
            Assert.That(dto.Metrics["totalCreatedCount"], Is.EqualTo("120"));
            Assert.That(dto.Metrics["currentParallelism"], Is.EqualTo("2"));
            Assert.That(dto.Metrics["configuredParallelism"], Is.EqualTo("5"));
            Assert.That(dto.Metrics["activeWorkerCount"], Is.EqualTo("1"));
            Assert.That(dto.Metrics["workerId"], Is.EqualTo("8"));
            Assert.That(dto.Metrics["workerLabel"], Is.EqualTo("Thread 8"));
        });
    }

    [Test]
    public void Queue実行成功結果をWorkerJobResultDtoへ写せる()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();

        WorkerJobResultDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobResultDto(
                leasedItem,
                succeeded: true,
                artifactPath: "thumbs/movie-key-001.jpg",
                elapsedMs: 2345,
                metrics: new Dictionary<string, string>
                {
                    ["engine"] = "in-process",
                    ["decodedFrames"] = "1",
                }
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-movie-key-001-queue-77"));
            Assert.That(dto.Status, Is.EqualTo("succeeded"));
            Assert.That(dto.Artifact.ArtifactKind, Is.EqualTo("thumbnail-image"));
            Assert.That(dto.Artifact.Path, Is.EqualTo("thumbs/movie-key-001.jpg"));
            Assert.That(dto.Artifact.ContentType, Is.EqualTo("image/jpeg"));
            Assert.That(dto.Artifact.SizeBytes, Is.Zero);
            Assert.That(dto.Artifact.Sha256, Is.Empty);
            Assert.That(dto.Artifact.Metadata["queueId"], Is.EqualTo("77"));
            Assert.That(dto.Artifact.Metadata["moviePathKey"], Is.EqualTo("movie-key-001"));
            Assert.That(dto.Artifact.Metadata["tabIndex"], Is.EqualTo("2"));
            Assert.That(dto.FailureReason, Is.Empty);
            Assert.That(dto.ElapsedMs, Is.EqualTo(2345));
            Assert.That(dto.Retryability, Is.EqualTo("not-retryable"));
            Assert.That(dto.Metrics["queueId"], Is.EqualTo("77"));
            Assert.That(dto.Metrics["moviePathKey"], Is.EqualTo("movie-key-001"));
            Assert.That(dto.Metrics["attemptCount"], Is.EqualTo("3"));
            Assert.That(dto.Metrics["status"], Is.EqualTo("succeeded"));
            Assert.That(dto.Metrics["failureKind"], Is.Empty);
            Assert.That(dto.Metrics["retryable"], Is.EqualTo("false"));
            Assert.That(dto.Metrics["elapsedMs"], Is.EqualTo("2345"));
            Assert.That(dto.Metrics["engine"], Is.EqualTo("in-process"));
            Assert.That(dto.Metrics["decodedFrames"], Is.EqualTo("1"));
            Assert.That(dto.Logs[0], Does.Contain("status=succeeded"));
        });
    }

    [Test]
    public void Queue実行失敗結果はfailureKindとretryableをWorkerJobResultDtoへ写せる()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();

        WorkerJobResultDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobResultDto(
                leasedItem,
                succeeded: false,
                failureKind: "TransientDecodeFailure",
                failureReason: "decode failed",
                retryable: true,
                elapsedMs: -1
            );

        Assert.Multiple(() =>
        {
            Assert.That(dto.JobId, Is.EqualTo("thumbnail-movie-key-001-queue-77"));
            Assert.That(dto.Status, Is.EqualTo("failed"));
            Assert.That(dto.Artifact.Path, Is.Empty);
            Assert.That(dto.Artifact.ArtifactKind, Is.Empty);
            Assert.That(dto.FailureReason, Is.EqualTo("decode failed"));
            Assert.That(dto.ElapsedMs, Is.Zero);
            Assert.That(dto.Retryability, Is.EqualTo("retryable"));
            Assert.That(dto.Metrics["failureKind"], Is.EqualTo("TransientDecodeFailure"));
            Assert.That(dto.Metrics["retryable"], Is.EqualTo("true"));
            Assert.That(dto.Metrics["status"], Is.EqualTo("failed"));
            Assert.That(dto.Logs, Does.Contain("failure_kind=TransientDecodeFailure"));
            Assert.That(dto.Logs, Does.Contain("failure_reason=decode failed"));
        });
    }

    [Test]
    public void WorkerJobResultDtoから実行ログ用Fieldsを作れる()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();
        WorkerJobResultDto dto =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobResultDto(
                leasedItem,
                succeeded: false,
                failureKind: "Decode",
                failureReason: "decode failed",
                retryable: true,
                elapsedMs: 123
            );

        string logFields = ThumbnailQueueWorkerContractAdapter.BuildWorkerJobResultLogFields(dto);

        Assert.Multiple(() =>
        {
            Assert.That(logFields, Does.Contain("job_id=thumbnail-movie-key-001-queue-77"));
            Assert.That(logFields, Does.Contain("worker_kind=thumbnail-create"));
            Assert.That(logFields, Does.Contain("worker_contract=worker-job-v1"));
            Assert.That(logFields, Does.Contain("status=failed"));
            Assert.That(logFields, Does.Contain("artifact_kind=''"));
            Assert.That(logFields, Does.Contain("retryability=retryable"));
            Assert.That(logFields, Does.Contain("elapsed_ms=123"));
            Assert.That(logFields, Does.Contain($"metric_count={dto.Metrics.Count}"));
            Assert.That(logFields, Does.Contain("failure_kind=Decode"));
            Assert.That(logFields, Does.Contain("failure_reason='decode failed'"));
            Assert.That(logFields, Does.Contain("output_artifact_path=''"));
            Assert.That(logFields, Does.Contain("queue_id=77"));
            Assert.That(logFields, Does.Contain("movie_path_key=movie-key-001"));
            Assert.That(logFields, Does.Contain("priority=Preferred"));
            Assert.That(logFields, Does.Contain("attempt_count=3"));
            Assert.That(
                ThumbnailQueueWorkerContractAdapter.BuildWorkerJobResultLogFields(
                    new WorkerJobResultDto()
                ),
                Does.Contain("metric_count=0")
            );
            Assert.That(
                ThumbnailQueueWorkerContractAdapter.BuildWorkerJobResultLogFields(
                    new WorkerJobResultDto { Metrics = null! }
                ),
                Does.Contain("metric_count=0")
            );
        });
    }

    [Test]
    public void Worker契約FieldsはRequestProgressResultを1行へ畳める()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();
        WorkerJobRequestDto request =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobRequestDto(
                leasedItem,
                outputArtifactPath: "thumbs/movie-key-001.jpg",
                timeoutMs: 60000
            );
        WorkerJobProgressDto progress =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobProgressDto(
                leasedItem,
                completedCount: 1,
                totalCount: 4,
                currentParallelism: 2,
                configuredParallelism: 6,
                stage: ThumbnailQueueWorkerContractAdapter.ProgressStageCompleted
            );
        WorkerJobResultDto result =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobResultDto(
                leasedItem,
                succeeded: true,
                artifactPath: "thumbs/movie-key-001.jpg",
                elapsedMs: 123
            );

        string requestFields =
            ThumbnailQueueWorkerContractAdapter.BuildWorkerJobRequestLogFields(request);
        string progressFields =
            ThumbnailQueueWorkerContractAdapter.BuildWorkerJobProgressLogFields(progress);
        string combinedFields =
            ThumbnailQueueWorkerContractAdapter.BuildWorkerQueueLogFields(
                request,
                progress,
                result
            );

        Assert.Multiple(() =>
        {
            Assert.That(requestFields, Does.Contain("job_id=thumbnail-movie-key-001-queue-77"));
            Assert.That(requestFields, Does.Contain("worker_contract=worker-job-v1"));
            Assert.That(requestFields, Does.Contain("input_count=1"));
            Assert.That(requestFields, Does.Contain("capability_count=3"));
            Assert.That(requestFields, Does.Contain("diagnostic_context_count=11"));
            Assert.That(requestFields, Does.Contain("queue_id=77"));
            Assert.That(progressFields, Does.Contain("worker_stage=completed"));
            Assert.That(progressFields, Does.Contain("worker_contract=worker-job-v1"));
            Assert.That(progressFields, Does.Contain("progress_total=4"));
            Assert.That(progressFields, Does.Contain("current_parallelism=2"));
            Assert.That(combinedFields, Does.Contain("worker_status=succeeded"));
            Assert.That(combinedFields, Does.Contain("worker_contract=worker-job-v1"));
            Assert.That(combinedFields, Does.Contain("worker_stage=completed"));
            Assert.That(combinedFields, Does.Contain($"metric_count={result.Metrics.Count}"));
            Assert.That(combinedFields, Does.Contain("progress_completed=1"));
            Assert.That(combinedFields, Does.Contain("progress_total=4"));
            Assert.That(combinedFields, Does.Contain("queue_id=77"));
            Assert.That(combinedFields, Does.Contain("priority=Preferred"));
            Assert.That(combinedFields, Does.Contain("attempt_count=3"));
            Assert.That(combinedFields, Does.Contain("current_parallelism=2"));
            Assert.That(combinedFields, Does.Contain("configured_parallelism=6"));
            Assert.That(combinedFields, Does.Contain("input_count=1"));
            Assert.That(combinedFields, Does.Contain("capability_count=3"));
            Assert.That(combinedFields, Does.Contain("diagnostic_context_count=11"));
            Assert.That(
                combinedFields,
                Does.Contain("output_artifact_path=thumbs/movie-key-001.jpg")
            );
            Assert.That(combinedFields, Does.Contain("timeout_ms=60000"));
        });
    }

    [Test]
    public void Worker契約Fieldsは失敗Skip相当の語彙も1行で読める()
    {
        QueueDbLeaseItem leasedItem = CreateLeaseItem();
        WorkerJobRequestDto request =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobRequestDto(
                leasedItem,
                outputArtifactPath: "thumbs/movie-key-001.jpg",
                timeoutMs: 60000
            );
        WorkerJobProgressDto progress =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobProgressDto(
                leasedItem,
                completedCount: 0,
                totalCount: 1,
                currentParallelism: 2,
                configuredParallelism: 6,
                stage: ThumbnailQueueWorkerContractAdapter.ProgressStageCompleted,
                message: "queue item failed"
            );
        WorkerJobResultDto result =
            ThumbnailQueueWorkerContractAdapter.ToWorkerJobResultDto(
                leasedItem,
                succeeded: false,
                failureKind: "TransientDecodeFailure",
                failureReason: "decode failed",
                retryable: true,
                elapsedMs: 321
            );

        string combinedFields =
            ThumbnailQueueWorkerContractAdapter.BuildWorkerQueueLogFields(
                request,
                progress,
                result
            );

        Assert.Multiple(() =>
        {
            Assert.That(combinedFields, Does.Contain("worker_contract=worker-job-v1"));
            Assert.That(combinedFields, Does.Contain("worker_status=failed"));
            Assert.That(combinedFields, Does.Contain("worker_stage=completed"));
            Assert.That(combinedFields, Does.Contain("retryability=retryable"));
            Assert.That(combinedFields, Does.Contain("retryable=true"));
            Assert.That(combinedFields, Does.Contain("failure_kind=TransientDecodeFailure"));
            Assert.That(combinedFields, Does.Contain("failure_reason='decode failed'"));
            Assert.That(combinedFields, Does.Contain($"metric_count={result.Metrics.Count}"));
            Assert.That(combinedFields, Does.Contain("capability_count=3"));
            Assert.That(combinedFields, Does.Contain("diagnostic_context_count=11"));
            Assert.That(combinedFields, Does.Contain("queue_id=77"));
            Assert.That(combinedFields, Does.Contain("attempt_count=3"));
            Assert.That(combinedFields, Does.Contain("current_parallelism=2"));
            Assert.That(combinedFields, Does.Contain("configured_parallelism=6"));
        });
    }

    private static QueueDbLeaseItem CreateLeaseItem()
    {
        return new QueueDbLeaseItem
        {
            QueueId = 77,
            MoviePath = "movies/sample.mp4",
            MoviePathKey = "movie-key-001",
            TabIndex = 2,
            MovieSizeBytes = 987654,
            ThumbPanelPos = 4,
            ThumbTimePos = 456,
            Priority = ThumbnailQueuePriority.Preferred,
            AttemptCount = 3,
            OwnerInstanceId = "worker-a",
            LeaseUntilUtc = DateTime.SpecifyKind(
                new DateTime(2026, 6, 18, 11, 0, 0),
                DateTimeKind.Utc
            ),
            LeaseBucketRank = 1,
            LeaseOrder = 5,
        };
    }
}

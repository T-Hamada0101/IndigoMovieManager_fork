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
                null,
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

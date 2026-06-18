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
}

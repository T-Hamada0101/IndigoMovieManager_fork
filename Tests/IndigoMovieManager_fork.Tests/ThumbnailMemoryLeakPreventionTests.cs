using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class ThumbnailMemoryLeakPreventionTests
{
    private const string EngineEnvName = "IMM_THUMB_ENGINE";

    [Test]
    public async Task CreateThumbAsync_完了後に出力ロック辞書が残留しない()
    {
        string tempRoot = CreateTempRoot();
        string? oldEngine = Environment.GetEnvironmentVariable(EngineEnvName);
        try
        {
            Environment.SetEnvironmentVariable(EngineEnvName, "auto");

            string moviePath = CreateDummyMovieFile(tempRoot);
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            var autogen = new RecordingEngine(
                "autogen",
                (ctx, _) =>
                    Task.FromResult(
                        ThumbnailCreationService.CreateSuccessResult(
                            ctx.SaveThumbFileName,
                            ctx.DurationSec
                        )
                    )
            );
            var ffmedia = new RecordingEngine("ffmediatoolkit", (_, _) => Task.FromException<ThumbnailCreateResult>(new InvalidOperationException("should not be used")));
            var ffmpeg1pass = new RecordingEngine("ffmpeg1pass", (_, _) => Task.FromException<ThumbnailCreateResult>(new InvalidOperationException("should not be used")));
            var opencv = new RecordingEngine("opencv", (_, _) => Task.FromException<ThumbnailCreateResult>(new InvalidOperationException("should not be used")));
            var service = new ThumbnailCreationService(ffmedia, ffmpeg1pass, opencv, autogen);

            Assert.That(ThumbnailCreationService.GetOutputFileLockEntryCountForTest(), Is.EqualTo(0));

            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj { MovieId = 1, Tabindex = 0, MovieFullPath = moviePath },
                dbName: "testdb",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(autogen.CreateCallCount, Is.EqualTo(1));
            Assert.That(ThumbnailCreationService.GetOutputFileLockEntryCountForTest(), Is.EqualTo(0));
        }
        finally
        {
            Environment.SetEnvironmentVariable(EngineEnvName, oldEngine);
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
}

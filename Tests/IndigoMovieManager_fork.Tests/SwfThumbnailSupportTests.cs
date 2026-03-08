using System.Drawing;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class SwfThumbnailSupportTests
{
    [Test]
    public void CreateDefault_既定タイムアウトは有限で30秒になる()
    {
        SwfThumbnailCaptureOptions options = SwfThumbnailCaptureOptions.CreateDefault(320, 240);

        Assert.That(options.ProcessTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(options.ProcessTimeout, Is.Not.EqualTo(Timeout.InfiniteTimeSpan));
    }

    [Test]
    public void TryAnalyzeFrame_壊れた画像は解析失敗で返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string path = Path.Combine(tempRoot, "broken.jpg");
            File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04]);

            bool analyzed = SwfThumbnailFrameAnalyzer.TryAnalyzeFrame(
                path,
                SwfThumbnailCaptureOptions.CreateDefault(320, 240),
                out bool isMostlyFlatBrightFrame
            );

            Assert.That(analyzed, Is.False);
            Assert.That(isMostlyFlatBrightFrame, Is.False);
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
    public void CreateRejected_処理成功フラグを明示値で保持する()
    {
        SwfThumbnailCandidate ffmpegFailure = SwfThumbnailCandidate.CreateRejected(
            2d,
            "temp.jpg",
            "ffmpeg failed",
            "exit=1",
            false,
            false
        );
        SwfThumbnailCandidate brightReject = SwfThumbnailCandidate.CreateRejected(
            5d,
            "temp.jpg",
            "blank",
            "",
            true,
            true
        );

        Assert.That(ffmpegFailure.IsProcessSucceeded, Is.False);
        Assert.That(brightReject.IsProcessSucceeded, Is.True);
    }

    [Test]
    public void IsMostlyFlatBrightFrame_白一色画像を検出する()
    {
        using Bitmap bitmap = new(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
        }

        bool actual = SwfThumbnailFrameAnalyzer.IsMostlyFlatBrightFrame(
            bitmap,
            SwfThumbnailCaptureOptions.CreateDefault(320, 240)
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public async Task HandleAsync_採用候補を既存サムネ形式へ整形して成功する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "sample.swf");
            File.WriteAllBytes(moviePath, [0x43, 0x57, 0x53, 0x09]);

            string thumbRoot = Path.Combine(tempRoot, "thumb");
            TabInfo tabInfo = new(0, "testdb", thumbRoot);
            string saveThumbPath = Path.Combine(tabInfo.OutPath, "sample.jpg");
            var handler = new SwfThumbnailRouteHandler(
                new StubSwfThumbnailGenerationService(outputPath =>
                {
                    using Bitmap bitmap = new(120, 90);
                    using Graphics g = Graphics.FromImage(bitmap);
                    g.Clear(Color.LightSkyBlue);
                    bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    return SwfThumbnailCandidate.CreateAccepted(2d, outputPath);
                })
            );

            SwfThumbnailRouteResult result = await handler.HandleAsync(
                new SwfThumbnailRouteRequest
                {
                    QueueObj = new QueueObj { MovieId = 10, Tabindex = 0, MovieFullPath = moviePath },
                    TabInfo = tabInfo,
                    MovieFullPath = moviePath,
                    SaveThumbFileName = saveThumbPath,
                    Detail = "swf_test",
                    IsResizeThumb = true,
                    IsManual = false,
                    DurationSec = 12,
                    FileSizeBytes = 1024,
                }
            );

            Assert.That(result.Result.IsSuccess, Is.True);
            Assert.That(result.ProcessEngineId, Is.EqualTo("swf-ffmpeg"));
            Assert.That(result.VideoCodec, Is.EqualTo("swf"));
            Assert.That(Path.Exists(saveThumbPath), Is.True);
            Assert.That(result.Result.PreviewFrame, Is.Not.Null);
            Assert.That(result.Result.PreviewFrame.IsValid(), Is.True);
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
    public async Task HandleAsync_候補全滅時はFlashプレースホルダーへ縮退する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "sample.swf");
            File.WriteAllBytes(moviePath, [0x46, 0x57, 0x53, 0x09]);

            string thumbRoot = Path.Combine(tempRoot, "thumb");
            TabInfo tabInfo = new(0, "testdb", thumbRoot);
            string saveThumbPath = Path.Combine(tabInfo.OutPath, "sample.jpg");
            var handler = new SwfThumbnailRouteHandler(
                new StubSwfThumbnailGenerationService(outputPath =>
                    SwfThumbnailCandidate.CreateRejected(
                        5d,
                        outputPath,
                        "ffmpeg capture failed",
                        "exit=1",
                        false,
                        false
                    )
                )
            );

            SwfThumbnailRouteResult result = await handler.HandleAsync(
                new SwfThumbnailRouteRequest
                {
                    QueueObj = new QueueObj { MovieId = 11, Tabindex = 0, MovieFullPath = moviePath },
                    TabInfo = tabInfo,
                    MovieFullPath = moviePath,
                    SaveThumbFileName = saveThumbPath,
                    Detail = "swf_test_reject",
                    IsResizeThumb = true,
                    IsManual = false,
                    DurationSec = 15,
                    FileSizeBytes = 2048,
                }
            );

            Assert.That(result.Result.IsSuccess, Is.True);
            Assert.That(result.ProcessEngineId, Is.EqualTo("swf-placeholder"));
            Assert.That(result.VideoCodec, Is.EqualTo("swf"));
            Assert.That(Path.Exists(saveThumbPath), Is.True);
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

    private sealed class StubSwfThumbnailGenerationService : SwfThumbnailGenerationService
    {
        private readonly Func<string, SwfThumbnailCandidate> candidateFactory;

        public StubSwfThumbnailGenerationService(Func<string, SwfThumbnailCandidate> candidateFactory)
        {
            this.candidateFactory = candidateFactory;
        }

        public override Task<SwfThumbnailCandidate> TryCaptureRepresentativeFrameAsync(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options,
            CancellationToken cts = default
        )
        {
            return Task.FromResult(candidateFactory(outputPath));
        }
    }
}

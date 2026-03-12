using IndigoMovieManager.Thumbnail.Engines;
using NUnit.Framework;

namespace IndigoMovieManager.Thumbnail.Test
{
    [TestFixture]
    public class AutogenRegressionTests
    {
        private const string EngineEnvName = "IMM_THUMB_ENGINE";

        [Test]
        public void Router_通常サムネはAutogenを優先する()
        {
            var autogen = new FakeEngine("autogen");
            var ffmedia = new FakeEngine("ffmediatoolkit");
            var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
            var opencv = new FakeEngine("opencv");
            var router = new ThumbnailEngineRouter([ffmedia, ffmpeg1pass, opencv, autogen]);

            var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
            var selected = router.ResolveForThumbnail(context);

            Assert.That(selected.EngineId, Is.EqualTo("autogen"));
        }

        [Test]
        public void Router_手動サムネはAutogenを優先する()
        {
            var autogen = new FakeEngine("autogen");
            var ffmedia = new FakeEngine("ffmediatoolkit");
            var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
            var opencv = new FakeEngine("opencv");
            var router = new ThumbnailEngineRouter([autogen, ffmedia, ffmpeg1pass, opencv]);

            var context = CreateContext(isManual: true, tabIndex: 0, fileSizeBytes: 1024);
            var selected = router.ResolveForThumbnail(context);

            Assert.That(selected.EngineId, Is.EqualTo("autogen"));
        }

        [Test]
        public void Router_強制環境変数がある場合は指定エンジンを使う()
        {
            string backup = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "ffmediatoolkit");

                var autogen = new FakeEngine("autogen");
                var ffmedia = new FakeEngine("ffmediatoolkit");
                var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
                var opencv = new FakeEngine("opencv");
                var router = new ThumbnailEngineRouter([autogen, ffmedia, ffmpeg1pass, opencv]);

                var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
                var selected = router.ResolveForThumbnail(context);

                Assert.That(selected.EngineId, Is.EqualTo("ffmediatoolkit"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, backup);
            }
        }

        [Test]
        public void Service_Autogen選択時のフォールバック順を維持する()
        {
            string backup = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                // 強制モードが有効だと順序生成が短絡されるため、テスト中は必ず auto 扱いにする。
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                var autogen = new FakeEngine("autogen");
                var ffmedia = new FakeEngine("ffmediatoolkit");
                var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
                var opencv = new FakeEngine("opencv");
                var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
                var order = BuildThumbnailEngineOrder(
                    ffmedia,
                    ffmpeg1pass,
                    opencv,
                    autogen,
                    autogen,
                    context
                );
                string actual = string.Join(">", order.Select(x => x.EngineId));

                Assert.That(actual, Is.EqualTo("autogen>ffmediatoolkit>ffmpeg1pass>opencv"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, backup);
            }
        }

        [Test]
        public void Service_RecoveryでFfmpeg1pass選択時もOpenCvへ落ちる()
        {
            string backup = Environment.GetEnvironmentVariable(EngineEnvName);
            try
            {
                Environment.SetEnvironmentVariable(EngineEnvName, "auto");

                var autogen = new FakeEngine("autogen");
                var ffmedia = new FakeEngine("ffmediatoolkit");
                var ffmpeg1pass = new FakeEngine("ffmpeg1pass");
                var opencv = new FakeEngine("opencv");
                var context = CreateContext(isManual: false, tabIndex: 0, fileSizeBytes: 1024);
                context.QueueObj.AttemptCount = 1;
                var order = BuildThumbnailEngineOrder(
                    ffmedia,
                    ffmpeg1pass,
                    opencv,
                    autogen,
                    ffmpeg1pass,
                    context
                );
                string actual = string.Join(">", order.Select(x => x.EngineId));

                Assert.That(actual, Is.EqualTo("ffmpeg1pass>opencv"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EngineEnvName, backup);
            }
        }

        private static ThumbnailJobContext CreateContext(
            bool isManual,
            int tabIndex,
            long fileSizeBytes
        )
        {
            return new ThumbnailJobContext
            {
                QueueObj = new QueueObj
                {
                    Tabindex = tabIndex,
                    MovieId = 1,
                    MovieFullPath = @"C:\dummy\movie.mp4",
                },
                TabInfo = new TabInfo(tabIndex, "testdb"),
                ThumbInfo = new ThumbInfo(),
                MovieFullPath = @"C:\dummy\movie.mp4",
                SaveThumbFileName = @"C:\dummy\out.jpg",
                IsResizeThumb = true,
                IsManual = isManual,
                DurationSec = 120,
                FileSizeBytes = fileSizeBytes,
                AverageBitrateMbps = 8,
                HasEmojiPath = false,
                VideoCodec = "h264",
            };
        }

        private static List<IThumbnailGenerationEngine> BuildThumbnailEngineOrder(
            IThumbnailGenerationEngine ffmedia,
            IThumbnailGenerationEngine ffmpeg1pass,
            IThumbnailGenerationEngine opencv,
            IThumbnailGenerationEngine autogen,
            IThumbnailGenerationEngine selectedEngine,
            ThumbnailJobContext context
        )
        {
            ThumbnailEngineCatalog catalog = new(
                ffmedia,
                ffmpeg1pass,
                opencv,
                autogen
            );
            return catalog.BuildExecutionOrder(selectedEngine, context);
        }

        private sealed class FakeEngine : IThumbnailGenerationEngine
        {
            public FakeEngine(string engineId)
            {
                EngineId = engineId;
                EngineName = engineId;
            }

            public string EngineId { get; }
            public string EngineName { get; }

            public bool CanHandle(ThumbnailJobContext context)
            {
                return true;
            }

            public Task<ThumbnailCreateResult> CreateAsync(
                ThumbnailJobContext context,
                CancellationToken cts = default
            )
            {
                return Task.FromResult(
                    ThumbnailResultFactory.CreateFailed(
                        context?.SaveThumbFileName ?? "",
                        context?.DurationSec,
                        "test"
                    )
                );
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
}

using System.Diagnostics;
using System.Drawing;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FfmpegOnePassThumbnailGenerationEngineTests
{
    [Test]
    public void ShouldUseTolerantInput_WmvとAsfだけtrueを返す()
    {
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseTolerantInput(@"E:\video\old.wmv"),
            Is.True
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseTolerantInput(@"E:\video\old.asf"),
            Is.True
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseTolerantInput(@"E:\video\movie.mp4"),
            Is.False
        );
    }

    [Test]
    public void AddInputArguments_Wmv時は寛容フラグ付きで入力後シークになる()
    {
        ProcessStartInfo psi = new();

        FfmpegOnePassThumbnailGenerationEngine.AddInputArguments(
            psi,
            @"E:\video\old.wmv",
            "48",
            useTolerantInput: true
        );

        Assert.That(
            psi.ArgumentList,
            Is.EqualTo(
                new[]
                {
                    "-err_detect",
                    "ignore_err",
                    "-fflags",
                    "+genpts+igndts+ignidx+discardcorrupt",
                    "-i",
                    @"E:\video\old.wmv",
                    "-ss",
                    "48",
                }
            )
        );
    }

    [Test]
    public void AddInputArguments_通常動画では従来どおり入力前シークになる()
    {
        ProcessStartInfo psi = new();

        FfmpegOnePassThumbnailGenerationEngine.AddInputArguments(
            psi,
            @"E:\video\movie.mp4",
            "12.5",
            useTolerantInput: false
        );

        Assert.That(
            psi.ArgumentList,
            Is.EqualTo(new[] { "-ss", "12.5", "-i", @"E:\video\movie.mp4" })
        );
    }

    [Test]
    public void ShouldUseCandidateFrameFiltering_RecoveryWmvかつ10パネル以下でtrueを返す()
    {
        ThumbnailJobContext recoveryWmv = new()
        {
            MovieFullPath = @"E:\video\old.wmv",
            QueueObj = new QueueObj { AttemptCount = 1 },
            TabInfo = new TabInfo(0, "testdb"),
        };
        ThumbnailJobContext normalMp4 = new()
        {
            MovieFullPath = @"E:\video\movie.mp4",
            QueueObj = new QueueObj { AttemptCount = 1 },
            TabInfo = new TabInfo(0, "testdb"),
        };
        ThumbnailJobContext firstAttemptWmv = new()
        {
            MovieFullPath = @"E:\video\old.wmv",
            QueueObj = new QueueObj { AttemptCount = 0 },
            TabInfo = new TabInfo(0, "testdb"),
        };
        ThumbnailJobContext recoveryWmv10Panels = new()
        {
            MovieFullPath = @"E:\video\old.wmv",
            QueueObj = new QueueObj { AttemptCount = 1 },
            TabInfo = new TabInfo(4, "testdb"),
        };
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseCandidateFrameFiltering(
                recoveryWmv
            ),
            Is.True
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseCandidateFrameFiltering(normalMp4),
            Is.False
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseCandidateFrameFiltering(
                firstAttemptWmv
            ),
            Is.False
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseCandidateFrameFiltering(
                recoveryWmv10Panels
            ),
            Is.True
        );
    }

    [Test]
    public void SelectCandidateIndices_黒コマを避けつつ必要枚数を返す()
    {
        List<int> actual = FfmpegOnePassThumbnailGenerationEngine.SelectCandidateIndices(
            [false, true, true, false, true, true],
            3
        );

        Assert.That(actual, Is.EqualTo(new[] { 1, 4, 5 }));
    }

    [Test]
    public void IsMostlyBlackPanel_黒画像だけtrueを返す()
    {
        using Bitmap black = new(32, 32);
        using Bitmap bright = new(32, 32);
        using (Graphics g = Graphics.FromImage(black))
        {
            g.Clear(Color.Black);
        }
        using (Graphics g = Graphics.FromImage(bright))
        {
            g.Clear(Color.LightSkyBlue);
        }

        Assert.That(FfmpegOnePassThumbnailGenerationEngine.IsMostlyBlackPanel(black), Is.True);
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.IsMostlyBlackPanel(bright),
            Is.False
        );
    }

    [Test]
    public void ResolveSampleIntervalSec_RecoveryWmvでは取得間隔を前半へ圧縮する()
    {
        double actual = FfmpegOnePassThumbnailGenerationEngine.ResolveSampleIntervalSec(
            intervalSec: 18,
            candidatePanelCount: 20,
            panelCount: 10,
            useCandidateFiltering: true,
            durationSec: 195
        );

        Assert.That(actual, Is.LessThan(2d));
        Assert.That(actual, Is.GreaterThanOrEqualTo(0.25d));
    }

    [Test]
    public void ResolvePackedRecoveryWindowSec_動画長に応じて4秒から20秒へ収める()
    {
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ResolvePackedRecoveryWindowSec(2),
            Is.EqualTo(4d)
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ResolvePackedRecoveryWindowSec(40),
            Is.EqualTo(10d)
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ResolvePackedRecoveryWindowSec(300),
            Is.EqualTo(20d)
        );
    }

    [Test]
    public void ShouldUseShortClipSeekFallback_1秒以下かつ5パネル以下だけtrueを返す()
    {
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseShortClipSeekFallback(0.069, 1),
            Is.True
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseShortClipSeekFallback(0.98, 5),
            Is.True
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseShortClipSeekFallback(1.2, 1),
            Is.False
        );
        Assert.That(
            FfmpegOnePassThumbnailGenerationEngine.ShouldUseShortClipSeekFallback(0.5, 10),
            Is.False
        );
    }

    [Test]
    public void BuildShortClipSeekCandidates_短尺候補を重複なく返し元開始秒は除外する()
    {
        IReadOnlyList<double> actual =
            FfmpegOnePassThumbnailGenerationEngine.BuildShortClipSeekCandidates(0.069, 0d);

        Assert.That(actual, Does.Contain(0.001d));
        Assert.That(actual, Does.Contain(0.005d));
        Assert.That(actual, Does.Contain(0.01d));
        Assert.That(actual, Does.Contain(0.016d));
        Assert.That(actual, Does.Contain(0.033d));
        Assert.That(actual, Does.Contain(0.05d));
        Assert.That(actual, Does.Contain(0.069d * 0.5d).Or.Contain(0.034d));
        Assert.That(actual, Does.Not.Contain(0d));
        Assert.That(actual.Distinct().Count(), Is.EqualTo(actual.Count));
    }

    [Test]
    public void BuildShortClipSeekCandidates_動画長超過候補は除外する()
    {
        IReadOnlyList<double> actual =
            FfmpegOnePassThumbnailGenerationEngine.BuildShortClipSeekCandidates(0.033, 0.01d);

        Assert.That(actual.All(x => x < 0.033d), Is.True);
        Assert.That(actual, Does.Not.Contain(0.01d));
    }

    [Test]
    public void ResolveProcessTimeout_未指定時はタイムアウトなしを返す()
    {
        TimeSpan actual = FfmpegOnePassThumbnailGenerationEngine.ResolveProcessTimeout(
            panelCount: 10,
            durationSec: TimeSpan.FromHours(2).TotalSeconds,
            useTolerantInput: true,
            useCandidateFiltering: true
        );

        Assert.That(actual, Is.EqualTo(Timeout.InfiniteTimeSpan));
    }

    [Test]
    public void ResolveProcessTimeout_環境変数指定を優先する()
    {
        const string envName = "IMM_THUMB_FFMPEG_TIMEOUT_SEC";
        string? original = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, "55");

            TimeSpan actual = FfmpegOnePassThumbnailGenerationEngine.ResolveProcessTimeout(
                panelCount: 3,
                durationSec: 600,
                useTolerantInput: false,
                useCandidateFiltering: false
            );

            Assert.That(actual, Is.EqualTo(TimeSpan.FromSeconds(55)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }

    [Test]
    public void ResolveChildProcessPriorityClass_未指定時はIdleを返す()
    {
        const string envName = "IMM_THUMB_FFMPEG_PRIORITY";
        string? original = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, null);

            ProcessPriorityClass? actual =
                FfmpegOnePassThumbnailGenerationEngine.ResolveChildProcessPriorityClass();

            Assert.That(actual, Is.EqualTo(ProcessPriorityClass.Idle));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }

    [Test]
    public void ResolveChildProcessPriorityClass_off指定時は変更しない()
    {
        const string envName = "IMM_THUMB_FFMPEG_PRIORITY";
        string? original = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, "off");

            ProcessPriorityClass? actual =
                FfmpegOnePassThumbnailGenerationEngine.ResolveChildProcessPriorityClass();

            Assert.That(actual, Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }

    [Test]
    public void ResolveChildProcessPriorityClass_環境変数でIdle指定を受け付ける()
    {
        const string envName = "IMM_THUMB_FFMPEG_PRIORITY";
        string? original = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, "Idle");

            ProcessPriorityClass? actual =
                FfmpegOnePassThumbnailGenerationEngine.ResolveChildProcessPriorityClass();

            Assert.That(actual, Is.EqualTo(ProcessPriorityClass.Idle));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, original);
        }
    }
}

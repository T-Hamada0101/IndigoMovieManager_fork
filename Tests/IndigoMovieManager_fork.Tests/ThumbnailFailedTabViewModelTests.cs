using System.Reflection;
using System.Text;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailFailedTabViewModelTests
{
    [Test]
    public void ToThumbnailFailedRecordViewModel_ExtraJsonの補助列を復元できる()
    {
        ThumbnailFailureRecord record = new()
        {
            RecordId = 77,
            MainDbPathHash = "abcd1234",
            MoviePath = @"E:\movies\sample.mkv",
            MoviePathKey = "key-1",
            TabIndex = 2,
            MovieSizeBytes = 12345,
            PanelType = "grid",
            QueueStatus = "Pending",
            FailureKind = ThumbnailFailureKind.HangSuspected,
            Reason = "retry-scheduled",
            AttemptCount = 1,
            LastError = "thumbnail processing timeout",
            OwnerInstanceId = "owner-1",
            WorkerRole = "Normal",
            EngineId = "",
            LeaseUntilUtc = "2026-03-10T10:00:00.000Z",
            StartedAtUtc = "2026-03-10T09:59:00.000Z",
            OccurredAtUtc = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 3, 10, 10, 0, 1, DateTimeKind.Utc),
            ExtraJson =
                """
                {
                  "QueueId": 99,
                  "ThumbPanelPos": 3,
                  "ThumbTimePos": 15,
                  "FailureKindSource": "queue",
                  "material_duration_sec": 12.3,
                  "engine_attempted": "autogen",
                  "engine_succeeded": false,
                  "seek_strategy": "original",
                  "seek_sec": 0,
                  "repair_attempted": true,
                  "repair_succeeded": false,
                  "preflight_branch": "unsupported-codec",
                  "result_signature": "no-frames-decoded",
                  "repro_confirmed": false,
                  "recovery_route": "retry",
                  "decision_basis": "postprocess-placeholder:placeholder-create-failed",
                  "WasRunning": true,
                  "AttemptCountAfter": 2,
                  "MovieExists": false,
                  "ResultFailureStage": "postprocess-placeholder",
                  "ResultPolicyDecision": "placeholder-create-failed",
                  "ResultPlaceholderAction": "failed",
                  "ResultPlaceholderKind": "UnsupportedCodec",
                  "ResultFinalizerAction": "deleted",
                  "ResultFinalizerDetail": "cleanup"
                }
                """
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "ToThumbnailFailedRecordViewModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        ThumbnailFailedRecordViewModel vm =
            (ThumbnailFailedRecordViewModel?)method!.Invoke(null, [record])
            ?? throw new AssertionException("view model conversion returned null");

        Assert.Multiple(() =>
        {
            Assert.That(vm.QueueId, Is.EqualTo(99));
            Assert.That(vm.ThumbPanelPos, Is.EqualTo(3));
            Assert.That(vm.ThumbTimePos, Is.EqualTo(15));
            Assert.That(vm.FailureKindSource, Is.EqualTo("queue"));
            Assert.That(vm.MaterialDurationSec, Is.EqualTo(12.3));
            Assert.That(vm.EngineAttempted, Is.EqualTo("autogen"));
            Assert.That(vm.EngineSucceeded, Is.False);
            Assert.That(vm.SeekStrategy, Is.EqualTo("original"));
            Assert.That(vm.SeekSec, Is.EqualTo(0));
            Assert.That(vm.RepairAttempted, Is.True);
            Assert.That(vm.RepairSucceeded, Is.False);
            Assert.That(vm.PreflightBranch, Is.EqualTo("unsupported-codec"));
            Assert.That(vm.ResultSignature, Is.EqualTo("no-frames-decoded"));
            Assert.That(vm.ReproConfirmed, Is.False);
            Assert.That(vm.RecoveryRoute, Is.EqualTo("retry"));
            Assert.That(vm.DecisionBasis, Is.EqualTo("postprocess-placeholder:placeholder-create-failed"));
            Assert.That(vm.WasRunning, Is.True);
            Assert.That(vm.AttemptCountAfter, Is.EqualTo(2));
            Assert.That(vm.MovieExists, Is.False);
            Assert.That(vm.ResultFailureStage, Is.EqualTo("postprocess-placeholder"));
            Assert.That(vm.ResultPolicyDecision, Is.EqualTo("placeholder-create-failed"));
            Assert.That(vm.ResultPlaceholderAction, Is.EqualTo("failed"));
            Assert.That(vm.ResultPlaceholderKind, Is.EqualTo("UnsupportedCodec"));
            Assert.That(vm.ResultFinalizerAction, Is.EqualTo("deleted"));
            Assert.That(vm.ResultFinalizerDetail, Is.EqualTo("cleanup"));
        });
    }

    [Test]
    public void ToThumbnailFailedRecordViewModel_workthree最小fixtureのsnake_caseを復元できる()
    {
        string fixturePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "workthree_failuredb_minimal.json"
        );
        Assert.That(File.Exists(fixturePath), Is.True, fixturePath);

        string extraJson = File.ReadAllText(fixturePath, Encoding.UTF8);
        ThumbnailFailureRecord record = new()
        {
            RecordId = 78,
            MainDbPathHash = "wthree01",
            MoviePath = @"E:\sample\35967.mp4",
            MoviePathKey = "key-35967",
            TabIndex = 2,
            MovieSizeBytes = 999999,
            PanelType = "grid",
            QueueStatus = "Done",
            FailureKind = ThumbnailFailureKind.TransientDecodeFailure,
            Reason = "seek-1200",
            AttemptCount = 0,
            LastError = "No frames decoded",
            OwnerInstanceId = "owner-2",
            WorkerRole = "explicit-test",
            EngineId = "ffmpeg1pass",
            LeaseUntilUtc = "",
            StartedAtUtc = "",
            OccurredAtUtc = new DateTime(2026, 3, 11, 1, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 3, 11, 1, 0, 1, DateTimeKind.Utc),
            ExtraJson = extraJson,
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "ToThumbnailFailedRecordViewModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        ThumbnailFailedRecordViewModel vm =
            (ThumbnailFailedRecordViewModel?)method!.Invoke(null, [record])
            ?? throw new AssertionException("view model conversion returned null");

        Assert.Multiple(() =>
        {
            Assert.That(vm.FailureKindSource, Is.EqualTo("playground"));
            Assert.That(vm.MaterialDurationSec, Is.EqualTo(2872.529));
            Assert.That(vm.EngineAttempted, Is.EqualTo("ffmpeg1pass"));
            Assert.That(vm.EngineSucceeded, Is.True);
            Assert.That(vm.SeekStrategy, Is.EqualTo("midpoint"));
            Assert.That(vm.SeekSec, Is.EqualTo(1200));
            Assert.That(vm.RepairAttempted, Is.False);
            Assert.That(vm.RepairSucceeded, Is.False);
            Assert.That(vm.PreflightBranch, Is.EqualTo("none"));
            Assert.That(vm.ResultSignature, Is.EqualTo("no-frames-decoded"));
            Assert.That(vm.ReproConfirmed, Is.True);
            Assert.That(vm.RecoveryRoute, Is.EqualTo("ffmpeg1pass"));
            Assert.That(vm.DecisionBasis, Is.EqualTo("autogen-no-frames-longclip"));
        });
    }

    [Test]
    public void ToThumbnailFailedRecordViewModel_補助列のsnake_caseも復元できる()
    {
        ThumbnailFailureRecord record = new()
        {
            RecordId = 79,
            MainDbPathHash = "snakecase1",
            MoviePath = @"E:\sample\snakecase.mkv",
            MoviePathKey = "key-snake",
            TabIndex = 3,
            MovieSizeBytes = 54321,
            PanelType = "list",
            QueueStatus = "Failed",
            FailureKind = ThumbnailFailureKind.PhysicalCorruption,
            Reason = "final-failed",
            AttemptCount = 3,
            LastError = "Failed to open input: End of file",
            OccurredAtUtc = new DateTime(2026, 3, 11, 2, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 3, 11, 2, 0, 1, DateTimeKind.Utc),
            ExtraJson =
                """
                {
                  "thumb_panel_pos": 7,
                  "thumb_time_pos": 33,
                  "was_running": true,
                  "attempt_count_after": 4,
                  "movie_exists": false,
                  "result_failure_stage": "engine-open",
                  "result_policy_decision": "ffmpeg1pass-failed",
                  "result_placeholder_action": "skipped",
                  "result_placeholder_kind": "None",
                  "result_finalizer_action": "kept-error-jpg",
                  "result_finalizer_detail": "eof-signature"
                }
                """
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "ToThumbnailFailedRecordViewModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        ThumbnailFailedRecordViewModel vm =
            (ThumbnailFailedRecordViewModel?)method!.Invoke(null, [record])
            ?? throw new AssertionException("view model conversion returned null");

        Assert.Multiple(() =>
        {
            Assert.That(vm.ThumbPanelPos, Is.EqualTo(7));
            Assert.That(vm.ThumbTimePos, Is.EqualTo(33));
            Assert.That(vm.WasRunning, Is.True);
            Assert.That(vm.AttemptCountAfter, Is.EqualTo(4));
            Assert.That(vm.MovieExists, Is.False);
            Assert.That(vm.ResultFailureStage, Is.EqualTo("engine-open"));
            Assert.That(vm.ResultPolicyDecision, Is.EqualTo("ffmpeg1pass-failed"));
            Assert.That(vm.ResultPlaceholderAction, Is.EqualTo("skipped"));
            Assert.That(vm.ResultPlaceholderKind, Is.EqualTo("None"));
            Assert.That(vm.ResultFinalizerAction, Is.EqualTo("kept-error-jpg"));
            Assert.That(vm.ResultFinalizerDetail, Is.EqualTo("eof-signature"));
        });
    }

    [Test]
    public void IsThumbnailFailedRecordMatched_ResultSignatureで部分一致絞り込みできる()
    {
        ThumbnailFailedRecordViewModel item = new()
        {
            ResultSignature = "no-frames-decoded",
            RecoveryRoute = "ffmpeg1pass",
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "IsThumbnailFailedRecordMatched",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        bool matched = (bool)method!.Invoke(
            null,
            [item, "frames", ""]
        )!;

        Assert.That(matched, Is.True);
    }

    [Test]
    public void IsThumbnailFailedRecordMatched_RecoveryRouteで部分一致絞り込みできる()
    {
        ThumbnailFailedRecordViewModel item = new()
        {
            ResultSignature = "near-black",
            RecoveryRoute = "retry-onepass",
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "IsThumbnailFailedRecordMatched",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        bool matched = (bool)method!.Invoke(
            null,
            [item, "", "onepass"]
        )!;

        Assert.That(matched, Is.True);
    }

    [Test]
    public void IsThumbnailFailedRecordMatched_どちらかが不一致なら除外する()
    {
        ThumbnailFailedRecordViewModel item = new()
        {
            ResultSignature = "no-frames-decoded",
            RecoveryRoute = "ffmpeg1pass",
        };

        MethodInfo? method = typeof(IndigoMovieManager.MainWindow).GetMethod(
            "IsThumbnailFailedRecordMatched",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        bool matched = (bool)method!.Invoke(
            null,
            [item, "frames", "retry"]
        )!;

        Assert.That(matched, Is.False);
    }
}

using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiHangScrollBurstCorrelationSourceTests
{
    [Test]
    public void HeartbeatSampleはprobe投入時刻を保持しpendingとcompleteで同一起点を渡す()
    {
        string source = GetRepoText("Views", "Main", "UiHangHeartbeatMonitor.cs");
        string pendingMethod = GetMethodBlock(
            source,
            "private void PublishPendingDelayIfNeeded("
        );
        string completeMethod = GetMethodBlock(source, "private void CompleteProbe(");

        Assert.That(
            source,
            Does.Contain("internal readonly record struct UiHangHeartbeatSample(")
        );
        Assert.That(source, Does.Contain("long PostedTimestamp = 0"));
        AssertSampleUsesPostedTimestamp(pendingMethod, "true");
        AssertSampleUsesPostedTimestamp(completeMethod, "false");
    }

    [Test]
    public void ScrollBurstSnapshotはburst開始時刻を保持する()
    {
        string coordinatorSource = GetRepoText(
            "Views",
            "Main",
            "UiHangNotificationCoordinator.cs"
        );
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string snapshotMethod = GetMethodBlock(
            viewportSource,
            "private PlayerScrollBurstSnapshot GetPlayerScrollBurstSnapshot()"
        );

        Assert.That(
            coordinatorSource,
            Does.Contain("internal readonly record struct PlayerScrollBurstSnapshot(")
        );
        Assert.That(coordinatorSource, Does.Contain("long StartedTimestamp = 0"));
        Assert.That(
            snapshotMethod,
            Does.Contain("Volatile.Read(ref _playerThumbnailScrollStartedTimestamp)")
        );
        Assert.That(
            snapshotMethod,
            Does.Contain("new PlayerScrollBurstSnapshot(burstId, isActive, startedTimestamp)")
        );
    }

    [Test]
    public void Sample起点がburst開始前なら相関せず開始後だけ同じburstを返す()
    {
        string source = GetRepoText("Views", "Main", "UiHangNotificationCoordinator.cs");
        string sampleMethod = GetMethodBlock(source, "private void HandleHeartbeatSample(");
        string correlationMethod = GetMethodBlock(
            source,
            "private PlayerScrollBurstSnapshot GetPlayerScrollBurstSnapshot(UiHangHeartbeatSample sample)"
        );

        Assert.That(
            sampleMethod,
            Does.Contain("GetPlayerScrollBurstSnapshot(sample);")
        );
        Assert.That(correlationMethod, Does.Contain("sample.PostedTimestamp"));
        Assert.That(correlationMethod, Does.Contain("snapshot.StartedTimestamp"));
        Assert.That(
            correlationMethod,
            Does.Contain("sample.PostedTimestamp >= snapshot.StartedTimestamp")
        );
        Assert.That(correlationMethod, Does.Contain("PlayerScrollBurstSnapshot.Inactive"));
        Assert.That(correlationMethod, Does.Contain("? snapshot"));
    }

    [Test]
    public void Heartbeatとburst相関はStopwatch時計だけを使う()
    {
        string heartbeatSource = GetRepoText("Views", "Main", "UiHangHeartbeatMonitor.cs");
        string coordinatorSource = GetRepoText(
            "Views",
            "Main",
            "UiHangNotificationCoordinator.cs"
        );
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );

        Assert.That(heartbeatSource, Does.Contain("Stopwatch.GetTimestamp()"));
        Assert.That(
            viewportSource,
            Does.Contain("_playerThumbnailScrollStartedTimestamp = Stopwatch.GetTimestamp();")
        );
        Assert.That(coordinatorSource, Does.Not.Contain("DateTime.UtcNow"));
        Assert.That(coordinatorSource, Does.Not.Contain("Environment.TickCount"));
    }

    [Test]
    public void Hang検出と更新はログ時だけscroll_snapshotを読み既存行へ相関fieldを足す()
    {
        string source = GetRepoText("Views", "Main", "UiHangNotificationCoordinator.cs");
        string sampleMethod = GetMethodBlock(source, "private void HandleHeartbeatSample(");

        Assert.That(sampleMethod, Does.Contain("ui hang detected:"));
        Assert.That(sampleMethod, Does.Contain("ui hang updated:"));
        Assert.That(sampleMethod, Does.Contain("burst_id={scrollSnapshot.BurstId}"));
        Assert.That(sampleMethod, Does.Contain("scroll_active={scrollSnapshot.IsActive"));
        Assert.That(
            CountOccurrences(sampleMethod, "GetPlayerScrollBurstSnapshot(sample);"),
            Is.EqualTo(2)
        );
    }

    [Test]
    public void CoordinatorはFuncの軽量snapshotを保持しstatic_globalを追加しない()
    {
        string source = GetRepoText("Views", "Main", "UiHangNotificationCoordinator.cs");

        Assert.That(source, Does.Contain("Func<PlayerScrollBurstSnapshot>"));
        Assert.That(source, Does.Contain("Volatile.Write(ref _playerScrollBurstSnapshotProvider"));
        Assert.That(source, Does.Not.Contain("static PlayerScrollBurstSnapshot _"));
    }

    [Test]
    public void 既存のInput優先度と閾値と相関ログfieldを維持する()
    {
        string heartbeatSource = GetRepoText("Views", "Main", "UiHangHeartbeatMonitor.cs");
        string coordinatorSource = GetRepoText(
            "Views",
            "Main",
            "UiHangNotificationCoordinator.cs"
        );

        Assert.That(heartbeatSource, Does.Contain("DispatcherPriority.Input"));
        Assert.That(
            coordinatorSource,
            Does.Contain("private const int DetectThresholdMs = 250;")
        );
        Assert.That(
            coordinatorSource,
            Does.Contain("private const int WarningThresholdMs = 1000;")
        );
        Assert.That(coordinatorSource, Does.Contain("burst_id={scrollSnapshot.BurstId}"));
        Assert.That(coordinatorSource, Does.Contain("scroll_active={scrollSnapshot.IsActive"));
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertSampleUsesPostedTimestamp(string method, string pendingValue)
    {
        Assert.That(method, Does.Contain("new UiHangHeartbeatSample("));
        Assert.That(method, Does.Contain("GetElapsedMilliseconds(postedTimestamp)"));
        Assert.That(method, Does.Contain($"{pendingValue},"));
        Assert.That(method, Does.Contain("postedTimestamp"));
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"method not found: {signature}");
        int bodyStart = source.IndexOf('{', start);
        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(start, index - start + 1);
            }
        }

        Assert.Fail($"method end not found: {signature}");
        return string.Empty;
    }

    private static string GetRepoText(params string[] parts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string path = Path.Combine([current.FullName, .. parts]);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            current = current.Parent;
        }

        Assert.Fail($"repo file not found: {Path.Combine(parts)}");
        return string.Empty;
    }
}

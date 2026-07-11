using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiHangHeartbeatInputPrioritySourceTests
{
    [Test]
    public void HeartbeatはInput優先度で単一pending_probeだけを予約する()
    {
        string source = GetRepoText("Views", "Main", "UiHangHeartbeatMonitor.cs");
        string queueMethod = GetMethodBlock(source, "private void TryQueueProbe(");

        Assert.That(queueMethod, Does.Contain("DispatcherPriority.Input"));
        Assert.That(queueMethod, Does.Not.Contain("DispatcherPriority.Background"));
        Assert.That(queueMethod, Does.Contain("if (_cts == null || _probePending)"));
        Assert.That(queueMethod, Does.Contain("_probePending = true;"));
        Assert.That(CountOccurrences(queueMethod, "_dispatcher.BeginInvoke("), Is.EqualTo(1));
    }

    [Test]
    public void Monitor閾値とCoordinator表示契約は優先度変更で変えない()
    {
        string source = GetRepoText("Views", "Main", "UiHangNotificationCoordinator.cs");

        Assert.That(source, Does.Contain("private const int DetectThresholdMs = 250;"));
        Assert.That(source, Does.Contain("private const int WarningThresholdMs = 1000;"));
        Assert.That(source, Does.Contain("private const int ShowConsecutiveCount = 2;"));
        Assert.That(source, Does.Contain("private const int RecoverConsecutiveCount = 3;"));
        Assert.That(source, Does.Contain("UiHangNotificationLevel.Caution => $\"{activityLabel}で応答低下を検知\""));
        Assert.That(source, Does.Contain("UiHangNotificationLevel.Warning => $\"{activityLabel}を継続中\""));
        Assert.That(source, Does.Contain("UiHangNotificationLevel.Critical => $\"{activityLabel}の応答停止の可能性があります\""));
    }

    [Test]
    public void CoordinatorはPlayer_burst相関fieldを維持する()
    {
        string source = GetRepoText("Views", "Main", "UiHangNotificationCoordinator.cs");
        string sampleMethod = GetMethodBlock(source, "private void HandleHeartbeatSample(");

        Assert.That(sampleMethod, Does.Contain("burst_id={scrollSnapshot.BurstId}"));
        Assert.That(sampleMethod, Does.Contain("scroll_active={scrollSnapshot.IsActive"));
        Assert.That(CountOccurrences(sampleMethod, "GetPlayerScrollBurstSnapshot();"), Is.EqualTo(2));
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

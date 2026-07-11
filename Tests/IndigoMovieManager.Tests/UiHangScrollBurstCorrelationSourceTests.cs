using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiHangScrollBurstCorrelationSourceTests
{
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
            CountOccurrences(sampleMethod, "GetPlayerScrollBurstSnapshot();"),
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

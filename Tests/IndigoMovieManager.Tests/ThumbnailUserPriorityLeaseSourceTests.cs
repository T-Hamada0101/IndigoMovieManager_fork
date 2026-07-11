using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailUserPriorityLeaseSourceTests
{
    [Test]
    public void MainWindow_入力優先状態をQueueのLease境界へ接続する()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailCreation.cs");
        string method = ExtractMethod(source, "private async Task CheckThumbAsync(");

        Assert.That(
            method,
            Does.Contain("shouldDeferLeaseResolver: IsUserPriorityWorkActive")
        );
        Assert.That(
            method,
            Does.Contain("log: message => DebugRuntimeLog.Write(\"queue-consumer\", message)")
        );
    }

    [Test]
    public void Queue_入力優先中は新規Leaseだけを止め解除後に自然再開する()
    {
        string gateSource = GetRepoText(
            "src",
            "IndigoMovieManager.Thumbnail.Queue",
            "ThumbnailLeaseDeferralGate.cs"
        );
        string coordinatorSource = GetRepoText(
            "src",
            "IndigoMovieManager.Thumbnail.Queue",
            "ThumbnailLeaseCoordinator.cs"
        );

        Assert.That(gateSource, Does.Contain("consumer lease deferred: reason=user-priority"));
        Assert.That(
            gateSource,
            Does.Contain("consumer lease resumed: reason=user-priority-released")
        );
        Assert.That(coordinatorSource, Does.Contain("WaitUntilLeaseAllowedAsync"));
        Assert.That(
            coordinatorSource.IndexOf("WaitUntilLeaseAllowedAsync", StringComparison.Ordinal),
            Is.LessThan(coordinatorSource.IndexOf("AcquireLeasedItems(", StringComparison.Ordinal))
        );
    }

    [Test]
    public void UserPriority境界_Thumbnail常駐Taskを再起動しない()
    {
        string source = GetRepoText("Watcher", "MainWindow.UserPriorityRuntime.cs");
        string beginMethod = ExtractMethod(source, "private void BeginUserPriorityWork(");
        string endMethod = ExtractMethod(source, "private void EndUserPriorityWork(");

        Assert.That(beginMethod, Does.Not.Contain("RestartThumbnailTask"));
        Assert.That(beginMethod, Does.Not.Contain("_thumbCheckCts"));
        Assert.That(endMethod, Does.Not.Contain("RestartThumbnailTask"));
        Assert.That(endMethod, Does.Not.Contain("_thumbCheckCts"));
    }

    private static string GetRepoText(
        string firstRelativePathPart,
        params string[] remainingRelativePathParts
    )
    {
        string repoRoot = FindRepoRoot();
        string candidate = Path.Combine(
            [repoRoot, firstRelativePathPart, .. remainingRelativePathParts]
        );
        Assert.That(File.Exists(candidate), Is.True, $"{candidate} が見つかりません。");
        return File.ReadAllText(candidate).Replace("\r\n", "\n");
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath)!);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
        {
            current = current.Parent;
        }

        Assert.That(current, Is.Not.Null, "repo root が見つかりません。");
        return current!.FullName;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0));

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の終端を解決できませんでした。");
        return string.Empty;
    }
}

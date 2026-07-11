using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class PlayerRightRailWarmScrollSuppressionSourcePolicyTests
{
    [Test]
    public void Scroll中のwarm要求はqueue状態を変える前にSuppressedを返す()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );
        string queueMethod = ExtractMethod(
            source,
            "internal static PlayerRightRailImageWarmQueueResult Queue("
        );
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );

        int suppressionIndex = queueMethod.IndexOf(
            "PlayerRightRailImageWarmQueueResult.Suppressed",
            StringComparison.Ordinal
        );
        int pendingKeyIndex = queueMethod.IndexOf("PendingKeys.Add(", StringComparison.Ordinal);
        int pendingQueueIndex = queueMethod.IndexOf("Pending.Enqueue(", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Suppressed"));
            Assert.That(
                viewportSource,
                Does.Contain("() => _isPlayerThumbnailScrollUserPriorityActive")
            );
            Assert.That(viewportSource, Does.Contain("SetSuspensionProvider(null)"));
            Assert.That(suppressionIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(pendingKeyIndex, Is.GreaterThan(suppressionIndex));
            Assert.That(pendingQueueIndex, Is.GreaterThan(suppressionIndex));
        });
    }

    [Test]
    public void Scroll解除後は保留revisionを一度flushする()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string releaseMethod = ExtractMethod(
            source,
            "private void ReleasePlayerThumbnailScrollUserPriority("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyPlayerRightRailWarmRefresh()"
        );

        Assert.Multiple(() =>
        {
            Assert.That(releaseMethod, Does.Contain("QueuePlayerRightRailWarmRefresh();"));
            Assert.That(releaseMethod, Does.Contain("releaseReason, \"idle\""));
            Assert.That(applyMethod, Does.Contain("_playerRightRailViewportRevisionPending"));
            Assert.That(applyMethod, Does.Contain("PlayerRightRailImageRevision"));
            Assert.That(applyMethod, Does.Contain("RefreshPlayerRightRailImageRevision();"));
        });
    }

    [Test]
    public void Burst集約はwarm抑止件数を独立して記録する()
    {
        string converterSource = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );
        string viewportSource = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Viewport.cs"
        );
        string recordMethod = ExtractMethod(
            converterSource,
            "internal void RecordQueueResult(PlayerRightRailImageWarmQueueResult result)"
        );
        string snapshotMethod = ExtractMethod(
            converterSource,
            "internal PlayerRightRailImageBurstMetricsSnapshot CreateSnapshot()"
        );
        string endMethod = ExtractMethod(
            viewportSource,
            "private void EndPlayerThumbnailScrollBurstMetrics("
        );

        Assert.Multiple(() =>
        {
            Assert.That(converterSource, Does.Contain("QueueSuppressedCount"));
            Assert.That(recordMethod, Does.Contain("_queueSuppressedCount"));
            Assert.That(snapshotMethod, Does.Contain("_queueSuppressedCount"));
            Assert.That(endMethod, Does.Contain("suppressed_count="));
            Assert.That(endMethod, Does.Contain("converterMetrics.QueueSuppressedCount"));
        });
    }

    [Test]
    public void 通常時のcapacity重複排除とsingle_worker契約を維持する()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );
        string queueMethod = ExtractMethod(
            source,
            "internal static PlayerRightRailImageWarmQueueResult Queue("
        );
        string processMethod = ExtractMethod(source, "private static void ProcessAsync()");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal const int Capacity = 64"));
            Assert.That(queueMethod, Does.Contain("PendingKeys.Add(key)"));
            Assert.That(queueMethod, Does.Contain("PlayerRightRailImageWarmQueueResult.Duplicate"));
            Assert.That(queueMethod, Does.Contain("PlayerRightRailImageWarmQueueResult.Enqueued"));
            Assert.That(queueMethod, Does.Contain("if (_workerActive)"));
            Assert.That(queueMethod, Does.Contain("Task.Run(ProcessAsync)"));
            Assert.That(processMethod, Does.Contain("_workerActive = false"));
            Assert.That(processMethod, Does.Contain("PendingKeys.Remove(request.Key)"));
        });
    }

    [Test]
    public void ConverterはUIスレッドでdecodeせずcache_missをwarmへ渡す()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Player",
            "PlayerRightRailImageSourceConverter.cs"
        );
        string convertMethod = ExtractMethod(source, "public object Convert(");
        string processMethod = ExtractMethod(source, "private static void ProcessAsync()");

        Assert.Multiple(() =>
        {
            Assert.That(convertMethod, Does.Contain("TryGetCachedDecodeRequest("));
            Assert.That(convertMethod, Does.Contain("PlayerRightRailImageWarmQueue.Queue("));
            Assert.That(convertMethod, Does.Not.Contain("ConvertDecodeRequest("));
            Assert.That(convertMethod, Does.Not.Contain("BitmapDecoder"));
            Assert.That(convertMethod, Does.Not.Contain("BitmapImage"));
            Assert.That(processMethod, Does.Contain("ConvertDecodeRequest("));
        });
    }

    private static string GetRepoText(params string[] relativeParts)
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine([current.FullName, .. relativeParts]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativeParts)} をrepo rootから解決できませんでした。");
        return "";
    }

    private static IEnumerable<DirectoryInfo> EnumerateRepoSearchRoots(
        [CallerFilePath] string callerFilePath = ""
    )
    {
        string? callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrWhiteSpace(callerDirectory))
        {
            yield return new DirectoryInfo(callerDirectory);
        }

        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
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

        Assert.Fail($"{signature} の本文終端が見つかりません。");
        return "";
    }
}

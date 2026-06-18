using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatcherCreationLogContractSourceTests
{
    [Test]
    public void Watcher作成ログはCreateWatcherStatusApplied要約だけでなく計画とApplySummaryの内訳を残す()
    {
        string source = File.ReadAllText(GetRepoFilePath("Watcher", "MainWindow.WatcherRegistration.cs"))
            .Replace("\r\n", "\n");
        string buildMethod = ExtractMethod(
            source,
            "private WatcherCreationPlan BuildWatcherCreationPlan("
        );
        string applyMethod = ExtractMethod(
            source,
            "private void ApplyWatcherCreationPlan("
        );

        // 計画作成時点で、Everything可用性と監視テーブル読込、フォルダ計画の時間を同じ行に残す。
        Assert.That(buildMethod, Does.Contain("watcher creation plan built:"));
        Assert.That(buildMethod, Does.Contain("availability_ms="));
        Assert.That(buildMethod, Does.Contain("watch_table_load_ms="));
        Assert.That(buildMethod, Does.Contain("folder_plan_ms="));

        // Apply側では、登録実行と全体適用の時間を apply summary と旧CreateWatcher要約の両方で読める。
        Assert.That(applyMethod, Does.Contain("watcher creation apply summary:"));
        Assert.That(applyMethod, Does.Contain("registration_ms="));
        Assert.That(applyMethod, Does.Contain("apply_ms="));
        Assert.That(applyMethod, Does.Contain("status=applied"));

        // 旧要約だけで完了扱いにせず、apply summary に登録試行数、失敗数、初回登録時刻を残す。
        string applySummaryLine = ExtractContainingLine(
            applyMethod,
            "watcher creation apply summary:"
        );
        Assert.That(applySummaryLine, Does.Contain("attempted="));
        Assert.That(applySummaryLine, Does.Contain("failed="));
        Assert.That(applySummaryLine, Does.Contain("first_registered_ms="));
    }

    private static string GetRepoFilePath(params string[] relativePathParts)
    {
        foreach (DirectoryInfo searchRoot in EnumerateRepoSearchRoots())
        {
            DirectoryInfo? current = searchRoot;
            while (current != null)
            {
                string candidate = Path.Combine(
                    new[] { current.FullName }.Concat(relativePathParts).ToArray()
                );
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} を repo root から解決できませんでした。");
        return string.Empty;
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

        yield return new DirectoryInfo(Environment.CurrentDirectory);
        yield return new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThanOrEqualTo(0), $"{signature} の開始波括弧が見つかりません。");

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

        Assert.Fail($"{signature} の終端が見つかりません。");
        return string.Empty;
    }

    private static string ExtractContainingLine(string source, string marker)
    {
        string line = source
            .Split('\n')
            .FirstOrDefault(text => text.Contains(marker, StringComparison.Ordinal))
            ?? string.Empty;

        Assert.That(line, Is.Not.Empty, $"{marker} を含む行が見つかりません。");
        return line;
    }
}

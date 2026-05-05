using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;
using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailFailureSyncUiTests
{
    [Test]
    public void ApplyThumbnailPathWithForcedRebind_同一パスなら空経由で再通知する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\grid.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\grid.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\grid.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(2));
    }

    [Test]
    public void ApplyThumbnailPathWithForcedRebind_別パスなら通常の一回通知で更新する()
    {
        MovieRecords item = new()
        {
            ThumbPathGrid = @"C:\thumb\old.#hash.jpg",
        };
        int changedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MovieRecords.ThumbPathGrid))
            {
                changedCount++;
            }
        };

        bool applied = MainWindow.TryApplyThumbnailPathToMovieRecord(
            item,
            2,
            @"C:\thumb\new.#hash.jpg"
        );

        Assert.That(applied, Is.True);
        Assert.That(item.ThumbPathGrid, Is.EqualTo(@"C:\thumb\new.#hash.jpg"));
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void ThumbPathBig10_更新時は正しいPropertyNameで通知する()
    {
        MovieRecords item = new();
        string propertyName = "";
        item.PropertyChanged += (_, e) => propertyName = e.PropertyName ?? "";

        item.ThumbPathBig10 = @"C:\thumb\big10.#hash.jpg";

        Assert.That(propertyName, Is.EqualTo(nameof(MovieRecords.ThumbPathBig10)));
    }

    [Test]
    public void ShouldRefreshVisibleThumbnailUiAfterCreate_PreferredだけTrueを返す()
    {
        QueueObj preferred = new() { Priority = ThumbnailQueuePriority.Preferred };
        QueueObj normal = new() { Priority = ThumbnailQueuePriority.Normal };

        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(preferred), Is.True);
        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(normal), Is.False);
        Assert.That(MainWindow.ShouldRefreshVisibleThumbnailUiAfterCreate(null), Is.False);
    }

    [Test]
    public void ShouldRequestMainTabFullReloadAfterThumbnailSuccess_直接反映済みならFalseを返す()
    {
        QueueObj preferred = new() { Priority = ThumbnailQueuePriority.Preferred };
        QueueObj normal = new() { Priority = ThumbnailQueuePriority.Normal };

        Assert.That(
            MainWindow.ShouldRequestMainTabFullReloadAfterThumbnailSuccess(
                preferred,
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabFullReloadAfterThumbnailSuccess(
                preferred,
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabFullReloadAfterThumbnailSuccess(
                normal,
                appliedDirectlyToMainMovie: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabFullReloadAfterThumbnailSuccess(
                shouldRefreshVisibleUi: true,
                appliedDirectlyToMainMovie: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldRequestMainTabFullReloadAfterThumbnailSuccess(
                shouldRefreshVisibleUi: true,
                appliedDirectlyToMainMovie: false
            ),
            Is.True
        );
    }

    [Test]
    public void RescuedThumbnailUiApplyResult_AppliedCountが1以上ならUi反映済み()
    {
        MainWindow.RescuedThumbnailUiApplyResult nonSelectedApplied = new(
            AppliedCount: 1,
            AppliedToSelectedRecord: false
        );
        MainWindow.RescuedThumbnailUiApplyResult selectedApplied = new(
            AppliedCount: 1,
            AppliedToSelectedRecord: true
        );
        MainWindow.RescuedThumbnailUiApplyResult notApplied = new(
            AppliedCount: 0,
            AppliedToSelectedRecord: false
        );

        Assert.That(nonSelectedApplied.AppliedToUi, Is.True);
        Assert.That(nonSelectedApplied.AppliedToSelectedRecord, Is.False);
        Assert.That(selectedApplied.AppliedToUi, Is.True);
        Assert.That(selectedApplied.AppliedToSelectedRecord, Is.True);
        Assert.That(notApplied.AppliedToUi, Is.False);
    }

    [Test]
    public void ShouldRefreshMainViewAfterRescuedSync_選択中へ反映された時だけTrue()
    {
        Assert.That(
            MainWindow.ShouldRefreshMainViewAfterRescuedSync(appliedToSelectedRecord: true),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldRefreshMainViewAfterRescuedSync(appliedToSelectedRecord: false),
            Is.False
        );
    }

    [Test]
    public void RescuedSync完了時はRefreshだけ選択中反映で絞る()
    {
        string source = GetRepoText("Thumbnail", "MainWindow.ThumbnailFailureSync.cs")
            .Replace("\r\n", "\n");
        string syncMethod = ExtractMethod(
            source,
            "private async Task TrySyncRescuedThumbnailRecordsAsync("
        );

        int invalidateIndex = syncMethod.IndexOf(
            "InvalidateThumbnailErrorRecords(refreshIfVisible: true);",
            StringComparison.Ordinal
        );
        int policyIndex = syncMethod.IndexOf(
            "ShouldRefreshMainViewAfterRescuedSync(",
            StringComparison.Ordinal
        );
        int refreshIndex = syncMethod.IndexOf("Refresh();", policyIndex, StringComparison.Ordinal);
        int progressIndex = syncMethod.IndexOf(
            "RequestThumbnailProgressSnapshotRefresh();",
            StringComparison.Ordinal
        );

        Assert.That(invalidateIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(policyIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(refreshIndex, Is.GreaterThan(policyIndex));
        Assert.That(progressIndex, Is.GreaterThan(refreshIndex));
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
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

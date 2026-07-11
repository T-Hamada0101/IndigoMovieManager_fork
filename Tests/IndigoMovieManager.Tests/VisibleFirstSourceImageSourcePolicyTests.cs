using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class VisibleFirstSourceImageSourcePolicyTests
{
    [Test]
    public void BulkProbeが0件または失敗した時もfallbackを維持する()
    {
        string factorySource = GetMovieRecordFactorySource();
        string probeSource = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(factorySource, Does.Contain("bool deferSourceImageProbe = bulkMetrics != null;"));
            Assert.That(factorySource, Does.Contain("allowSourceImageProbe: !deferSourceImageProbe"));
            Assert.That(factorySource, Does.Contain("return fallbackPath;"));
            Assert.That(probeSource, Does.Contain("if (targets.Length == 0)"));
            Assert.That(probeSource, Does.Contain("catch (Exception ex)"));
            Assert.That(probeSource, Does.Not.Contain("record.ThumbPathSmall = fallback"));
        });
    }

    [Test]
    public void 管理サムネがある用途はsourceImageで上書きしない()
    {
        string factorySource = GetMovieRecordFactorySource();
        string probeSource = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(factorySource, Does.Contain("existingFileNames.Contains(currentFileName)"));
            Assert.That(factorySource, Does.Contain("existingFileNames.Contains(legacyFileName)"));
            Assert.That(probeSource, Does.Contain("ApplySourceImageToPlaceholder("));
            Assert.That(
                probeSource,
                Does.Contain("ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(currentPath)")
            );
            Assert.That(
                probeSource,
                Does.Contain("ThumbnailErrorPlaceholderHelper.CountPlaceholders(record) > 0")
            );
        });
    }

    [Test]
    public void BulkProbe対象はpreferredVisibleMoviePathKeysだけから組み立てる()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_preferredVisibleMoviePathKeysSnapshot"));
            Assert.That(source, Does.Contain("FirstNearVisibleIndex"));
            Assert.That(source, Does.Contain("LastNearVisibleIndex"));
            Assert.That(source, Does.Contain("preferredKeys.Remove(moviePathKey)"));
        });
    }

    [Test]
    public void 一レコード六用途は一度だけ探索してplaceholderだけ反映する()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(
                CountOccurrences(source, "TryResolveSameNameThumbnailSourceImagePath("),
                Is.EqualTo(1)
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbPathSmall = value")
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbPathBig = value")
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbPathGrid = value")
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbPathList = value")
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbPathBig10 = value")
            );
            Assert.That(
                source,
                Does.Contain("value => record.ThumbDetail = value")
            );
        });
    }

    [Test]
    public void Db_Filter_SourceRevisionが変わった後着結果はapplyしない()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("dbFullPath"));
            Assert.That(source, Does.Contain("filterRevision"));
            Assert.That(source, Does.Contain("probeRevision"));
            Assert.That(source, Does.Contain("AreSameMainDbPath("));
            Assert.That(source, Does.Contain("IsVisibleSourceImageProbeRequestCurrent("));
        });
    }

    [Test]
    public void Apply時は現在表示中レコードとの同一参照をguardする()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.That(source, Does.Contain("ReferenceEquals"));
    }

    [Test]
    public void UserPriority中は単一workerが最新要求だけ保持して解除後一度だけflushする()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_visibleSourceImageProbePendingRequest"));
            Assert.That(source, Does.Contain("_visibleSourceImageProbeWorkerRunning"));
            Assert.That(source, Does.Contain("Interlocked.CompareExchange"));
            Assert.That(source, Does.Contain("Interlocked.Exchange"));
            Assert.That(source, Does.Contain("while (IsUserPriorityWorkActive())"));
            Assert.That(source, Does.Contain("await Task.Delay(120)"));
            Assert.That(source, Does.Contain("IsVisibleSourceImageProbeRequestCurrent("));
            Assert.That(
                CountOccurrences(source, "RunVisibleSourceImageProbeWorkerAsync("),
                Is.EqualTo(2),
                "定義と単一worker起動の1箇所以外からprobe workerを増やさない"
            );
            Assert.That(
                source,
                Does.Not.Contain("_ = RunVisibleSourceImageProbeAsync(")
            );
        });
    }

    [Test]
    public void Pending要求はrevision付きsnapshotで保持し古い要求をflushしない()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("VisibleSourceImageProbeRequest"));
            Assert.That(source, Does.Contain("ProbeRevision"));
            Assert.That(source, Does.Contain("FilterRevision"));
            Assert.That(source, Does.Contain("DbFullPath"));
            Assert.That(source, Does.Contain("Targets"));
            Assert.That(source, Does.Contain("IsVisibleSourceImageProbeRequestCurrent("));
            Assert.That(source, Does.Contain("Volatile.Read(ref _visibleSourceImageProbePendingRequest)"));
        });
    }

    [Test]
    public void VisibleFirst後段から全件sourceImage走査へ戻さない()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(
                source,
                Does.Not.Contain(
                    "foreach (MovieRecords current in MainVM.FilteredMovieRecs)"
                )
            );
            Assert.That(
                source,
                Does.Not.Contain("foreach (MovieRecords current in MainVM.MovieRecs)")
            );
            Assert.That(
                source,
                Does.Not.Contain("foreach (MovieRecords item in MainVM.MovieRecs)")
            );
            Assert.That(
                source,
                Does.Not.Contain("MainVM.FilteredMovieRecs.ToArray()")
            );
        });
    }

    [Test]
    public void SourceImage反映はPlayerとSharedの両revisionへ接続する()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("RefreshSharedUpperTabImageRevision"));
            Assert.That(source, Does.Contain("RefreshPlayerRightRailImageRevision"));
        });
    }

    private static string GetMovieRecordFactorySource()
    {
        return GetRepoText("Views", "Main", "MainWindow.MovieRecordFactory.cs");
    }

    private static string GetVisibleSourceImageProbeSource()
    {
        return GetRepoText("Views", "Main", "MainWindow.VisibleSourceImageProbe.cs");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string GetRepoText(
        string firstPathPart,
        string secondPathPart,
        string fileName,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? "");
        while (current != null)
        {
            string candidate = Path.Combine(
                current.FullName,
                firstPathPart,
                secondPathPart,
                fileName
            );
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"{Path.Combine(firstPathPart, secondPathPart, fileName)} が見つかりません。");
        return "";
    }
}

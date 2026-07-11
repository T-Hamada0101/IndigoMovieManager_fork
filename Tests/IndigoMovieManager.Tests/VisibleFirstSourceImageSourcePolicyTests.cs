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
            Assert.That(factorySource, Does.Contain("allowSourceImageProbe: false"));
            Assert.That(factorySource, Does.Contain("return fallbackPath;"));
            Assert.That(probeSource, Does.Contain("if (targets.Length == 0)"));
            Assert.That(probeSource, Does.Contain("catch (Exception ex)"));
            Assert.That(probeSource, Does.Not.Contain("record.ThumbPathSmall = fallback"));
        });
    }

    [Test]
    public void 管理サムネがある用途はsourceImageで上書きしない()
    {
        string source = GetMovieRecordFactorySource();

        Assert.That(source, Does.Contain("existingFileNames.Contains(currentFileName)"));
        Assert.That(source, Does.Contain("existingFileNames.Contains(legacyFileName)"));
        Assert.That(source, Does.Contain("return Path.Combine(thumbnailOutPath, currentFileName)"));
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
    public void 一レコード六用途は同じlazyResolverを共有する()
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
                Does.Contain("record.ThumbPathSmall = resolution.SourceImagePath;")
            );
            Assert.That(
                source,
                Does.Contain("record.ThumbPathBig = resolution.SourceImagePath;")
            );
            Assert.That(
                source,
                Does.Contain("record.ThumbPathGrid = resolution.SourceImagePath;")
            );
            Assert.That(
                source,
                Does.Contain("record.ThumbPathList = resolution.SourceImagePath;")
            );
            Assert.That(
                source,
                Does.Contain("record.ThumbPathBig10 = resolution.SourceImagePath;")
            );
            Assert.That(
                source,
                Does.Contain("record.ThumbDetail = resolution.SourceImagePath;")
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
    public void UserPriority中はprobeを延期して解除後だけ再予約する()
    {
        string source = GetVisibleSourceImageProbeSource();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("while (IsUserPriorityWorkActive())"));
            Assert.That(source, Does.Contain("await Task.Delay(120)"));
            Assert.That(source, Does.Contain("IsVisibleSourceImageProbeRequestCurrent("));
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

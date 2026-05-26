using System.Data.SQLite;
using System.Globalization;
using IndigoMovieManager.UpperTabs.DuplicateVideos;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabDuplicateVideoAnalyzerTests
{
    [Test]
    public void DuplicateItemViewModel_値変更時だけPropertyChangedを出す()
    {
        UpperTabDuplicateItemViewModel vm = new();
        List<string> changedProperties = [];
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? "");

        vm.MovieName = "movie-a";
        vm.MovieName = "movie-a";
        vm.MoviePath = "movies/a.mp4";

        Assert.That(changedProperties, Is.EqualTo(new[] { "MovieName", "MoviePath" }));
    }

    [Test]
    public void DuplicateGroupViewModel_値変更時だけPropertyChangedを出す()
    {
        UpperTabDuplicateGroupViewModel vm = new();
        List<string> changedProperties = [];
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName ?? "");

        vm.RepresentativeMovieName = "movie-a";
        vm.RepresentativeMovieName = "movie-a";
        vm.RepresentativeThumbnailPath = "thumb/a.jpg";

        Assert.That(
            changedProperties,
            Is.EqualTo(new[] { "RepresentativeMovieName", "RepresentativeThumbnailPath" })
        );
    }

    [Test]
    public void TryApplyUpperTabDuplicateMovieNameChange_全体Refreshへ戻らない()
    {
        string source = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "MainWindow.UpperTabs.DuplicateVideosTab.cs"
        );
        string method = GetMethodBlock(
            source,
            "private bool TryApplyUpperTabDuplicateMovieNameChange("
        );

        Assert.That(method, Does.Not.Contain("Items.Refresh()"));
    }

    [Test]
    public void ApplySelectedUpperTabDuplicateGroupDetails_詳細生成をTaskRunへ逃がす()
    {
        string source = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "MainWindow.UpperTabs.DuplicateVideosTab.cs"
        );
        string method = GetMethodBlock(
            source,
            "private async void ApplySelectedUpperTabDuplicateGroupDetails("
        );

        Assert.That(method, Does.Contain("Task.Run("));
        Assert.That(method, Does.Contain("BuildUpperTabDuplicateDetailItems("));
        Assert.That(method, Does.Not.Contain("File.Exists("));
    }

    [Test]
    public void ApplyUpperTabDuplicateGroups_代表サムネ解決をTaskRunへ逃がす()
    {
        string source = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "MainWindow.UpperTabs.DuplicateVideosTab.cs"
        );
        string applyMethod = GetMethodBlock(
            source,
            "private async Task ApplyUpperTabDuplicateGroupsAsync("
        );
        string sortMethod = GetMethodBlock(
            source,
            "private async Task ApplyUpperTabDuplicateGroupSortAsync("
        );

        Assert.That(source, Does.Contain("_upperTabDuplicateGroupRefreshRevision"));
        Assert.That(applyMethod, Does.Contain("await ApplyUpperTabDuplicateGroupSortAsync("));
        Assert.That(sortMethod, Does.Contain("Task.Run("));
        Assert.That(sortMethod, Does.Contain("BuildUpperTabDuplicateGroupItems("));
        Assert.That(sortMethod, Does.Contain("Volatile.Read(ref _upperTabDuplicateGroupRefreshRevision)"));
        Assert.That(applyMethod, Does.Not.Contain("File.Exists("));
        Assert.That(sortMethod, Does.Not.Contain("File.Exists("));
    }

    [Test]
    public void ApplySelectedUpperTabDuplicateGroupDetails_revisionで後着を破棄する()
    {
        string source = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "MainWindow.UpperTabs.DuplicateVideosTab.cs"
        );
        string method = GetMethodBlock(
            source,
            "private async void ApplySelectedUpperTabDuplicateGroupDetails("
        );

        Assert.That(source, Does.Contain("_upperTabDuplicateDetailRefreshRevision"));
        Assert.That(method, Does.Contain("Interlocked.Increment("));
        Assert.That(method, Does.Contain("Volatile.Read(ref _upperTabDuplicateDetailRefreshRevision)"));
        Assert.That(method, Does.Contain("return;"));
    }

    [Test]
    public void DuplicateVideos_存在確認は候補数でboundedと全列挙を切り替える()
    {
        string source = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "MainWindow.UpperTabs.DuplicateVideosTab.cs"
        );
        string detailItemsMethod = GetMethodBlock(
            source,
            "private UpperTabDuplicateItemViewModel[] BuildUpperTabDuplicateDetailItems("
        );
        string groupItemsMethod = GetMethodBlock(
            source,
            "private UpperTabDuplicateGroupViewModel[] BuildUpperTabDuplicateGroupItems("
        );
        string lookupMethod = GetMethodBlock(
            source,
            "private UpperTabDuplicateLookupContext BuildUpperTabDuplicateLookupContext("
        );
        string thresholdMethod = GetMethodBlock(
            source,
            "private static bool ShouldUseUpperTabDuplicateDirectorySnapshot("
        );
        string boundedMethod = GetMethodBlock(
            source,
            "private static HashSet<string> BuildUpperTabDuplicateBoundedFileNameLookup("
        );
        string movieRecordMethod = GetMethodBlock(
            source,
            "private MovieRecords BuildUpperTabDuplicateMovieRecord("
        );
        string thumbnailResolveMethod = GetMethodBlock(
            source,
            "private string ResolveUpperTabDuplicateThumbnailPath("
        );

        Assert.That(source, Does.Contain("UpperTabDuplicateLookupSnapshotMinTargetCount"));
        Assert.That(detailItemsMethod, Does.Contain("BuildUpperTabDuplicateLookupContext("));
        Assert.That(groupItemsMethod, Does.Contain("BuildUpperTabDuplicateLookupContext("));
        Assert.That(lookupMethod, Does.Contain("ShouldUseUpperTabDuplicateDirectorySnapshot("));
        Assert.That(lookupMethod, Does.Contain("BuildUpperTabDuplicateBoundedThumbnailFileNameLookup("));
        Assert.That(lookupMethod, Does.Contain("BuildUpperTabDuplicateBoundedFileNameLookup("));
        Assert.That(lookupMethod, Does.Contain("BuildThumbnailFileNameLookup(outPath)"));
        Assert.That(lookupMethod, Does.Contain("BuildUpperTabDuplicateFileNameLookup(directoryPath)"));
        Assert.That(thresholdMethod, Does.Contain(">= UpperTabDuplicateLookupSnapshotMinTargetCount"));
        Assert.That(boundedMethod, Does.Contain("File.Exists(candidatePath)"));
        Assert.That(movieRecordMethod, Does.Contain("IsUpperTabDuplicateMovieKnownToExist("));
        Assert.That(thumbnailResolveMethod, Does.Contain("ThumbnailPathResolver.BuildThumbnailFileName("));
        Assert.That(thumbnailResolveMethod, Does.Not.Contain("Path.Exists("));
    }

    [Test]
    public void ExtractProbText_prob付きファイル名から抽出する()
    {
        string result = UpperTabDuplicateVideoAnalyzer.ExtractProbText(
            "sample_scale_2x_prob-3",
            @"C:\movies\sample_scale_2x_prob-3.mp4"
        );

        Assert.That(result, Is.EqualTo("prob-3"));
    }

    [Test]
    public void BuildGroupSummaries_件数優先と代表選定を行う()
    {
        UpperTabDuplicateMovieRecord[] records =
        [
            new(1, "a", @"C:\movies\a.mp4", 100, "2026-03-20 10:00:00", 60, 0, "hash-a"),
            new(2, "b", @"C:\movies\b.mp4", 250, "2026-03-20 11:00:00", 60, 0, "hash-a"),
            new(3, "c", @"C:\movies\c.mp4", 150, "2026-03-20 12:00:00", 60, 0, "hash-b"),
            new(4, "d", @"C:\movies\d.mp4", 140, "2026-03-20 09:00:00", 60, 0, "hash-b"),
            new(5, "e", @"C:\movies\e.mp4", 130, "2026-03-20 08:00:00", 60, 0, "hash-b"),
        ];

        UpperTabDuplicateGroupSummary[] groups = UpperTabDuplicateVideoAnalyzer.BuildGroupSummaries(records);

        Assert.That(groups.Length, Is.EqualTo(2));
        Assert.That(groups[0].Hash, Is.EqualTo("hash-b"));
        Assert.That(groups[0].DuplicateCount, Is.EqualTo(3));
        Assert.That(groups[0].Representative.MovieId, Is.EqualTo(3));
        Assert.That(groups[1].Representative.MovieId, Is.EqualTo(2));
    }

    [Test]
    public void BuildSizeCompareText_最大と差分を短く返す()
    {
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(3000, 3000, 1000),
            Is.EqualTo("最大")
        );
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(1000, 3000, 1000),
            Is.EqualTo("最小")
        );
        Assert.That(
            UpperTabDuplicateVideoAnalyzer.BuildSizeCompareText(2000, 3000, 1000),
            Is.EqualTo("-1.0 MB")
        );
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

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

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

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return string.Empty;
    }
}

[TestFixture]
public sealed class UpperTabDuplicateVideoReadServiceTests
{
    [Test]
    public void ReadDuplicateMovieRecords_空hashを除いて重複群だけ返す()
    {
        string dbPath = CreateTempMainDb();

        try
        {
            SeedMovieRows(dbPath);
            UpperTabDuplicateVideoReadService service = new();

            UpperTabDuplicateMovieRecord[] records = service.ReadDuplicateMovieRecords(dbPath);

            Assert.That(records.Length, Is.EqualTo(4));
            Assert.That(records.All(x => !string.IsNullOrWhiteSpace(x.Hash)), Is.True);
            Assert.That(records.Select(x => x.Hash).Distinct().OrderBy(x => x), Is.EqualTo(new[] { "hash-a", "hash-c" }));
            Assert.That(records.First().MovieId, Is.EqualTo(2));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void ReadDuplicateMovieRecords_特殊カルチャでもISO日付文字列を崩さない()
    {
        string dbPath = CreateTempMainDb();
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            using SQLiteConnection connection = new($"Data Source={dbPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    file_date,
    score,
    hash
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-04-01 12:34:56', 1, 'hash-a'),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-04-01 01:02:03', 2, 'hash-a');";
            command.ExecuteNonQuery();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th-TH");

            UpperTabDuplicateVideoReadService service = new();
            UpperTabDuplicateMovieRecord[] records = service.ReadDuplicateMovieRecords(dbPath);

            Assert.That(records.Length, Is.EqualTo(2));
            Assert.That(
                records.First(x => x.MovieId == 2).FileDateText,
                Is.EqualTo("2026-04-01 01:02:03")
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-dup-tab-{Guid.NewGuid():N}.wb");
        SQLiteConnection.CreateFile(dbPath);

        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE movie (
    movie_id INTEGER PRIMARY KEY,
    movie_name TEXT NOT NULL,
    movie_path TEXT NOT NULL,
    movie_length INTEGER NOT NULL,
    movie_size INTEGER NOT NULL,
    file_date TEXT NOT NULL,
    score INTEGER NOT NULL,
    hash TEXT NOT NULL
);";
        command.ExecuteNonQuery();
        return dbPath;
    }

    private static void SeedMovieRows(string dbPath)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    file_date,
    score,
    hash
)
VALUES
    (1, 'movie-a', 'C:\movies\a.mp4', 60, 100, '2026-03-18 10:00:00', 1, 'hash-a'),
    (2, 'movie-b', 'C:\movies\b.mp4', 70, 300, '2026-03-19 10:00:00', 2, 'hash-a'),
    (3, 'movie-c', 'C:\movies\c.mp4', 80, 200, '2026-03-20 10:00:00', 3, ''),
    (4, 'movie-d', 'C:\movies\d.mp4', 90, 50, '2026-03-20 10:00:00', 4, 'hash-c'),
    (5, 'movie-e', 'C:\movies\e.mp4', 95, 40, '2026-03-20 09:00:00', 5, 'hash-c');";
        command.ExecuteNonQuery();
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}

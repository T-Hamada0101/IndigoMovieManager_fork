using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieViewSelectionContinuityPolicyTests
{
    [Test]
    public void TryCaptureStableKey_選択中レコードのstable_keyを既存policyで捕捉する()
    {
        MovieRecords selected = new() { Movie_Id = 42, Movie_Path = @"C:\movies\before.mp4" };

        bool captured = MovieViewSelectionContinuityPolicy.TryCaptureStableKey(
            selected,
            out string stableKey
        );

        Assert.That(captured, Is.True);
        Assert.That(stableKey, Is.EqualTo("id:42"));
    }

    [Test]
    public void TryCaptureStableKey_選択がNullなら捕捉しない()
    {
        bool captured = MovieViewSelectionContinuityPolicy.TryCaptureStableKey(
            null!,
            out string stableKey
        );

        Assert.That(captured, Is.False);
        Assert.That(stableKey, Is.Empty);
    }

    [Test]
    public void CaptureStableKeys_主選択を先頭にして残りの選択順を保つ()
    {
        MovieRecords primary = new() { Movie_Id = 2 };
        MovieRecords first = new() { Movie_Id = 1 };
        MovieRecords third = new() { Movie_Id = 3 };

        IReadOnlyList<string> actual = MovieViewSelectionContinuityPolicy.CaptureStableKeys(
            primary,
            [first, primary, third]
        );

        Assert.That(actual, Is.EqualTo(new[] { "id:2", "id:1", "id:3" }));
    }

    [Test]
    public void CaptureStableKeys_key重複を大小文字無視で除外し解決不可も含めない()
    {
        MovieRecords primary = new() { Movie_Path = @"C:\movies\sample.mp4" };

        IReadOnlyList<string> actual = MovieViewSelectionContinuityPolicy.CaptureStableKeys(
            primary,
            [
                new MovieRecords { Movie_Path = @"c:\MOVIES\SAMPLE.MP4" },
                null!,
                new MovieRecords(),
                new MovieRecords { Movie_Id = 7 },
            ]
        );

        Assert.That(actual, Is.EqualTo(new[] { @"path:C:\movies\sample.mp4", "id:7" }));
    }

    [Test]
    public void ResolveAfterCollectionApply_Reset変更ありなら同じIdの現行実体を返す()
    {
        MovieRecords current = new() { Movie_Id = 42, Movie_Path = @"D:\movies\after.mp4" };

        MovieRecords actual = MovieViewSelectionContinuityPolicy.ResolveAfterCollectionApply(
            "id:42",
            [new MovieRecords { Movie_Id = 1 }, current],
            FilteredMovieRecsUpdateMode.Reset,
            hasCollectionChanges: true
        );

        Assert.That(actual, Is.SameAs(current));
    }

    [Test]
    public void ResolveAfterCollectionApply_Path_fallbackでも同じ現行実体を返す()
    {
        MovieRecords current = new() { Movie_Id = 0, Movie_Path = @"C:\MOVIES\SAMPLE.MP4" };

        MovieRecords actual = MovieViewSelectionContinuityPolicy.ResolveAfterCollectionApply(
            @"path:c:\movies\sample.mp4",
            [current],
            FilteredMovieRecsUpdateMode.Reset,
            hasCollectionChanges: true
        );

        Assert.That(actual, Is.SameAs(current));
    }

    [TestCase(FilteredMovieRecsUpdateMode.Diff, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Move, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Reset, false)]
    public void ResolveAfterCollectionApply_Reset変更あり以外は復元しない(
        FilteredMovieRecsUpdateMode updateMode,
        bool hasCollectionChanges
    )
    {
        MovieRecords current = new() { Movie_Id = 42 };

        MovieRecords actual = MovieViewSelectionContinuityPolicy.ResolveAfterCollectionApply(
            "id:42",
            [current],
            updateMode,
            hasCollectionChanges
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void ResolveAfterCollectionApply_対象が結果から消えた時は復元しない()
    {
        MovieRecords actual = MovieViewSelectionContinuityPolicy.ResolveAfterCollectionApply(
            "id:42",
            [new MovieRecords { Movie_Id = 7 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasCollectionChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void ResolveAfterCollectionApply_key解決不可なら復元しない()
    {
        MovieRecords actual = MovieViewSelectionContinuityPolicy.ResolveAfterCollectionApply(
            "",
            [new MovieRecords { Movie_Id = 42 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasCollectionChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void ResolveManyAfterCollectionApply_Reset変更ありならsnapshot順で現行実体を返す()
    {
        MovieRecords primary = new() { Movie_Id = 2 };
        MovieRecords first = new() { Movie_Id = 1 };

        IReadOnlyList<MovieRecords> actual =
            MovieViewSelectionContinuityPolicy.ResolveManyAfterCollectionApply(
                ["id:2", "id:1"],
                [first, primary],
                FilteredMovieRecsUpdateMode.Reset,
                hasCollectionChanges: true
            );

        Assert.That(actual, Is.EqualTo(new[] { primary, first }));
    }

    [Test]
    public void ResolveManyAfterCollectionApply_対象消失とkey空を除外する()
    {
        MovieRecords current = new() { Movie_Id = 2 };

        IReadOnlyList<MovieRecords> actual =
            MovieViewSelectionContinuityPolicy.ResolveManyAfterCollectionApply(
                ["id:1", "", "id:2"],
                [current],
                FilteredMovieRecsUpdateMode.Reset,
                hasCollectionChanges: true
            );

        Assert.That(actual, Is.EqualTo(new[] { current }));
    }

    [Test]
    public void ResolveManyAfterCollectionApply_key一覧が空なら空結果を返す()
    {
        IReadOnlyList<MovieRecords> actual =
            MovieViewSelectionContinuityPolicy.ResolveManyAfterCollectionApply(
                [],
                [new MovieRecords { Movie_Id = 42 }],
                FilteredMovieRecsUpdateMode.Reset,
                hasCollectionChanges: true
            );

        Assert.That(actual, Is.Empty);
    }

    [Test]
    public void ResolveManyAfterCollectionApply_現行一覧でstable_keyが重複する対象は復元しない()
    {
        MovieRecords unique = new() { Movie_Id = 7 };

        IReadOnlyList<MovieRecords> actual =
            MovieViewSelectionContinuityPolicy.ResolveManyAfterCollectionApply(
                [@"path:C:\movies\duplicate.mp4", "id:7"],
                [
                    new MovieRecords { Movie_Path = @"C:\MOVIES\DUPLICATE.MP4" },
                    unique,
                    new MovieRecords { Movie_Path = @"c:\movies\duplicate.mp4" },
                ],
                FilteredMovieRecsUpdateMode.Reset,
                hasCollectionChanges: true
            );

        Assert.That(actual, Is.EqualTo(new[] { unique }));
    }

    [TestCase(FilteredMovieRecsUpdateMode.Diff, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Move, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Reset, false)]
    public void ResolveManyAfterCollectionApply_Reset変更あり以外は介入しない(
        FilteredMovieRecsUpdateMode updateMode,
        bool hasCollectionChanges
    )
    {
        IReadOnlyList<MovieRecords> actual =
            MovieViewSelectionContinuityPolicy.ResolveManyAfterCollectionApply(
                ["id:42"],
                [new MovieRecords { Movie_Id = 42 }],
                updateMode,
                hasCollectionChanges
            );

        Assert.That(actual, Is.Empty);
    }
}

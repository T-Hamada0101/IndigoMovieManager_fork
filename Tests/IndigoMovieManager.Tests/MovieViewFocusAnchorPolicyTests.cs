using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieViewFocusAnchorPolicyTests
{
    [Test]
    public void TryCapture_MovieIdが正ならIdを優先する()
    {
        MovieRecords focused = new()
        {
            Movie_Id = 42,
            Movie_Path = @"C:\movies\sample.mp4",
        };

        bool captured = MovieViewFocusAnchorPolicy.TryCapture(focused, out var anchor);

        Assert.That(captured, Is.True);
        Assert.That(anchor.StableKey, Is.EqualTo("id:42"));
    }

    [Test]
    public void TryCapture_MovieIdが正でなければPathへフォールバックする()
    {
        MovieRecords focused = new() { Movie_Path = @"C:\movies\sample.mp4" };

        bool captured = MovieViewFocusAnchorPolicy.TryCapture(focused, out var anchor);

        Assert.That(captured, Is.True);
        Assert.That(anchor.StableKey, Is.EqualTo(@"path:C:\movies\sample.mp4"));
    }

    [Test]
    public void TryCapture_StableKeyを解決できなければ捕捉しない()
    {
        bool captured = MovieViewFocusAnchorPolicy.TryCapture(
            new MovieRecords(),
            out var anchor
        );

        Assert.That(captured, Is.False);
        Assert.That(anchor, Is.EqualTo(default(MovieViewFocusAnchor)));
    }

    [Test]
    public void ResolveAfterCollectionApply_Reset変更ありなら同じIdの現行実体を返す()
    {
        MovieRecords current = new() { Movie_Id = 42, Movie_Path = @"D:\movies\after.mp4" };

        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor("id:42"),
            [new MovieRecords { Movie_Id = 1 }, current],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.SameAs(current));
    }

    [Test]
    public void ResolveAfterCollectionApply_Path一致の現行実体を大小文字無視で返す()
    {
        MovieRecords current = new() { Movie_Path = @"C:\MOVIES\SAMPLE.MP4" };

        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor(@"path:c:\movies\sample.mp4"),
            [current],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.SameAs(current));
    }

    [TestCase(FilteredMovieRecsUpdateMode.Diff, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Move, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Reset, false)]
    public void ResolveAfterCollectionApply_Reset変更あり以外は復元しない(
        FilteredMovieRecsUpdateMode updateMode,
        bool hasChanges
    )
    {
        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor("id:42"),
            [new MovieRecords { Movie_Id = 42 }],
            updateMode,
            hasChanges
        );

        Assert.That(actual, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveAfterCollectionApply_InvalidAnchorなら復元しない(string? stableKey)
    {
        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor(stableKey!),
            [new MovieRecords { Movie_Id = 42 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void ResolveAfterCollectionApply_同じStableKeyが重複すれば復元しない()
    {
        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor("id:42"),
            [new MovieRecords { Movie_Id = 42 }, new MovieRecords { Movie_Id = 42 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void ResolveAfterCollectionApply_対象が現行一覧から消えた時は復元しない()
    {
        MovieRecords actual = MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewFocusAnchor("id:42"),
            [new MovieRecords { Movie_Id = 7 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.Null);
    }
}

using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieViewScrollAnchorPolicyTests
{
    [Test]
    public void TryCapture_Idを優先したanchorを保持する()
    {
        MovieRecords movie = new() { Movie_Id = 42, Movie_Path = @"C:\movies\sample.mp4" };

        bool captured = MovieViewScrollAnchorPolicy.TryCapture(movie, 12.5, out var anchor);

        Assert.That(captured, Is.True);
        Assert.That(anchor.StableKey, Is.EqualTo("id:42"));
        Assert.That(anchor.TopOffset, Is.EqualTo(12.5));
    }

    [Test]
    public void TryCapture_IdなしならPathを使い負のTopOffsetも保持する()
    {
        MovieRecords movie = new() { Movie_Path = @"C:\movies\sample.mp4" };

        bool captured = MovieViewScrollAnchorPolicy.TryCapture(movie, -8.25, out var anchor);

        Assert.That(captured, Is.True);
        Assert.That(anchor.StableKey, Is.EqualTo(@"path:C:\movies\sample.mp4"));
        Assert.That(anchor.TopOffset, Is.EqualTo(-8.25));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void TryCapture_TopOffsetが非finiteなら捕捉しない(double topOffset)
    {
        bool captured = MovieViewScrollAnchorPolicy.TryCapture(
            new MovieRecords { Movie_Id = 42 },
            topOffset,
            out var anchor
        );

        Assert.That(captured, Is.False);
        Assert.That(anchor, Is.EqualTo(default(MovieViewScrollAnchor)));
    }

    [Test]
    public void TryCapture_StableKeyを解決できなければ捕捉しない()
    {
        bool captured = MovieViewScrollAnchorPolicy.TryCapture(
            new MovieRecords(),
            0,
            out var anchor
        );

        Assert.That(captured, Is.False);
        Assert.That(anchor, Is.EqualTo(default(MovieViewScrollAnchor)));
    }

    [Test]
    public void ResolveAfterCollectionApply_Reset変更ありならPath一致の現行実体を返す()
    {
        var anchor = new MovieViewScrollAnchor(@"path:c:\movies\sample.mp4", -3);
        MovieRecords current = new() { Movie_Path = @"C:\MOVIES\SAMPLE.MP4" };

        MovieRecords actual = MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply(
            anchor,
            [current],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.SameAs(current));
    }

    [Test]
    public void ResolveAfterCollectionApply_同じStableKeyが重複すれば復元しない()
    {
        var anchor = new MovieViewScrollAnchor("id:42", 0);

        MovieRecords actual = MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply(
            anchor,
            [new MovieRecords { Movie_Id = 42 }, new MovieRecords { Movie_Id = 42 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [TestCase(FilteredMovieRecsUpdateMode.Diff, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Move, true)]
    [TestCase(FilteredMovieRecsUpdateMode.Reset, false)]
    public void ResolveAfterCollectionApply_Reset変更あり以外は復元しない(
        FilteredMovieRecsUpdateMode updateMode,
        bool hasChanges
    )
    {
        MovieRecords actual = MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewScrollAnchor("id:42", 0),
            [new MovieRecords { Movie_Id = 42 }],
            updateMode,
            hasChanges
        );

        Assert.That(actual, Is.Null);
    }

    [TestCase("", 0)]
    [TestCase("id:42", double.NaN)]
    [TestCase("id:42", double.PositiveInfinity)]
    public void ResolveAfterCollectionApply_InvalidAnchorなら復元しない(
        string stableKey,
        double topOffset
    )
    {
        MovieRecords actual = MovieViewScrollAnchorPolicy.ResolveAfterCollectionApply(
            new MovieViewScrollAnchor(stableKey, topOffset),
            [new MovieRecords { Movie_Id = 42 }],
            FilteredMovieRecsUpdateMode.Reset,
            hasChanges: true
        );

        Assert.That(actual, Is.Null);
    }

    [Test]
    public void CalculateRestoredVerticalOffset_Container位置差を現在Offsetへ反映する()
    {
        double actual = MovieViewScrollAnchorPolicy.CalculateRestoredVerticalOffset(
            currentVerticalOffset: 100,
            currentContainerTop: 24,
            anchorTop: -6
        );

        Assert.That(actual, Is.EqualTo(130));
    }

    [Test]
    public void CalculateRestoredVerticalOffset_計算結果を0以上へclampする()
    {
        double actual = MovieViewScrollAnchorPolicy.CalculateRestoredVerticalOffset(
            currentVerticalOffset: 10,
            currentContainerTop: -30,
            anchorTop: 5
        );

        Assert.That(actual, Is.Zero);
    }

    [TestCase(25, double.NaN, 4, 25)]
    [TestCase(-10, 3, double.PositiveInfinity, 0)]
    [TestCase(double.NaN, 3, 4, 0)]
    [TestCase(double.PositiveInfinity, 3, 4, 0)]
    public void CalculateRestoredVerticalOffset_非finite入力なら現在Offsetをclampして維持する(
        double currentVerticalOffset,
        double currentContainerTop,
        double anchorTop,
        double expected
    )
    {
        double actual = MovieViewScrollAnchorPolicy.CalculateRestoredVerticalOffset(
            currentVerticalOffset,
            currentContainerTop,
            anchorTop
        );

        Assert.That(actual, Is.EqualTo(expected));
    }
}

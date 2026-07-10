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
}

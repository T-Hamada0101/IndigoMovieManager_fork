using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieViewDiffTests
{
    [Test]
    public void NoChangeは選択とスクロールを維持する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 10,
            viewRevision: 10,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(false, 3, 0, 0, 0, 0),
            selectionRefreshApplied: false,
            fallbackReason: ""
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.NoChange));
            Assert.That(diff.OperationLogValue, Is.EqualTo("no-change"));
            Assert.That(diff.SelectionImpact, Is.EqualTo(MovieViewSelectionImpact.Preserve));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Preserve));
            Assert.That(diff.FallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.StableKey, Is.EqualTo(MovieViewDiffFactory.StableKeyMoviePath));
            Assert.That(diff.SourceRevision, Is.EqualTo(10));
            Assert.That(diff.ViewRevision, Is.EqualTo(10));
        });
    }

    [Test]
    public void Resetはscroll_resetとして扱う()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 11,
            viewRevision: 12,
            FilteredMovieRecsUpdateMode.Reset,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 3, 3, 0),
            selectionRefreshApplied: true,
            fallbackReason: "db-switch"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Reset));
            Assert.That(diff.OperationLogValue, Is.EqualTo("reset"));
            Assert.That(diff.SelectionImpact, Is.EqualTo(MovieViewSelectionImpact.Refresh));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Reset));
            Assert.That(diff.FallbackReason, Is.EqualTo("db-switch"));
        });
    }

    [Test]
    public void Diffは追加削除を伴う差分として分類する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 13,
            viewRevision: 14,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 2, 1, 0),
            selectionRefreshApplied: false,
            fallbackReason: "changed-path"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Diff));
            Assert.That(diff.OperationLogValue, Is.EqualTo("diff"));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Recalculate));
            Assert.That(diff.FallbackReason, Is.EqualTo("changed-path"));
        });
    }

    [Test]
    public void Moveは追加削除なしの移動として分類する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 20,
            viewRevision: 20,
            FilteredMovieRecsUpdateMode.Move,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 0, 0, 2),
            selectionRefreshApplied: false,
            fallbackReason: "sort-only"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Move));
            Assert.That(diff.OperationLogValue, Is.EqualTo("move"));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Recalculate));
        });
    }

    [Test]
    public void Move指定でも追加削除が混ざる時はDiffとして扱う()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 21,
            viewRevision: 21,
            FilteredMovieRecsUpdateMode.Move,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 1, 1, 2),
            selectionRefreshApplied: false,
            fallbackReason: "unsafe"
        );

        Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Diff));
    }
}

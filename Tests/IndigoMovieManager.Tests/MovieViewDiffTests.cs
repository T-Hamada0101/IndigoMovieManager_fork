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
            Assert.That(diff.IsDiffApplyCandidate, Is.True);
            Assert.That(diff.ApplyKindLogValue, Is.EqualTo("diff-apply"));
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.StableKey, Is.EqualTo(MovieViewDiffFactory.StableKeyMovieIdOrPath));
            Assert.That(diff.SourceRevision, Is.EqualTo(10));
            Assert.That(diff.ViewRevision, Is.EqualTo(10));
        });
    }

    [Test]
    public void Resetはfull_fallbackとして扱う()
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
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.FullFallback));
            Assert.That(diff.OperationLogValue, Is.EqualTo("full-fallback"));
            Assert.That(diff.SelectionImpact, Is.EqualTo(MovieViewSelectionImpact.Refresh));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Reset));
            Assert.That(diff.FallbackReason, Is.EqualTo("db-switch"));
            Assert.That(diff.IsDiffApplyCandidate, Is.False);
            Assert.That(diff.ApplyKindLogValue, Is.EqualTo("full-fallback"));
            Assert.That(diff.FullFallbackReason, Is.EqualTo("db-switch"));
            Assert.That(diff.AddedCount, Is.EqualTo(3));
            Assert.That(diff.DeletedCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Addは追加差分として分類する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 13,
            viewRevision: 14,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 0, 2, 0),
            selectionRefreshApplied: false,
            fallbackReason: "changed-path"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Add));
            Assert.That(diff.OperationLogValue, Is.EqualTo("add"));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Recalculate));
            Assert.That(diff.FallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.IsDiffApplyCandidate, Is.True);
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.AddedCount, Is.EqualTo(2));
            Assert.That(diff.DeletedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Deleteは削除差分として分類する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 13,
            viewRevision: 14,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 2, 0, 0),
            selectionRefreshApplied: false,
            fallbackReason: "changed-path"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Delete));
            Assert.That(diff.OperationLogValue, Is.EqualTo("delete"));
            Assert.That(diff.ScrollImpact, Is.EqualTo(MovieViewScrollImpact.Recalculate));
            Assert.That(diff.FallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.IsDiffApplyCandidate, Is.True);
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
            Assert.That(diff.AddedCount, Is.EqualTo(0));
            Assert.That(diff.DeletedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Updateは同一stable_keyの置換として分類する()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 13,
            viewRevision: 14,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 2, 2, 0, UpdatedCount: 2),
            selectionRefreshApplied: false,
            fallbackReason: "dirty-fields-unsafe:Hash"
        );

        Assert.Multiple(() =>
        {
            Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Update));
            Assert.That(diff.OperationLogValue, Is.EqualTo("update"));
            Assert.That(diff.FallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonUnsafe));
            Assert.That(diff.IsDiffApplyCandidate, Is.False);
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonUnsafe));
            Assert.That(diff.UpdatedCount, Is.EqualTo(2));
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
            Assert.That(diff.FallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonSort));
            Assert.That(diff.IsDiffApplyCandidate, Is.False);
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonSort));
            Assert.That(diff.MovedCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Move指定でも追加削除が混ざる時はUpdateとして扱う()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 21,
            viewRevision: 21,
            FilteredMovieRecsUpdateMode.Move,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 1, 1, 2),
            selectionRefreshApplied: false,
            fallbackReason: "unsafe"
        );

        Assert.That(diff.Operation, Is.EqualTo(MovieViewDiffOperation.Update));
    }

    [TestCase("", MovieViewDiffFactory.FallbackReasonNone)]
    [TestCase("sort-only", MovieViewDiffFactory.FallbackReasonSort)]
    [TestCase("db-switch", MovieViewDiffFactory.FallbackReasonDbSwitch)]
    [TestCase("dirty-fields-unsafe:Hash", MovieViewDiffFactory.FallbackReasonUnsafe)]
    [TestCase("dup-hash-dirty", MovieViewDiffFactory.FallbackReasonUnsafe)]
    [TestCase("bulk-watch-batch", MovieViewDiffFactory.FallbackReasonMassive)]
    [TestCase("query-change", MovieViewDiffFactory.FallbackReasonQuery)]
    [TestCase("is-get-new", MovieViewDiffFactory.FallbackReasonQuery)]
    [TestCase("changed-path", MovieViewDiffFactory.FallbackReasonNone)]
    [TestCase("thumbnail-success", MovieViewDiffFactory.FallbackReasonNone)]
    public void FallbackReasonはログで選べる最小語彙へ畳む(
        string fallbackReason,
        string expected
    )
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 1,
            viewRevision: 1,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 0, 1, 0),
            selectionRefreshApplied: false,
            fallbackReason
        );

        Assert.That(diff.FallbackReason, Is.EqualTo(expected));
    }

    [TestCase(MovieViewDiffFactory.FallbackReasonQuery)]
    [TestCase(MovieViewDiffFactory.FallbackReasonSort)]
    [TestCase(MovieViewDiffFactory.FallbackReasonDbSwitch)]
    [TestCase(MovieViewDiffFactory.FallbackReasonUnsafe)]
    [TestCase(MovieViewDiffFactory.FallbackReasonMassive)]
    public void FullFallback理由は5語彙だけで判定する(string fallbackReason)
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 1,
            viewRevision: 1,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 0, 1, 0),
            selectionRefreshApplied: false,
            fallbackReason
        );

        Assert.Multiple(() =>
        {
            Assert.That(MovieViewDiffApplyPolicy.IsFullFallbackReason(fallbackReason), Is.True);
            Assert.That(diff.IsDiffApplyCandidate, Is.False);
            Assert.That(diff.ApplyKindLogValue, Is.EqualTo("full-fallback"));
            Assert.That(diff.FullFallbackReason, Is.EqualTo(fallbackReason));
        });
    }

    [TestCase("")]
    [TestCase("none")]
    [TestCase("changed-path")]
    [TestCase("thumbnail-success")]
    [TestCase("single-movie-update")]
    public void 小変更理由はdiff_apply候補に残す(string fallbackReason)
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 1,
            viewRevision: 1,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 0, 1, 0),
            selectionRefreshApplied: false,
            fallbackReason
        );

        Assert.Multiple(() =>
        {
            Assert.That(MovieViewDiffApplyPolicy.IsFullFallbackReason(fallbackReason), Is.False);
            Assert.That(diff.IsDiffApplyCandidate, Is.True);
            Assert.That(diff.ApplyKindLogValue, Is.EqualTo("diff-apply"));
            Assert.That(diff.FullFallbackReason, Is.EqualTo(MovieViewDiffFactory.FallbackReasonNone));
        });
    }

    [Test]
    public void WatchQueryOnlyの変更setはReadModel差分ログと同じdiff_apply候補になる()
    {
        MovieViewDiffApplyPlan requestPlan =
            MovieViewDiffApplyPolicy.ResolveWatchUiApplyCandidate(
                useQueryOnlyReload: true,
                changedMovieCount: 1,
                fullFallbackReason: ""
            );
        MovieViewDiff readModelDiff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 3,
            viewRevision: 3,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 0, 0, 0, UpdatedCount: 1),
            selectionRefreshApplied: false,
            fallbackReason: "changed-path"
        );

        Assert.Multiple(() =>
        {
            Assert.That(requestPlan.IsDiffApplyCandidate, Is.True);
            Assert.That(
                MovieViewDiffApplyPolicy.BuildDiffApplyPlanLogFields(requestPlan),
                Does.Contain("diff_contract=readmodel-diff-v1")
            );
            Assert.That(
                requestPlan.ApplyKindLogValue,
                Is.EqualTo(readModelDiff.ApplyKindLogValue)
            );
            Assert.That(
                requestPlan.FullFallbackReason,
                Is.EqualTo(readModelDiff.FullFallbackReason)
            );
            Assert.That(readModelDiff.IsDiffApplyCandidate, Is.True);
            Assert.That(readModelDiff.ApplyKindLogValue, Is.EqualTo("diff-apply"));
        });
    }

    [Test]
    public void WatchFullFallbackの理由はReadModel差分ログと同じfull_fallback語彙へ畳む()
    {
        MovieViewDiffApplyPlan requestPlan =
            MovieViewDiffApplyPolicy.ResolveWatchUiApplyCandidate(
                useQueryOnlyReload: false,
                changedMovieCount: 1,
                fullFallbackReason: "dirty-fields-unsafe:Hash"
            );
        MovieViewDiff readModelDiff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 4,
            viewRevision: 4,
            FilteredMovieRecsUpdateMode.Reset,
            new FilteredMovieRecsUpdateResult(true, 0, 0, 1, 1, 0),
            selectionRefreshApplied: true,
            fallbackReason: "dirty-fields-unsafe:Hash"
        );

        Assert.Multiple(() =>
        {
            Assert.That(requestPlan.IsDiffApplyCandidate, Is.False);
            Assert.That(
                requestPlan.ApplyKindLogValue,
                Is.EqualTo(readModelDiff.ApplyKindLogValue)
            );
            Assert.That(
                requestPlan.FullFallbackReason,
                Is.EqualTo(readModelDiff.FullFallbackReason)
            );
            Assert.That(readModelDiff.IsDiffApplyCandidate, Is.False);
            Assert.That(readModelDiff.ApplyKindLogValue, Is.EqualTo("full-fallback"));
            Assert.That(
                readModelDiff.FullFallbackReason,
                Is.EqualTo(MovieViewDiffFactory.FallbackReasonUnsafe)
            );
        });
    }

    [Test]
    public void DiffログfieldsはReadModelとwatchで共有できる語彙を出す()
    {
        MovieViewDiff diff = MovieViewDiffFactory.FromCollectionUpdate(
            sourceRevision: 7,
            viewRevision: 8,
            FilteredMovieRecsUpdateMode.Diff,
            new FilteredMovieRecsUpdateResult(true, 1, 1, 0, 1, 0, UpdatedCount: 1),
            selectionRefreshApplied: true,
            fallbackReason: "changed-path"
        );

        string fullLog = MovieViewDiffApplyPolicy.BuildDiffLogFields(diff);
        string planLog = MovieViewDiffApplyPolicy.BuildDiffApplyPlanLogFields(diff.ApplyPlan);

        Assert.Multiple(() =>
        {
            Assert.That(fullLog, Does.Contain("diff_operation=update"));
            Assert.That(fullLog, Does.Contain("diff_contract=readmodel-diff-v1"));
            Assert.That(fullLog.Split("diff_contract=").Length - 1, Is.EqualTo(1));
            Assert.That(fullLog, Does.Contain("diff_apply_kind=diff-apply"));
            Assert.That(fullLog, Does.Contain("diff_apply_candidate=True"));
            Assert.That(fullLog, Does.Contain("diff_full_fallback_reason=none"));
            Assert.That(fullLog, Does.Contain("diff_stable_key=movie-id-or-path"));
            Assert.That(fullLog, Does.Contain("diff_changed_total=2"));
            Assert.That(fullLog, Does.Contain("diff_selection=refresh"));
            Assert.That(planLog, Is.EqualTo("diff_contract=readmodel-diff-v1 diff_apply_kind=diff-apply diff_apply_candidate=True diff_full_fallback_reason=none"));
        });
    }
}

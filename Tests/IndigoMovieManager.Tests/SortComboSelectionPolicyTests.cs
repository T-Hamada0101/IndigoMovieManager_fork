namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SortComboSelectionPolicyTests
{
    [TestCase("", false, 1, "1")]
    [TestCase("movie.wb", true, 1, "1")]
    [TestCase("movie.wb", false, 0, "1")]
    public void BuildPlan_操作できない状態では何もしない(
        string dbFullPath,
        bool isSelectionChangeSuppressed,
        int movieCount,
        string selectedSortId
    )
    {
        SortComboSelectionPlan plan = SortComboSelectionPolicy.BuildPlan(
            dbFullPath,
            isSelectionChangeSuppressed,
            movieCount,
            selectedSortId,
            isStartupFeedPartialActive: false
        );

        Assert.That(plan.ShouldHandle, Is.False);
    }

    [Test]
    public void BuildPlan_選択sortがなければ何もしない()
    {
        SortComboSelectionPlan plan = SortComboSelectionPolicy.BuildPlan(
            "movie.wb",
            isSelectionChangeSuppressed: false,
            movieCount: 1,
            selectedSortId: null,
            isStartupFeedPartialActive: false
        );

        Assert.That(plan.ShouldHandle, Is.False);
    }

    [Test]
    public void BuildPlan_段階ロード中はfull_reload_fallbackへ流す()
    {
        SortComboSelectionPlan plan = SortComboSelectionPolicy.BuildPlan(
            "movie.wb",
            isSelectionChangeSuppressed: false,
            movieCount: 10,
            selectedSortId: "1",
            isStartupFeedPartialActive: true
        );

        Assert.That(plan.ShouldHandle, Is.True);
        Assert.That(plan.SortId, Is.EqualTo("1"));
        Assert.That(plan.ShouldUseStartupFullReload, Is.True);
        Assert.That(plan.ShouldRefreshThumbnailErrorRecords, Is.False);
    }

    [Test]
    public void BuildPlan_通常時はsort_onlyへ流しサムネerror順だけ下部も更新する()
    {
        SortComboSelectionPlan plan = SortComboSelectionPolicy.BuildPlan(
            "movie.wb",
            isSelectionChangeSuppressed: false,
            movieCount: 10,
            selectedSortId: "28",
            isStartupFeedPartialActive: false
        );

        Assert.That(plan.ShouldHandle, Is.True);
        Assert.That(plan.SortId, Is.EqualTo("28"));
        Assert.That(plan.ShouldUseStartupFullReload, Is.False);
        Assert.That(plan.ShouldRefreshThumbnailErrorRecords, Is.True);
    }
}

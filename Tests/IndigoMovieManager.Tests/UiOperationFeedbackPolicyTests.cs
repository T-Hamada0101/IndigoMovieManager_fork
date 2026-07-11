namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiOperationFeedbackPolicyTests
{
    [Test]
    public void DelayMs_表示開始の遅延は250msで固定する()
    {
        Assert.That(UiOperationFeedbackPolicy.DelayMs, Is.EqualTo(250));
    }

    [TestCase("search", "検索中")]
    [TestCase("SEARCH", "検索中")]
    [TestCase("sort", "並び替え中")]
    [TestCase("SoRt", "並び替え中")]
    [TestCase("player", "Player準備中")]
    [TestCase("PLAYER", "Player準備中")]
    public void ResolveStatusText_既知reasonを大小文字によらず表示文言へ変換する(
        string reason,
        string expected
    )
    {
        Assert.That(UiOperationFeedbackPolicy.ResolveStatusText(reason), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unknown")]
    public void ResolveStatusText_未知reasonは処理中へフォールバックする(string? reason)
    {
        Assert.That(UiOperationFeedbackPolicy.ResolveStatusText(reason), Is.EqualTo("処理中"));
    }

    [Test]
    public void ShouldShow_revision一致かつuser_priority中だけ表示する()
    {
        Assert.That(
            UiOperationFeedbackPolicy.ShouldShow(
                delayedRequestRevision: 12,
                currentRevision: 12,
                isUserPriorityActive: true
            ),
            Is.True
        );
    }

    [Test]
    public void ShouldShow_revision不一致なら古いtickを表示しない()
    {
        Assert.That(
            UiOperationFeedbackPolicy.ShouldShow(
                delayedRequestRevision: 12,
                currentRevision: 13,
                isUserPriorityActive: true
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldShow_user_priority解除後は表示しない()
    {
        Assert.That(
            UiOperationFeedbackPolicy.ShouldShow(
                delayedRequestRevision: 12,
                currentRevision: 12,
                isUserPriorityActive: false
            ),
            Is.False
        );
    }

    [Test]
    public void ShouldShow_操作終了時のrevision更新で待機中tickを無効化できる()
    {
        const long delayedRequestRevision = 12;
        long currentRevision = delayedRequestRevision;

        // 操作終了時にrevisionを進め、250ms待機中だった要求を古いものとして捨てる。
        currentRevision++;

        Assert.That(
            UiOperationFeedbackPolicy.ShouldShow(
                delayedRequestRevision,
                currentRevision,
                isUserPriorityActive: true
            ),
            Is.False
        );
    }
}

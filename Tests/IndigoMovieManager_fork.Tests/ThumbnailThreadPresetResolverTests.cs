using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailThreadPresetResolverTests
{
    [TestCase("slow", 8, 16, 2)]
    [TestCase("normal", 8, 12, 4)]
    [TestCase("ballence", 8, 12, 3)]
    [TestCase("fast", 8, 12, 6)]
    [TestCase("max", 8, 12, 12)]
    [TestCase("custum", 9, 12, 9)]
    [TestCase("unknown", 9, 12, 9)]
    [TestCase("custum", 1, 12, 2)]
    [TestCase("max", 8, 64, 24)]
    public void ResolveParallelism_プリセットに応じた並列数を返す(
        string preset,
        int manualParallelism,
        int logicalCoreCount,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveParallelism(
            preset,
            manualParallelism,
            logicalCoreCount);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("fast", 8, 0, 2)]
    [TestCase("normal", 8, -4, 2)]
    [TestCase("max", 8, 0, 2)]
    public void ResolveParallelism_論理コア数が不正でも安全値へ丸める(
        string preset,
        int manualParallelism,
        int logicalCoreCount,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveParallelism(
            preset,
            manualParallelism,
            logicalCoreCount);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("SLOW", "slow")]
    [TestCase(" normal ", "normal")]
    [TestCase("BALLENCE", "ballence")]
    [TestCase("fast", "fast")]
    [TestCase("max", "max")]
    [TestCase("custum", "custum")]
    [TestCase("other", "custum")]
    public void NormalizePresetKey_既知の6値へ丸める(string input, string expected)
    {
        string actual = ThumbnailThreadPresetResolver.NormalizePresetKey(input);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("slow", true)]
    [TestCase("SLOW", true)]
    [TestCase("fast", false)]
    [TestCase("custum", false)]
    public void IsLowLoadPreset_slowだけ低負荷扱いにする(string input, bool expected)
    {
        bool actual = ThumbnailThreadPresetResolver.IsLowLoadPreset(input);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("slow", 3000, 6000)]
    [TestCase("fast", 3000, 3000)]
    [TestCase("custum", 200, 200)]
    [TestCase("slow", 10, 200)]
    public void ResolveQueuePollIntervalMs_低負荷プリセット時だけ待機を伸ばす(
        string preset,
        int basePollIntervalMs,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveQueuePollIntervalMs(
            preset,
            basePollIntervalMs);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveQueuePollIntervalMs_slowでint上限超過時はMaxValueへ丸める()
    {
        int actual = ThumbnailThreadPresetResolver.ResolveQueuePollIntervalMs(
            "slow",
            int.MaxValue);

        Assert.That(actual, Is.EqualTo(int.MaxValue));
    }

    [TestCase("slow", 750)]
    [TestCase("fast", 0)]
    [TestCase("custum", 0)]
    public void ResolveBatchCooldownMs_低負荷プリセット時だけクールダウンを返す(
        string preset,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveBatchCooldownMs(preset);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("slow", 8, 2)]
    [TestCase("ballence", 8, 3)]
    [TestCase("normal", 8, 4)]
    [TestCase("fast", 3, 3)]
    public void ResolveDynamicMinimumParallelism_プリセット意図込みの動的下限を返す(
        string preset,
        int configuredParallelism,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveDynamicMinimumParallelism(
            preset,
            configuredParallelism);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("ballence", 2, 2)]
    [TestCase("normal", 3, 3)]
    [TestCase("slow", 1, 2)]
    public void ResolveDynamicMinimumParallelism_設定上限を超えないよう丸める(
        string preset,
        int configuredParallelism,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveDynamicMinimumParallelism(
            preset,
            configuredParallelism);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("slow", false)]
    [TestCase("ballence", true)]
    [TestCase("fast", true)]
    public void ResolveAllowDynamicScaleUp_slowだけ復帰を止める(string preset, bool expected)
    {
        bool actual = ThumbnailThreadPresetResolver.ResolveAllowDynamicScaleUp(preset);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("slow", 2)]
    [TestCase("normal", 3)]
    [TestCase("ballence", 4)]
    [TestCase("fast", 2)]
    public void ResolveScaleUpDemandFactor_プリセットごとに復帰需要係数を返す(
        string preset,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveScaleUpDemandFactor(preset);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("custum", 2)]
    [TestCase("unknown", 2)]
    public void ResolveScaleUpDemandFactor_既定外は高速系既定値へ寄せる(
        string preset,
        int expected)
    {
        int actual = ThumbnailThreadPresetResolver.ResolveScaleUpDemandFactor(preset);

        Assert.That(actual, Is.EqualTo(expected));
    }
}

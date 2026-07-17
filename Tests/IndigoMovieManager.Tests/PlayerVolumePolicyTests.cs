namespace IndigoMovieManager.Tests;

using System.Text.Json;

[TestFixture]
public sealed class PlayerVolumePolicyTests
{
    [Test]
    public void 初期音量は25パーセントに固定する()
    {
        Assert.That(PlayerVolumePolicy.DefaultVolume, Is.EqualTo(0.25d));
    }

    [TestCase(0d)]
    [TestCase(0.25d)]
    [TestCase(1d)]
    public void 正規音量は100パーセントを含めて維持する(double volume)
    {
        Assert.That(PlayerVolumePolicy.Normalize(volume), Is.EqualTo(volume));
        Assert.That(PlayerVolumePolicy.RequiresRepair(volume), Is.False);
    }

    [TestCase(-0.1d, 0d)]
    [TestCase(1.1d, 1d)]
    public void 範囲外の保存値だけを安全域へ修復する(double volume, double expected)
    {
        Assert.That(PlayerVolumePolicy.Normalize(volume), Is.EqualTo(expected));
        Assert.That(PlayerVolumePolicy.RequiresRepair(volume), Is.True);
    }

    [Test]
    public void 非数値は初期音量へ修復する()
    {
        Assert.That(PlayerVolumePolicy.Normalize(double.NaN), Is.EqualTo(0.25d));
        Assert.That(PlayerVolumePolicy.Normalize(double.PositiveInfinity), Is.EqualTo(0.25d));
        Assert.That(PlayerVolumePolicy.RequiresRepair(double.NaN), Is.True);
        Assert.That(PlayerVolumePolicy.RequiresRepair(double.PositiveInfinity), Is.True);
    }

    [Test]
    public void 全画面スナップショットはWebViewの最新音量を復元する()
    {
        const string json = """{"currentTime":12.5,"paused":true,"volume":0.37}""";

        PlayerWebViewPlaybackSnapshot snapshot =
            JsonSerializer.Deserialize<PlayerWebViewPlaybackSnapshot>(json)
            ?? throw new AssertionException("全画面スナップショットを復元できませんでした。");

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.CurrentTime, Is.EqualTo(12.5d));
            Assert.That(snapshot.Paused, Is.True);
            Assert.That(snapshot.Volume, Is.EqualTo(0.37d));
        });
    }
}

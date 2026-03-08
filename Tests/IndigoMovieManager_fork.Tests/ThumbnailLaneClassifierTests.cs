using System.Reflection;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailLaneClassifierTests
{
    private const string SlowLaneEnvName = "IMM_THUMB_SLOW_LANE_MIN_GB";
    private string? originalSlowLaneEnvValue;
    private int originalSlowLaneMinGb;

    [SetUp]
    public void SetUp()
    {
        originalSlowLaneEnvValue = Environment.GetEnvironmentVariable(SlowLaneEnvName);
        originalSlowLaneMinGb = IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb;
        Environment.SetEnvironmentVariable(SlowLaneEnvName, null);
        ResetClassifierCache();
    }

    [TearDown]
    public void TearDown()
    {
        IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = originalSlowLaneMinGb;
        Environment.SetEnvironmentVariable(SlowLaneEnvName, originalSlowLaneEnvValue);
        ResetClassifierCache();
    }

    [Test]
    public void ResolveSlowLaneMinBytes_環境変数があれば設定値より優先する()
    {
        IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = 50;
        Environment.SetEnvironmentVariable(SlowLaneEnvName, "77");
        ResetClassifierCache();

        long slowLaneMinBytes = ThumbnailLaneClassifier.ResolveSlowLaneMinBytes();
        long expectedBytes = 77L * 1024L * 1024L * 1024L;

        Assert.That(slowLaneMinBytes, Is.EqualTo(expectedBytes));
        Assert.That(
            ThumbnailLaneClassifier.ResolveLane(expectedBytes - 1),
            Is.EqualTo(ThumbnailExecutionLane.Normal)
        );
        Assert.That(
            ThumbnailLaneClassifier.ResolveLane(expectedBytes),
            Is.EqualTo(ThumbnailExecutionLane.Slow)
        );
    }

    [Test]
    public void ResolveSlowLaneMinBytes_不正な環境変数は設定値へフォールバックする()
    {
        IndigoMovieManager.Properties.Settings.Default.ThumbnailSlowLaneMinGb = 12;
        Environment.SetEnvironmentVariable(SlowLaneEnvName, "0");
        ResetClassifierCache();

        long slowLaneMinBytes = ThumbnailLaneClassifier.ResolveSlowLaneMinBytes();
        long expectedBytes = 12L * 1024L * 1024L * 1024L;

        Assert.That(slowLaneMinBytes, Is.EqualTo(expectedBytes));
    }

    // キャッシュが残ると環境変数変更が次テストへ漏れるため、反射で初期化する。
    private static void ResetClassifierCache()
    {
        Type type = typeof(ThumbnailLaneClassifier);
        FieldInfo? ticksField = type.GetField(
            "lastSettingsReadUtcTicks",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        FieldInfo? cachedSlowLaneField = type.GetField(
            "cachedSlowLaneMinGb",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.That(ticksField, Is.Not.Null);
        Assert.That(cachedSlowLaneField, Is.Not.Null);

        ticksField!.SetValue(null, 0L);
        cachedSlowLaneField!.SetValue(null, 50);
    }
}

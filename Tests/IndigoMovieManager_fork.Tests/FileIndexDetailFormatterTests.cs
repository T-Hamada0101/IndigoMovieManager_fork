using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FileIndexDetailFormatterTests
{
    [Test]
    public void Describe_AvailabilityAdminServiceUnavailableは接続不能文言を返す()
    {
        (string code, string message) = FileIndexDetailFormatter.Describe(
            "usnmft",
            $"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminServiceUnavailable"
        );

        Assert.That(
            code,
            Is.EqualTo($"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminServiceUnavailable")
        );
        Assert.That(message, Is.EqualTo("usnmft の管理者サービスへ接続できないため通常監視で継続します"));
    }

    [Test]
    public void Describe_AvailabilityTimeoutは可用性確認タイムアウト文言を返す()
    {
        (_, string message) = FileIndexDetailFormatter.Describe(
            "usnmft",
            $"{EverythingReasonCodes.AvailabilityErrorPrefix}TimeoutException"
        );

        Assert.That(message, Is.EqualTo("usnmft の可用性確認がタイムアウトしたため通常監視で継続します"));
    }

    [Test]
    public void Describe_QueryTimeoutは応答タイムアウト文言を返す()
    {
        (_, string message) = FileIndexDetailFormatter.Describe(
            "usnmft",
            $"{EverythingReasonCodes.EverythingQueryErrorPrefix}TimeoutException"
        );

        Assert.That(message, Is.EqualTo("usnmft の応答がタイムアウトしたため通常監視へ切り替えます"));
    }

    [Test]
    public void Describe_QueryUnauthorizedは権限不足文言を返す()
    {
        (_, string message) = FileIndexDetailFormatter.Describe(
            "usnmft",
            $"{EverythingReasonCodes.EverythingQueryErrorPrefix}UnauthorizedAccessException"
        );

        Assert.That(message, Is.EqualTo("usnmft の問い合わせで権限不足が発生したため通常監視へ切り替えます"));
    }

    [Test]
    public void Describe_EmptyResultFallbackは再走査文言を返す()
    {
        (_, string message) = FileIndexDetailFormatter.Describe(
            "Everything",
            EverythingReasonCodes.EmptyResultFallback
        );

        Assert.That(message, Is.EqualTo("Everything が0件を返したため通常監視で再走査しました"));
    }
}

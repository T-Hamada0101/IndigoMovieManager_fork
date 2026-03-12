using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailPlaceholderUtilityTests
{
    [Test]
    public void ClassifyFailure_UnknownCodec単独ではUnsupported扱いにしない()
    {
        FailurePlaceholderKind actual = ThumbnailPlaceholderUtility.ClassifyFailure(
            "unknown",
            ["Object reference not set to an instance of an object."]
        );

        Assert.That(actual, Is.EqualTo(FailurePlaceholderKind.None));
    }

    [Test]
    public void ClassifyFailure_H264破損ログはUnsupported扱いにしない()
    {
        FailurePlaceholderKind actual = ThumbnailPlaceholderUtility.ClassifyFailure(
            "h264",
            [
                "exit=69, err=[h264 @ 000002a425411240] Invalid NAL unit size (0 > 1266).",
                "[h264 @ 000002a425411240] missing picture in access unit with size 1270",
            ]
        );

        Assert.That(actual, Is.EqualTo(FailurePlaceholderKind.None));
    }

    [Test]
    public void ClassifyFailure_Codec空でも破損ログ優先でUnsupported扱いにしない()
    {
        FailurePlaceholderKind actual = ThumbnailPlaceholderUtility.ClassifyFailure(
            "",
            [
                "Invalid data found when processing input",
                "Invalid NAL unit size (0 > 1266).",
                "Error splitting the input into NAL units.",
                "Error submitting packet to decoder: Invalid data found when processing input",
            ]
        );

        Assert.That(actual, Is.EqualTo(FailurePlaceholderKind.None));
    }

    [Test]
    public void ClassifyFailure_UnknownCodec文言はUnsupported扱いを維持する()
    {
        FailurePlaceholderKind actual = ThumbnailPlaceholderUtility.ClassifyFailure(
            "aac",
            ["unknown codec"]
        );

        Assert.That(actual, Is.EqualTo(FailurePlaceholderKind.UnsupportedCodec));
    }
}

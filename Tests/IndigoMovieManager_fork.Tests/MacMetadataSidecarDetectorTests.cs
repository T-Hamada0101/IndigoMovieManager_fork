using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class MacMetadataSidecarDetectorTests
{
    [Test]
    public void IsAppleDoubleSidecar_AppleDoubleHeader付きのDotUnderscoreだけtrueを返す()
    {
        string path = Path.Combine(Path.GetTempPath(), $".__mac_meta_{Guid.NewGuid():N}.mp4");
        try
        {
            File.WriteAllBytes(
                path,
                [
                    0x00,
                    0x05,
                    0x16,
                    0x07,
                    0x00,
                    0x02,
                    0x00,
                    0x00,
                ]
            );

            bool actual = MacMetadataSidecarDetector.IsAppleDoubleSidecar(path);

            Assert.That(actual, Is.True);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void IsAppleDoubleSidecar_名前一致でもヘッダー不一致ならfalseを返す()
    {
        string path = Path.Combine(Path.GetTempPath(), $".__fake_meta_{Guid.NewGuid():N}.mp4");
        try
        {
            File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x00]);

            bool actual = MacMetadataSidecarDetector.IsAppleDoubleSidecar(path);

            Assert.That(actual, Is.False);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void IsAppleDoubleSidecar_通常ファイル名は読まずにfalseを返す()
    {
        string path = Path.Combine(Path.GetTempPath(), $"movie_{Guid.NewGuid():N}.mp4");
        try
        {
            File.WriteAllBytes(
                path,
                [
                    0x00,
                    0x05,
                    0x16,
                    0x07,
                ]
            );

            bool actual = MacMetadataSidecarDetector.IsAppleDoubleSidecar(path);

            Assert.That(actual, Is.False);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

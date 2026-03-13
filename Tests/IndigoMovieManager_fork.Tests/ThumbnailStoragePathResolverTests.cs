using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailStoragePathResolverTests
{
    [Test]
    public void TabInfo_設定済みなら指定されたThumbFolderを使う()
    {
        string configured = Path.Combine(Path.GetTempPath(), "custom-thumb-root");
        TabInfo tabInfo = new(0, "sampledb", configured);
        string expected = Path.Combine(configured, "120x90x3x1");

        Assert.That(tabInfo.OutPath, Is.EqualTo(expected));
    }

    [Test]
    [NonParallelizable]
    public void TabInfo_未設定時は作業ディレクトリではなく実行ファイル配置先を使う()
    {
        string originalCurrentDirectory = Directory.GetCurrentDirectory();
        string tempCurrentDirectory = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(tempCurrentDirectory);
        try
        {
            Directory.SetCurrentDirectory(tempCurrentDirectory);

            TabInfo tabInfo = new(0, "sampledb", "");
            string expected = Path.Combine(
                Path.GetFullPath(AppContext.BaseDirectory),
                "Thumb",
                "sampledb",
                "120x90x3x1"
            );
            string unexpected = Path.Combine(
                tempCurrentDirectory,
                "Thumb",
                "sampledb",
                "120x90x3x1"
            );

            Assert.That(tabInfo.OutPath, Is.EqualTo(expected));
            Assert.That(tabInfo.OutPath, Is.Not.EqualTo(unexpected));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            if (Directory.Exists(tempCurrentDirectory))
            {
                Directory.Delete(tempCurrentDirectory, recursive: true);
            }
        }
    }
}

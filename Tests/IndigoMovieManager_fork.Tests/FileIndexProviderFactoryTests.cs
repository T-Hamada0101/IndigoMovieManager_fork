using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FileIndexProviderFactoryTests
{
    [TestCase("everything", FileIndexProviderFactory.ProviderEverything)]
    [TestCase("Everything", FileIndexProviderFactory.ProviderEverything)]
    [TestCase("usnmft", FileIndexProviderFactory.ProviderUsnMft)]
    [TestCase("USNMFT", FileIndexProviderFactory.ProviderUsnMft)]
    [TestCase("standardfilesystem", FileIndexProviderFactory.ProviderStandardFileSystem)]
    [TestCase("StandardFileSystem", FileIndexProviderFactory.ProviderStandardFileSystem)]
    [TestCase("", FileIndexProviderFactory.ProviderEverything)]
    [TestCase("unknown", FileIndexProviderFactory.ProviderEverything)]
    public void NormalizeProviderKey_RoundsToKnownValue(string raw, string expected)
    {
        string normalized = FileIndexProviderFactory.NormalizeProviderKey(raw);

        Assert.That(normalized, Is.EqualTo(expected));
    }
}

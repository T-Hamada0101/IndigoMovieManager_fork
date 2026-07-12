namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DockLayoutStorageTests
{
    [Test]
    public void 旧インストール先のlayoutをユーザーデータ領域へ移行する()
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            $"IndigoMovieManager-DockLayoutStorageTests-{Guid.NewGuid():N}"
        );
        string legacyDirectory = Path.Combine(testRoot, "legacy");
        string targetDirectory = Path.Combine(testRoot, "target");

        try
        {
            Directory.CreateDirectory(legacyDirectory);
            File.WriteAllText(Path.Combine(legacyDirectory, "layout.xml"), "current");
            File.WriteAllText(Path.Combine(legacyDirectory, "layout.default.xml"), "default");

            IReadOnlyList<string> migratedFiles = DockLayoutStorage.MigrateLegacyFiles(
                legacyDirectory,
                targetDirectory
            );

            Assert.Multiple(() =>
            {
                Assert.That(migratedFiles, Is.EquivalentTo(new[] { "layout.xml", "layout.default.xml" }));
                Assert.That(File.Exists(Path.Combine(legacyDirectory, "layout.xml")), Is.False);
                Assert.That(File.Exists(Path.Combine(legacyDirectory, "layout.default.xml")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "layout.xml")), Is.EqualTo("current"));
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "layout.default.xml")), Is.EqualTo("default"));
            });
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Test]
    public void 新保存先に同名layoutがある時は上書きしない()
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            $"IndigoMovieManager-DockLayoutStorageTests-{Guid.NewGuid():N}"
        );
        string legacyDirectory = Path.Combine(testRoot, "legacy");
        string targetDirectory = Path.Combine(testRoot, "target");

        try
        {
            Directory.CreateDirectory(legacyDirectory);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(Path.Combine(legacyDirectory, "layout.xml"), "legacy");
            File.WriteAllText(Path.Combine(targetDirectory, "layout.xml"), "current");

            IReadOnlyList<string> migratedFiles = DockLayoutStorage.MigrateLegacyFiles(
                legacyDirectory,
                targetDirectory
            );

            Assert.Multiple(() =>
            {
                Assert.That(migratedFiles, Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(legacyDirectory, "layout.xml")), Is.EqualTo("legacy"));
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "layout.xml")), Is.EqualTo("current"));
            });
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Test]
    public void 通常保存先はLocalAppData配下に固定する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DockLayoutStorage.LayoutFilePath, Does.StartWith(AppLocalDataPaths.LayoutsPath));
            Assert.That(DockLayoutStorage.LayoutFilePath, Does.EndWith("layout.xml"));
            Assert.That(DockLayoutStorage.DefaultLayoutFilePath, Does.EndWith("layout.default.xml"));
            Assert.That(
                AppLocalDataPaths.LayoutsPath,
                Does.StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            );
        });
    }
}

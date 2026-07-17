using IndigoMovieManager.BottomTabs.FileOrganizer;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileOrganizerDestinationItemTests
{
    [Test]
    public void ショートカットは登録済みかつONの時だけ有効になる()
    {
        FileOrganizerDestinationItem item = new(3);

        Assert.That(item.IsShortcutActive, Is.False);

        item.FolderPath = @"C:\Movies";
        Assert.That(item.IsShortcutActive, Is.False, "起動時のONは既定OFFとする");

        item.IsShortcutEnabled = true;
        Assert.That(item.IsShortcutActive, Is.True);

        item.FolderPath = "";
        Assert.That(item.IsShortcutActive, Is.False);
    }
}

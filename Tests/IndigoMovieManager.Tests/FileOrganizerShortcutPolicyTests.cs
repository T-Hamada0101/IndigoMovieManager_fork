using System.Windows.Input;
using IndigoMovieManager.BottomTabs.FileOrganizer;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class FileOrganizerShortcutPolicyTests
{
    [TestCase(Key.D1, 1)]
    [TestCase(Key.D9, 9)]
    [TestCase(Key.NumPad1, 1)]
    [TestCase(Key.NumPad9, 9)]
    public void アクティブ時はCtrlと数字1から9を移動先番号へ変換する(Key key, int expected)
    {
        int actual = FileOrganizerShortcutPolicy.ResolveShortcutNumber(
            key,
            ModifierKeys.Control,
            isFileOrganizerActive: true
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(Key.D1, ModifierKeys.Control, false)]
    [TestCase(Key.D1, ModifierKeys.None, true)]
    [TestCase(Key.D1, ModifierKeys.Control | ModifierKeys.Shift, true)]
    [TestCase(Key.D0, ModifierKeys.Control, true)]
    [TestCase(Key.A, ModifierKeys.Control, true)]
    public void 非アクティブまたは契約外のキーは反応しない(
        Key key,
        ModifierKeys modifiers,
        bool isActive
    )
    {
        int actual = FileOrganizerShortcutPolicy.ResolveShortcutNumber(key, modifiers, isActive);

        Assert.That(actual, Is.Zero);
    }
}

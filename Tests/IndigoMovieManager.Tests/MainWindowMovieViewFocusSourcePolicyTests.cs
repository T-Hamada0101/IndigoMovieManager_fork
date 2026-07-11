namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowMovieViewFocusSourcePolicyTests
{
    [Test]
    public void Focus接続は現在の標準タブ内だけを捕捉し実現済みReset対象だけへ戻す()
    {
        string source = GetRepoText(
            "UpperTabs",
            "Common",
            "MainWindow.UpperTabs.Focus.cs"
        );
        string capture = GetMethodBlock(source, "private MovieViewFocusContext? CaptureMovieViewFocus()");
        string restore = GetMethodBlock(source, "private void RestoreMovieViewFocus(");

        Assert.That(source, Does.Contain("int TabIndex,"));
        Assert.That(source, Does.Contain("MovieViewFocusAnchor Anchor"));
        Assert.That(capture, Does.Contain("TryGetCurrentUpperTabContext("));
        Assert.That(capture, Does.Contain("!isStandardUpperTab"));
        Assert.That(capture, Does.Contain("!itemsControl.IsKeyboardFocusWithin"));
        Assert.That(capture, Does.Contain("Keyboard.FocusedElement"));
        Assert.That(capture, Does.Contain("ItemsControl.ContainerFromElement("));
        Assert.That(capture, Does.Contain("ItemContainerGenerator.ItemFromContainer("));
        Assert.That(capture, Does.Contain("frameworkElement.DataContext is MovieRecords"));
        Assert.That(capture, Does.Contain("MovieViewFocusAnchorPolicy.TryCapture("));

        Assert.That(restore, Does.Contain("!IsActive"));
        Assert.That(restore, Does.Contain("currentTabIndex != captured.TabIndex"));
        Assert.That(restore, Does.Contain("MovieViewFocusAnchorPolicy.ResolveAfterCollectionApply("));
        Assert.That(restore, Does.Contain("ItemContainerGenerator.ContainerFromItem(focusedMovie)"));
        Assert.That(restore, Does.Contain("is not UIElement realizedContainer"));
        Assert.That(restore, Does.Contain("realizedContainer.Focus()"));
        Assert.That(restore, Does.Not.Contain("ScrollIntoView("));
        Assert.That(restore, Does.Not.Contain("UpdateLayout("));
        Assert.That(source, Does.Not.Contain("Dispatcher"));
        Assert.That(source, Does.Not.Contain("DebugRuntimeLog"));
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);

        int braceStart = source.IndexOf('{', start);
        Assert.That(braceStart, Is.GreaterThan(start), signature);

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(index + 1)];
                }
            }
        }

        Assert.Fail($"メソッド終端を解決できません: {signature}");
        return string.Empty;
    }
}

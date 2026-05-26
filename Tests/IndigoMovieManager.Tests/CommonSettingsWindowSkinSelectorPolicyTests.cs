using System.IO;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class CommonSettingsWindowSkinSelectorPolicyTests
{
    [Test]
    public void 設定画面skin一覧更新は同期catalog取得へ戻らない()
    {
        string settingsSource = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml.cs");
        string refreshMethod = ExtractMethod(
            settingsSource,
            "private async Task RefreshSkinSelectorAsync()"
        );
        string initializeMethod = ExtractMethod(settingsSource, "private void InitializeSkinSelector()");

        Assert.Multiple(() =>
        {
            Assert.That(
                settingsSource,
                Does.Contain("Activated += async (_, _) => await RefreshSkinSelectorAsync();")
            );
            Assert.That(initializeMethod, Does.Contain("_ = RefreshSkinSelectorAsync();"));
            Assert.That(
                refreshMethod,
                Does.Contain("await skinOrchestrator.GetAvailableSkinDefinitionsAsync();")
            );
            Assert.That(refreshMethod, Does.Not.Contain("GetAvailableSkinDefinitions()"));
            Assert.That(refreshMethod, Does.Contain("_skinSelectorRefreshRevision"));
            Assert.That(refreshMethod, Does.Contain("revision != _skinSelectorRefreshRevision"));
            Assert.That(refreshMethod, Does.Contain("catch (Exception ex)"));
            Assert.That(refreshMethod, Does.Contain("DebugRuntimeLog.Write("));
            Assert.That(refreshMethod, Does.Contain("\"settings-ui\""));
            Assert.That(refreshMethod, Does.Contain("SkinComboBox.IsEnabled = hasCurrentDb;"));
            Assert.That(refreshMethod, Does.Contain("UpdateSkinSelectorToolTip("));
            Assert.That(refreshMethod, Does.Contain("SkinComboBox.IsEnabled = false;"));
        });
    }

    [Test]
    public void Orchestratorのasync一覧取得はTaskRunでcatalog_loadを背景化する()
    {
        string orchestratorSource = GetRepoText("WhiteBrowserSkin", "WhiteBrowserSkinOrchestrator.cs");
        string asyncMethod = ExtractMethod(
            orchestratorSource,
            "public async Task<IReadOnlyList<WhiteBrowserSkinDefinition>> GetAvailableSkinDefinitionsAsync()"
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                asyncMethod,
                Does.Contain("await Task.Run(() => WhiteBrowserSkinCatalogService.Load(skinRootPath))")
            );
            Assert.That(asyncMethod, Does.Contain("availableSkinDefinitions = loadedDefinitions;"));
            Assert.That(asyncMethod, Does.Contain("return BuildAvailableSkinDefinitionSnapshot();"));
        });
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

        Assert.Fail($"{Path.Combine(relativePathParts)} の位置を repo root から解決できませんでした。");
        return string.Empty;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
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
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return string.Empty;
    }
}

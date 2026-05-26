using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinOrchestratorCatalogReuseTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinCatalogService.ResetCacheForTesting();
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void GetAvailableSkinDefinitionsとApplySkinByNameは同一catalog_cacheを再利用する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReuseGrid");
        string currentSkinName = "";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            bool applied = orchestrator.ApplySkinByName("ReuseGrid", persistToCurrentDb: false);
            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(currentSkinName, Is.EqualTo("ReuseGrid"));
                Assert.That(selectedTabs, Is.EqualTo(new[] { "DefaultGrid" }));
                Assert.That(first, Is.SameAs(second));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(2));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(3));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public async Task GetAvailableSkinDefinitionsAsyncはcatalog_cacheを背景経路で更新する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("AsyncListGrid");
        string currentSkinName = "AsyncListGrid";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                await orchestrator.GetAvailableSkinDefinitionsAsync();
            WhiteBrowserSkinDefinition? externalSkin =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "AsyncListGrid");

            Assert.Multiple(() =>
            {
                Assert.That(externalSkin, Is.Not.Null);
                Assert.That(externalSkin!.Name, Is.EqualTo("AsyncListGrid"));
                Assert.That(orchestrator.GetCurrentSkinName(), Is.EqualTo("AsyncListGrid"));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void ApplySkinByNameはbuilt_in_skinならcatalog署名確認を繰り返さない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ExternalForBuiltInApply");
        string currentSkinName = "";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            _ = orchestrator.GetAvailableSkinDefinitions();
            bool firstApplied = orchestrator.ApplySkinByName("DefaultGrid", persistToCurrentDb: false);
            bool secondApplied = orchestrator.ApplySkinByName("DefaultGrid", persistToCurrentDb: false);

            Assert.Multiple(() =>
            {
                Assert.That(firstApplied, Is.True);
                Assert.That(secondApplied, Is.True);
                Assert.That(currentSkinName, Is.EqualTo("DefaultGrid"));
                Assert.That(selectedTabs, Is.EqualTo(new[] { "DefaultGrid", "DefaultGrid" }));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void ApplySkinByNameは未ロードでもbuilt_in_skinならcatalog署名確認へ進まない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ExternalForColdBuiltInApply");
        string currentSkinName = "";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            bool applied = orchestrator.ApplySkinByName("DefaultGrid", persistToCurrentDb: false);

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(currentSkinName, Is.EqualTo("DefaultGrid"));
                Assert.That(selectedTabs, Is.EqualTo(new[] { "DefaultGrid" }));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(0));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void ApplySkinByNameはsnapshotにないskinならcatalogを再確認する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("FirstGrid");
        string secondSkinDirectoryPath = Path.Combine(rootPath, "SecondGrid");
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            _ = orchestrator.GetAvailableSkinDefinitions();
            Directory.CreateDirectory(secondSkinDirectoryPath);
            File.WriteAllText(
                Path.Combine(secondSkinDirectoryPath, "SecondGrid.htm"),
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 180;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );

            bool applied = orchestrator.ApplySkinByName("SecondGrid", persistToCurrentDb: false);

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(currentSkinName, Is.EqualTo("SecondGrid"));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void GetCurrentSkinDefinitionは未ロードのbuilt_in_skinならcatalog署名確認へ進まない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ExternalForColdCurrentBuiltIn");
        string currentSkinName = "DefaultGrid";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            WhiteBrowserSkinDefinition definition = orchestrator.GetCurrentSkinDefinition();

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Name, Is.EqualTo("DefaultGrid"));
                Assert.That(definition.IsBuiltIn, Is.True);
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(0));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void GetCurrentSkinDefinitionもbuilt_in_skinならcatalog署名確認を繰り返さない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ExternalForCurrentBuiltIn");
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            _ = orchestrator.GetAvailableSkinDefinitions();
            currentSkinName = "DefaultGrid";
            WhiteBrowserSkinDefinition definition = orchestrator.GetCurrentSkinDefinition();

            Assert.Multiple(() =>
            {
                Assert.That(definition, Is.Not.Null);
                Assert.That(definition.Name, Is.EqualTo("DefaultGrid"));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void RefreshCurrentSkinDefinitionは同名外部skinのhtml更新を再読込する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("RefreshSameExternal", thumbWidth: 160);
        string htmlPath = Path.Combine(rootPath, "RefreshSameExternal", "RefreshSameExternal.htm");
        string currentSkinName = "RefreshSameExternal";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            WhiteBrowserSkinDefinition first = orchestrator.GetCurrentSkinDefinition();
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 224;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));

            WhiteBrowserSkinDefinition refreshed = orchestrator.RefreshCurrentSkinDefinition();

            Assert.Multiple(() =>
            {
                Assert.That(first.Config.ThumbWidth, Is.EqualTo(160));
                Assert.That(refreshed.Config.ThumbWidth, Is.EqualTo(224));
                Assert.That(refreshed.IsMissing, Is.False);
                Assert.That(selectedTabs, Is.Empty);
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public async Task RefreshCurrentSkinDefinitionAsyncは同名外部skinのhtml更新を再読込する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("RefreshSameExternalAsync", thumbWidth: 144);
        string htmlPath = Path.Combine(rootPath, "RefreshSameExternalAsync", "RefreshSameExternalAsync.htm");
        string currentSkinName = "RefreshSameExternalAsync";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            WhiteBrowserSkinDefinition first = orchestrator.GetCurrentSkinDefinition();
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 288;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));

            WhiteBrowserSkinDefinition refreshed =
                await orchestrator.RefreshCurrentSkinDefinitionAsync();

            Assert.Multiple(() =>
            {
                Assert.That(first.Config.ThumbWidth, Is.EqualTo(144));
                Assert.That(refreshed.Config.ThumbWidth, Is.EqualTo(288));
                Assert.That(refreshed.IsMissing, Is.False);
                Assert.That(selectedTabs, Is.Empty);
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void RefreshCurrentSkinDefinitionは削除済み外部skinをmissingへ更新する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("RefreshDeletedExternal");
        string skinDirectoryPath = Path.Combine(rootPath, "RefreshDeletedExternal");
        string currentSkinName = "RefreshDeletedExternal";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            WhiteBrowserSkinDefinition first = orchestrator.GetCurrentSkinDefinition();
            Directory.Delete(skinDirectoryPath, recursive: true);

            WhiteBrowserSkinDefinition refreshed = orchestrator.RefreshCurrentSkinDefinition();

            Assert.Multiple(() =>
            {
                Assert.That(first.IsMissing, Is.False);
                Assert.That(refreshed.Name, Is.EqualTo("RefreshDeletedExternal"));
                Assert.That(refreshed.IsMissing, Is.True);
                Assert.That(refreshed.RequiresWebView2, Is.True);
                Assert.That(selectedTabs, Is.Empty);
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public async Task RefreshCurrentSkinDefinitionAsyncは削除済み外部skinをmissingへ更新する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("RefreshDeletedExternalAsync");
        string skinDirectoryPath = Path.Combine(rootPath, "RefreshDeletedExternalAsync");
        string currentSkinName = "RefreshDeletedExternalAsync";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            WhiteBrowserSkinDefinition first = orchestrator.GetCurrentSkinDefinition();
            Directory.Delete(skinDirectoryPath, recursive: true);

            WhiteBrowserSkinDefinition refreshed =
                await orchestrator.RefreshCurrentSkinDefinitionAsync();

            Assert.Multiple(() =>
            {
                Assert.That(first.IsMissing, Is.False);
                Assert.That(refreshed.Name, Is.EqualTo("RefreshDeletedExternalAsync"));
                Assert.That(refreshed.IsMissing, Is.True);
                Assert.That(refreshed.RequiresWebView2, Is.True);
                Assert.That(selectedTabs, Is.Empty);
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void CachedAvailableSkinDefinitionsはcatalog署名確認を繰り返さない()
    {
        string rootPath = CreateSkinRootWithSingleSkin("HeaderCachedGrid");
        string currentSkinName = "HeaderCachedGrid";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            IReadOnlyList<WhiteBrowserSkinDefinition> cachedFirst =
                orchestrator.GetCachedAvailableSkinDefinitions();
            IReadOnlyList<WhiteBrowserSkinDefinition> cachedSecond =
                orchestrator.GetCachedAvailableSkinDefinitions();

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(cachedFirst));
                Assert.That(cachedFirst, Is.SameAs(cachedSecond));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(0));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogSignatureBuildCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void ApplySkinByName後にhtml更新が入ると次回一覧取得でcatalog_cacheを再読込する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReloadOrchestratorGrid", thumbWidth: 160);
        string htmlPath = Path.Combine(
            rootPath,
            "ReloadOrchestratorGrid",
            "ReloadOrchestratorGrid.htm"
        );
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            bool applied = orchestrator.ApplySkinByName("ReloadOrchestratorGrid", persistToCurrentDb: false);
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 220;
                    thum-height : 160;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition reloaded = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReloadOrchestratorGrid"
            );

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.Config.ThumbWidth, Is.EqualTo(220));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void 一部skin更新後の一覧再取得では未変更skin定義を参照再利用する()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-orchestrator-reuse-{Guid.NewGuid():N}"
        );
        string keepSkinDirectoryPath = Path.Combine(rootPath, "ReuseKeepSkin");
        string changedSkinDirectoryPath = Path.Combine(rootPath, "ReuseChangedSkin");
        Directory.CreateDirectory(keepSkinDirectoryPath);
        Directory.CreateDirectory(changedSkinDirectoryPath);
        File.WriteAllText(
            Path.Combine(keepSkinDirectoryPath, "ReuseKeepSkin.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 160;
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        string changedHtmlPath = Path.Combine(changedSkinDirectoryPath, "ReuseChangedSkin.htm");
        File.WriteAllText(
            changedHtmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 200;
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition firstKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition firstChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseChangedSkin"
            );

            File.WriteAllText(
                changedHtmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 240;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(changedHtmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition secondKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition secondChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseChangedSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(firstKeep, Is.SameAs(secondKeep));
                Assert.That(firstChanged, Is.Not.SameAs(secondChanged));
                Assert.That(secondChanged, Is.Not.Null);
                Assert.That(secondChanged.Config.ThumbWidth, Is.EqualTo(240));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreReusedDefinitionCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    private static WhiteBrowserSkinOrchestrator CreateOrchestrator(
        string skinRootPath,
        Func<string> getCurrentSkinNameFromViewModel,
        Action<string> setCurrentSkinNameToViewModel,
        Action<string>? selectUpperTabDefaultViewBySkinName = null
    )
    {
        return new WhiteBrowserSkinOrchestrator(
            getCurrentDbFullPath: () => "",
            getCurrentSkinNameFromViewModel: getCurrentSkinNameFromViewModel,
            setCurrentSkinNameToViewModel: setCurrentSkinNameToViewModel,
            normalizeTabStateName: skinName => string.IsNullOrWhiteSpace(skinName) ? "DefaultGrid" : skinName,
            selectUpperTabDefaultViewBySkinName: selectUpperTabDefaultViewBySkinName ?? (_ => { }),
            getCurrentUpperTabFixedIndex: () => 0,
            resolvePersistedSkinNameByTabIndex: _ => "DefaultGrid",
            resolveUpperTabStateNameByFixedIndex: _ => "DefaultGrid",
            enqueuePersistRequest: _ => true,
            skinRootPath: skinRootPath
        );
    }

    private static string CreateSkinRootWithSingleSkin(string skinName, int thumbWidth = 160)
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-orchestrator-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, skinName);
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, $"{skinName}.htm"),
            $$"""
            <html>
            <body>
              <div id="config">
                thum-width : {{thumbWidth}};
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        return rootPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // テスト後掃除の失敗は本体判定を優先する。
        }
    }
}

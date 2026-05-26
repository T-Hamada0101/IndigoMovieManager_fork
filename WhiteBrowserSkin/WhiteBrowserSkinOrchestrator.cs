using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IndigoMovieManager.Skin.Runtime;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// skin 一覧、現在スキン、永続化の流れを 1 か所へ寄せる司令塔。
    /// MainWindow 側は「今の状態を渡す」「選択結果を反映する」だけに薄く保つ。
    /// </summary>
    public sealed class WhiteBrowserSkinOrchestrator
    {
        private const string DefaultGridSkinName = "DefaultGrid";
        private const string SkinProfileLastUpperTabKey = "LastUpperTab";

        private readonly Func<string> getCurrentDbFullPath;
        private readonly Func<string> getCurrentSkinNameFromViewModel;
        private readonly Action<string> setCurrentSkinNameToViewModel;
        private readonly Func<string, string> normalizeTabStateName;
        private readonly Action<string> selectUpperTabDefaultViewBySkinName;
        private readonly Func<int> getCurrentUpperTabFixedIndex;
        private readonly Func<int, string> resolvePersistedSkinNameByTabIndex;
        private readonly Func<int, string> resolveUpperTabStateNameByFixedIndex;
        private readonly Func<WhiteBrowserSkinStatePersistRequest, bool> enqueuePersistRequest;
        private readonly Action<WhiteBrowserSkinStatePersistRequest> fallbackPersistRequest;
        private readonly Func<string, string, string, string> selectProfileValue;
        private readonly string skinRootPath;

        private IReadOnlyList<WhiteBrowserSkinDefinition> availableSkinDefinitions =
            Array.Empty<WhiteBrowserSkinDefinition>();
        private WhiteBrowserSkinDefinition activeSkinDefinition;
        private InitialTabResolution lastInitialTabResolution;

        public WhiteBrowserSkinOrchestrator(
            Func<string> getCurrentDbFullPath,
            Func<string> getCurrentSkinNameFromViewModel,
            Action<string> setCurrentSkinNameToViewModel,
            Func<string, string> normalizeTabStateName,
            Action<string> selectUpperTabDefaultViewBySkinName,
            Func<int> getCurrentUpperTabFixedIndex,
            Func<int, string> resolvePersistedSkinNameByTabIndex,
            Func<int, string> resolveUpperTabStateNameByFixedIndex,
            Func<WhiteBrowserSkinStatePersistRequest, bool> enqueuePersistRequest,
            Action<WhiteBrowserSkinStatePersistRequest> fallbackPersistRequest = null,
            string skinRootPath = "",
            Func<string, string, string, string> selectProfileValue = null
        )
        {
            this.getCurrentDbFullPath =
                getCurrentDbFullPath ?? throw new ArgumentNullException(nameof(getCurrentDbFullPath));
            this.getCurrentSkinNameFromViewModel =
                getCurrentSkinNameFromViewModel
                ?? throw new ArgumentNullException(nameof(getCurrentSkinNameFromViewModel));
            this.setCurrentSkinNameToViewModel =
                setCurrentSkinNameToViewModel
                ?? throw new ArgumentNullException(nameof(setCurrentSkinNameToViewModel));
            this.normalizeTabStateName =
                normalizeTabStateName ?? throw new ArgumentNullException(nameof(normalizeTabStateName));
            this.selectUpperTabDefaultViewBySkinName =
                selectUpperTabDefaultViewBySkinName
                ?? throw new ArgumentNullException(nameof(selectUpperTabDefaultViewBySkinName));
            this.getCurrentUpperTabFixedIndex =
                getCurrentUpperTabFixedIndex
                ?? throw new ArgumentNullException(nameof(getCurrentUpperTabFixedIndex));
            this.resolvePersistedSkinNameByTabIndex =
                resolvePersistedSkinNameByTabIndex
                ?? throw new ArgumentNullException(nameof(resolvePersistedSkinNameByTabIndex));
            this.resolveUpperTabStateNameByFixedIndex =
                resolveUpperTabStateNameByFixedIndex
                ?? throw new ArgumentNullException(nameof(resolveUpperTabStateNameByFixedIndex));
            this.enqueuePersistRequest =
                enqueuePersistRequest ?? throw new ArgumentNullException(nameof(enqueuePersistRequest));
            this.fallbackPersistRequest = fallbackPersistRequest;
            this.selectProfileValue = selectProfileValue ?? SelectProfileValue;
            this.skinRootPath = string.IsNullOrWhiteSpace(skinRootPath)
                ? WhiteBrowserSkinCatalogService.ResolveSkinRootPath(AppContext.BaseDirectory)
                : skinRootPath;
        }

        public IReadOnlyList<WhiteBrowserSkinDefinition> GetAvailableSkinDefinitions()
        {
            ReloadAvailableSkinDefinitions();
            return BuildAvailableSkinDefinitionSnapshot();
        }

        public async Task<IReadOnlyList<WhiteBrowserSkinDefinition>> GetAvailableSkinDefinitionsAsync()
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions =
                await Task.Run(() => WhiteBrowserSkinCatalogService.Load(skinRootPath));
            availableSkinDefinitions = loadedDefinitions;

            // 設定画面の一覧更新は catalog 走査だけを背景へ逃がし、
            // 現在 skin の missing 補完は既存 snapshot 生成の流れへ揃える。
            return BuildAvailableSkinDefinitionSnapshot();
        }

        public IReadOnlyList<WhiteBrowserSkinDefinition> GetCachedAvailableSkinDefinitions()
        {
            // ヘッダーの表示同期では catalog の署名確認を繰り返さず、
            // 初回でも built-in と現在名の missing 補完だけで軽い snapshot を返す。
            return BuildCachedAvailableSkinDefinitionSnapshot();
        }

        public string GetCurrentSkinName()
        {
            return NormalizeStoredSkinNameCore(
                getCurrentSkinNameFromViewModel(),
                availableSkinDefinitions
            );
        }

        public WhiteBrowserSkinDefinition GetCurrentSkinDefinition()
        {
            return ResolveCurrentDefinition();
        }

        public WhiteBrowserSkinDefinition RefreshCurrentSkinDefinition()
        {
            ReloadAvailableSkinDefinitions();

            // 明示 reload 用の定義再確認だけを行う。
            // タブ復元や保存は ApplySkinByName(...) 側の責務として残し、reload で表示状態を動かさない。
            string currentSkinName = GetCurrentSkinName();
            activeSkinDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    currentSkinName
                )
                ?? CreateMissingExternalDefinition(currentSkinName);
            return activeSkinDefinition;
        }

        public async Task<WhiteBrowserSkinDefinition> RefreshCurrentSkinDefinitionAsync()
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions =
                await Task.Run(() => WhiteBrowserSkinCatalogService.Load(skinRootPath));
            availableSkinDefinitions = loadedDefinitions;

            // 明示 reload の catalog 読み直しは背景で済ませ、UI 側は最新 snapshot の採用だけにする。
            string currentSkinName = GetCurrentSkinName();
            activeSkinDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    currentSkinName
                )
                ?? CreateMissingExternalDefinition(currentSkinName);
            return activeSkinDefinition;
        }

        public bool ApplySkinByName(string skinName, bool persistToCurrentDb = true)
        {
            WhiteBrowserSkinDefinition definition = ResolveDefinitionByName(skinName);
            if (definition == null)
            {
                return false;
            }

            string previousSkinName = getCurrentSkinNameFromViewModel()?.Trim() ?? "";
            bool isSameSkinApply = string.Equals(
                previousSkinName,
                definition.Name,
                StringComparison.OrdinalIgnoreCase
            );

            activeSkinDefinition = definition;
            setCurrentSkinNameToViewModel(definition.Name);

            string dbFullPath = getCurrentDbFullPath() ?? "";
            string targetTabStateName = ResolveInitialTabStateNameForSkin(
                definition,
                dbFullPath,
                isSameSkinApply
            );
            selectUpperTabDefaultViewBySkinName(targetTabStateName);

            if (persistToCurrentDb && !string.IsNullOrWhiteSpace(dbFullPath))
            {
                PersistCurrentSkinState(dbFullPath);
            }

            return true;
        }

        public string NormalizeStoredSkinName(string skinName)
        {
            return NormalizeStoredSkinNameCore(skinName, availableSkinDefinitions);
        }

        private string NormalizeStoredSkinNameCore(
            string skinName,
            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions
        )
        {
            string normalizedSkinName = skinName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSkinName))
            {
                return DefaultGridSkinName;
            }

            WhiteBrowserSkinDefinition exactDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    loadedDefinitions,
                    normalizedSkinName
                );
            if (exactDefinition != null)
            {
                return exactDefinition.Name;
            }

            if (loadedDefinitions == null || loadedDefinitions.Count < 1)
            {
                exactDefinition = ResolveDefinitionByName(normalizedSkinName);
            }

            return exactDefinition?.Name ?? normalizedSkinName;
        }

        public void PersistCurrentSkinState(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            int currentTabIndex = getCurrentUpperTabFixedIndex();
            string currentTabStateName = resolveUpperTabStateNameByFixedIndex(currentTabIndex);
            WhiteBrowserSkinDefinition currentDefinition = ResolveCurrentDefinition();

            if (currentDefinition != null && !currentDefinition.IsBuiltIn)
            {
                PersistRequestOrFallback(
                    WhiteBrowserSkinStatePersistRequest.CreateSystem(
                        dbFullPath,
                        "skin",
                        currentDefinition.Name,
                        DebugRuntimeLog.GetCurrentScopeText()
                    )
                );
                PersistRequestOrFallback(
                    WhiteBrowserSkinStatePersistRequest.CreateProfile(
                        dbFullPath,
                        currentDefinition.Name,
                        SkinProfileLastUpperTabKey,
                        currentTabStateName,
                        DebugRuntimeLog.GetCurrentScopeText()
                    )
                );
                return;
            }

            PersistRequestOrFallback(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(
                    dbFullPath,
                    "skin",
                    resolvePersistedSkinNameByTabIndex(currentTabIndex),
                    DebugRuntimeLog.GetCurrentScopeText()
                )
            );
        }

        private void PersistRequestOrFallback(WhiteBrowserSkinStatePersistRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (enqueuePersistRequest(request))
            {
                return;
            }

            // shutdown 後など queue が閉じた時だけ、最後の状態を落とさないため直書き fallback へ回す。
            fallbackPersistRequest?.Invoke(request);
        }

        private WhiteBrowserSkinDefinition ResolveCurrentDefinition()
        {
            string currentSkinName = GetCurrentSkinName();
            if (
                activeSkinDefinition != null
                && string.Equals(activeSkinDefinition.Name, currentSkinName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return activeSkinDefinition;
            }

            activeSkinDefinition =
                ResolveDefinitionByName(currentSkinName)
                ?? CreateMissingExternalDefinition(currentSkinName);
            return activeSkinDefinition;
        }

        private WhiteBrowserSkinDefinition ResolveDefinitionByName(string skinName)
        {
            WhiteBrowserSkinDefinition builtInDefinition =
                WhiteBrowserSkinCatalogService.TryResolveBuiltInByName(skinName);
            if (builtInDefinition != null)
            {
                // built-in skin は catalog 変化の影響を受けないため、
                // 単純 apply や reload 判定では外部 skin 再走査へ進めない。
                return builtInDefinition;
            }

            WhiteBrowserSkinDefinition cachedDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    skinName
                );
            if (cachedDefinition?.IsBuiltIn == true)
            {
                // built-in skin 定義は不変なので、一覧 snapshot から即解決して
                // Grid へ戻るだけの操作で catalog 署名確認へ戻らない。
                return cachedDefinition;
            }

            ReloadAvailableSkinDefinitions();
            return WhiteBrowserSkinCatalogService.TryResolveExactByName(
                availableSkinDefinitions,
                skinName
            );
        }

        private void ReloadAvailableSkinDefinitions()
        {
            availableSkinDefinitions = WhiteBrowserSkinCatalogService.Load(skinRootPath);
        }

        private IReadOnlyList<WhiteBrowserSkinDefinition> BuildAvailableSkinDefinitionSnapshot()
        {
            string currentSkinName = GetCurrentSkinName();
            WhiteBrowserSkinDefinition currentDefinition = activeSkinDefinition;
            WhiteBrowserSkinDefinition loadedCurrentDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    currentSkinName
                );
            if (
                currentDefinition == null
                || !string.Equals(currentDefinition.Name, currentSkinName, StringComparison.OrdinalIgnoreCase)
                || (currentDefinition.IsMissing && loadedCurrentDefinition != null)
            )
            {
                // 一覧 snapshot のために catalog を掘り直さず、今ロード済みの definitions だけで現在 skin を解決する。
                currentDefinition =
                    loadedCurrentDefinition ?? CreateMissingExternalDefinition(currentSkinName);
                activeSkinDefinition = currentDefinition;
            }

            if (
                currentDefinition == null
                || !currentDefinition.IsMissing
                || WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    currentDefinition.Name
                ) != null
            )
            {
                return availableSkinDefinitions;
            }

            List<WhiteBrowserSkinDefinition> snapshot = [.. availableSkinDefinitions, currentDefinition];
            return snapshot;
        }

        private IReadOnlyList<WhiteBrowserSkinDefinition> BuildCachedAvailableSkinDefinitionSnapshot()
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> snapshotBase =
                availableSkinDefinitions.Count > 0
                    ? availableSkinDefinitions
                    : WhiteBrowserSkinCatalogService.GetBuiltInDefinitions();
            string currentSkinName = NormalizeStoredSkinNameForCachedSnapshot(
                getCurrentSkinNameFromViewModel(),
                snapshotBase
            );
            WhiteBrowserSkinDefinition currentDefinition = activeSkinDefinition;
            WhiteBrowserSkinDefinition cachedCurrentDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(snapshotBase, currentSkinName);
            if (
                currentDefinition == null
                || !string.Equals(currentDefinition.Name, currentSkinName, StringComparison.OrdinalIgnoreCase)
                || (currentDefinition.IsMissing && cachedCurrentDefinition != null)
            )
            {
                // cached API では外部 skin の鮮度確認を明示 reload 側へ任せ、
                // 今ある snapshot と built-in 共有定義だけで現在表示名を守る。
                currentDefinition = cachedCurrentDefinition ?? CreateMissingExternalDefinition(currentSkinName);
                activeSkinDefinition = currentDefinition;
            }

            if (
                currentDefinition == null
                || !currentDefinition.IsMissing
                || WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    snapshotBase,
                    currentDefinition.Name
                ) != null
            )
            {
                return snapshotBase;
            }

            List<WhiteBrowserSkinDefinition> snapshot = [.. snapshotBase, currentDefinition];
            return snapshot;
        }

        private static string NormalizeStoredSkinNameForCachedSnapshot(
            string skinName,
            IReadOnlyList<WhiteBrowserSkinDefinition> loadedDefinitions
        )
        {
            string normalizedSkinName = skinName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSkinName))
            {
                return DefaultGridSkinName;
            }

            WhiteBrowserSkinDefinition exactDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    loadedDefinitions,
                    normalizedSkinName
                );
            return exactDefinition?.Name ?? normalizedSkinName;
        }

        private string ResolveInitialTabStateNameForSkin(
            WhiteBrowserSkinDefinition definition,
            string dbFullPath,
            bool allowSameSkinResolvedCache
        )
        {
            if (definition == null)
            {
                return DefaultGridSkinName;
            }

            if (definition.IsBuiltIn)
            {
                return normalizeTabStateName(definition.Name);
            }

            if (!string.IsNullOrWhiteSpace(dbFullPath))
            {
                if (
                    WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
                        dbFullPath,
                        definition.Name,
                        SkinProfileLastUpperTabKey,
                        out string cachedTabState
                    )
                )
                {
                    string cachedResolvedTabStateName = !string.IsNullOrWhiteSpace(cachedTabState)
                        ? normalizeTabStateName(cachedTabState)
                        : ResolvePreferredTabStateName(definition);
                    RememberInitialTabResolution(
                        dbFullPath,
                        definition.Name,
                        cachedResolvedTabStateName
                    );
                    return cachedResolvedTabStateName;
                }

                if (
                    allowSameSkinResolvedCache
                    && TryGetRememberedInitialTabResolution(
                        dbFullPath,
                        definition.Name,
                        out string rememberedTabStateName
                    )
                )
                {
                    return rememberedTabStateName;
                }

                string savedTabState = selectProfileValue(
                    dbFullPath,
                    definition.Name,
                    SkinProfileLastUpperTabKey
                );
                if (!string.IsNullOrWhiteSpace(savedTabState))
                {
                    string savedResolvedTabStateName = normalizeTabStateName(savedTabState);
                    WhiteBrowserSkinProfileValueCache.RecordPersisted(
                        dbFullPath,
                        definition.Name,
                        SkinProfileLastUpperTabKey,
                        savedTabState
                    );
                    RememberInitialTabResolution(
                        dbFullPath,
                        definition.Name,
                        savedResolvedTabStateName
                    );
                    return savedResolvedTabStateName;
                }
            }

            string preferredTabStateName = ResolvePreferredTabStateName(definition);
            RememberInitialTabResolution(dbFullPath, definition.Name, preferredTabStateName);
            return preferredTabStateName;
        }

        private string ResolvePreferredTabStateName(WhiteBrowserSkinDefinition definition)
        {
            return normalizeTabStateName(definition?.PreferredTabStateName);
        }

        private void RememberInitialTabResolution(
            string dbFullPath,
            string skinName,
            string tabStateName
        )
        {
            string dbIdentity = WhiteBrowserSkinDbIdentity.NormalizeMainDbPath(dbFullPath);
            string normalizedSkinName = skinName?.Trim() ?? "";
            string normalizedTabStateName = normalizeTabStateName(tabStateName);
            if (
                string.IsNullOrWhiteSpace(dbIdentity)
                || string.IsNullOrWhiteSpace(normalizedSkinName)
                || string.IsNullOrWhiteSpace(normalizedTabStateName)
            )
            {
                lastInitialTabResolution = default;
                return;
            }

            // 同じ skin を連続適用した時だけ使う一時結果として持ち、DB 読み取りを重ねない。
            lastInitialTabResolution = new InitialTabResolution(
                dbIdentity,
                normalizedSkinName,
                normalizedTabStateName
            );
        }

        private bool TryGetRememberedInitialTabResolution(
            string dbFullPath,
            string skinName,
            out string tabStateName
        )
        {
            tabStateName = "";
            string dbIdentity = WhiteBrowserSkinDbIdentity.NormalizeMainDbPath(dbFullPath);
            string normalizedSkinName = skinName?.Trim() ?? "";
            if (
                string.IsNullOrWhiteSpace(dbIdentity)
                || string.IsNullOrWhiteSpace(normalizedSkinName)
                || string.IsNullOrWhiteSpace(lastInitialTabResolution.DbIdentity)
            )
            {
                return false;
            }

            if (
                !string.Equals(
                    lastInitialTabResolution.DbIdentity,
                    dbIdentity,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    lastInitialTabResolution.SkinName,
                    normalizedSkinName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            tabStateName = lastInitialTabResolution.TabStateName;
            return !string.IsNullOrWhiteSpace(tabStateName);
        }

        private readonly record struct InitialTabResolution(
            string DbIdentity,
            string SkinName,
            string TabStateName
        );

        private WhiteBrowserSkinDefinition CreateMissingExternalDefinition(string skinName)
        {
            if (string.IsNullOrWhiteSpace(skinName))
            {
                return WhiteBrowserSkinCatalogService.ResolveByName(availableSkinDefinitions, DefaultGridSkinName);
            }

            return new WhiteBrowserSkinDefinition(
                skinName.Trim(),
                "",
                "",
                WhiteBrowserSkinConfig.Empty,
                DefaultGridSkinName,
                isBuiltIn: false,
                isMissing: true
            );
        }

        internal string ResolvePersistedSkinNameForCurrentState()
        {
            int currentTabIndex = getCurrentUpperTabFixedIndex();
            return resolvePersistedSkinNameByTabIndex(currentTabIndex);
        }
    }
}

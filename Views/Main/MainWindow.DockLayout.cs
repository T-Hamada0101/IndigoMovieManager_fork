using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// 前回保存した AvalonDock レイアウト（layout.xml）の復元を試みる。
        /// 現在の配置を default(layout.default.xml) にも保存し、
        /// 通常レイアウトが無い・壊れている時は default から復元する。
        /// </summary>
        private void TryRestoreDockLayout()
        {
            _ = RunRestoreDockLayoutAsync();
        }

        private async Task RunRestoreDockLayoutAsync()
        {
            try
            {
                await Task.Run(EnsureDockLayoutStorageReady).ConfigureAwait(false);

                if (
                    await TryRestoreDockLayoutFromFile(
                        DockLayoutStorage.LayoutFilePath,
                        backupInvalidLayout: true
                    )
                )
                {
                    return;
                }

                _ = await TryRestoreDockLayoutFromFile(
                    DockLayoutStorage.DefaultLayoutFilePath,
                    backupInvalidLayout: false
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore task failed. reason={ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        private static void EnsureDockLayoutStorageReady()
        {
            try
            {
                IReadOnlyList<string> migratedFiles = DockLayoutStorage.MigrateLegacyFiles(
                    AppContext.BaseDirectory,
                    AppLocalDataPaths.LayoutsPath
                );
                if (migratedFiles.Count > 0)
                {
                    DebugRuntimeLog.Write(
                        "layout",
                        $"legacy layout migrated. files='{string.Join(",", migratedFiles)}'"
                    );
                }
            }
            catch (Exception ex)
            {
                // 移行に失敗しても起動は止めず、新しい保存先で既定レイアウトを使う。
                DebugRuntimeLog.Write(
                    "layout",
                    $"legacy layout migration failed. reason={ex.GetType().Name}: {ex.Message}"
                );
                Directory.CreateDirectory(AppLocalDataPaths.LayoutsPath);
            }
        }

        /// <summary>
        /// 指定ファイルのレイアウト復元を試みる。
        /// 通常 layout.xml は互換外時に退避し、default 側は壊れていても静かに無視する。
        /// </summary>
        private async Task<bool> TryRestoreDockLayoutFromFile(
            string layoutFilePath,
            bool backupInvalidLayout
        )
        {
            bool shouldShowThumbnailErrorBottomTab = ShouldShowThumbnailErrorBottomTab;
            bool shouldShowDebugTab = ShouldShowDebugTab;
            DockLayoutRestoreFileLoadResult loadResult = await Task.Run(
                    () =>
                        LoadDockLayoutRestoreText(
                            layoutFilePath,
                            backupInvalidLayout,
                            shouldShowThumbnailErrorBottomTab,
                            shouldShowDebugTab
                        )
                )
                .ConfigureAwait(false);

            if (loadResult.LayoutText == null)
            {
                return false;
            }

            try
            {
                return await Dispatcher.InvokeAsync(
                        () => TryDeserializeDockLayoutText(loadResult),
                        DispatcherPriority.ContextIdle
                    )
                    .Task;
            }
            catch (TaskCanceledException ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore dispatch canceled. file='{layoutFilePath}' reason={ex.Message}"
                );
                return false;
            }
            catch (InvalidOperationException ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore dispatch failed. file='{layoutFilePath}' reason={ex.Message}"
                );
                return false;
            }
        }

        private DockLayoutRestoreFileLoadResult LoadDockLayoutRestoreText(
            string layoutFilePath,
            bool backupInvalidLayout,
            bool shouldShowThumbnailErrorBottomTab,
            bool shouldShowDebugTab
        )
        {
            if (!Path.Exists(layoutFilePath))
            {
                return DockLayoutRestoreFileLoadResult.Missing(layoutFilePath, backupInvalidLayout);
            }

            try
            {
                // ファイルI/Oと互換テキスト検証は背景側で済ませ、起動直列のUI待ちを増やさない。
                string layoutText = File.ReadAllText(layoutFilePath);
                string invalidReason = DockLayoutRestorePolicy.FindMissingRequiredDockLayoutReason(
                    layoutText,
                    shouldShowThumbnailErrorBottomTab,
                    shouldShowDebugTab
                );

                if (!string.IsNullOrEmpty(invalidReason))
                {
                    if (backupInvalidLayout)
                    {
                        BackupLegacyDockLayout(layoutFilePath, invalidReason);
                    }

                    return DockLayoutRestoreFileLoadResult.Invalid(
                        layoutFilePath,
                        backupInvalidLayout
                    );
                }

                return DockLayoutRestoreFileLoadResult.Ready(
                    layoutFilePath,
                    backupInvalidLayout,
                    layoutText
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore read failed. file='{layoutFilePath}' reason={ex.Message}"
                );
                if (backupInvalidLayout)
                {
                    BackupLegacyDockLayout(layoutFilePath, "deserialize-failed");
                }

                return DockLayoutRestoreFileLoadResult.Invalid(
                    layoutFilePath,
                    backupInvalidLayout
                );
            }
        }

        private bool TryDeserializeDockLayoutText(DockLayoutRestoreFileLoadResult loadResult)
        {
            if (loadResult.LayoutText == null)
            {
                return false;
            }

            try
            {
                XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
                // 背景側で検証済みの文字列だけを渡し、UI側ではファイルを掘り直さない。
                using var reader = new StringReader(loadResult.LayoutText);
                layoutSerializer.Deserialize(reader);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "layout",
                    $"layout restore failed. file='{loadResult.LayoutFilePath}' reason={ex.Message}"
                );
                if (loadResult.BackupInvalidLayout)
                {
                    _ = Task.Run(
                        () => BackupLegacyDockLayout(loadResult.LayoutFilePath, "deserialize-failed")
                    );
                }

                return false;
            }
        }

        /// <summary>
        /// 互換性のない旧レイアウトファイルを日時付きで退避し、次回は既定レイアウトで起動させる。
        /// </summary>
        private string ValidateDockLayoutText(string layoutText)
        {
            return DockLayoutRestorePolicy.FindMissingRequiredDockLayoutReason(
                layoutText,
                ShouldShowThumbnailErrorBottomTab,
                ShouldShowDebugTab
            );
        }

        internal static string FindMissingRequiredDockLayoutReason(
            string layoutText,
            bool shouldShowThumbnailErrorBottomTab,
            bool shouldShowDebugTab
        )
        {
            return DockLayoutRestorePolicy.FindMissingRequiredDockLayoutReason(
                layoutText,
                shouldShowThumbnailErrorBottomTab,
                shouldShowDebugTab
            );
        }

        private sealed record DockLayoutRestoreFileLoadResult(
            string LayoutFilePath,
            bool BackupInvalidLayout,
            string LayoutText
        )
        {
            public static DockLayoutRestoreFileLoadResult Missing(
                string layoutFilePath,
                bool backupInvalidLayout
            )
            {
                return new DockLayoutRestoreFileLoadResult(
                    layoutFilePath,
                    backupInvalidLayout,
                    null
                );
            }

            public static DockLayoutRestoreFileLoadResult Invalid(
                string layoutFilePath,
                bool backupInvalidLayout
            )
            {
                return new DockLayoutRestoreFileLoadResult(
                    layoutFilePath,
                    backupInvalidLayout,
                    null
                );
            }

            public static DockLayoutRestoreFileLoadResult Ready(
                string layoutFilePath,
                bool backupInvalidLayout,
                string layoutText
            )
            {
                return new DockLayoutRestoreFileLoadResult(
                    layoutFilePath,
                    backupInvalidLayout,
                    layoutText
                );
            }
        }

        // 下部の常設タブは、古いレイアウト復元や誤操作で木から外れても保存前に必ず戻す。
        private void EnsureRequiredBottomTabsPresent()
        {
            LayoutAnchorablePane targetPane = ResolveActiveBottomTabPane();
            if (targetPane == null)
            {
                return;
            }

            LayoutAnchorable selectedTabBeforeRepair = GetSelectedBottomTabOrNull(targetPane);

            EnsureRequiredBottomTabPresent(
                targetPane,
                TagEditorBottomTab,
                TagEditorBottomTabContentId,
                canHide: false
            );
            EnsureRequiredBottomTabPresent(
                targetPane,
                FileOrganizerBottomTab,
                FileOrganizerBottomTabContentId,
                canHide: false
            );
            EnsureRequiredBottomTabPresent(
                targetPane,
                exDetail,
                ExtensionBottomTabContentId,
                canHide: false
            );
            EnsureRequiredBottomTabPresent(
                targetPane,
                exBookMark,
                BookmarkBottomTabContentId,
                canHide: false
            );
            EnsureRequiredBottomTabPresent(
                targetPane,
                TagBar,
                SavedSearchBottomTabContentId,
                canHide: false
            );
            EnsureRequiredBottomTabPresent(
                targetPane,
                ThumbnailProgressTab,
                ThumbnailProgressContentId,
                canHide: false
            );

            if (ShouldShowThumbnailErrorBottomTab)
            {
                EnsureRequiredBottomTabPresent(
                    targetPane,
                    ThumbnailErrorBottomTab,
                    ThumbnailErrorBottomTabContentId,
                    canHide: false
                );
            }

            if (ShouldShowDebugTab)
            {
                // Debug 系も自動非表示ペインへ流れたままにならないよう、下部ペインへ戻す。
                EnsureRequiredBottomTabPresent(
                    targetPane,
                    DebugTab,
                    DebugToolContentId,
                    canHide: true
                );
                EnsureRequiredBottomTabPresent(
                    targetPane,
                    LogTab,
                    LogToolContentId,
                    canHide: true
                );
            }

            if (
                selectedTabBeforeRepair != null
                && ReferenceEquals(selectedTabBeforeRepair.Parent, targetPane)
                && !selectedTabBeforeRepair.IsHidden
            )
            {
                selectedTabBeforeRepair.IsSelected = true;
            }
        }

        private LayoutAnchorablePane ResolveActiveBottomTabPane()
        {
            if (uxDockingManager?.Layout?.RootPanel == null)
            {
                return uxAnchorablePane2;
            }

            LayoutAnchorablePane paneWithKnownContent = FindDockedPaneWithKnownBottomTab(
                uxDockingManager.Layout.RootPanel
            );
            if (paneWithKnownContent != null)
            {
                return paneWithKnownContent;
            }

            return FindFirstDockedAnchorablePane(uxDockingManager.Layout.RootPanel) ?? uxAnchorablePane2;
        }

        private LayoutAnchorablePane FindDockedPaneWithKnownBottomTab(ILayoutContainer container)
        {
            if (container == null)
            {
                return null;
            }

            foreach (ILayoutElement child in container.Children)
            {
                if (child is LayoutAnchorablePane pane)
                {
                    foreach (ILayoutElement paneChild in pane.Children)
                    {
                        if (
                            paneChild is LayoutAnchorable anchorable
                            && IsKnownBottomTabContentId(anchorable.ContentId)
                        )
                        {
                            return pane;
                        }
                    }
                }

                if (child is ILayoutContainer childContainer)
                {
                    LayoutAnchorablePane found = FindDockedPaneWithKnownBottomTab(childContainer);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private LayoutAnchorablePane FindFirstDockedAnchorablePane(ILayoutContainer container)
        {
            if (container == null)
            {
                return null;
            }

            foreach (ILayoutElement child in container.Children)
            {
                if (child is LayoutAnchorablePane pane)
                {
                    return pane;
                }

                if (child is ILayoutContainer childContainer)
                {
                    LayoutAnchorablePane found = FindFirstDockedAnchorablePane(childContainer);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static bool IsKnownBottomTabContentId(string contentId)
        {
            return contentId is TagEditorBottomTabContentId
                or FileOrganizerBottomTabContentId
                or ExtensionBottomTabContentId
                or BookmarkBottomTabContentId
                or SavedSearchBottomTabContentId
                or ThumbnailProgressContentId
                or ThumbnailErrorBottomTabContentId
                or DebugToolContentId
                or LogToolContentId;
        }

        private LayoutAnchorable GetSelectedBottomTabOrNull(LayoutAnchorablePane targetPane)
        {
            if (targetPane == null)
            {
                return null;
            }

            foreach (ILayoutElement child in targetPane.Children)
            {
                if (child is LayoutAnchorable tab && tab.IsSelected)
                {
                    return tab;
                }
            }

            return null;
        }

        private LayoutAnchorable FindLayoutAnchorableByContentId(
            ILayoutContainer container,
            string contentId
        )
        {
            if (container == null || string.IsNullOrWhiteSpace(contentId))
            {
                return null;
            }

            foreach (ILayoutElement child in container.Children)
            {
                if (
                    child is LayoutAnchorable anchorable
                    && string.Equals(
                        anchorable.ContentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return anchorable;
                }

                if (child is ILayoutContainer childContainer)
                {
                    LayoutAnchorable found = FindLayoutAnchorableByContentId(
                        childContainer,
                        contentId
                    );
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private void EnsureRequiredBottomTabPresent(
            LayoutAnchorablePane targetPane,
            LayoutAnchorable fallbackTab,
            string contentId,
            bool canHide
        )
        {
            if (targetPane == null)
            {
                return;
            }

            LayoutAnchorable tab =
                FindLayoutAnchorableByContentId(uxDockingManager?.Layout, contentId) ?? fallbackTab;
            if (tab == null)
            {
                return;
            }

            // 保存済みレイアウトで別ペインや自動非表示へ流れても、正規の下部ペインへ戻す。
            tab.CanClose = false;
            tab.CanHide = canHide;
            tab.CanDockAsTabbedDocument = false;

            if (tab.Parent is ILayoutContainer currentParent && !ReferenceEquals(currentParent, targetPane))
            {
                currentParent.RemoveChild(tab);
            }

            if (!targetPane.Children.Contains(tab))
            {
                targetPane.Children.Add(tab);
            }

            if (tab.IsHidden)
            {
                tab.Show();
            }
        }

        /// <summary>
        /// 現在のタブ配置を通常保存用と default 保存用の両方へ書き出す。
        /// これにより、ユーザーが整えた配置を次回以降の既定値としても再利用できる。
        /// </summary>
        private void SaveDockLayoutToFile(string layoutFilePath)
        {
            EnsureRequiredBottomTabsPresent();
            string directoryPath = Path.GetDirectoryName(layoutFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            XmlLayoutSerializer layoutSerializer = new(uxDockingManager);
            using var writer = new StreamWriter(layoutFilePath);
            layoutSerializer.Serialize(writer);
        }

        private static void BackupLegacyDockLayout(string layoutFilePath, string reason)
        {
            try
            {
                string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string directoryPath = Path.GetDirectoryName(layoutFilePath);
                string fileName = Path.GetFileNameWithoutExtension(layoutFilePath);
                string extension = Path.GetExtension(layoutFilePath);
                string backupFileName = $"{fileName}.{reason}.{suffix}{extension}";
                string backupPath = string.IsNullOrWhiteSpace(directoryPath)
                    ? backupFileName
                    : Path.Combine(directoryPath, backupFileName);
                File.Move(layoutFilePath, backupPath, true);
            }
            catch
            {
                try
                {
                    File.Delete(layoutFilePath);
                }
                catch
                {
                    // 退避失敗時は何もしない。次回起動時も復元は試みない前提で進める。
                }
            }
        }

        /// <summary>
        /// マルチモニタ切断や解像度変更で画面外に飛んだウィンドウ位置を安全に補正して復元する。
        /// </summary>
        private void RestoreWindowBoundsSafely()
        {
            const double minWindowWidth = 640;
            const double minWindowHeight = 480;

            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            double targetWidth = Math.Max(minWindowWidth, Properties.Settings.Default.MainSize.Width);
            double targetHeight = Math.Max(minWindowHeight, Properties.Settings.Default.MainSize.Height);
            targetWidth = Math.Min(targetWidth, Math.Max(minWindowWidth, virtualRight - virtualLeft));
            targetHeight = Math.Min(targetHeight, Math.Max(minWindowHeight, virtualBottom - virtualTop));

            double targetLeft = Properties.Settings.Default.MainLocation.X;
            double targetTop = Properties.Settings.Default.MainLocation.Y;

            bool outOfScreen =
                targetLeft + targetWidth < virtualLeft
                || targetLeft > virtualRight
                || targetTop + targetHeight < virtualTop
                || targetTop > virtualBottom;

            if (outOfScreen)
            {
                targetLeft = virtualLeft + Math.Max(0, (virtualRight - virtualLeft - targetWidth) / 2);
                targetTop = virtualTop + Math.Max(0, (virtualBottom - virtualTop - targetHeight) / 2);
            }
            else
            {
                targetLeft = Math.Min(Math.Max(targetLeft, virtualLeft), virtualRight - targetWidth);
                targetTop = Math.Min(Math.Max(targetTop, virtualTop), virtualBottom - targetHeight);
            }

            Left = targetLeft;
            Top = targetTop;
            Width = targetWidth;
            Height = targetHeight;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private bool _isExternalSkinMinimalSkinSelectorSyncing;

        // 外部 skin でもヘッダーは差し替えず、共通ヘッダー上の skin ドロップダウンだけ同期する。
        private void ApplyExternalSkinMinimalChromeVisibility(
            bool hostReady,
            IndigoMovieManager.Skin.WhiteBrowserSkinDefinition definition
        )
        {
            bool externalSkinVisible = hostReady && definition?.RequiresWebView2 == true;

            if (MainHeaderStandardChromePanel != null)
            {
                MainHeaderStandardChromePanel.Visibility = Visibility.Visible;
            }

            if (ExternalSkinMinimalChromePanel != null)
            {
                ExternalSkinMinimalChromePanel.Visibility = Visibility.Collapsed;
            }

            if (ExternalSkinMinimalSkinNameText != null)
            {
                string displaySkinName = externalSkinVisible
                    ? ResolveRequestedSkinName(definition)
                    : GetCurrentSkinName();
                ExternalSkinMinimalSkinNameText.Text = displaySkinName;
                ExternalSkinMinimalSkinNameText.ToolTip = displaySkinName;
                SyncExternalSkinMinimalSkinSelector(true, displaySkinName);
            }
            else
            {
                SyncExternalSkinMinimalSkinSelector(true, GetCurrentSkinName());
            }
        }

        // 共通ヘッダーでは built-in と外部 skin を同じドロップダウンから切り替える。
        private void SyncExternalSkinMinimalSkinSelector(
            bool minimalVisible,
            string displaySkinName
        )
        {
            if (ExternalSkinMinimalSkinSelector == null)
            {
                return;
            }

            List<WhiteBrowserSkinDefinition> selectableDefinitions = [];
            if (minimalVisible)
            {
                foreach (WhiteBrowserSkinDefinition candidate in GetCachedAvailableSkinDefinitions())
                {
                    if (candidate != null)
                    {
                        selectableDefinitions.Add(candidate);
                    }
                }
            }

            _isExternalSkinMinimalSkinSelectorSyncing = true;
            try
            {
                ExternalSkinMinimalSkinSelector.ItemsSource = selectableDefinitions;
                ExternalSkinMinimalSkinSelector.IsEnabled =
                    minimalVisible && selectableDefinitions.Count > 0;
                ExternalSkinMinimalSkinSelector.SelectedValue = minimalVisible ? displaySkinName : null;
                ExternalSkinMinimalSkinSelector.ToolTip = minimalVisible ? displaySkinName : null;
            }
            finally
            {
                _isExternalSkinMinimalSkinSelectorSyncing = false;
            }
        }

        private void ExternalSkinMinimalSkinSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_isExternalSkinMinimalSkinSelectorSyncing || sender is not ComboBox selector)
            {
                return;
            }

            if (selector.SelectedValue is not string skinName || string.IsNullOrWhiteSpace(skinName))
            {
                return;
            }

            // 同じ skin を再選択した時は無駄な host refresh を積まず、表示名だけ同期する。
            if (string.Equals(GetCurrentSkinName(), skinName, StringComparison.OrdinalIgnoreCase))
            {
                selector.ToolTip = skinName;
                return;
            }

            if (ApplySkinByName(skinName, persistToCurrentDb: true))
            {
                selector.ToolTip = skinName;
                return;
            }

            WhiteBrowserSkinDefinition currentDefinition = GetCurrentExternalSkinDefinition();
            SyncExternalSkinMinimalSkinSelector(
                currentDefinition != null,
                ResolveRequestedSkinName(currentDefinition)
            );
        }

        private void ApplyExternalSkinFallbackNotice(
            string noticeText,
            string toolTipText,
            bool showRuntimeDownloadAction
        )
        {
            bool hasNotice = !string.IsNullOrWhiteSpace(noticeText);

            if (ExternalSkinFallbackNoticeBorder != null)
            {
                ExternalSkinFallbackNoticeBorder.Visibility = hasNotice
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                ExternalSkinFallbackNoticeBorder.ToolTip = hasNotice ? toolTipText : null;
            }

            if (ExternalSkinFallbackNoticeText != null)
            {
                ExternalSkinFallbackNoticeText.Text = hasNotice ? noticeText : "";
                ExternalSkinFallbackNoticeText.ToolTip = hasNotice ? toolTipText : null;
            }

            if (ExternalSkinFallbackOpenRuntimeDownloadButton != null)
            {
                ExternalSkinFallbackOpenRuntimeDownloadButton.Visibility =
                    hasNotice && showRuntimeDownloadAction
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
        }

        private static string BuildExternalSkinFallbackNoticeText(
            WhiteBrowserSkinDefinition definition,
            WhiteBrowserSkinHostOperationResult operationResult
        )
        {
            string skinName = operationResult?.RequestedSkinName ?? definition?.Name ?? "";
            if (operationResult == null)
            {
                return "";
            }

            if (!operationResult.RuntimeAvailable)
            {
                return $"外部スキン「{skinName}」は WebView2 Runtime が見つからないため表示できません。標準表示で継続できます。";
            }

            if (
                string.Equals(
                    operationResult.ErrorType,
                    "SkinHtmlMissing",
                    StringComparison.Ordinal
                )
            )
            {
                return $"外部スキン「{skinName}」の HTML が見つからないため表示できません。標準表示へ戻しています。";
            }

            return $"外部スキン「{skinName}」の初期化に失敗したため標準表示へ戻しています。";
        }

        private static string BuildExternalSkinFallbackNoticeToolTip(
            WhiteBrowserSkinDefinition definition,
            WhiteBrowserSkinHostOperationResult operationResult,
            string reason
        )
        {
            if (operationResult == null)
            {
                return "";
            }

            string skinName = operationResult.RequestedSkinName ?? definition?.Name ?? "";
            string logPath = ResolveExternalSkinFallbackLogPath();
            string runtimeDownloadUrl = ResolveExternalSkinRuntimeDownloadUrl();
            string nextAction = !operationResult.RuntimeAvailable
                ? "next: WebView2 Runtime 導入後に再読込、またはスキン再選択 / 再起動で再試行してください。"
                : string.Equals(operationResult.ErrorType, "SkinHtmlMissing", StringComparison.Ordinal)
                    ? "next: skin フォルダと HTML 配置を確認後に再読込してください。"
                    : "next: debug-runtime.log を確認し、再読込またはスキン再選択で再試行してください。";
            List<string> lines =
                new()
                {
                    $"skin: {skinName}",
                    $"errorType: {operationResult.ErrorType}",
                    $"error: {operationResult.ErrorMessage}",
                    $"reason: {reason ?? ""}",
                    "fallback: 標準 Grid / List 系表示へ戻しています。",
                    nextAction,
                };
            if (!operationResult.RuntimeAvailable)
            {
                lines.Add($"download: {runtimeDownloadUrl}");
            }

            lines.Add($"log: {logPath}");
            return string.Join(Environment.NewLine, lines);
        }

        private async void ExternalSkinMinimalReloadButton_Click(object sender, RoutedEventArgs e)
        {
            // blank 遷移完了を待ってから積み直し、clear と再 navigate の競合を避ける。
            await ClearExternalSkinHostBeforeRefreshAsync("minimal-chrome-reload");
            _ = QueueExternalSkinHostRefresh("minimal-chrome-reload");
        }

        private async void ExternalSkinFallbackRetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentExternalSkinDefinition() == null)
            {
                return;
            }

            await ClearExternalSkinHostBeforeRefreshAsync("fallback-notice-retry");
            _ = QueueExternalSkinHostRefresh("fallback-notice-retry");
        }

        private async Task ClearExternalSkinHostBeforeRefreshAsync(string reason)
        {
            if (_externalSkinHostControl == null)
            {
                return;
            }

            try
            {
                await _externalSkinHostControl.ClearAsync();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"host clear before refresh failed: err='{ex.GetType().Name}: {ex.Message}' reason={reason}"
                );
            }
        }

        private async void ExternalSkinFallbackOpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            string logPath = ResolveExternalSkinFallbackLogPath();

            try
            {
                if (ExternalSkinFallbackOpenLogActionForTesting != null)
                {
                    ExternalSkinFallbackOpenLogActionForTesting(logPath);
                    return;
                }

                ExternalSkinFallbackLogExplorerTarget explorerTarget =
                    await ResolveExternalSkinFallbackLogExplorerTargetAsync(logPath);
                if (explorerTarget.HasTarget)
                {
                    Process.Start("explorer.exe", explorerTarget.Arguments);
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"fallback log open failed: err='{ex.GetType().Name}: {ex.Message}' path='{logPath}'"
                );
            }
        }

        private static Task<ExternalSkinFallbackLogExplorerTarget> ResolveExternalSkinFallbackLogExplorerTargetAsync(
            string logPath
        )
        {
            return Task.Run(
                () =>
                {
                    // Explorer起動はUI側に残し、存在確認だけを背景へ逃がす。
                    if (File.Exists(logPath))
                    {
                        return new ExternalSkinFallbackLogExplorerTarget($"/select,{logPath}", true);
                    }

                    string targetDirectory = Path.GetDirectoryName(logPath) ?? "";
                    if (Directory.Exists(targetDirectory))
                    {
                        return new ExternalSkinFallbackLogExplorerTarget(targetDirectory, true);
                    }

                    return new ExternalSkinFallbackLogExplorerTarget("", false);
                }
            );
        }

        private readonly record struct ExternalSkinFallbackLogExplorerTarget(
            string Arguments,
            bool HasTarget
        );

        private void ExternalSkinFallbackOpenRuntimeDownloadButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            string runtimeDownloadUrl = ResolveExternalSkinRuntimeDownloadUrl();

            try
            {
                if (ExternalSkinFallbackOpenRuntimeDownloadActionForTesting != null)
                {
                    ExternalSkinFallbackOpenRuntimeDownloadActionForTesting(runtimeDownloadUrl);
                    return;
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = runtimeDownloadUrl,
                        UseShellExecute = true,
                    }
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"fallback runtime download open failed: err='{ex.GetType().Name}: {ex.Message}' url='{runtimeDownloadUrl}'"
                );
            }
        }

        private void ExternalSkinBackToGridButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ApplySkinByName("DefaultGrid"))
            {
                if (MainVM?.DbInfo != null)
                {
                    MainVM.DbInfo.Skin = "DefaultGrid";
                }

                SelectUpperTabGridAsDefaultView();
            }
        }

        private void ExternalSkinMinimalSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MenuToggleButton.IsChecked = false;
            CommonSettingsWindow commonSettingsWindow = new()
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            commonSettingsWindow.ShowDialog();
            ApplyThumbnailGpuDecodeSetting();
        }
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.Converter;
using IndigoMovieManager.DB;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Watcher;
using Microsoft.Win32;

namespace IndigoMovieManager
{
    /// <summary>
    /// 共通設定画面の View 層。アプリ全体に影響する設定（プレイヤー・テーマ・Everythingモード・
    /// 一覧画像キャッシュ等）の表示と永続化を管理する。
    ///
    /// 【全体の流れでの位置づけ】
    ///   メニュー → ★ここ★ CommonSettingsWindow
    ///     → 閉じる時（OnClosing）で Properties.Settings へ保存
    /// </summary>
    public partial class CommonSettingsWindow : Window
    {
        private bool _isUpperTabImageCacheMaxEntriesSyncing;
        private bool _isSkinSelectorInitializing;
        private bool _isThemeSelectorInitializing;
        private bool _isThumbnailStorageModeInitializing;
        private int _skinSelectorRefreshRevision;
        private int _originalThumbnailStorageMode;
        private string _originalSharedThumbnailRoot = "";
        private bool _isSharedThumbnailMigrationRunning;
        private bool _isSharedThumbnailCleanupRunning;
        private string _completedMigrationSourceRoot = "";
        private string _completedMigrationTargetRoot = "";
        private string _completedMigrationSharedRoot = "";
        private bool _closeAccepted;
        private string _currentDbSettingsPath = "";
        private string _currentDbOriginalThumbFolder = "";
        private string _currentDbOriginalBookmarkFolder = "";
        private string _currentDbOriginalKeepHistory = "";
        private string _currentDbOriginalPlayerPrg = "";
        private string _currentDbOriginalPlayerParam = "";

        // 共通設定画面の初期化。
        // 閉じるイベントで設定保存するため、ここでイベントを接続する。
        public CommonSettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            Loaded += CommonSettingsWindow_Loaded;
            Closing += OnClosing;
            Closed += CommonSettingsWindow_Closed;
            Activated += async (_, _) => await RefreshSkinSelectorAsync();
            sliderUpperTabImageCacheMaxEntries.ValueChanged +=
                SliderUpperTabImageCacheMaxEntries_ValueChanged;
            Properties.Settings.Default.PropertyChanged += SettingsDefault_PropertyChanged;
            DefaultPlayerParam.ItemsSource = new string[]
            {
                "/start <ms>",
                "<file> player -seek pos=<ms>",
            };
            string normalizedProvider = FileIndexProviderFactory.NormalizeProviderKey(
                Properties.Settings.Default.FileIndexProvider
            );
            FileIndexProviderSelector.SelectedValue = normalizedProvider;
            InitializeThumbnailStorageMode();
            SyncUpperTabImageCacheMaxEntriesSliderFromSettings();
            InitializeSkinSelector();
            InitializeCurrentDbSettings();
            UpdateThumbnailStorageModeUi();

            InitializeThemeSelector();
        }

        /// <summary>
        /// 共通設定画面のクローズ時に、画面上の全設定値を Properties.Settings へ書き戻して永続化する。
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (IsSharedThumbnailFileOperationRunning())
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "サムネイルの移行または旧ファイル削除を処理中です。完了メッセージが表示されるまでお待ちください。",
                    "サムネイル処理中",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            _closeAccepted = false;
            if (!TryConfirmThumbnailStorageModeChange(e))
            {
                return;
            }

            // 画面上の値を Properties.Settings へ反映して永続化する。
            Properties.Settings.Default.AutoOpen = (bool)AutoOpen.IsChecked;
            Properties.Settings.Default.ConfirmExit = (bool)ConfirmExit.IsChecked;
            Properties.Settings.Default.DefaultPlayerPath = DefaultPlayerPath.Text;
            Properties.Settings.Default.DefaultPlayerParam = DefaultPlayerParam.Text;
            Properties.Settings.Default.RecentFilesCount = (int)slider.Value;
            Properties.Settings.Default.ThumbnailGpuDecodeEnabled = (bool)
                ThumbnailGpuDecodeEnabled.IsChecked;
            Properties.Settings.Default.ThumbnailStorageMode =
                SharedThumbnailStoragePolicy.NormalizeMode(
                    ThumbnailStorageModeSelector.SelectedIndex
                );
            Properties.Settings.Default.SharedThumbnailRoot =
                SharedThumbnailRoot.Text?.Trim() ?? "";
            // Delete系ショートカットの動作を保存する。範囲外は各既定値へ戻す。
            int deleteKeyActionMode = DeleteKeyActionMode.SelectedIndex;
            if (deleteKeyActionMode < 0 || deleteKeyActionMode > 3)
            {
                deleteKeyActionMode = 0;
            }
            Properties.Settings.Default.DeleteKeyActionMode = deleteKeyActionMode;
            int shiftDeleteKeyActionMode = ShiftDeleteKeyActionMode.SelectedIndex;
            if (shiftDeleteKeyActionMode < 0 || shiftDeleteKeyActionMode > 3)
            {
                shiftDeleteKeyActionMode = 2;
            }
            Properties.Settings.Default.ShiftDeleteKeyActionMode = shiftDeleteKeyActionMode;
            int ctrlDeleteKeyActionMode = CtrlDeleteKeyActionMode.SelectedIndex;
            if (ctrlDeleteKeyActionMode < 0 || ctrlDeleteKeyActionMode > 3)
            {
                ctrlDeleteKeyActionMode = 1;
            }
            Properties.Settings.Default.CtrlDeleteKeyActionMode = ctrlDeleteKeyActionMode;
            // OFF/AUTO/ONの3値設定を保存する。範囲外はAUTOへ丸める。
            int integrationMode = EverythingIntegrationMode.SelectedIndex;
            if (integrationMode < 0 || integrationMode > 2)
            {
                integrationMode = 1;
            }
            Properties.Settings.Default.EverythingIntegrationMode = integrationMode;
            // 旧設定との互換のため、OFF以外をtrueとして同期する。
            Properties.Settings.Default.EverythingIntegrationEnabled = integrationMode != 0;
            string selectedProvider = FileIndexProviderSelector.SelectedValue as string;
            Properties.Settings.Default.FileIndexProvider =
                FileIndexProviderFactory.NormalizeProviderKey(selectedProvider);
            // 一覧画像キャッシュ件数は converter 側の安全範囲へ丸めて保存する。
            Properties.Settings.Default.UpperTabImageCacheMaxEntries =
                ClampUpperTabImageCacheMaxEntries(
                    (int)System.Math.Round(sliderUpperTabImageCacheMaxEntries.Value)
                );
            if (App.IsDiagnosticNoPersistEnabled())
            {
                DebugRuntimeLog.Write(
                    "settings",
                    "common settings save skipped: diagnostic_no_persist=1"
                );
            }
            else
            {
                Properties.Settings.Default.Save();
            }
            PersistCurrentDbSettingsValuesIfNeeded();
            ThumbnailEnvConfig.ApplyFfmpegOnePassExecutionHintsForCurrentSettings();
            _closeAccepted = true;
        }

        private void InitializeThumbnailStorageMode()
        {
            _originalThumbnailStorageMode = SharedThumbnailStoragePolicy.NormalizeMode(
                Properties.Settings.Default.ThumbnailStorageMode
            );
            _originalSharedThumbnailRoot = SharedThumbnailStoragePolicy.ResolveSharedRoot(
                Properties.Settings.Default.SharedThumbnailRoot
            );

            _isThumbnailStorageModeInitializing = true;
            try
            {
                ThumbnailStorageModeSelector.SelectedIndex = _originalThumbnailStorageMode;
                SharedThumbnailRoot.Text = _originalSharedThumbnailRoot;
                UpdateThumbnailStorageModeUi();
            }
            finally
            {
                _isThumbnailStorageModeInitializing = false;
            }
        }

        private bool TryConfirmThumbnailStorageModeChange(CancelEventArgs e)
        {
            int selectedMode = SharedThumbnailStoragePolicy.NormalizeMode(
                ThumbnailStorageModeSelector.SelectedIndex
            );
            string selectedSharedRoot = SharedThumbnailStoragePolicy.ResolveSharedRoot(
                SharedThumbnailRoot.Text
            );
            if (
                !SharedThumbnailStoragePolicy.HasConfigurationChanged(
                    _originalThumbnailStorageMode,
                    _originalSharedThumbnailRoot,
                    selectedMode,
                    selectedSharedRoot
                )
            )
            {
                return true;
            }

            bool modeChanged = selectedMode != _originalThumbnailStorageMode;
            string message =
                selectedMode == SharedThumbnailStoragePolicy.SharedStoreMode
                    ? modeChanged
                        ? "共通サムネイルストアへ切り替えます。\n\n・反映にはアプリの再起動が必要です。\n・各DBの初回表示ではサムネイル再構築が必要です。\n・WhiteBrowserは共通ストアを参照しません。\n・旧サムネイルは安全のため自動削除されません。\n\n詳しい説明HTMLを確認したうえで切り替えてください。続行しますか？"
                        : "共通サムネイルストアの保存先を変更します。\n\n・反映にはアプリの再起動が必要です。\n・新しい保存先に対象DBの画像が無ければ、DBごとに再構築が必要です。\n・以前の共通ストアは自動移動・削除されません。\n\n続行しますか？"
                    : "DB別モードへ戻します。\n\n切り替え前のDB別サムネイルが残っていれば再構築は不要です。削除・退避済みの場合は、DBごとに再構築が必要です。\n反映にはアプリの再起動が必要です。\n\n続行しますか？";
            MessageBoxResult result = MessageBox.Show(
                this,
                message,
                "サムネイル保存方式の変更",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (result == MessageBoxResult.Yes)
            {
                return true;
            }

            e.Cancel = true;
            return false;
        }

        private bool HasThumbnailStorageConfigurationChanged()
        {
            return SharedThumbnailStoragePolicy.HasConfigurationChanged(
                _originalThumbnailStorageMode,
                _originalSharedThumbnailRoot,
                ThumbnailStorageModeSelector.SelectedIndex,
                SharedThumbnailRoot.Text
            );
        }

        // Closeは警告でキャンセルされ得るため、後続操作は実際に閉じられた時だけ開始する。
        private bool TryCloseBeforeFollowUpAction(MainWindow mainWindow)
        {
            bool thumbnailStorageChanged = HasThumbnailStorageConfigurationChanged();
            _closeAccepted = false;
            Close();
            if (!ShouldRunFollowUpAction(_closeAccepted, thumbnailStorageChanged))
            {
                if (_closeAccepted && thumbnailStorageChanged)
                {
                    ShowThumbnailStorageRestartRequired(mainWindow);
                }

                return false;
            }

            return true;
        }

        internal static bool ShouldRunFollowUpAction(
            bool closeAccepted,
            bool thumbnailStorageChanged
        ) => closeAccepted && !thumbnailStorageChanged;

        private void ShowThumbnailStorageRestartRequired(MainWindow mainWindow)
        {
            const string message =
                "サムネイル保存方式の変更を確定しました。\n反映にはIndigoMovieManagerの再起動が必要なため、選択した操作は開始していません。\n再起動後にもう一度実行してください。";
            if (mainWindow != null)
            {
                MessageBox.Show(
                    mainWindow,
                    message,
                    "再起動後に実行してください",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    message,
                    "再起動後に実行してください",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        // 共通設定を閉じる時にイベント購読を解除する。
        private void CommonSettingsWindow_Closed(object sender, System.EventArgs e)
        {
            Interlocked.Increment(ref _skinSelectorRefreshRevision);
            sliderUpperTabImageCacheMaxEntries.ValueChanged -=
                SliderUpperTabImageCacheMaxEntries_ValueChanged;
            Properties.Settings.Default.PropertyChanged -= SettingsDefault_PropertyChanged;
            Loaded -= CommonSettingsWindow_Loaded;
            Closed -= CommonSettingsWindow_Closed;
        }

        // Owner が確定してから、メイン画面の最近使った管理ファイルを設定フォームへ流す。
        private void CommonSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SyncRecentMainDbListFromOwner();
        }

        // 設定フォームは履歴を持たず、MainWindow 側の最新リストだけを表示する。
        private void SyncRecentMainDbListFromOwner()
        {
            MainWindow mainWindow = GetOwnerMainWindow();
            var recentRoot = mainWindow?.MainVM?.RecentTreeRoot;
            var recentItems =
                recentRoot != null && recentRoot.Count > 0 ? recentRoot[0].Children : null;
            bool hasRecentItems = recentItems != null && recentItems.Count > 0;

            SettingsRecentFilesList.ItemsSource = recentItems;
            SettingsRecentFilesList.Visibility = hasRecentItems
                ? Visibility.Visible
                : Visibility.Collapsed;
            SettingsRecentFilesEmptyMessage.Visibility = hasRecentItems
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // 他経路（ショートカット等）で設定値が変わった時、スライダーを即時追従させる。
        private void SettingsDefault_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            string propertyName = e?.PropertyName ?? "";
            if (
                string.Equals(
                    propertyName,
                    nameof(Properties.Settings.Default.UpperTabImageCacheMaxEntries),
                    System.StringComparison.Ordinal
                )
            )
            {
                SyncUpperTabImageCacheMaxEntriesSliderFromSettings();
                return;
            }
        }

        // 一覧画像キャッシュ件数は設定変更を即時反映し、次回 decode から使う。
        private void SliderUpperTabImageCacheMaxEntries_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isUpperTabImageCacheMaxEntriesSyncing)
            {
                return;
            }

            int next = ClampUpperTabImageCacheMaxEntries((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.UpperTabImageCacheMaxEntries == next)
            {
                return;
            }

            Properties.Settings.Default.UpperTabImageCacheMaxEntries = next;
        }

        // 設定値をスライダーへ同期し、範囲外値もここで吸収する。
        private void SyncUpperTabImageCacheMaxEntriesSliderFromSettings()
        {
            int next = ClampUpperTabImageCacheMaxEntries(
                Properties.Settings.Default.UpperTabImageCacheMaxEntries
            );
            if (Properties.Settings.Default.UpperTabImageCacheMaxEntries != next)
            {
                Properties.Settings.Default.UpperTabImageCacheMaxEntries = next;
            }

            if (System.Math.Abs(sliderUpperTabImageCacheMaxEntries.Value - next) < 0.0001d)
            {
                return;
            }

            _isUpperTabImageCacheMaxEntriesSyncing = true;
            try
            {
                sliderUpperTabImageCacheMaxEntries.Value = next;
            }
            finally
            {
                _isUpperTabImageCacheMaxEntriesSyncing = false;
            }
        }

        // 一覧画像キャッシュ件数は 256〜4096 の範囲に制限する。
        private static int ClampUpperTabImageCacheMaxEntries(int value)
        {
            return NoLockImageConverter.ClampImageCacheEntryLimit(value);
        }

        private void InitializeSkinSelector()
        {
            _ = RefreshSkinSelectorAsync();
        }

        private void InitializeThemeSelector()
        {
            // 旧値を新しい4モードへ寄せてから、画面の選択状態へ反映する。
            string normalizedTheme = App.NormalizeThemeMode(Properties.Settings.Default.ThemeMode);
            if (Properties.Settings.Default.ThemeMode != normalizedTheme)
            {
                App.SaveThemeModeSettingBestEffort(normalizedTheme, "settings-window-initialize");
            }

            _isThemeSelectorInitializing = true;
            try
            {
                foreach (System.Windows.Controls.ComboBoxItem item in ThemeComboBox.Items)
                {
                    if (item.Tag?.ToString() == normalizedTheme)
                    {
                        ThemeComboBox.SelectedItem = item;
                        return;
                    }
                }

                ThemeComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isThemeSelectorInitializing = false;
            }
        }

        private void InitializeCurrentDbSettings()
        {
            // 現在DBの設定だけを右ペインへ流し込み、DB未選択時は編集不能にする。
            MainWindow mainWindow = GetOwnerMainWindow();
            _currentDbSettingsPath = mainWindow?.MainVM?.DbInfo?.DBFullPath ?? "";
            bool hasCurrentDb = !string.IsNullOrWhiteSpace(_currentDbSettingsPath);

            CurrentDbSettingsEmptyMessage.Visibility = hasCurrentDb
                ? Visibility.Collapsed
                : Visibility.Visible;
            CurrentDbSettingsPanel.IsEnabled = hasCurrentDb;
            if (!hasCurrentDb)
            {
                CurrentDbSettingsPanel.DataContext = null;
                _currentDbOriginalThumbFolder = "";
                _currentDbOriginalBookmarkFolder = "";
                _currentDbOriginalKeepHistory = "";
                _currentDbOriginalPlayerPrg = "";
                _currentDbOriginalPlayerParam = "";
                return;
            }

            DbSettings currentDbSettings = new(_currentDbSettingsPath);
            CurrentDbSettingsPanel.DataContext = currentDbSettings;
            _currentDbOriginalThumbFolder = currentDbSettings.ThumbFolder ?? "";
            _currentDbOriginalBookmarkFolder = currentDbSettings.BookmarkFolder ?? "";
            _currentDbOriginalKeepHistory = currentDbSettings.KeepHistory.ToString();
            _currentDbOriginalPlayerPrg = currentDbSettings.PlayerPrg ?? "";
            _currentDbOriginalPlayerParam = currentDbSettings.PlayerParam ?? "";
        }

        private void PersistCurrentDbSettingsValuesIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_currentDbSettingsPath))
            {
                return;
            }

            MainWindow mainWindow = GetOwnerMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            if (
                !MainWindow.AreSameMainDbPath(
                    _currentDbSettingsPath,
                    mainWindow.MainVM?.DbInfo?.DBFullPath ?? ""
                )
            )
            {
                string writeLogFields = MainWindow.BuildCurrentDbSettingsWriteLogFields(
                    "settings-window-closing"
                );
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"settings persist skipped: reason=db-changed {writeLogFields} db='{_currentDbSettingsPath}'"
                );
                return;
            }

            string thumbFolder = ThumbFolder.Text ?? "";
            string bookmarkFolder = BookmarkFolder.Text ?? "";
            string keepHistory = KeepHistory.Text ?? "";
            string playerPrg = PlayerPrg.Text ?? "";
            string playerParam = PlayerParam.Text?.ToString() ?? "";
            if (
                string.Equals(thumbFolder, _currentDbOriginalThumbFolder, StringComparison.Ordinal)
                && string.Equals(
                    bookmarkFolder,
                    _currentDbOriginalBookmarkFolder,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    keepHistory,
                    _currentDbOriginalKeepHistory,
                    StringComparison.Ordinal
                )
                && string.Equals(playerPrg, _currentDbOriginalPlayerPrg, StringComparison.Ordinal)
                && string.Equals(
                    playerParam,
                    _currentDbOriginalPlayerParam,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            int persistedSettingsCount =
                mainWindow.PersistCurrentDbSettingsValuesFromSettingsWindow(
                    _currentDbSettingsPath,
                    thumbFolder,
                    bookmarkFolder,
                    keepHistory,
                    playerPrg,
                    playerParam
                );
            if (persistedSettingsCount != 5)
            {
                string writeLogFields = MainWindow.BuildCurrentDbSettingsWriteLogFields(
                    "settings-window-closing"
                );
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"settings persist partial: success={persistedSettingsCount}/5 {writeLogFields} db='{_currentDbSettingsPath}'"
                );
            }
        }

        private MainWindow GetOwnerMainWindow()
        {
            return Owner as MainWindow ?? Application.Current?.MainWindow as MainWindow;
        }

        private async Task RefreshSkinSelectorAsync()
        {
            int revision = Interlocked.Increment(ref _skinSelectorRefreshRevision);
            WhiteBrowserSkinOrchestrator skinOrchestrator = GetMainWindowSkinOrchestrator();
            if (skinOrchestrator == null)
            {
                SkinComboBox.IsEnabled = false;
                SkinComboBox.ToolTip = "メインウィンドウ初期化後に利用できます。";
                return;
            }

            try
            {
                IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                    await skinOrchestrator.GetAvailableSkinDefinitionsAsync();
                if (revision != _skinSelectorRefreshRevision)
                {
                    return;
                }

                _isSkinSelectorInitializing = true;
                try
                {
                    SkinComboBox.ItemsSource = definitions;
                    SkinComboBox.SelectedValue = skinOrchestrator.GetCurrentSkinName();
                }
                finally
                {
                    _isSkinSelectorInitializing = false;
                }

                MainWindow mainWindow = Application.Current?.MainWindow as MainWindow;
                bool hasCurrentDb = !string.IsNullOrWhiteSpace(
                    mainWindow?.MainVM?.DbInfo?.DBFullPath ?? ""
                );
                SkinComboBox.IsEnabled = hasCurrentDb;
                UpdateSkinSelectorToolTip(
                    skinOrchestrator.GetCurrentSkinDefinition(),
                    hasCurrentDb
                );
            }
            catch (Exception ex)
            {
                if (revision != _skinSelectorRefreshRevision)
                {
                    return;
                }

                DebugRuntimeLog.Write(
                    "settings-ui",
                    $"skin selector refresh failed: {ex.GetType().Name}: {ex.Message}"
                );
                SkinComboBox.IsEnabled = false;
                SkinComboBox.ToolTip = "スキン一覧の取得に失敗しました。ログを確認してください。";
            }
        }

        private WhiteBrowserSkinOrchestrator GetMainWindowSkinOrchestrator()
        {
            return (Application.Current?.MainWindow as MainWindow)?.GetSkinOrchestrator();
        }

        private void UpdateSkinSelectorToolTip(
            WhiteBrowserSkinDefinition selectedDefinition,
            bool hasCurrentDb
        )
        {
            if (!hasCurrentDb)
            {
                SkinComboBox.ToolTip = "DB を開くと選択できます。";
                return;
            }

            if (selectedDefinition?.IsMissing == true)
            {
                SkinComboBox.ToolTip =
                    "現在 DB には未解決の外部スキン名が保存されています。名前は保持しつつ、表示はフォールバック前提です。";
                return;
            }

            SkinComboBox.ToolTip =
                selectedDefinition?.RequiresWebView2 == true
                    ? "現在 DB の system.skin へ保存します。外部スキンは WebView2 経路で表示する前提です。"
                    : "現在 DB の system.skin へ保存します。既定スキンは従来の高速表示を使います。";
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定画面を閉じてメインへ戻る。
            Close();
        }

        private void OpenDialogPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定の既定プレイヤー実行ファイルを選択する。
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                ),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "既定のプレイヤー選択",
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                DefaultPlayerPath.Text = ofd.FileName;
            }
        }

        private void OpenCurrentDbFolderDialog_Click(object sender, RoutedEventArgs e)
        {
            // 現在DBのフォルダ系設定を、押されたボタンに対応する入力欄へ反映する。
            if (sender is not Button item)
            {
                return;
            }

            if (item.Name is not ("OpenThumbFolder" or "OpenBookmarkFolder"))
            {
                return;
            }

            var dlg = new OpenFolderDialog
            {
                Title =
                    item.Name == "OpenThumbFolder" ? "サムネイルの保存先" : "ブックマークの保存先",
                Multiselect = false,
                AddToRecent = true,
            };

            if (dlg.ShowDialog() != true)
            {
                return;
            }

            TextBox textBox = item.Name == "OpenThumbFolder" ? ThumbFolder : BookmarkFolder;
            textBox.Text = dlg.FolderName;
        }

        private void ThumbnailStorageModeSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_isThumbnailStorageModeInitializing)
            {
                return;
            }

            ClearCompletedThumbnailMigrationContext();
            UpdateThumbnailStorageModeUi();
        }

        private void UpdateThumbnailStorageModeUi()
        {
            bool isShared = SharedThumbnailStoragePolicy.IsSharedStoreEnabled(
                ThumbnailStorageModeSelector.SelectedIndex
            );
            bool hasCurrentDb = !string.IsNullOrWhiteSpace(_currentDbSettingsPath);
            bool isFileOperationRunning = IsSharedThumbnailFileOperationRunning();
            bool hasCompletedMigration = HasCompletedThumbnailMigrationContext();
            ThumbnailStorageModeSelector.IsEnabled = !isFileOperationRunning;
            SharedThumbnailRootPanel.IsEnabled = isShared && !isFileOperationRunning;
            SharedThumbnailWarning.Visibility = isShared
                ? Visibility.Visible
                : Visibility.Collapsed;
            SharedThumbnailMigrationPanel.Visibility = isShared
                ? Visibility.Visible
                : Visibility.Collapsed;
            MigrateCurrentDbThumbnailsButton.IsEnabled =
                isShared && hasCurrentDb && !isFileOperationRunning;
            DeleteMigratedSourceThumbnailsButton.IsEnabled = ShouldEnableMigratedSourceCleanup(
                isShared,
                hasCurrentDb,
                isFileOperationRunning,
                hasCompletedMigration
            );

            if (isShared && string.IsNullOrWhiteSpace(SharedThumbnailRoot.Text))
            {
                SharedThumbnailRoot.Text = SharedThumbnailStoragePolicy.GetDefaultSharedRoot();
            }

            if (isShared && !hasCurrentDb)
            {
                SharedThumbnailMigrationStatus.Text =
                    "現在のDBが開かれていないため、一発移行は利用できません。";
            }
        }

        private bool IsSharedThumbnailFileOperationRunning()
        {
            return _isSharedThumbnailMigrationRunning || _isSharedThumbnailCleanupRunning;
        }

        private bool HasCompletedThumbnailMigrationContext()
        {
            return !string.IsNullOrWhiteSpace(_completedMigrationSourceRoot)
                && !string.IsNullOrWhiteSpace(_completedMigrationTargetRoot)
                && !string.IsNullOrWhiteSpace(_completedMigrationSharedRoot);
        }

        internal static bool ShouldEnableMigratedSourceCleanup(
            bool isShared,
            bool hasCurrentDb,
            bool isFileOperationRunning,
            bool hasCompletedMigration
        )
        {
            return isShared && hasCurrentDb && !isFileOperationRunning && hasCompletedMigration;
        }

        private void ClearCompletedThumbnailMigrationContext()
        {
            _completedMigrationSourceRoot = "";
            _completedMigrationTargetRoot = "";
            _completedMigrationSharedRoot = "";
        }

        private void OpenSharedThumbnailRoot_Click(object sender, RoutedEventArgs e)
        {
            string defaultRoot = SharedThumbnailStoragePolicy.GetDefaultSharedRoot();
            var dialog = new OpenFolderDialog
            {
                Title = "共通サムネイルストアの保存先",
                Multiselect = false,
                AddToRecent = true,
                InitialDirectory = Directory.Exists(SharedThumbnailRoot.Text)
                    ? SharedThumbnailRoot.Text
                    : Directory.GetParent(defaultRoot)?.FullName
                        ?? Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData
                        ),
            };

            if (dialog.ShowDialog() == true)
            {
                if (
                    !string.Equals(
                        SharedThumbnailStoragePolicy.ResolveSharedRoot(SharedThumbnailRoot.Text),
                        SharedThumbnailStoragePolicy.ResolveSharedRoot(dialog.FolderName),
                        System.StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    ClearCompletedThumbnailMigrationContext();
                }

                SharedThumbnailRoot.Text = dialog.FolderName;
                UpdateThumbnailStorageModeUi();
            }
        }

        private async void MigrateCurrentDbThumbnailsToSharedStore_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            if (_isSharedThumbnailMigrationRunning)
            {
                return;
            }

            MainWindow mainWindow = GetOwnerMainWindow();
            string currentDbPath = mainWindow?.MainVM?.DbInfo?.DBFullPath ?? "";
            if (
                mainWindow == null
                || string.IsNullOrWhiteSpace(_currentDbSettingsPath)
                || !MainWindow.AreSameMainDbPath(_currentDbSettingsPath, currentDbPath)
            )
            {
                MessageBox.Show(
                    this,
                    "設定画面を開いた後に現在のDBが変わりました。設定画面を開き直してから実行してください。",
                    "一発移行を開始できません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            string dbName = mainWindow.MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(dbName))
            {
                dbName = Path.GetFileNameWithoutExtension(currentDbPath) ?? "database";
            }

            // コピー元は編集欄の将来値ではなく、現在プロセスが実際に使っているルートに固定する。
            // これによりDB別→共通だけでなく、共通ストア保存先の変更にも同じ導線を使える。
            string sourceRoot = mainWindow.MainVM?.DbInfo?.ThumbFolder ?? "";
            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                sourceRoot = ThumbRootResolver.ResolveRuntimeThumbRoot(
                    currentDbPath,
                    dbName,
                    ThumbFolder.Text ?? ""
                );
            }
            string sharedRoot = SharedThumbnailStoragePolicy.ResolveSharedRoot(
                SharedThumbnailRoot.Text
            );
            string targetRoot = SharedThumbnailStoragePolicy.ResolveEffectiveThumbnailRoot(
                currentDbPath,
                dbName,
                sourceRoot,
                SharedThumbnailStoragePolicy.SharedStoreMode,
                sharedRoot
            );

            if (SharedThumbnailStoragePolicy.AreSameOrNestedRoots(sourceRoot, targetRoot))
            {
                bool alreadyUsingSelectedRoot = string.Equals(
                    Path.GetFullPath(sourceRoot)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(targetRoot)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    System.StringComparison.OrdinalIgnoreCase
                );
                string message = alreadyUsingSelectedRoot
                    ? "現在のDBは、すでに選択した共通ストアを使用しています。移行は不要です。"
                    : "コピー元とコピー先が同じフォルダー階層に含まれています。データ混在を防ぐため移行できません。共通ストアを旧サムネイルフォルダーの外へ変更してください。";
                SharedThumbnailMigrationStatus.Text = message;
                MessageBox.Show(
                    this,
                    message,
                    alreadyUsingSelectedRoot ? "移行済みです" : "保存先を変更してください",
                    MessageBoxButton.OK,
                    alreadyUsingSelectedRoot ? MessageBoxImage.Information : MessageBoxImage.Warning
                );
                return;
            }

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"現在のDBのサムネイル一式を共通ストアへ移行します。\n\nコピー元:\n{sourceRoot}\n\nコピー先:\n{targetRoot}\n\n・通常JPEGと #ERROR.jpg を移行します。\n・旧フォルダーは削除せず、DB別方式へ戻すために残します。\n・全件成功後に新方式の設定を保存します。\n・完了後はアプリの再起動が必要です。\n\n開始しますか？",
                "共通ストアへ一発移行",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ClearCompletedThumbnailMigrationContext();
            _isSharedThumbnailMigrationRunning = true;
            SharedThumbnailMigrationStatus.Text =
                "移行処理中です。画像数が多い場合は完了まで時間がかかります。";
            UpdateThumbnailStorageModeUi();

            SharedThumbnailMigrationResult result;
            try
            {
                result = await mainWindow.RunSharedThumbnailMigrationFromSettingsAsync(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );
            }
            catch (System.Exception ex)
            {
                result = SharedThumbnailMigrationResult.Rejected(
                    sourceRoot,
                    targetRoot,
                    $"{ex.GetType().Name}:{ex.Message}"
                );
            }

            if (!result.Succeeded)
            {
                _isSharedThumbnailMigrationRunning = false;
                UpdateThumbnailStorageModeUi();
                SharedThumbnailMigrationStatus.Text =
                    $"移行未完了：成功 {result.CopiedFiles}/{result.TotalFiles} 件、失敗 {result.FailedFiles} 件。旧フォルダーとコピー済みファイルは残しています。";
                DebugRuntimeLog.Write(
                    "thumbnail-storage",
                    $"shared thumbnail migration incomplete: started={result.Started} total={result.TotalFiles} copied={result.CopiedFiles} failed={result.FailedFiles} reason='{result.Reason}' source='{result.SourceRoot}' target='{result.TargetRoot}'"
                );
                MessageBox.Show(
                    this,
                    $"一発移行は完了していません。\n\n成功: {result.CopiedFiles}/{result.TotalFiles} 件\n失敗: {result.FailedFiles} 件\n最初の理由: {result.Reason}\n\n新方式の設定は保存していません。旧フォルダーはそのままです。原因を解消後、同じボタンで再実行できます。",
                    "共通ストア移行が未完了です",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            // 全件移行できた時だけ選択中の新方式を確定し、次回起動へ引き渡す。
            int previousMode = Properties.Settings.Default.ThumbnailStorageMode;
            string previousSharedRoot = Properties.Settings.Default.SharedThumbnailRoot;
            SharedThumbnailRoot.Text = sharedRoot;
            Properties.Settings.Default.ThumbnailStorageMode =
                SharedThumbnailStoragePolicy.SharedStoreMode;
            Properties.Settings.Default.SharedThumbnailRoot = sharedRoot;
            bool settingsSaved;
            try
            {
                settingsSaved = await mainWindow.SaveApplicationSettingsFromSettingsWindowAsync(
                    "shared-thumbnail-one-click-migration"
                );
            }
            catch (System.Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-storage",
                    $"shared thumbnail migration settings save task failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
                settingsSaved = false;
            }
            if (!settingsSaved)
            {
                Properties.Settings.Default.ThumbnailStorageMode = previousMode;
                Properties.Settings.Default.SharedThumbnailRoot = previousSharedRoot;
                _isSharedThumbnailMigrationRunning = false;
                UpdateThumbnailStorageModeUi();
                SharedThumbnailMigrationStatus.Text =
                    $"画像 {result.CopiedFiles} 件の移行は完了しましたが、新方式の設定を保存できませんでした。同じボタンで再実行してください。";
                DebugRuntimeLog.Write(
                    "thumbnail-storage",
                    $"shared thumbnail migration settings save failed: total={result.TotalFiles} copied={result.CopiedFiles} source='{result.SourceRoot}' target='{result.TargetRoot}'"
                );
                MessageBox.Show(
                    this,
                    "画像の移行は完了しましたが、新方式の設定を保存できませんでした。\n\n旧フォルダーと移行済みファイルは残しています。設定の保存先や書き込み権限を確認し、同じボタンで再実行してください。",
                    "設定を保存できませんでした",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            _originalThumbnailStorageMode = SharedThumbnailStoragePolicy.SharedStoreMode;
            _originalSharedThumbnailRoot = sharedRoot;
            _completedMigrationSourceRoot = result.SourceRoot;
            _completedMigrationTargetRoot = result.TargetRoot;
            _completedMigrationSharedRoot = sharedRoot;
            _isSharedThumbnailMigrationRunning = false;
            UpdateThumbnailStorageModeUi();

            SharedThumbnailMigrationStatus.Text =
                $"移行完了：{result.CopiedFiles} 件（共有化 {result.DeduplicatedJpegs} 件、通常コピー {result.FallbackJpegs} 件）。再起動後に新方式が有効になります。";
            DebugRuntimeLog.Write(
                "thumbnail-storage",
                $"shared thumbnail migration completed: total={result.TotalFiles} copied={result.CopiedFiles} deduplicated={result.DeduplicatedJpegs} fallback={result.FallbackJpegs} source='{result.SourceRoot}' target='{result.TargetRoot}'"
            );
            MessageBox.Show(
                this,
                $"共通ストアへの一発移行が完了しました。\n\n移行: {result.CopiedFiles} 件\n共有化: {result.DeduplicatedJpegs} 件\n通常コピー: {result.FallbackJpegs} 件\n\n新方式の設定も保存しました。旧フォルダーは安全のため残しています。IndigoMovieManagerを再起動してください。",
                "一発移行完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private async void DeleteMigratedSourceThumbnails_Click(object sender, RoutedEventArgs e)
        {
            if (IsSharedThumbnailFileOperationRunning() || !HasCompletedThumbnailMigrationContext())
            {
                return;
            }

            MainWindow mainWindow = GetOwnerMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            string sourceRoot = _completedMigrationSourceRoot;
            string targetRoot = _completedMigrationTargetRoot;
            string sharedRoot = _completedMigrationSharedRoot;
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"共通ストア側と内容が一致する旧サムネイルだけを削除します。\n\n削除元:\n{sourceRoot}\n\n確認先:\n{targetRoot}\n\n・この削除は元に戻せません。\n・未移行、移行後に更新されたファイル、確認不能なファイルは削除しません。\n・削除後にDB別方式へ戻す場合やWhiteBrowserで表示する場合は、サムネイル再構築が必要です。\n・管理DB（.wb）は変更しません。\n\n実行しますか？",
                "移動済みサムネイル削除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            _isSharedThumbnailCleanupRunning = true;
            SharedThumbnailMigrationStatus.Text =
                "移動済み旧ファイルを照合して削除しています。完了までお待ちください。";
            UpdateThumbnailStorageModeUi();

            SharedThumbnailSourceCleanupResult result;
            try
            {
                result = await mainWindow.RunSharedThumbnailSourceCleanupFromSettingsAsync(
                    sourceRoot,
                    targetRoot,
                    sharedRoot
                );
            }
            catch (System.Exception ex)
            {
                result = SharedThumbnailSourceCleanupResult.Rejected(
                    sourceRoot,
                    targetRoot,
                    $"{ex.GetType().Name}:{ex.Message}"
                );
            }
            finally
            {
                _isSharedThumbnailCleanupRunning = false;
                UpdateThumbnailStorageModeUi();
            }

            if (!result.Succeeded)
            {
                SharedThumbnailMigrationStatus.Text =
                    $"旧ファイル削除は未完了です。削除 {result.DeletedFiles} 件、保留 {result.SkippedFiles} 件、失敗 {result.FailedFiles} 件。";
                DebugRuntimeLog.Write(
                    "thumbnail-storage",
                    $"shared thumbnail source cleanup incomplete: started={result.Started} scanned={result.ScannedFiles} deleted={result.DeletedFiles} skipped={result.SkippedFiles} failed={result.FailedFiles} reason='{result.Reason}' source='{result.SourceRoot}' target='{result.TargetRoot}'"
                );
                MessageBox.Show(
                    this,
                    $"旧ファイル削除は完了していません。\n\n削除: {result.DeletedFiles} 件\n保留: {result.SkippedFiles} 件\n失敗: {result.FailedFiles} 件\n理由: {result.Reason}\n\n削除できなかったファイルは旧フォルダーに残しています。",
                    "旧ファイル削除が未完了です",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            if (result.SkippedFiles > 0)
            {
                ClearCompletedThumbnailMigrationContext();
                UpdateThumbnailStorageModeUi();
                SharedThumbnailMigrationStatus.Text =
                    $"削除 {result.DeletedFiles} 件、保留 {result.SkippedFiles} 件。保留分を削除するには一発移行を再実行してください。";
                MessageBox.Show(
                    this,
                    $"内容一致を確認できた旧ファイルを削除しました。\n\n削除: {result.DeletedFiles} 件\n保留: {result.SkippedFiles} 件\n\n保留ファイルは未移行または更新済みの可能性があるため残しています。必要なら一発移行を再実行してから、もう一度削除してください。",
                    "一部の旧ファイルを安全のため残しました",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            ClearCompletedThumbnailMigrationContext();
            UpdateThumbnailStorageModeUi();
            SharedThumbnailMigrationStatus.Text =
                $"移動済み旧ファイルを {result.DeletedFiles} 件削除しました。共通ストア側は維持されています。";
            DebugRuntimeLog.Write(
                "thumbnail-storage",
                $"shared thumbnail source cleanup completed: scanned={result.ScannedFiles} deleted={result.DeletedFiles} removed_directories={result.RemovedDirectories} source='{result.SourceRoot}' target='{result.TargetRoot}'"
            );
            MessageBox.Show(
                this,
                $"移動済み旧ファイルの削除が完了しました。\n\n削除: {result.DeletedFiles} 件\n空フォルダー削除: {result.RemovedDirectories} 件\n\n共通ストア側のファイルは削除していません。DB別方式またはWhiteBrowserへ戻す場合はサムネイルを再構築してください。",
                "移動済みサムネイル削除完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void OpenSharedThumbnailHelp_Click(object sender, RoutedEventArgs e)
        {
            string helpPath = Path.Combine(
                AppContext.BaseDirectory,
                "Docs",
                "Help",
                "共通サムネイルストア_利用ガイド.html"
            );
            if (!File.Exists(helpPath))
            {
                MessageBox.Show(
                    this,
                    $"説明ファイルが見つかりません。\n{helpPath}",
                    "説明HTMLを開けません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = helpPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"説明HTMLを開けませんでした。\n{ex.Message}",
                    "説明HTMLを開けません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void OpenCurrentDbPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 現在DB専用の再生プレイヤー実行ファイルを選択する。
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                ),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "既定のプレイヤー選択",
            };

            if (ofd.ShowDialog() == true)
            {
                PlayerPrg.Text = ofd.FileName;
            }
        }

        private void OpenWatchFolderEditorFromSettings_Click(object sender, RoutedEventArgs e)
        {
            // 設定フォームを閉じ、メイン側の既存入口から監視フォルダ編集を開く。
            MainWindow mainWindow = GetOwnerMainWindow();
            if (!TryCloseBeforeFollowUpAction(mainWindow))
            {
                return;
            }

            mainWindow?.OpenWatchFolderEditorFromSettingsWindow();
        }

        private async void OpenRecentMainDbFromSettings_Click(object sender, RoutedEventArgs e)
        {
            // 最近使った管理ファイルは既存のDB切替フローへ戻し、履歴の再構築も一箇所へ寄せる。
            if (sender is not Button item)
            {
                return;
            }

            string dbFullPath = item.Tag?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            MainWindow mainWindow = GetOwnerMainWindow();
            if (!TryCloseBeforeFollowUpAction(mainWindow))
            {
                return;
            }

            if (mainWindow != null)
            {
                await mainWindow.OpenRecentMainDbFromSettingsWindowAsync(dbFullPath);
            }
        }

        private void ManualWatchCheckFromSettings_Click(object sender, RoutedEventArgs e)
        {
            // 手動チェックは既存のキュー入口へ渡し、設定フォーム自体は先に閉じる。
            MainWindow mainWindow = GetOwnerMainWindow();
            if (!TryCloseBeforeFollowUpAction(mainWindow))
            {
                return;
            }

            mainWindow?.QueueManualWatchCheckFromSettingsWindow();
        }

        private void RecreateAllThumbnailsFromSettings_Click(object sender, RoutedEventArgs e)
        {
            // サムネイル再作成も既存のメイン画面処理へ集約する。
            MainWindow mainWindow = GetOwnerMainWindow();
            if (!TryCloseBeforeFollowUpAction(mainWindow))
            {
                return;
            }

            mainWindow?.QueueRecreateAllThumbnailsFromSettingsWindow();
        }

        private void ThemeComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e
        )
        {
            if (_isThemeSelectorInitializing)
            {
                return;
            }

            if (
                ThemeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
                && item.Tag is string themeTag
            )
            {
                string normalizedTheme = App.NormalizeThemeMode(themeTag);
                if (IndigoMovieManager.Properties.Settings.Default.ThemeMode != normalizedTheme)
                {
                    App.SaveThemeModeSettingBestEffort(
                        normalizedTheme,
                        "settings-window-selection"
                    );
                    // 選択直後に、開いている画面も同じテーマへ流す。
                    App.ApplyTheme(normalizedTheme);
                }
            }
        }

        private void SkinComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e
        )
        {
            if (_isSkinSelectorInitializing)
            {
                return;
            }

            WhiteBrowserSkinOrchestrator skinOrchestrator = GetMainWindowSkinOrchestrator();
            if (skinOrchestrator == null)
            {
                return;
            }

            if (
                SkinComboBox.SelectedValue is string skinName
                && !string.IsNullOrWhiteSpace(skinName)
            )
            {
                _ = skinOrchestrator.ApplySkinByName(skinName, persistToCurrentDb: true);
                UpdateSkinSelectorToolTip(
                    skinOrchestrator.GetCurrentSkinDefinition(),
                    hasCurrentDb: true
                );
            }
        }
    }
}

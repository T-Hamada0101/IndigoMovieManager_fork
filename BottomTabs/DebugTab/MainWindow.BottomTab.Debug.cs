using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvalonDock.Layout;
using IndigoMovieManager.BottomTabs.Debug;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using System.Data.SQLite;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string DebugToolContentId = "ToolDebug";
        private const int DebugLogRefreshIntervalMs = 3000;
        private static bool ShouldShowDebugTab => EvaluateShowDebugTab();

        private DispatcherTimer _debugTabRefreshTimer;
        private DebugTabPresenter _debugTabPresenter;
        private string _debugCurrentDbRecordCountPath = "";
        private string _debugCurrentQueueDbRecordCountPath = "";
        private string _debugCurrentFailureDbRecordCountPath = "";
        private int _debugCurrentDbRecordCountRevision;
        private int _debugCurrentQueueDbRecordCountRevision;
        private int _debugCurrentFailureDbRecordCountRevision;
        private int _debugExplorerOpenRequestRevision;

        private void InitializeDebugTabSupport()
        {
            if (!ShouldShowDebugTab || DebugTab == null)
            {
                return;
            }

            if (_debugTabPresenter != null || DebugTab == null)
            {
                return;
            }

            _debugTabRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebugLogRefreshIntervalMs),
            };
            _debugTabRefreshTimer.Tick += DebugTabRefreshTimer_Tick;
            _debugTabPresenter = new DebugTabPresenter(
                DebugTab,
                _debugTabRefreshTimer,
                () => ShouldShowDebugTab,
                forceRefresh => UpdateDebugTabRefreshState(forceRefresh),
                isActive => UpdateDebugTabRefreshTimerState(isActive)
            );
            _debugTabPresenter.Initialize();
        }

        private static bool EvaluateShowDebugTab()
        {
            bool isDebuggerAttached = Debugger.IsAttached;
#if DEBUG
            const bool isDebugBuild = true;
#else
            const bool isDebugBuild = false;
#endif
            // 開発中の確認用途では、Release 実行でも debugger 接続中なら明示的に見せる。
            return ShouldShowDebugTabCore(
                isDebugBuild,
                IsReleaseBuild(),
                isDebuggerAttached
            );
        }

        internal static bool ShouldShowDebugTabCore(
            bool isDebugBuild,
            bool isReleaseBuild,
            bool isDebuggerAttached
        )
        {
            if (isDebuggerAttached)
            {
                return true;
            }

            return isDebugBuild && !isReleaseBuild;
        }

        private static bool IsReleaseBuild()
        {
            string configuration = typeof(MainWindow).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                ?? "";
            return string.Equals(
                configuration,
                "Release",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private bool IsDebugTabActive()
        {
            return _debugTabPresenter?.IsActive() == true;
        }

        private void DebugTabRefreshTimer_Tick(object sender, EventArgs e)
        {
            _debugTabPresenter?.HandleTimerTick(() => RefreshDebugArtifactPaths());
        }

        // Debugタブがアクティブな間だけ低頻度で状態を更新し、前面に来た瞬間だけ強制反映する。
        private void UpdateDebugTabRefreshState(bool forceRefresh)
        {
            bool isActive = IsDebugTabActive();
            UpdateDebugTabRefreshTimerState(isActive);

            if (isActive && (forceRefresh || !(_debugTabPresenter?.WasActive ?? false)))
            {
                RefreshDebugArtifactPaths();
                RefreshDebugRecordCounts(force: true);
            }

            _debugTabPresenter?.RecordRefreshState(isActive);
        }

        private void UpdateDebugTabRefreshTimerState(bool isActive)
        {
            if (_debugTabRefreshTimer == null)
            {
                return;
            }

            if (ShouldShowDebugTab && isActive)
            {
                if (!_debugTabRefreshTimer.IsEnabled)
                {
                    TryStartDispatcherTimer(
                        _debugTabRefreshTimer,
                        nameof(_debugTabRefreshTimer)
                    );
                }

                return;
            }

            if (_debugTabRefreshTimer.IsEnabled)
            {
                StopDispatcherTimerSafely(
                    _debugTabRefreshTimer,
                    nameof(_debugTabRefreshTimer)
                );
            }
        }

        // 開発用タブは Debug 構成か debugger 接続時だけ下部ペインへ残す。
        private void ApplyDebugTabVisibility()
        {
            if (DebugTab == null || uxAnchorablePane2 == null)
            {
                DebugRuntimeLog.Write(
                    "debug-tab",
                    $"skip apply. DebugTabNull={DebugTab == null} PaneNull={uxAnchorablePane2 == null}"
                );
                return;
            }

            if (!ShouldShowDebugTab)
            {
                DebugRuntimeLog.Write("debug-tab", "hide because ShouldShowDebugTab=false");
                DebugTab.IsSelected = false;
                DebugTab.IsActive = false;
                DebugTab.Hide();
                return;
            }

            // 旧レイアウト復元で Hidden 側や別ペインへ流れても、必ず下部ペインへ戻す。
            if (
                DebugTab.Parent is ILayoutContainer currentParent
                && !ReferenceEquals(currentParent, uxAnchorablePane2)
            )
            {
                DebugRuntimeLog.Write(
                    "debug-tab",
                    $"move from parent={currentParent.GetType().Name} to uxAnchorablePane2"
                );
                currentParent.RemoveChild(DebugTab);
            }

            if (!uxAnchorablePane2.Children.Contains(DebugTab))
            {
                DebugRuntimeLog.Write("debug-tab", "add DebugTab to uxAnchorablePane2");
                uxAnchorablePane2.Children.Add(DebugTab);
            }

            DebugTab.Show();
            DebugTab.IsSelected = true;
            DebugRuntimeLog.Write(
                "debug-tab",
                $"show complete. Parent={DebugTab.Parent?.GetType().Name ?? "null"} Selected={DebugTab.IsSelected} Hidden={DebugTab.IsHidden}"
            );
            UpdateDebugTabRefreshState(forceRefresh: true);
        }

        // Debugタブの各パス表示を、現在の選択DBに追従させる。
        private void RefreshDebugArtifactPaths()
        {
            if (!ShouldShowDebugTab || !IsDebugTabActive())
            {
                return;
            }

            string currentDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string currentQueueDbPath = ResolveCurrentQueueDbPathForDebug();
            string currentFailureDbPath = ResolveCurrentFailureDbPathForDebug();

            if (DebugTabViewHost?.CurrentDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentDbPathTextBox,
                    FormatDebugPath(currentDbPath, "現在DBは未選択です。")
                );
            }

            if (DebugTabViewHost?.CurrentQueueDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentQueueDbPathTextBox,
                    FormatDebugPath(
                        currentQueueDbPath,
                        "現在QueueDBは未解決です。"
                    )
                );
            }

            if (DebugTabViewHost?.CurrentFailureDbPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentFailureDbPathTextBox,
                    FormatDebugPath(
                        currentFailureDbPath,
                        "現在FailureDBは未解決です。"
                    )
                );
            }

            if (DebugTabViewHost?.CurrentThumbnailPathTextBox != null)
            {
                SetTextIfChanged(
                    DebugTabViewHost.CurrentThumbnailPathTextBox,
                    FormatDebugPath(
                        ResolveCurrentThumbnailRootForDebug(),
                        "現在サムネイルパスは未解決です。"
                    )
                );
            }

            if (!string.Equals(_debugCurrentDbRecordCountPath, currentDbPath, StringComparison.OrdinalIgnoreCase))
            {
                RefreshDebugCurrentDbRecordCount(currentDbPath, force: true);
            }

            if (
                !string.Equals(
                    _debugCurrentQueueDbRecordCountPath,
                    currentQueueDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                RefreshDebugCurrentQueueDbRecordCount(currentQueueDbPath, force: true);
            }

            if (
                !string.Equals(
                    _debugCurrentFailureDbRecordCountPath,
                    currentFailureDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                RefreshDebugCurrentFailureDbRecordCount(currentFailureDbPath, force: true);
            }
        }

        private void RefreshDebugRecordCounts(bool force = false)
        {
            if (!IsDebugTabActive())
            {
                return;
            }

            RefreshDebugCurrentDbRecordCount(MainVM?.DbInfo?.DBFullPath ?? "", force);
            RefreshDebugCurrentQueueDbRecordCount(ResolveCurrentQueueDbPathForDebug(), force);
            RefreshDebugCurrentFailureDbRecordCount(ResolveCurrentFailureDbPathForDebug(), force);
        }

        private async void RefreshDebugCurrentDbRecordCount(string dbPath, bool force)
        {
            TextBlock recordCountTextBlock = DebugTabViewHost?.CurrentDbRecordCountTextBlock;
            if (recordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentDbRecordCountPath,
                    dbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            // UI側では要求の同一性だけを固定し、実ファイル確認とSQLite readは背景側へ逃がす。
            string dbPathSnapshot = dbPath ?? "";
            _debugCurrentDbRecordCountPath = dbPathSnapshot;
            int requestRevision = Interlocked.Increment(ref _debugCurrentDbRecordCountRevision);

            try
            {
                string text = await BuildDebugCurrentDbRecordCountTextAsync(dbPathSnapshot)
                    .ConfigureAwait(true);

                if (!IsDebugCurrentDbRecordCountRequestCurrent(requestRevision, dbPathSnapshot))
                {
                    DebugRuntimeLog.Write(
                        "debug-ui",
                        $"debug record count skipped: target=main revision={requestRevision} path='{dbPathSnapshot}'"
                    );
                    return;
                }

                SetTextIfChanged(recordCountTextBlock, text);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug record count failed: target=main path='{dbPathSnapshot}' err='{ex.GetType().Name}: {ex.Message}'"
                );

                if (IsDebugCurrentDbRecordCountRequestCurrent(requestRevision, dbPathSnapshot))
                {
                    SetTextIfChanged(
                        recordCountTextBlock,
                        $"レコード数: 取得失敗 ({ex.Message})"
                    );
                }
            }
        }

        private async void RefreshDebugCurrentQueueDbRecordCount(string queueDbPath, bool force)
        {
            TextBlock recordCountTextBlock = DebugTabViewHost?.CurrentQueueDbRecordCountTextBlock;
            if (recordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentQueueDbRecordCountPath,
                    queueDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            // UI側では要求の同一性だけを固定し、実ファイル確認とSQLite readは背景側へ逃がす。
            string queueDbPathSnapshot = queueDbPath ?? "";
            _debugCurrentQueueDbRecordCountPath = queueDbPathSnapshot;
            int requestRevision = Interlocked.Increment(ref _debugCurrentQueueDbRecordCountRevision);

            try
            {
                string text = await BuildDebugCurrentQueueDbRecordCountTextAsync(queueDbPathSnapshot)
                    .ConfigureAwait(true);

                if (!IsDebugCurrentQueueDbRecordCountRequestCurrent(requestRevision, queueDbPathSnapshot))
                {
                    DebugRuntimeLog.Write(
                        "debug-ui",
                        $"debug record count skipped: target=queue revision={requestRevision} path='{queueDbPathSnapshot}'"
                    );
                    return;
                }

                SetTextIfChanged(recordCountTextBlock, text);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug record count failed: target=queue path='{queueDbPathSnapshot}' err='{ex.GetType().Name}: {ex.Message}'"
                );

                if (IsDebugCurrentQueueDbRecordCountRequestCurrent(requestRevision, queueDbPathSnapshot))
                {
                    SetTextIfChanged(
                        recordCountTextBlock,
                        $"レコード数: 取得失敗 ({ex.Message})"
                    );
                }
            }
        }

        private async void RefreshDebugCurrentFailureDbRecordCount(string failureDbPath, bool force)
        {
            TextBlock recordCountTextBlock = DebugTabViewHost?.CurrentFailureDbRecordCountTextBlock;
            if (recordCountTextBlock == null)
            {
                return;
            }

            if (
                !force
                && string.Equals(
                    _debugCurrentFailureDbRecordCountPath,
                    failureDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            // UI側では要求の同一性だけを固定し、実ファイル確認とSQLite readは背景側へ逃がす。
            string failureDbPathSnapshot = failureDbPath ?? "";
            _debugCurrentFailureDbRecordCountPath = failureDbPathSnapshot;
            int requestRevision = Interlocked.Increment(ref _debugCurrentFailureDbRecordCountRevision);

            try
            {
                string text = await BuildDebugCurrentFailureDbRecordCountTextAsync(failureDbPathSnapshot)
                    .ConfigureAwait(true);

                if (!IsDebugCurrentFailureDbRecordCountRequestCurrent(requestRevision, failureDbPathSnapshot))
                {
                    DebugRuntimeLog.Write(
                        "debug-ui",
                        $"debug record count skipped: target=failure revision={requestRevision} path='{failureDbPathSnapshot}'"
                    );
                    return;
                }

                SetTextIfChanged(recordCountTextBlock, text);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug record count failed: target=failure path='{failureDbPathSnapshot}' err='{ex.GetType().Name}: {ex.Message}'"
                );

                if (IsDebugCurrentFailureDbRecordCountRequestCurrent(requestRevision, failureDbPathSnapshot))
                {
                    SetTextIfChanged(
                        recordCountTextBlock,
                        $"レコード数: 取得失敗 ({ex.Message})"
                    );
                }
            }
        }

        private static Task<string> BuildDebugCurrentDbRecordCountTextAsync(string dbPath)
        {
            return Task.Run(() => BuildDebugCurrentDbRecordCountText(dbPath));
        }

        private static Task<string> BuildDebugCurrentQueueDbRecordCountTextAsync(string queueDbPath)
        {
            return Task.Run(() => BuildDebugCurrentQueueDbRecordCountText(queueDbPath));
        }

        private static Task<string> BuildDebugCurrentFailureDbRecordCountTextAsync(string failureDbPath)
        {
            return Task.Run(() => BuildDebugCurrentFailureDbRecordCountText(failureDbPath));
        }

        private bool IsDebugCurrentDbRecordCountRequestCurrent(int requestRevision, string dbPath)
        {
            return IsDebugRecordCountUiAvailable()
                && requestRevision == Volatile.Read(ref _debugCurrentDbRecordCountRevision)
                && string.Equals(
                    _debugCurrentDbRecordCountPath,
                    dbPath ?? "",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private bool IsDebugCurrentQueueDbRecordCountRequestCurrent(
            int requestRevision,
            string queueDbPath
        )
        {
            return IsDebugRecordCountUiAvailable()
                && requestRevision == Volatile.Read(ref _debugCurrentQueueDbRecordCountRevision)
                && string.Equals(
                    _debugCurrentQueueDbRecordCountPath,
                    queueDbPath ?? "",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private bool IsDebugCurrentFailureDbRecordCountRequestCurrent(
            int requestRevision,
            string failureDbPath
        )
        {
            return IsDebugRecordCountUiAvailable()
                && requestRevision == Volatile.Read(ref _debugCurrentFailureDbRecordCountRevision)
                && string.Equals(
                    _debugCurrentFailureDbRecordCountPath,
                    failureDbPath ?? "",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private bool IsDebugRecordCountUiAvailable()
        {
            return IsDebugTabActive()
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished;
        }

        private static string BuildDebugCurrentDbRecordCountText(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return "レコード数: DB未選択";
            }

            if (!File.Exists(dbPath))
            {
                return "レコード数: DBなし";
            }

            try
            {
                using SQLiteConnection connection = CreateReadOnlyConnection(dbPath);
                connection.Open();

                int movieCount = ReadDebugTableCount(connection, "movie");
                int bookmarkCount = ReadDebugTableCount(connection, "bookmark");
                int historyCount = ReadDebugTableCount(connection, "history");
                int findFactCount = ReadDebugTableCount(connection, "findfact");
                int watchCount = ReadDebugTableCount(connection, "watch");

                return
                    $"レコード数 movie={movieCount} / bookmark={bookmarkCount} / history={historyCount} / findfact={findFactCount} / watch={watchCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private static string BuildDebugCurrentQueueDbRecordCountText(string queueDbPath)
        {
            if (string.IsNullOrWhiteSpace(queueDbPath))
            {
                return "レコード数: QueueDB未解決";
            }

            if (!File.Exists(queueDbPath))
            {
                return "レコード数: QueueDBなし";
            }

            try
            {
                SQLiteConnectionStringBuilder builder = new()
                {
                    DataSource = queueDbPath,
                    ReadOnly = true,
                };
                using SQLiteConnection connection = new(builder.ConnectionString);
                connection.Open();

                int queueCount = ReadDebugTableCount(connection, "ThumbnailQueue");
                return $"レコード数 ThumbnailQueue={queueCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private static string BuildDebugCurrentFailureDbRecordCountText(string failureDbPath)
        {
            if (string.IsNullOrWhiteSpace(failureDbPath))
            {
                return "レコード数: FailureDB未解決";
            }

            if (!File.Exists(failureDbPath))
            {
                return "レコード数: FailureDBなし";
            }

            try
            {
                SQLiteConnectionStringBuilder builder = new()
                {
                    DataSource = failureDbPath,
                    ReadOnly = true,
                };
                using SQLiteConnection connection = new(builder.ConnectionString);
                connection.Open();

                int failureCount = ReadDebugTableCount(connection, "ThumbnailFailure");
                return $"レコード数 ThumbnailFailure={failureCount}";
            }
            catch (Exception ex)
            {
                return $"レコード数: 取得失敗 ({ex.Message})";
            }
        }

        private string ResolveCurrentFailureDbPathForDebug()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbPath))
            {
                return "";
            }

            return ThumbnailFailureDbPathResolver.ResolveFailureDbPath(mainDbPath);
        }

        private static int ReadDebugTableCount(SQLiteConnection connection, string tableName)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(1) FROM [{tableName}]";
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static void SetTextIfChanged(TextBox textBox, string nextText)
        {
            if (textBox == null)
            {
                return;
            }

            string safeText = nextText ?? "";
            if (string.Equals(textBox.Text, safeText, StringComparison.Ordinal))
            {
                return;
            }

            textBox.Text = safeText;
        }

        private static void SetTextIfChanged(TextBlock textBlock, string nextText)
        {
            if (textBlock == null)
            {
                return;
            }

            string safeText = nextText ?? "";
            if (string.Equals(textBlock.Text, safeText, StringComparison.Ordinal))
            {
                return;
            }

            textBlock.Text = safeText;
        }

        private static string FormatDebugPath(string path, string emptyMessage)
        {
            return string.IsNullOrWhiteSpace(path) ? emptyMessage : path;
        }

        // 現在DBがあれば対応QueueDBを返し、未選択時は最後に使っていたQueueDBを見せる。
        private string ResolveCurrentQueueDbPathForDebug()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (!string.IsNullOrWhiteSpace(mainDbPath))
            {
                return QueueDbPathResolver.ResolveQueueDbPath(mainDbPath);
            }

            return currentQueueDbService?.QueueDbFullPath ?? "";
        }

        // 個別設定が無い時は既定のThumbルートを採用する。
        private string ResolveCurrentThumbnailRootForDebug()
        {
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return "";
            }

            string thumbRoot = MainVM?.DbInfo?.ThumbFolder ?? "";
            return string.IsNullOrWhiteSpace(thumbRoot)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbRoot;
        }

        private void DebugOpenAppDataDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(AppLocalDataPaths.RootPath, preferSelectFile: false);
        }

        private void DebugOpenCurrentDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(Environment.CurrentDirectory, preferSelectFile: false);
        }

        private void DebugOpenThumbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentThumbnailRootForDebug(), preferSelectFile: false);
        }

        private void DebugOpenDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(MainVM?.DbInfo?.DBFullPath ?? "", preferSelectFile: true);
        }

        private void DebugOpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(AppLocalDataPaths.LogsPath, preferSelectFile: false);
        }

        private void DebugOpenCurrentDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(MainVM?.DbInfo?.DBFullPath ?? "", preferSelectFile: true);
        }

        private void DebugOpenQueueDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentQueueDbPathForDebug(), preferSelectFile: true);
        }

        private void DebugOpenFailureDbDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentFailureDbPathForDebug(), preferSelectFile: true);
        }

        private void DebugOpenThumbnailDir_Click(object sender, RoutedEventArgs e)
        {
            OpenDebugPathInExplorer(ResolveCurrentThumbnailRootForDebug(), preferSelectFile: false);
        }

        private void DebugRefreshCurrentDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentDbRecordCount(MainVM?.DbInfo?.DBFullPath ?? "", force: true);
        }

        private void DebugClearCurrentDbRecords_Click(object sender, RoutedEventArgs e)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                ShowDebugPathMissingMessage("現在DBが選択されていません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在DBのレコードをクリア",
                    "movie / bookmark / history / findfact / watch を空にします。"
                )
            )
            {
                return;
            }

            ClearMainDataRecords(dbPath);

            QueueDbService queueDbService = ResolveDebugQueueDbService();
            if (queueDbService != null)
            {
                int queueDeleted = queueDbService.ClearAll();
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug clear queue db after main clear: deleted={queueDeleted} path='{queueDbService.QueueDbFullPath}'"
                );
            }

            ClearThumbnailQueue();
            OpenDatafile(dbPath);
            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private void DebugDeleteCurrentDb_Click(object sender, RoutedEventArgs e)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                ShowDebugPathMissingMessage("現在DBが選択されていません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在DBを削除",
                    "現在開いているMainDBファイルを削除し、画面のDB選択も外します。"
                )
            )
            {
                return;
            }

            ShutdownCurrentDb();

            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }

                if (string.Equals(Properties.Settings.Default.LastDoc, dbPath, StringComparison.OrdinalIgnoreCase))
                {
                    Properties.Settings.Default.LastDoc = "";
                    Properties.Settings.Default.Save();
                }

                ResetDebugCurrentDbUiState();
                DebugRuntimeLog.Write("debug-ui", $"debug delete main db: path='{dbPath}'");
            }
            catch (Exception ex)
            {
                if (File.Exists(dbPath))
                {
                    OpenDatafile(dbPath);
                }

                MessageBox.Show(
                    this,
                    $"DB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private void DebugRefreshQueueDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentQueueDbRecordCount(ResolveCurrentQueueDbPathForDebug(), force: true);
        }

        private void DebugRefreshFailureDbRecordCount_Click(object sender, RoutedEventArgs e)
        {
            RefreshDebugCurrentFailureDbRecordCount(
                ResolveCurrentFailureDbPathForDebug(),
                force: true
            );
        }

        private void DebugClearFailureDbRecords_Click(object sender, RoutedEventArgs e)
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbPath))
            {
                ShowDebugPathMissingMessage("現在FailureDBの元DBが選択されていません。");
                return;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                ShowDebugPathMissingMessage("現在FailureDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在FailureDBのレコードをクリア",
                    "ThumbnailFailure テーブルのレコードをすべて削除します。"
                )
            )
            {
                return;
            }

            try
            {
                int deleted = failureDbService.ClearMainFailureRecords();
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug clear failure db: deleted={deleted} path='{failureDbService.FailureDbFullPath}'"
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"FailureDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private void DebugDeleteFailureDb_Click(object sender, RoutedEventArgs e)
        {
            string failureDbPath = ResolveCurrentFailureDbPathForDebug();
            if (string.IsNullOrWhiteSpace(failureDbPath))
            {
                ShowDebugPathMissingMessage("現在FailureDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在FailureDBを削除",
                    "現在FailureDBファイルを削除します。必要になれば再作成されます。"
                )
            )
            {
                return;
            }

            try
            {
                if (File.Exists(failureDbPath))
                {
                    File.Delete(failureDbPath);
                }

                DebugRuntimeLog.Write("debug-ui", $"debug delete failure db: path='{failureDbPath}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"FailureDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private void DebugClearQueueDbRecords_Click(object sender, RoutedEventArgs e)
        {
            QueueDbService queueDbService = ResolveDebugQueueDbService();
            if (queueDbService == null)
            {
                ShowDebugPathMissingMessage("現在QueueDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在QueueDBのレコードをクリア",
                    "ThumbnailQueue テーブルのレコードをすべて削除します。"
                )
            )
            {
                return;
            }

            int deleted = queueDbService.ClearAll();
            ClearThumbnailQueue();
            DebugRuntimeLog.Write(
                "debug-ui",
                $"debug clear queue db: deleted={deleted} path='{queueDbService.QueueDbFullPath}'"
            );
            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private void DebugDeleteQueueDb_Click(object sender, RoutedEventArgs e)
        {
            string queueDbPath = ResolveCurrentQueueDbPathForDebug();
            if (string.IsNullOrWhiteSpace(queueDbPath))
            {
                ShowDebugPathMissingMessage("現在QueueDBが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在QueueDBを削除",
                    "現在QueueDBファイルを削除します。必要になれば再作成されます。"
                )
            )
            {
                return;
            }

            try
            {
                ClearThumbnailQueue();
                if (File.Exists(queueDbPath))
                {
                    File.Delete(queueDbPath);
                }

                DebugRuntimeLog.Write("debug-ui", $"debug delete queue db: path='{queueDbPath}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"QueueDB削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshDebugRecordCounts(force: true);
            RefreshLogTabPreview(force: true);
        }

        private async void DebugDeleteThumbnailDir_Click(object sender, RoutedEventArgs e)
        {
            string thumbnailRoot = ResolveCurrentThumbnailRootForDebug();
            if (string.IsNullOrWhiteSpace(thumbnailRoot))
            {
                ShowDebugPathMissingMessage("現在サムネイルパスが特定できません。");
                return;
            }

            if (
                !ConfirmDebugAction(
                    "現在サムネイルを削除",
                    "現在サムネイルフォルダ配下を再帰的に削除します。"
                )
            )
            {
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    // フォルダ存在確認と再帰削除は重いので、UIへ戻す前に背景側で完結させる。
                    if (Directory.Exists(thumbnailRoot))
                    {
                        Directory.Delete(thumbnailRoot, true);
                    }
                });

                await RefreshLoadedThumbnailUiAfterDebugDeleteAsync();

                DebugRuntimeLog.Write("debug-ui", $"debug delete thumbnail dir: path='{thumbnailRoot}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"サムネイル削除に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            RefreshLogTabPreview(force: true);
        }

        // Debugの全サムネ削除はDBを変えないため、読み直しではなく表示中モデルだけを軽く無効化する。
        private async Task RefreshLoadedThumbnailUiAfterDebugDeleteAsync()
        {
            if (MainVM?.MovieRecs == null || MainVM.MovieRecs.Count < 1)
            {
                RequestThumbnailErrorSnapshotRefresh();
                RequestThumbnailProgressSnapshotRefresh();
                return;
            }

            int changedCount = 0;
            foreach (MovieRecords record in MainVM.MovieRecs)
            {
                if (ClearThumbnailPathsForThumbnailOnlyDelete(record))
                {
                    changedCount++;
                }
            }

            if (changedCount > 0)
            {
                InvalidateThumbnailErrorRecords(refreshIfVisible: true);
                RequestUpperTabVisibleRangeRefresh(immediate: true, reason: "debug-thumbnail-delete");
                RefreshUpperTabPreferredMoviePathKeysRevision();
            }

            RequestThumbnailErrorSnapshotRefresh();
            RequestThumbnailProgressSnapshotRefresh();

            if (string.Equals(MainVM?.DbInfo?.Sort, "28", StringComparison.Ordinal))
            {
                // サムネERROR順だけは件数がsort keyなので、DB再読込なしで現在一覧を並べ直す。
                await SortDataAsync(MainVM.DbInfo.Sort);
            }
        }

        private void DebugRecreateAllThumbnails_Click(object sender, RoutedEventArgs e)
        {
            if (QueueRecreateAllThumbnailsFromCurrentTab(closeMenu: false))
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug recreate all thumbnails queued: tab={GetCurrentUpperTabFixedIndex()}"
                );
                RefreshLogTabPreview(force: true);
            }
        }

        // 現在DBが無くても、直前に握っていたQueueDbServiceがあればそれを使う。
        private QueueDbService ResolveDebugQueueDbService()
        {
            string mainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (!string.IsNullOrWhiteSpace(mainDbPath))
            {
                return ResolveCurrentQueueDbService();
            }

            return currentQueueDbService;
        }

        // DB削除後に、UI側の現在DB状態だけを空へ戻す。
        private void ResetDebugCurrentDbUiState()
        {
            Tabs.SelectedIndex = -1;
            SearchBox.Text = "";
            HideExtensionDetail();

            movieData?.Clear();
            bookmarkData?.Clear();
            historyData?.Clear();
            watchData?.Clear();
            systemData?.Clear();

            MainVM.MovieRecs.Clear();
            MainVM.ReplaceFilteredMovieRecs([]);
            MainVM.PendingMovieRecs.Clear();
            MainVM.BookmarkRecs.Clear();
            MainVM.HistoryRecs.Clear();

            MainVM.DbInfo.DBFullPath = "";
            MainVM.DbInfo.DBName = "";
            MainVM.DbInfo.Skin = "";
            MainVM.DbInfo.Sort = "";
            MainVM.DbInfo.ThumbFolder = "";
            MainVM.DbInfo.BookmarkFolder = "";
            MainVM.DbInfo.SearchKeyword = "";
            ResetMainHeaderCounts();
            MainVM.DbInfo.CurrentTabIndex = -1;
            _debugCurrentDbRecordCountPath = "";
            _debugCurrentQueueDbRecordCountPath = "";
            _debugCurrentFailureDbRecordCountPath = "";
        }

        private async void OpenDebugPathInExplorer(string path, bool preferSelectFile)
        {
            string pathSnapshot = path?.Trim() ?? "";
            bool preferSelectFileSnapshot = preferSelectFile;
            if (string.IsNullOrWhiteSpace(pathSnapshot))
            {
                ShowDebugPathMissingMessage("対象パスがありません。");
                return;
            }

            int requestRevision = Interlocked.Increment(ref _debugExplorerOpenRequestRevision);
            try
            {
                DebugExplorerOpenPlan plan = await Task.Run(() =>
                        ResolveDebugExplorerOpenPlan(pathSnapshot, preferSelectFileSnapshot)
                    )
                    .ConfigureAwait(true);

                if (!IsDebugExplorerOpenRequestCurrent(requestRevision))
                {
                    DebugRuntimeLog.Write(
                        "debug-ui",
                        $"debug explorer open skipped: stale_or_shutdown revision={requestRevision} path='{pathSnapshot}'"
                    );
                    return;
                }

                if (plan.HasExplorerArguments)
                {
                    Process.Start("explorer.exe", plan.ExplorerArguments);
                    return;
                }

                ShowDebugPathMissingMessage(plan.MissingMessage);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "debug-ui",
                    $"debug explorer open failed: path='{pathSnapshot}' err='{ex.GetType().Name}: {ex.Message}'"
                );

                if (!IsDebugExplorerOpenRequestCurrent(requestRevision))
                {
                    return;
                }

                MessageBox.Show(
                    this,
                    $"Explorer起動に失敗しました。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private static DebugExplorerOpenPlan ResolveDebugExplorerOpenPlan(
            string path,
            bool preferSelectFile
        )
        {
            // Explorerへ渡す形の判断だけを背景側で済ませ、UIスレッドには起動結果だけを戻す。
            if (preferSelectFile && File.Exists(path))
            {
                return DebugExplorerOpenPlan.Open($"/select,\"{path}\"");
            }

            if (Directory.Exists(path))
            {
                return DebugExplorerOpenPlan.Open($"\"{path}\"");
            }

            string parentDir = Path.GetDirectoryName(path) ?? "";
            if (Directory.Exists(parentDir))
            {
                return preferSelectFile
                    ? DebugExplorerOpenPlan.Open($"/select,\"{path}\"")
                    : DebugExplorerOpenPlan.Open($"\"{parentDir}\"");
            }

            return DebugExplorerOpenPlan.Missing($"パスが存在しません。\n{path}");
        }

        private bool IsDebugExplorerOpenRequestCurrent(int requestRevision)
        {
            return requestRevision == Volatile.Read(ref _debugExplorerOpenRequestRevision)
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished;
        }

        private readonly struct DebugExplorerOpenPlan
        {
            private DebugExplorerOpenPlan(string explorerArguments, string missingMessage)
            {
                ExplorerArguments = explorerArguments;
                MissingMessage = missingMessage;
            }

            public string ExplorerArguments { get; }

            public string MissingMessage { get; }

            public bool HasExplorerArguments => !string.IsNullOrWhiteSpace(ExplorerArguments);

            public static DebugExplorerOpenPlan Open(string explorerArguments)
            {
                return new DebugExplorerOpenPlan(explorerArguments, "");
            }

            public static DebugExplorerOpenPlan Missing(string missingMessage)
            {
                return new DebugExplorerOpenPlan("", missingMessage);
            }
        }

        private bool ConfirmDebugAction(string title, string message)
        {
            return MessageBox.Show(
                    this,
                    $"{message}\n\n続行しますか？",
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                ) == MessageBoxResult.Yes;
        }

        private void ShowDebugPathMissingMessage(string message)
        {
            MessageBox.Show(
                this,
                message,
                Assembly.GetExecutingAssembly().GetName().Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }
}

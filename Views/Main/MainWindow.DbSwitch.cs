using System.Data;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using IndigoMovieManager.Thumbnail.QueueDb;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string WhiteBrowserDefaultDirectory = @"C:\WhiteBrowser";

        private readonly record struct MainDbSwitchContext(
            string CurrentDbFullPath,
            string TargetDbFullPath,
            MainDbSwitchSource Source
        );

        private sealed class MainDbSwitchPreflightResult
        {
            public bool IsValid { get; init; }
            public string SchemaError { get; init; } = "";
            public DataTable SystemData { get; init; }
        }

        private int _mainDbSwitchPreflightRevision;

        internal enum MainDbSwitchSource
        {
            New,
            OpenDialog,
            DragDrop,
            RecentMenu,
            StartupAutoOpen,
        }

        // MainDB切り替えの入口を1か所へ寄せ、保存順と成功後処理を揃える。
        private async Task<bool> TrySwitchMainDb(string dbFullPath, MainDbSwitchSource source)
        {
            using IDisposable uiHangScope = TrackUiHangActivity(
                source == MainDbSwitchSource.StartupAutoOpen
                    ? UiHangActivityKind.Startup
                    : UiHangActivityKind.Database
            );
            MainDbSwitchContext context = BuildMainDbSwitchContext(dbFullPath, source);
            if (string.IsNullOrWhiteSpace(context.TargetDbFullPath))
            {
                return false;
            }

            bool switchSucceeded = false;
            bool transitionStarted = false;
            int preflightRevision = Interlocked.Increment(ref _mainDbSwitchPreflightRevision);
            try
            {
                ShowUiHangDbSwitchStatus("DB切替: スキーマと設定を確認中");
                MainDbSwitchPreflightResult preflightResult =
                    await RunMainDbSwitchPreflightAsync(context, preflightRevision);
                if (!IsMainDbSwitchPreflightCurrent(context, preflightRevision))
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"switch preflight skipped: reason=stale revision={preflightRevision} target='{context.TargetDbFullPath}'"
                    );
                    return false;
                }

                if (!preflightResult.IsValid)
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"open canceled: schema validation failed. db='{context.TargetDbFullPath}', reason='{preflightResult.SchemaError}'"
                    );
                    MessageBox.Show(
                        this,
                        BuildMainDbValidationFailureMessage(preflightResult.SchemaError),
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                ShowUiHangDbSwitchStatus("DB切替: 切替準備中");
                RunMainDbPreSwitch(context);
                transitionStarted = true;

                ShowUiHangDbSwitchStatus("DB切替: DBを開いています");
                if (!TryActivateMainDbSession(context, preflightResult))
                {
                    return false;
                }

                // 切替成功時だけセッション印を進め、旧DB向けQueueRequestをstale化する。
                _ = AdvanceCurrentMainDbQueueRequestSessionStamp();
                ShowUiHangDbSwitchStatus("DB切替: 切替後処理を反映中");
                RunMainDbPostSwitch(context);
                switchSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"switch failed: target='{context.TargetDbFullPath}', source={context.Source}, err='{ex.GetType().Name}: {ex.Message}'"
                );
                MessageBox.Show(
                    this,
                    $"データベースを開けませんでした。\n{ex.Message}",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return false;
            }
            finally
            {
                if (transitionStarted)
                {
                    ShowUiHangDbSwitchStatus(
                        switchSucceeded
                            ? "DB切替: 最終調整を実行中"
                            : "DB切替: 失敗後処理を実行中"
                    );
                    CompleteMainDbSwitchTransition(switchSucceeded);
                }

                HideUiHangDbSwitchStatus();
            }
        }

        // 既存MainDBを開くダイアログは、直前に使ったフォルダを優先し、未保存時だけWB既定配置へ寄せる。
        private string GetMainDbDialogInitialDirectory()
        {
            return ResolveMainDbDialogInitialDirectory(
                Properties.Settings.Default.LastMainDbDialogDirectory,
                WhiteBrowserDefaultDirectory,
                AppContext.BaseDirectory
            );
        }

        // 新規MainDBは install 配下へ寄りにくくするため、未保存時は Documents 配下を既定にする。
        private string GetNewMainDbDialogInitialDirectory()
        {
            return ResolveNewMainDbDialogInitialDirectory(
                Properties.Settings.Default.LastMainDbDialogDirectory,
                BuildDefaultNewMainDbDirectoryPath(),
                WhiteBrowserDefaultDirectory,
                AppContext.BaseDirectory
            );
        }

        // ダイアログで確定したパスから親フォルダだけを抜き、次回の初期位置として覚える。
        private void RememberMainDbDialogDirectory(string selectedPath)
        {
            string resolvedDirectory = ExtractMainDbDialogDirectory(selectedPath);
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                return;
            }

            if (
                string.Equals(
                    Properties.Settings.Default.LastMainDbDialogDirectory,
                    resolvedDirectory,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            Properties.Settings.Default.LastMainDbDialogDirectory = resolvedDirectory;
            QueueApplicationSettingsSave("main-db-dialog-directory");
        }

        // 切り替え前の見た目保存とメニュー状態調整をまとめて扱う。
        private void RunMainDbPreSwitch(MainDbSwitchContext context)
        {
            if (ShouldCloseMainMenuBeforeDbSwitch(context.Source))
            {
                MenuToggleButton.IsChecked = false;
            }

            // 切替中は旧DB由来の投入を止め、成功後にだけ新セッションを再開する。
            SetThumbnailQueueInputEnabled(false);
            TryPersistCurrentDbViewStateBeforeSwitch(context);
        }

        // DB本体の切り替えはここでだけ行い、失敗時は後段へ進ませない。
        private bool TryActivateMainDbSession(
            MainDbSwitchContext context,
            MainDbSwitchPreflightResult preflightResult
        )
        {
            return OpenDatafile(context.TargetDbFullPath, preflightResult.SystemData);
        }

        // 旧DBを止める前に、重い検証と system 読込だけを背景で済ませる。
        private Task<MainDbSwitchPreflightResult> RunMainDbSwitchPreflightAsync(
            MainDbSwitchContext context,
            int revision
        )
        {
            string targetDbFullPath = context.TargetDbFullPath;
            return Task.Run(() =>
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"switch preflight start: revision={revision} target='{targetDbFullPath}' source={context.Source}"
                );

                if (!TryValidateMainDatabaseSchema(targetDbFullPath, out string schemaError))
                {
                    return new MainDbSwitchPreflightResult
                    {
                        IsValid = false,
                        SchemaError = schemaError,
                    };
                }

                DataTable loadedSystemData = _mainDbMovieReadFacade.LoadSystemTable(
                    targetDbFullPath
                );
                DebugRuntimeLog.Write(
                    "db",
                    $"switch preflight end: revision={revision} target='{targetDbFullPath}' system_rows={loadedSystemData?.Rows.Count ?? 0}"
                );

                return new MainDbSwitchPreflightResult
                {
                    IsValid = true,
                    SystemData = loadedSystemData,
                };
            });
        }

        // preflight 完了後に別切替が始まっていたら、古い検証結果は使わない。
        private bool IsMainDbSwitchPreflightCurrent(
            MainDbSwitchContext context,
            int revision
        )
        {
            if (revision != Volatile.Read(ref _mainDbSwitchPreflightRevision))
            {
                return false;
            }

            string currentDbFullPath = NormalizeMainDbPath(MainVM?.DbInfo?.DBFullPath ?? "");
            return string.Equals(
                currentDbFullPath,
                context.CurrentDbFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // open成功後のRecent/LastDoc更新をここへ集約する。
        private void RunMainDbPostSwitch(MainDbSwitchContext context)
        {
            TryDiscardPreviousDbPendingThumbnailQueueItems(context);

            if (ShouldUpdateRecentFilesOnSuccessfulDbSwitch(context.Source))
            {
                ReStackRecentTree(context.TargetDbFullPath);
            }

            if (ShouldRememberLastDocOnSuccessfulDbSwitch(context.Source))
            {
                Properties.Settings.Default.LastDoc = context.TargetDbFullPath;
                QueueApplicationSettingsSave("main-db-last-doc");
            }

            // DBが変わったら、poll用の監視フォルダsnapshotも次回だけ組み直す。
            InvalidateEverythingWatchPollWatchFolderSnapshot();

            // Debugタブ表示中は、DB切替後のパス表示だけすぐ追従させる。
            UpdateDebugTabRefreshState(forceRefresh: true);
        }

        // 別DBへ切り替えた後は、旧QueueDBに積みっぱなしだった未着手pendingを掃除する。
        private void TryDiscardPreviousDbPendingThumbnailQueueItems(MainDbSwitchContext context)
        {
            if (
                !ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
                    context.CurrentDbFullPath,
                    context.TargetDbFullPath
                )
            )
            {
                return;
            }

            // 切替完了の体感待ちを減らすため、旧DB掃除はUI外で安全に後追いする。
            _ = Task.Run(
                () =>
                    DiscardPreviousDbPendingThumbnailQueueItemsInBackground(
                        context.CurrentDbFullPath,
                        context.TargetDbFullPath
                    )
            );
        }

        private static void DiscardPreviousDbPendingThumbnailQueueItemsInBackground(
            string currentDbFullPath,
            string targetDbFullPath
        )
        {
            string oldQueueDbPath = QueueDbPathResolver.ResolveQueueDbPath(currentDbFullPath);
            if (!Path.Exists(oldQueueDbPath))
            {
                return;
            }

            try
            {
                QueueDbService queueDbService = new(currentDbFullPath);
                int deleted = queueDbService.DeletePending();
                DebugRuntimeLog.Write(
                    "queue-ops",
                    $"switch pending discard: deleted={deleted} current='{currentDbFullPath}' target='{targetDbFullPath}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "queue-ops",
                    $"switch pending discard failed: current='{currentDbFullPath}' target='{targetDbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 切替成否に関わらず最後に投入を戻す。失敗時は旧セッションをそのまま継続する。
        private void CompleteMainDbSwitchTransition(bool switchSucceeded)
        {
            if (switchSucceeded && IsStartupFeedPartialActive)
            {
                DebugRuntimeLog.Write(
                    "queue",
                    "input enable deferred: startup feed partial active."
                );
                return;
            }

            SetThumbnailQueueInputEnabled(true);
            if (!switchSucceeded)
            {
                RequestThumbnailProgressSnapshotRefresh();
            }
        }

        // UI起点の切り替えだけ、旧DBの見た目状態を切り替え前に保存する。
        private void TryPersistCurrentDbViewStateBeforeSwitch(MainDbSwitchContext context)
        {
            if (
                !ShouldPersistCurrentDbViewStateBeforeSwitch(
                    context.CurrentDbFullPath,
                    context.TargetDbFullPath,
                    context.Source
                )
            )
            {
                return;
            }

            try
            {
                UpdateSkin(context.CurrentDbFullPath);
                UpdateSort(context.CurrentDbFullPath);
                DebugRuntimeLog.Write(
                    "db",
                    $"pre-switch view state saved: current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}' source={context.Source}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"pre-switch view state save failed: current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}' source={context.Source} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private MainDbSwitchContext BuildMainDbSwitchContext(
            string targetDbFullPath,
            MainDbSwitchSource source
        )
        {
            return new MainDbSwitchContext(
                NormalizeMainDbPath(MainVM?.DbInfo?.DBFullPath ?? ""),
                NormalizeMainDbPath(targetDbFullPath),
                source
            );
        }

        internal static bool ShouldPersistCurrentDbViewStateBeforeSwitch(
            string currentDbFullPath,
            string targetDbFullPath,
            MainDbSwitchSource source
        )
        {
            if (
                source != MainDbSwitchSource.New
                && source != MainDbSwitchSource.OpenDialog
                && source != MainDbSwitchSource.DragDrop
                && source != MainDbSwitchSource.RecentMenu
            )
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentDbFullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDbFullPath))
            {
                return false;
            }

            return !AreSameMainDbPath(currentDbFullPath, targetDbFullPath);
        }

        internal static bool ShouldUpdateRecentFilesOnSuccessfulDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldRememberLastDocOnSuccessfulDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldCloseMainMenuBeforeDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
            string currentDbFullPath,
            string targetDbFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(currentDbFullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDbFullPath))
            {
                return false;
            }

            return !AreSameMainDbPath(currentDbFullPath, targetDbFullPath);
        }

        internal static string ResolveMainDbDialogInitialDirectory(
            string savedDirectory,
            string whiteBrowserDirectory,
            string appBaseDirectory
        )
        {
            string saved = NormalizeExistingDirectory(savedDirectory);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }

            string whiteBrowser = NormalizeExistingDirectory(whiteBrowserDirectory);
            if (!string.IsNullOrWhiteSpace(whiteBrowser))
            {
                return whiteBrowser;
            }

            string appBase = NormalizeExistingDirectory(appBaseDirectory);
            if (!string.IsNullOrWhiteSpace(appBase))
            {
                return appBase;
            }

            return AppContext.BaseDirectory;
        }

        internal static string ResolveNewMainDbDialogInitialDirectory(
            string savedDirectory,
            string defaultDocumentsDirectory,
            string whiteBrowserDirectory,
            string appBaseDirectory
        )
        {
            string saved = NormalizeExistingDirectory(savedDirectory);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }

            string documents = EnsureDirectoryExists(defaultDocumentsDirectory);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                return documents;
            }

            return ResolveMainDbDialogInitialDirectory(
                "",
                whiteBrowserDirectory,
                appBaseDirectory
            );
        }

        internal static string ExtractMainDbDialogDirectory(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return "";
            }

            try
            {
                string normalizedPath = Path.GetFullPath(selectedPath.Trim().Trim('"'));
                if (Directory.Exists(normalizedPath))
                {
                    return normalizedPath;
                }

                string parentDirectory = Path.GetDirectoryName(normalizedPath);
                return NormalizeExistingDirectory(parentDirectory);
            }
            catch
            {
                return "";
            }
        }

        // パス表記の揺れを吸収し、同じMainDBかどうかを安全側で判定する。
        internal static bool AreSameMainDbPath(string left, string right)
        {
            string normalizedLeft = NormalizeMainDbPath(left);
            string normalizedRight = NormalizeMainDbPath(right);
            if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            {
                return false;
            }

            return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string NormalizeMainDbPath(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return "";
            }

            string normalized = dbFullPath.Trim().Trim('"');
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // 不正な文字を含む場合は元文字列比較へ落とす。
            }

            return normalized.Replace('/', '\\');
        }

        private static string NormalizeExistingDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            try
            {
                string normalized = Path.GetFullPath(directoryPath.Trim().Trim('"'));
                if (!Directory.Exists(normalized))
                {
                    return "";
                }

                return normalized.Replace('/', '\\');
            }
            catch
            {
                return "";
            }
        }

        private static string EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return "";
            }

            try
            {
                string normalized = Path.GetFullPath(directoryPath.Trim().Trim('"'));
                Directory.CreateDirectory(normalized);
                return normalized.Replace('/', '\\');
            }
            catch
            {
                return "";
            }
        }

        private static string BuildDefaultNewMainDbDirectoryPath()
        {
            string documentsDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments
            );
            if (string.IsNullOrWhiteSpace(documentsDirectory))
            {
                return "";
            }

            return Path.Combine(documentsDirectory, "IndigoMovieManager");
        }

        internal static bool IsMainDbSchemaMismatchError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            return errorMessage.Contains("必須テーブル", StringComparison.Ordinal)
                || errorMessage.Contains("必須列", StringComparison.Ordinal);
        }

        internal static string BuildMainDbValidationFailureMessage(string errorMessage)
        {
            string detail = errorMessage ?? "";
            if (IsMainDbSchemaMismatchError(detail))
            {
                return $"メインDBのスキーマ不一致を検知したため、開く処理を中止しました。\n\n{detail}";
            }

            return $"データベースを開けませんでした。\n{detail}";
        }
    }
}

using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.ViewModels;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// メインヘッダーの検索件数・登録総数を一括リセットし、前DBの残像表示を防ぐ。
        /// DB切替（ShutdownCurrentDb）時に呼ばれる初期化メソッド。
        /// </summary>
        private void ResetMainHeaderCounts()
        {
            MainVM.DbInfo.SearchCount = 0;
            MainVM.DbInfo.RegisteredMovieCount = 0;
            _registeredMovieCountInitialized = false;
            Interlocked.Increment(ref _registeredMovieCountRevision);
        }

        /// <summary>
        /// 登録動画の総件数をバックグラウンドで取得し、完了後にUIへ反映する。
        /// first-page 表示を止めずに正確値を後追いで確定する。
        /// </summary>
        private void QueueRegisteredMovieCountRefresh(string dbFullPath)
        {
            string targetDbPath = dbFullPath ?? "";
            int revision = Interlocked.Increment(ref _registeredMovieCountRevision);
            _registeredMovieCountInitialized = false;

            if (string.IsNullOrWhiteSpace(targetDbPath))
            {
                MainVM.DbInfo.RegisteredMovieCount = 0;
                return;
            }

            _ = RefreshRegisteredMovieCountAsync(targetDbPath, revision);
        }

        // 初回の正確値取得後は差分だけ加減算し、未確定なら正確値再取得へ逃がす。
        private void TryAdjustRegisteredMovieCount(string dbFullPath, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            void apply()
            {
                if (
                    !string.Equals(
                        MainVM?.DbInfo?.DBFullPath,
                        dbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }

                if (!_registeredMovieCountInitialized)
                {
                    QueueRegisteredMovieCountRefresh(dbFullPath);
                    return;
                }

                if (MainVM?.DbInfo == null)
                {
                    return;
                }

                MainVM.DbInfo.RegisteredMovieCount = Math.Max(0, MainVM.DbInfo.RegisteredMovieCount + delta);
                Interlocked.Increment(ref _registeredMovieCountRevision);
            }

            if (Dispatcher == null)
            {
                apply();
                return;
            }

            _ = Dispatcher.InvokeAsync(apply, DispatcherPriority.Background);
        }

        // バックグラウンドで数えた結果は、現在選択中のDBに対する最新値だけ反映する。
        private async Task RefreshRegisteredMovieCountAsync(string dbFullPath, int revision)
        {
            try
            {
                int registeredMovieCount = await Task.Run(
                    () => _mainDbMovieReadFacade.ReadRegisteredMovieCount(dbFullPath)
                );
                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        bool isCurrentDb = string.Equals(
                            MainVM?.DbInfo?.DBFullPath,
                            dbFullPath,
                            StringComparison.OrdinalIgnoreCase
                        );
                        if (
                            !RegisteredMovieCountRefreshPolicy.ShouldApplyRefreshResult(
                                revision,
                                Volatile.Read(ref _registeredMovieCountRevision),
                                isCurrentDb
                            )
                        )
                        {
                            return;
                        }

                        MainVM.DbInfo.RegisteredMovieCount = registeredMovieCount;
                        _registeredMovieCountInitialized = true;
                    },
                    DispatcherPriority.Background
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"registered count refresh failed: db='{dbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        /// <summary>
        /// データベースを開き、画面表示から履歴、監視モードまでを現在DBの状態へ切り替える。
        /// 内部は「旧DBの完全シャットダウン」→「新DBの起動」の2フェーズ構成で安全に切り替える。
        /// </summary>
        private bool OpenDatafile(string dbFullPath, DataTable preflightSystemData = null)
        {
            Stopwatch sw = Stopwatch.StartNew();
            DebugRuntimeLog.TaskStart(nameof(OpenDatafile), $"db='{dbFullPath}'");
            bool isOpened = false;
            string previousMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";

            try
            {
                if (preflightSystemData == null)
                {
                    // 互換直呼びだけは安全側へ倒し、旧DB停止前に従来同等の同期検証を通す。
                    ShowUiHangDbSwitchStatus("DB切替: スキーマを確認中");
                    DebugRuntimeLog.Write(
                        "db",
                        $"open fallback preflight: synchronous schema validation. db='{dbFullPath}'"
                    );
                    if (!TryValidateMainDatabaseSchema(dbFullPath, out string schemaError))
                    {
                        DebugRuntimeLog.Write(
                            "db",
                            $"open canceled: schema validation failed. db='{dbFullPath}', reason='{schemaError}'"
                        );
                        MessageBox.Show(
                            this,
                            BuildMainDbValidationFailureMessage(schemaError),
                            Assembly.GetExecutingAssembly().GetName().Name,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                        return false;
                    }
                }

                ShowUiHangDbSwitchStatus("DB切替: rescue worker を停止中");
                StopThumbnailRescueWorkersForDbSwitch(previousMainDbFullPath, dbFullPath);

                // === Phase 1: 旧DBの完全シャットダウン ===
                ShowUiHangDbSwitchStatus("DB切替: 旧DBを停止中");
                ShutdownCurrentDb();

                // === Phase 2: 新DBの起動 ===
                ShowUiHangDbSwitchStatus("DB切替: 新DBを起動中");
                BootNewDb(dbFullPath, preflightSystemData);
                isOpened = true;
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"open failed: db='{dbFullPath}', err='{ex.GetType().Name}: {ex.Message}'"
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
                sw.Stop();
                DebugRuntimeLog.TaskEnd(
                    nameof(OpenDatafile),
                    $"db='{dbFullPath}' opened={isOpened} elapsed_ms={sw.ElapsedMilliseconds}"
                );
            }
        }

        /// <summary>
        /// 旧DBに紐づくリソースを後始末する。Watcher停止・キュークリア・データクリアを順に実行する。
        /// </summary>
        private void ShutdownCurrentDb()
        {
            CancelDeferredWatchUiReload("shutdown-current-db");
            ResetStartupFeedState("shutdown-current-db");
            CancelKanaBackfill("shutdown-current-db");

            // タブを強制リセット（前回のタブが0だった場合の対応）
            Tabs.SelectedIndex = -1;
            MainVM.DbInfo.CurrentTabIndex = -1;

            // 旧FileSystemWatcherを全停止＆Dispose（イベントリーク防止）
            InvalidateWatcherCreation("shutdown-current-db");
            LogWatcherCreationStateForShutdown("shutdown-current-db");
            StopAndClearFileWatchers();
            ClearDeferredWatchScanStates();
            ClearDeferredWatchWorkByUiSuppression();

            // サムネイルキューのデバウンス情報をリセット
            ClearThumbnailQueue();

            // 旧DBの監視フォルダデータをクリア
            watchData?.Clear();

            // Everything通知フラグをリセット（新DBで再表示させるため）
            _hasShownEverythingModeNotice = false;
            _hasShownEverythingFallbackNotice = false;
            _hasShownFolderMonitoringNotice = false;

            // 検索キーワードをリセット
            MainVM.DbInfo.SearchKeyword = "";
            ResetMainHeaderCounts();
            movieData = null;
            filterList = [];
            MainVM.ReplaceMovieRecs([]);
            MainVM.ReplaceFilteredMovieRecs([], FilteredMovieRecsUpdateMode.Reset);
        }

        /// <summary>
        /// 新DBを読み込み、画面とWatcherの入口を新しいDB状態へ揃える。
        /// </summary>
        private void BootNewDb(string dbFullPath)
        {
            BootNewDb(dbFullPath, null);
        }

        private void BootNewDb(string dbFullPath, DataTable preflightSystemData)
        {
            using (BeginExternalSkinHostRefreshBatch("dbinfo-DBFullPath"))
            {
                MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbFullPath);
                MainVM.DbInfo.DBFullPath = dbFullPath;
                ShowUiHangDbSwitchStatus("DB切替: system 設定を読込中");
                GetSystemTable(dbFullPath, preflightSystemData);
                MainVM.ReplaceMovieRecs([]);
                MainVM.ReplaceFilteredMovieRecs([], FilteredMovieRecsUpdateMode.Reset);
                filterList = [];
                movieData = null;
                ResetMainHeaderCounts();
                QueueRegisteredMovieCountRefresh(dbFullPath);

                ShowUiHangDbSwitchStatus("DB切替: 履歴読込を予約中");
                QueueSearchHistoryReload(dbFullPath);
                ReloadSavedSearchItems();

                if (MainVM.DbInfo.Skin != null)
                {
                    ShowUiHangDbSwitchStatus("DB切替: タブ状態を復元中");
                    SwitchTab(MainVM.DbInfo.Skin);
                }
            }

            UpdateExtensionDetailVisibilityBySearchCount();
            ShowUiHangDbSwitchStatus("DB切替: 初期表示を準備中");
            BeginStartupDbOpen();
        }

        /// <summary>
        /// 全FileSystemWatcherを停止＆Disposeし、リストもクリアする。
        /// </summary>
        private static void StopAndClearFileWatchers()
        {
            foreach (var w in fileWatchers)
            {
                try
                {
                    TrySetFileWatcherEnabled(w, enabled: false, "dispose");
                    w.Dispose();
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("watch", $"watcher dispose failed: {ex.GetType().Name}");
                }
            }
            fileWatchers.Clear();
        }

        /// <summary>
        /// DB未選択の cold start でも、初回描画に必要な既定値だけは先に揃える。
        /// 実DBの値は ContentRendered 後の DB 切替で上書きする。
        /// </summary>
        private void ApplyColdStartSystemDefaults()
        {
            MainVM.DbInfo.Skin = "DefaultGrid";
            MainVM.DbInfo.Sort = "1";
            MainVM.DbInfo.ThumbFolder = "";
            MainVM.DbInfo.BookmarkFolder = "";
        }

        /// <summary>
        /// DBのsystemテーブルから、指定属性の値を取り出す。
        /// </summary>
        public string SelectSystemTable(string attr)
        {
            if (systemData != null)
            {
                DataRow[] drs = systemData.Select($"attr='{attr}'");
                if (drs.Length > 0)
                {
                    return drs[0]["value"].ToString();
                }
            }
            return "";
        }

        private void ApplyRuntimeSystemValue(string dbFullPath, string attr, string value)
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(attr)
                || !string.Equals(
                    MainVM?.DbInfo?.DBFullPath,
                    dbFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            string normalizedAttr = attr.Trim();
            string normalizedValue = value ?? "";
            UpsertSystemDataRow(normalizedAttr, normalizedValue);

            switch (normalizedAttr)
            {
                case "skin":
                    MainVM.DbInfo.Skin = string.IsNullOrWhiteSpace(normalizedValue)
                        ? "DefaultGrid"
                        : normalizedValue;
                    break;

                case "sort":
                    MainVM.DbInfo.Sort = string.IsNullOrEmpty(normalizedValue) ? "1" : normalizedValue;
                    break;

                case "thum":
                    string dbName = string.IsNullOrWhiteSpace(MainVM.DbInfo.DBName)
                        ? Path.GetFileNameWithoutExtension(dbFullPath) ?? ""
                        : MainVM.DbInfo.DBName;
                    MainVM.DbInfo.ThumbFolder = ThumbRootResolver.ResolveRuntimeThumbRoot(
                        dbFullPath,
                        dbName,
                        normalizedValue
                    );
                    break;

                case "bookmark":
                    MainVM.DbInfo.BookmarkFolder = normalizedValue;
                    break;
            }
        }

        private void UpsertSystemDataRow(string attr, string value)
        {
            if (string.IsNullOrWhiteSpace(attr))
            {
                return;
            }

            if (systemData == null)
            {
                systemData = new DataTable();
            }

            if (!systemData.Columns.Contains("attr"))
            {
                systemData.Columns.Add("attr", typeof(string));
            }

            if (!systemData.Columns.Contains("value"))
            {
                systemData.Columns.Add("value", typeof(string));
            }

            string escapedAttr = attr.Replace("'", "''");
            DataRow[] rows = systemData.Select($"attr='{escapedAttr}'");
            DataRow row = rows.Length > 0 ? rows[0] : systemData.NewRow();
            row["attr"] = attr;
            row["value"] = value ?? "";

            if (rows.Length < 1)
            {
                systemData.Rows.Add(row);
            }
        }

        // DB切替時の履歴読込は初期表示を待たせず、候補リストだけを後から差し替える。
        private void QueueSearchHistoryReload(string dbFullPath)
        {
            string dbFullPathSnapshot = dbFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPathSnapshot))
            {
                return;
            }

            // DB切替直後は first-page / input ready を先に通し、履歴候補だけを後追いで差し替える。
            string searchTextSnapshot = SearchBox?.Text ?? "";
            long reloadStamp = Interlocked.Increment(ref _searchHistoryRefreshStamp);
            _ = ReloadSearchHistoryForDbSwitchAsync(
                dbFullPathSnapshot,
                searchTextSnapshot,
                reloadStamp
            );
        }

        private async Task ReloadSearchHistoryForDbSwitchAsync(
            string dbFullPathSnapshot,
            string searchTextSnapshot,
            long reloadStamp
        )
        {
            History[] records;
            try
            {
                records = await Task.Run(() =>
                        SearchHistoryService.LoadLatestHistory(dbFullPathSnapshot)
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "search-history",
                    $"history reload failed: db='{dbFullPathSnapshot}' err='{ex.Message}'"
                );
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                await Dispatcher
                    .InvokeAsync(
                        () =>
                        {
                            if (
                                Dispatcher.HasShutdownStarted
                                || Dispatcher.HasShutdownFinished
                                || reloadStamp != _searchHistoryRefreshStamp
                                || !AreSameMainDbPath(
                                    dbFullPathSnapshot,
                                    MainVM?.DbInfo?.DBFullPath ?? ""
                                )
                            )
                            {
                                return;
                            }

                            // 読込中にユーザー入力が進んでいれば現在値を優先し、未生成時だけ起動時 snapshot に戻す。
                            ApplySearchHistoryRecords(records, SearchBox?.Text ?? searchTextSnapshot);
                        },
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
            catch (InvalidOperationException) when (
                Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            )
            {
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "search-history",
                    $"history reload apply failed: db='{dbFullPathSnapshot}' err='{ex.Message}'"
                );
            }
        }

        /// <summary>
        /// systemテーブルにあるスキン・ソート・フォルダ設定を読み込み、実行時設定へ反映する。
        /// </summary>
        private void GetSystemTable(string dbPath, DataTable preflightSystemData = null)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                // preflight 済みの system を受け取り、UI 側では表示設定の反映だけに絞る。
                if (preflightSystemData == null)
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"system fallback load: synchronous system read. db='{dbPath}'"
                    );
                }

                systemData = preflightSystemData ?? _mainDbMovieReadFacade.LoadSystemTable(dbPath);

                var skin = SelectSystemTable("skin");
                // 永続値は raw skin 名を残し、表示側だけで安全に built-in へフォールバックする。
                MainVM.DbInfo.Skin = string.IsNullOrWhiteSpace(skin) ? "DefaultGrid" : skin;

                var sort = SelectSystemTable("sort");
                MainVM.DbInfo.Sort = sort == "" ? "1" : sort;

                string dbName = string.IsNullOrWhiteSpace(MainVM.DbInfo.DBName)
                    ? Path.GetFileNameWithoutExtension(dbPath) ?? ""
                    : MainVM.DbInfo.DBName;
                string configuredThumbFolder = SelectSystemTable("thum");
                MainVM.DbInfo.ThumbFolder = ThumbRootResolver.ResolveRuntimeThumbRoot(
                    dbPath,
                    dbName,
                    configuredThumbFolder
                );

                MainVM.DbInfo.BookmarkFolder = SelectSystemTable("bookmark");
            }
            else
            {
                systemData?.Clear();
            }
        }

        /// <summary>
        /// systemテーブルのスキン名の表記ゆれ（大文字小文字・全角空白等）を正規化する。
        /// 不明な値は "DefaultGrid" へフォールバックし、起動時の迷子を防ぐ。
        /// </summary>
        private static string NormalizeSkinName(string skin)
        {
            if (string.IsNullOrWhiteSpace(skin))
            {
                return "DefaultGrid";
            }

            string compactSkin = skin.Trim().Replace(" ", "").Replace("　", "");
            if (string.Equals(compactSkin, "DefaultBig", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultBig";
            }

            if (string.Equals(compactSkin, "DefaultGrid", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultGrid";
            }

            if (string.Equals(compactSkin, "DefaultList", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultList";
            }

            if (string.Equals(compactSkin, "DefaultBig10", StringComparison.OrdinalIgnoreCase))
            {
                return "DefaultBig10";
            }

            return "DefaultGrid";
        }

        /// <summary>
        /// watch（監視）テーブルを、指定条件で読み込む。
        /// </summary>
        private void GetWatchTable(string dbPath, string sql)
        {
            watchData = GetWatchTableSnapshot(dbPath, sql);
        }

        private static DataTable GetWatchTableSnapshot(string dbPath, string sql)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                DataTable snapshot = GetData(dbPath, sql);
                WatchTableRowNormalizer.Normalize(snapshot);
                return snapshot;
            }

            return null;
        }

        /// <summary>
        /// 現在のソート条件をsystemテーブルへ保存する。
        /// </summary>
        private void UpdateSort()
        {
            UpdateSort(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private void UpdateSort(string dbFullPath)
        {
            if (
                !string.IsNullOrWhiteSpace(dbFullPath)
                && !string.IsNullOrEmpty(MainVM.DbInfo.Sort)
            )
            {
                TryPersistSystemValue(dbFullPath, "sort", MainVM.DbInfo.Sort);
            }
        }

        /// <summary>
        /// 現在表示しているタブ（スキン）を、互換性を保ちながらsystemテーブルへ保存する。
        /// </summary>
        private void UpdateSkin()
        {
            UpdateSkin(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private void UpdateSkin(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            PersistCurrentSkinState(dbFullPath);
        }

        /// <summary>
        /// 読み込んだスキン名に合わせて、表示するタブを切り替える。
        /// </summary>
        private void SwitchTab(string skin)
        {
            if (!ApplySkinByName(skin, persistToCurrentDb: false))
            {
                SelectUpperTabDefaultViewBySkinName("DefaultGrid");
            }
        }
    }
}

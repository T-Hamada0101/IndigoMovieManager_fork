using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Skin;
using IndigoMovieManager.ViewModels;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using IndigoMovieManager.UpperTabs.Common;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    /// <summary>
    /// アプリ全体の「司令塔」となるメインウィンドウの View 層。
    ///
    /// 【全体の流れでの位置づけ】
    ///   App.xaml → ★ここ★ MainWindow
    ///     → コンストラクタで常駐タスク（サムネキュー/Persister/Everythingポーリング）を配線
    ///     → ContentRendered でDB自動復元・常駐タスク開始
    ///     → Closing で全タスク停止・設定永続化・リソース解放
    ///
    /// partial class として Thumbnail・Queue・DB runtime・ReadModel 等の責務別ファイルに分割し、
    /// このファイルは constructor と不可避の Window lifecycle 配線を中心に残す。
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        //監視モード
        private enum CheckMode
        {
            Auto,
            Watch,
            Manual,
        }

        private Task _thumbCheckTask;
        private CancellationTokenSource _thumbCheckCts = new();

        [GeneratedRegex(@"^\r\n+")]
        private static partial Regex MyRegex();

        private const string RECENT_OPEN_FILE_LABEL = "最近開いたファイル";

        /// <summary>
        /// サムネイルキューを血眼で見張る待機間隔（ミリ秒）だぜ！👀
        /// </summary>
        private const int ThumbnailQueuePollIntervalMs = 3000;

        /// <summary>
        /// Everything先生に差分を尋ねるポーリング間隔（ミリ秒）！爆速の秘訣！🚀
        /// </summary>
        private const int EverythingWatchPollIntervalMs = 3000;
        private const int EverythingWatchPollIntervalBusyMs = 15000;
        private const int EverythingWatchPollIntervalMediumMs = 6000;
        private const int EverythingWatchPollIntervalCalmMs = 9000;
        private const int EverythingWatchPollBusyThreshold = 200;
        private const int EverythingWatchPollMediumThreshold = 50;
        private const int EverythingWatchPollLowUpdateThreshold = 1;
        private const int EverythingWatchPollCalmCyclesThreshold = 3;
        private const string DockLayoutFileName = "layout.xml";
        private const string DefaultDockLayoutFileName = "layout.default.xml";
        private const string ExtensionBottomTabContentId = "ToolExtension";
        private int _mainWindowClosingStarted;
        private const string BookmarkBottomTabContentId = "ToolBookmark";
        private const string SavedSearchBottomTabContentId = "ToolTagBar";
        private const string ThumbnailProgressContentId = "ToolThumbnailProgress";
        private const string TagEditorBottomTabContentId = "ToolTagEditor";
        /// <summary>
        /// QueueDBに怒涛の勢いで書き込むためのバッチ窓口（100〜300ms）！ここでまとめてドカンと流す！🔥
        /// </summary>
        private const int ThumbnailQueuePersistBatchWindowMs = 150;
        private Stack<string> recentFiles = new();

        private IEnumerable<MovieRecords> filterList = [];
        private int _filterAndSortRequestRevision;
        private readonly object _filterAndSortCancellationGate = new();
        private CancellationTokenSource _filterAndSortCancellation;
        private int _movieExistsRefreshRevision;
        private int _registeredMovieCountRevision;
        private bool _registeredMovieCountInitialized;

        /// <summary>
        /// ワーカー達が容赦なく投げ込んでくるジョブを受け止めるチャネル！Persister（単一Reader）が一人で捌き切ってDB化する最強の盾！盾🛡️
        /// </summary>
        private static readonly Channel<QueueRequest> queueRequestChannel =
            Channel.CreateUnbounded<QueueRequest>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }
            );

        private readonly IThumbnailCreationService _thumbnailCreationService =
            AppThumbnailCreationServiceFactory.Create(AppLocalDataPaths.LogsPath);
        private readonly ThumbnailQueueProcessor _thumbnailQueueProcessor = new();
        private readonly ThumbnailQueuePersister _thumbnailQueuePersister;
        private readonly IMainDbMovieReadFacade _mainDbMovieReadFacade =
            new MainDbMovieReadFacade();

        /// <summary>
        /// Persister本体じゃなく「監視タスク」を握っておくぜ！もし例外で死んでも不死鳥の如く蘇らせるための命綱だ！🐦‍🔥
        /// </summary>
        private Task _thumbnailQueuePersisterTask;
        private CancellationTokenSource _thumbnailQueuePersisterCts = new();

        /// <summary>
        /// Everything先生による監視ポーリングの完全常駐タスク！こいつが休むことはない！👁️
        /// </summary>
        private Task _everythingWatchPollTask;
        private CancellationTokenSource _everythingWatchPollCts = new();
        private int _lastEverythingPollDelayMs = EverythingWatchPollIntervalMs;
        private int _lastEverythingPollUpdateCount;
        private int _consecutiveCalmEverythingPollCount;
        private int _lastEverythingPollEligibleWatchFolderCount;

        private DataTable systemData;
        private DataTable movieData;
        private DataTable historyData;
        private DataTable watchData;
        private DataTable bookmarkData;

        // MainWindow クラス内の MainVM フィールドまたはプロパティの宣言を public に変更
        public readonly MainWindowViewModel MainVM;
        // 実起動 UI 統合テストでは、設定保存などの永続化だけを避けて window 局所の後始末は通す。
        internal bool SkipMainWindowClosingSideEffectsForTesting { get; set; }
        internal System.Windows.Point lbClickPoint = new();

        private DateTime _lastSliderTime = DateTime.MinValue;
        private readonly TimeSpan _timeSliderInterval = TimeSpan.FromSeconds(0.1);

        private readonly TimeSpan _timeInputInterval = TimeSpan.FromSeconds(0.5);
        private readonly DispatcherTimer _searchInputDebounceTimer;

        //結局、タイマー方式で動画とマニュアルサムネイルのスライダーを同期させた
        private readonly DispatcherTimer timer;
        //マニュアルサムネイル時の右クリックしたカラムの返却を受け取る変数
        private int manualPos = 0;

        //IME起動中的なフラグ。日本語入力中（未変換）にインクリメンタルサーチさせない為。
        private bool _imeFlag = false;

        private static readonly List<FileSystemWatcher> fileWatchers = [];

        //private bool _searchBoxItemSelectedByMouse = false;
        private bool _searchBoxItemSelectedByUser = false;

        public MainWindow()
        {
            MainVM = new MainWindowViewModel(); // ← 追加
            _thumbnailQueuePersister = new ThumbnailQueuePersister(
                queueRequestChannel.Reader,
                ThumbnailQueuePersistBatchWindowMs,
                message => DebugRuntimeLog.Write("queue-db", message),
                request =>
                    IsQueueRequestAcceptedForSession(
                        request,
                        ReadCurrentMainDbQueueRequestSessionStamp()
                    )
            );
            _whiteBrowserSkinStatePersister = new WhiteBrowserSkinStatePersister(
                _whiteBrowserSkinStatePersistChannel.Reader,
                WhiteBrowserSkinStatePersistBatchWindowMs,
                message => DebugRuntimeLog.Write("skin-db", message)
            );
            _whiteBrowserSkinStatePersisterTask = RunWhiteBrowserSkinStatePersisterSupervisorAsync(
                _whiteBrowserSkinStatePersisterCts.Token
            );

            //前のバージョンのプロパティを引き継ぐぜ。
            Properties.Settings.Default.Upgrade();
            InitializeDetailThumbnailModeRuntime();
            ApplyThumbnailGpuDecodeSetting();
            ApplyThumbnailFfmpegEcoSetting();
            // 起動前の同期DB読込は避け、最低限の既定値だけ先に入れて初回描画を優先する。
            ApplyColdStartSystemDefaults();
            recentFiles.Clear();

            InitializeComponent();
            _uiHangActivityTracker = new UiHangActivityTracker();
            _uiHangNotificationCoordinator = new UiHangNotificationCoordinator(
                Dispatcher,
                _uiHangActivityTracker,
                IsUiHangDangerState,
                ShouldDisplayUiHangNotification
            );
            InitializeUpperTabDisplayOrder();
            InitializeUpperTabRescueTab();
            InitializeUpperTabDuplicateVideosTab();
            // 起動直後の一時Small選択が残らないよう、まずは未選択へ戻しておく。
            Tabs.SelectedIndex = -1;
            MainVM.DbInfo.CurrentTabIndex = -1;
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            SourceInitialized += (_, _) =>
            {
                UpdateUiHangNotificationOwnerWindow();
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationPlacement();
            };
            LocationChanged += (_, _) => UpdateUiHangNotificationPlacement();
            SizeChanged += (_, _) => UpdateUiHangNotificationPlacement();
            StateChanged += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationPlacement();
                UpdateUiHangNotificationVisibilityPolicy();
            };
            Activated += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationVisibilityPolicy();
            };
            Deactivated += (_, _) =>
            {
                UpdateUiHangWindowStateSnapshot();
                UpdateUiHangNotificationVisibilityPolicy();
            };

            // アセンブリのファイルバージョンを取得
            var version = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version;

            this.Title = $"Indigo Movie Manager v{version}";

            ContentRendered += MainWindow_ContentRendered;
            ContentRendered += (_, _) => UpdateUiHangNotificationPlacement();
            Closing += MainWindow_Closing;
            Loaded += (_, _) =>
            {
                StartUiHangNotificationSupport();
                EnsureThumbnailProgressUiTimerRunning();
                SyncThumbnailProgressSettingControls();
                PrimeThumbnailProgressWorkerPanels();
            };
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            TextCompositionManager.AddPreviewTextInputHandler(SearchBox, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(
                SearchBox,
                OnPreviewTextInputStart
            );
            TextCompositionManager.AddPreviewTextInputUpdateHandler(
                SearchBox,
                OnPreviewTextInputUpdate
            );

            var rootItem = new TreeSource() { Text = RECENT_OPEN_FILE_LABEL, IsExpanded = false };
            MainVM.RecentTreeRoot.Add(rootItem);

            if (Properties.Settings.Default.RecentFiles != null)
            {
                foreach (var item in Properties.Settings.Default.RecentFiles)
                {
                    if (item == null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.ToString()))
                    {
                        continue;
                    }
                    recentFiles.Push(item);
                }
                foreach (var item in recentFiles)
                {
                    var childItem = new TreeSource() { Text = item, IsExpanded = false };
                    rootItem.Add(childItem);
                }
            }

            DataContext = MainVM;

            TryRestoreDockLayout();
            EnsureRequiredBottomTabsPresent();
            InitializeExtensionTabSupport();
            InitializeBookmarkTabSupport();
            InitializeSavedSearchTabSupport();
            InitializeTagEditorTabSupport();
            InitializeDebugTabSupport();
            InitializeLogTabSupport();
            ApplyDebugTabVisibility();
            ApplyLogTabVisibility();
            ApplyThumbnailErrorBottomTabVisibility();
            InitializeThumbnailErrorUiSupport();
            InitializeThumbnailProgressUiSupport();
            InitializeUpperTabViewportSupport();
            InitializeWebViewSkinIntegration();

            #region Player Initialize
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            timer.Tick += new EventHandler(Timer_Tick);
            _searchInputDebounceTimer = new DispatcherTimer { Interval = _timeInputInterval };
            _searchInputDebounceTimer.Tick += SearchInputDebounceTimer_Tick;

            // 保存済み音量は中央入口へ通し、内蔵プレイヤー・WebView2・表示の正本を揃える。
            RestorePlayerVolumeFromSettings();

            uxTime.Text = "00:00:00";
            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;
            #endregion
        }

        // 左ドロワー表示中だけ、watch の新規流入を抑えて操作テンポを守る。
        private void MenuToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            BeginWatchUiSuppression("left-drawer");
            SetWebViewPlayerHiddenForLeftDrawer(hidden: true);
        }

        // 左ドロワーを閉じた時だけ、保留があれば watch を1回 catch-up させる。
        private void MenuToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            SetWebViewPlayerHiddenForLeftDrawer(hidden: false);
            EndWatchUiSuppression("left-drawer");
        }

        /// <summary>
        /// 画面の描画完了後に走る最初の儀式！ウィンドウの復元と、裏で動く常駐タスクたちを一斉に叩き起こすぜ！🌅
        /// </summary>
        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            try
            {
                DebugRuntimeLog.TaskStart(nameof(MainWindow_ContentRendered));
                LogStartupWindowShownOnce();
                // 念のため起動時に入力を有効化してから、各常駐タスクを起動する。
                SetThumbnailQueueInputEnabled(true);
                ThumbnailTempFileCleaner.ClearCurrentWorkingTempJpg(); //一時ファイルの削除

                // 画面外へ飛んだ設定値を補正しつつロケーションとサイズを復元する。
                RestoreWindowBoundsSafely();

                QueueStartupAutoOpenLastDocSwitch();

                EnsureThumbnailProgressUiTimerRunning();
                TryStartInitialThumbnailFailureSync();
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(MainWindow_ContentRendered));
            }
        }

        private void QueueStartupAutoOpenLastDocSwitch()
        {
            bool diagnosticNoPersist = App.IsDiagnosticNoPersistEnabled();
            string diagnosticStartupDb = ResolveDiagnosticStartupDbOverride(
                diagnosticNoPersist,
                Environment.GetEnvironmentVariable(DiagnosticStartupDbEnvironmentVariable)
            );
            bool autoOpenSnapshot =
                Properties.Settings.Default.AutoOpen
                || !string.IsNullOrWhiteSpace(diagnosticStartupDb);
            string lastDocSnapshot = !string.IsNullOrWhiteSpace(diagnosticStartupDb)
                ? diagnosticStartupDb
                : Properties.Settings.Default.LastDoc ?? "";
            bool diagnosticStartupDbActive = !string.IsNullOrWhiteSpace(diagnosticStartupDb);

            if (!autoOpenSnapshot || string.IsNullOrWhiteSpace(lastDocSnapshot))
            {
                return;
            }

            // 初回描画を先に通し、LastDoc の存在確認だけを背景へ逃がして UI 入力を塞がない。
            _ = RunStartupAutoOpenLastDocSwitchAsync(
                autoOpenSnapshot,
                lastDocSnapshot,
                diagnosticStartupDbActive
            );
        }

        private async Task RunStartupAutoOpenLastDocSwitchAsync(
            bool autoOpenSnapshot,
            string lastDocSnapshot,
            bool diagnosticStartupDbActive
        )
        {
            bool lastDocExists;
            try
            {
                lastDocExists = await Task.Run(() => Path.Exists(lastDocSnapshot))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "startup",
                    $"startup auto-open LastDoc exists failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
                return;
            }

            if (!lastDocExists || IsStartupAutoOpenLastDocSwitchShutdownStarted())
            {
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync<Task>(
                        () =>
                        {
                            if (
                                !IsStartupAutoOpenLastDocSnapshotCurrent(
                                    autoOpenSnapshot,
                                    lastDocSnapshot,
                                    diagnosticStartupDbActive
                                )
                            )
                            {
                                return Task.CompletedTask;
                            }

                            return TrySwitchMainDb(
                                lastDocSnapshot,
                                MainDbSwitchSource.StartupAutoOpen
                            );
                        },
                        DispatcherPriority.Background
                    )
                    .Task.Unwrap();
            }
            catch (TaskCanceledException ex)
            {
                DebugRuntimeLog.Write(
                    "startup",
                    $"startup auto-open LastDoc dispatch canceled: err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
            catch (InvalidOperationException ex)
            {
                DebugRuntimeLog.Write(
                    "startup",
                    $"startup auto-open LastDoc dispatch failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "startup",
                    $"startup auto-open LastDoc switch failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private bool IsStartupAutoOpenLastDocSnapshotCurrent(
            bool autoOpenSnapshot,
            string lastDocSnapshot,
            bool diagnosticStartupDbActive
        )
        {
            if (IsStartupAutoOpenLastDocSwitchShutdownStarted())
            {
                return false;
            }

            if (diagnosticStartupDbActive)
            {
                return autoOpenSnapshot && !string.IsNullOrWhiteSpace(lastDocSnapshot);
            }

            return autoOpenSnapshot
                && Properties.Settings.Default.AutoOpen
                && !string.IsNullOrWhiteSpace(lastDocSnapshot)
                && string.Equals(
                    Properties.Settings.Default.LastDoc ?? "",
                    lastDocSnapshot,
                    StringComparison.Ordinal
                );
        }

        private bool IsStartupAutoOpenLastDocSwitchShutdownStarted()
        {
            return Volatile.Read(ref _mainWindowClosingStarted) != 0
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished;
        }

        private const string DiagnosticStartupDbEnvironmentVariable = "INDIGO_DIAGNOSTIC_STARTUP_DB";

        private static string ResolveDiagnosticStartupDbOverride(
            bool diagnosticNoPersist,
            string rawStartupDbPath
        )
        {
            return ResolveDiagnosticStartupDbOverrideForTesting(
                diagnosticNoPersist,
                rawStartupDbPath
            );
        }

        internal static string ResolveDiagnosticStartupDbOverrideForTesting(
            bool diagnosticNoPersist,
            string rawStartupDbPath
        )
        {
            // 診断用DB上書きは no-persist と組み合わせた時だけ有効にし、通常設定を汚さない。
            if (!diagnosticNoPersist)
            {
                return "";
            }

            return rawStartupDbPath?.Trim() ?? "";
        }

        /// <summary>
        /// アプリ終了時の大掃除！確認ダイアログから設定の保存、そしてタスク群への「止まれ！」の号令まで一手に引き受ける終末の処理だ！⏳
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (Properties.Settings.Default.ConfirmExit)
            {
                var result = MessageBox.Show(
                    this,
                    "本当に終了しますか？",
                    "終了確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question
                );
                if (result != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    MenuToggleButton.IsChecked = false;
                    return;
                }
            }

            Volatile.Write(ref _mainWindowClosingStarted, 1);
            bool skipProcessWideShutdownSideEffects =
                SkipMainWindowClosingSideEffectsForTesting || App.IsDiagnosticNoPersistEnabled();

            try
            {
                if (!skipProcessWideShutdownSideEffects)
                {
                    ShowUiHangShutdownStatus("終了処理: 設定を保存中");
                    Properties.Settings.Default.MainLocation = new System.Drawing.Point(
                        (int)Left,
                        (int)Top
                    );
                    Properties.Settings.Default.MainSize = new System.Drawing.Size(
                        (int)Width,
                        (int)Height
                    );
                    UpdateSkin();
                    UpdateSort();

                    Properties.Settings.Default.RecentFiles.Clear();
                    Properties.Settings.Default.RecentFiles.AddRange([.. recentFiles.Reverse()]);
                    QueueApplicationSettingsSave("main-window-closing");

                    ShowUiHangShutdownStatus("終了処理: レイアウトを保存中");
                    SaveDockLayoutToFile(DockLayoutFileName);
                    SaveDockLayoutToFile(DefaultDockLayoutFileName);

                    if (!string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
                    {
                        ShowUiHangShutdownStatus("終了処理: 履歴を整理中");
                        var keepHistoryData = SelectSystemTable("keepHistory");
                        int keepHistoryCount = Convert.ToInt32(
                            keepHistoryData == "" ? "30" : keepHistoryData
                        );
                        DeleteHistoryTable(MainVM.DbInfo.DBFullPath, keepHistoryCount);
                    }

                    ShowUiHangShutdownStatus("終了処理: 設定保存を確認中");
                    WaitForPlayerVolumeSettingSaveForShutdown();
                    WaitForApplicationSettingsSaveForShutdown("main-window-closing");
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                ShowUiHangShutdownStatus("終了処理: UI停止準備中");
                // 閉じ際に動画再生とUIタイマーを先に止め、追加のハンドル消費を抑える。
                uxVideoPlayer.Stop();
                StopDispatcherTimerSafely(timer, nameof(timer));
                StopDispatcherTimerSafely(
                    _searchInputDebounceTimer,
                    nameof(_searchInputDebounceTimer)
                );
                StopDispatcherTimerSafely(
                    _thumbnailProgressUiTimer,
                    nameof(_thumbnailProgressUiTimer)
                );
                StopDispatcherTimerSafely(_debugTabRefreshTimer, nameof(_debugTabRefreshTimer));
                StopDispatcherTimerSafely(_logTabRefreshTimer, nameof(_logTabRefreshTimer));

                ShowUiHangShutdownStatus("終了処理: 入力受付を停止中");
                // まず入力を止め、以降の監視イベントからの投入を遮断する。
                ShowUiHangShutdownStatus("終了処理: バックグラウンド処理を停止中");
                SetThumbnailQueueInputEnabled(false);
                queueRequestChannel.Writer.TryComplete();
                _everythingWatchPollCts.Cancel();
                InvalidateWatcherCreation("window-closing");
                LogWatcherCreationStateForShutdown("window-closing");
                StopAndClearFileWatchers();
                BeginWhiteBrowserSkinStatePersisterShutdown();
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: input stop requested and thumbnail queue input disabled."
                );
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "MainWindow closing: thumbnail token cancel requested."
                );
                _thumbCheckCts.Cancel();
                _thumbnailQueuePersisterCts.Cancel();
                CancelKanaBackfill("window-closing");
                BeginWatchEventQueueShutdownForClosing();

                // 即終了優先を守るため、各タスク待機は最大500msで打ち切る。
                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(1/5): サムネイル消費タスク停止待機");
                WaitBackgroundTaskForShutdown(_thumbCheckTask, "thumbnail-consumer");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(2/5): サムネイル保存タスク停止待機");
                WaitBackgroundTaskForShutdown(_thumbnailQueuePersisterTask, "thumbnail-persister");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(3/5): skin保存タスク停止待機");
                DrainWhiteBrowserSkinStatePersisterForShutdown();

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(4/5): 監視ポーリング停止待機");
                WaitBackgroundTaskForShutdown(_everythingWatchPollTask, "everything-poll");

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中(5/5): watch/check-folder停止待機");
                // watch queue / Created ready / check-folder runner を同じ短時間drainにまとめる。
                DrainWatchEventPipelinesForShutdown();

                ShowUiHangShutdownStatus("終了処理: 後始末を実行中: rescue worker を停止中");
                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: starting rescue worker cleanup."
                );
                DisposeThumbnailRescueWorkerLaunchers();

                DebugRuntimeLog.Write(
                    "lifecycle",
                    "shutdown: stopping ui hang notification support."
                );
                ShowUiHangShutdownStatus("終了処理: 後始末を実行中: オーバーレイ停止中");
                HideUiHangShutdownStatus();
                StopUiHangNotificationSupport();
            }
        }

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
                if (await TryRestoreDockLayoutFromFile(DockLayoutFileName, backupInvalidLayout: true))
                {
                    return;
                }

                _ = await TryRestoreDockLayoutFromFile(
                    DefaultDockLayoutFileName,
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
                string invalidReason = FindMissingRequiredDockLayoutReason(
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
            return FindMissingRequiredDockLayoutReason(
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
            // 下部の常設タブは、誤って保存済みレイアウトから落ちても次回起動で救済する。
            (string ContentId, string Reason)[] requiredTabs =
            [
                (ExtensionBottomTabContentId, "missing-extension-bottom-tab"),
                (BookmarkBottomTabContentId, "missing-bookmark-bottom-tab"),
                (SavedSearchBottomTabContentId, "missing-saved-search-bottom-tab"),
                (ThumbnailProgressContentId, "missing-thumbnail-progress"),
                (TagEditorBottomTabContentId, "missing-tag-editor-bottom-tab"),
            ];

            foreach ((string contentId, string reason) in requiredTabs)
            {
                if (
                    !layoutText.Contains(
                        $"ContentId=\"{contentId}\"",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return reason;
                }
            }

            if (
                ShouldRequireThumbnailErrorBottomTabInLayoutRestore(
                    layoutText,
                    shouldShowThumbnailErrorBottomTab
                )
            )
            {
                return "missing-thumbnail-error-bottom-tab";
            }

            // Debug 構成では開発用タブも必須扱いにして、古いレイアウトを引きずらない。
            if (
                shouldShowDebugTab
                && !layoutText.Contains(
                    $"ContentId=\"{DebugToolContentId}\"",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "missing-debug-tool";
            }

            if (
                shouldShowDebugTab
                && !layoutText.Contains(
                    $"ContentId=\"{LogToolContentId}\"",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "missing-log-tool";
            }

            return "";
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

        /// <summary>
        /// Persisterが過労で吹っ飛んでも、アプリが生きている限り何度でも蘇らせる地獄の無限監視ループ！🧟‍♂️
        /// </summary>
        private async Task RunThumbnailQueuePersisterSupervisorAsync(CancellationToken cts)
        {
            // 起動直後の呼び出し元を止めないよう、まず非同期境界へ出る。
            await Task.Yield();

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await _thumbnailQueuePersister.RunAsync(cts).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("queue-db", $"persister restart scheduled: {ex.Message}");
                    try
                    {
                        // 連続障害時の過剰再起動を避けるため、短い待機を挟んで再試行する。
                        await Task.Delay(500, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Everything連携向けの爆速短周期ポーリング！🚀
        /// ローカルの監視フォルダがある時だけ本気を出し、無い時はエコに待機する賢いヤツだ！🧠
        /// </summary>
        private async Task RunEverythingWatchPollLoopAsync(CancellationToken cts)
        {
            // 起動直後の初回描画を優先し、以降の周期判定は UI コンテキストへ戻さない。
            try
            {
                await Task.Delay(1, cts).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            // poll loop の再起動時も、静穏判定は新しい起点から測り直す。
            ResetEverythingWatchPollAdaptiveDelayState();
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    bool isDeferredByUiSuppression = false;
                    bool isDeferredByUserPriority = false;
                    if (IsWatchSuppressedByUi())
                    {
                        MarkWatchWorkDeferredWhileSuppressed("everything-poll");
                        isDeferredByUiSuppression = true;
                    }
                    else if (TryDeferEverythingWatchPollForUserPriority())
                    {
                        // 明示操作が終わった後の catch-up へ任せ、この周回では入口判定まで進めない。
                        isDeferredByUserPriority = true;
                    }
                    else if (await ShouldRunEverythingWatchPollPolicyAsync(cts).ConfigureAwait(false))
                    {
                        await QueueCheckFolderAsync(CheckMode.Watch, "EverythingPoll")
                            .ConfigureAwait(false);
                    }

                    int delayMs = ResolveEverythingWatchPollDelayMs(
                        shouldProbeQueueLoad: ShouldProbeEverythingWatchPollQueueLoad(
                            isDeferredByUiSuppression,
                            isDeferredByUserPriority
                        ),
                        isDeferredByUiSuppression: isDeferredByUiSuppression,
                        isDeferredByUserPriority: isDeferredByUserPriority,
                        isPlayerPlaybackActive: IsPlayerPlaybackActive()
                    );
                    await Task.Delay(delayMs, cts).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"everything poll restart scheduled: {ex.Message}"
                    );
                    try
                    {
                        await Task.Delay(1000, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// サムネイルキュー負荷に応じてEverythingポーリング間隔を動的に調整する。
        /// キュー残量が多い時はポーリングを15秒に延ばし、CPUの空振り消費を抑える。
        /// </summary>
        private int ResolveEverythingWatchPollDelayMs(
            bool shouldProbeQueueLoad = true,
            bool isDeferredByUiSuppression = false,
            bool isDeferredByUserPriority = false,
            bool isPlayerPlaybackActive = false
        )
        {
            int delayMs = EverythingWatchPollIntervalMs;
            try
            {
                if (shouldProbeQueueLoad)
                {
                    if (HasEverythingWatchPollEligibleFolders())
                    {
                        var queueDbService = ResolveCurrentQueueDbService();
                        int activeCount =
                            queueDbService?.GetActiveQueueCount(thumbnailQueueOwnerInstanceId) ?? 0;
                        delayMs = ResolveEverythingWatchPollDelayFromState(activeCount);
                    }
                    else
                    {
                        delayMs = ResolveEverythingWatchPollDelayFromState(0);
                    }
                }
                else
                {
                    // poll延期中は queue 負荷を読まない代わりに、直前の遅延状態を引き継いで無駄なwake-upを避ける。
                    delayMs = ResolveEverythingWatchPollBaseDelayWhenQueueProbeSkipped(
                        _lastEverythingPollDelayMs
                    );
                }
                delayMs = ApplyEverythingWatchPollInteractionDelayPolicy(
                    delayMs,
                    isDeferredByUiSuppression,
                    isDeferredByUserPriority,
                    isPlayerPlaybackActive
                );
                delayMs = ApplyEverythingWatchPollEligibilityDelayPolicy(
                    delayMs,
                    HasEverythingWatchPollEligibleFolders()
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll delay resolve failed: {ex.Message}"
                );
                delayMs = EverythingWatchPollIntervalMs;
            }

            if (delayMs != _lastEverythingPollDelayMs)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll interval changed: {_lastEverythingPollDelayMs} -> {delayMs} "
                        + $"last_updates={Volatile.Read(ref _lastEverythingPollUpdateCount)} "
                        + $"calm_cycles={Volatile.Read(ref _consecutiveCalmEverythingPollCount)}"
                );
                _lastEverythingPollDelayMs = delayMs;
            }
            return delayMs;
        }

        /// <summary>
        /// アプリ終了時、バックグラウンドタスクがグダグダ粘るのを許さない！最大500msで強制シャットダウンする完全処刑窓口だ！⚡
        /// </summary>
        private static void WaitBackgroundTaskForShutdown(Task task, string taskName)
        {
            if (task == null)
            {
                return;
            }
            try
            {
                Task completed = Task.WhenAny(task, Task.Delay(500)).GetAwaiter().GetResult();
                if (!ReferenceEquals(completed, task))
                {
                    DebugRuntimeLog.Write("lifecycle", $"{taskName} wait timeout: 500ms status={task.Status}");
                    return;
                }

                if (task.IsFaulted)
                {
                    string message = task.Exception?.GetBaseException()?.Message ?? "unknown";
                    DebugRuntimeLog.Write("lifecycle", $"{taskName} faulted: {message}");
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("lifecycle", $"{taskName} wait failed: {ex.Message}");
            }
        }

        // 複数 worker を持つ系は個別に短時間待機し、終了処理を引き延ばさない。
        private static void WaitBackgroundTasksForShutdown(IEnumerable<Task> tasks, string taskName)
        {
            if (tasks == null)
            {
                return;
            }

            int index = 0;
            foreach (Task task in tasks)
            {
                index++;
                WaitBackgroundTaskForShutdown(task, $"{taskName}[{index}]");
            }
        }

        // IME確定時に検索入力フラグを通常状態へ戻す。
        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = false;
        }

        // IME変換開始を検知して検索の即時実行を抑制する。
        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e)
        {
            _imeFlag = true;
        }

        // IME変換文字が空になったら検索入力フラグを解除する。
        private void OnPreviewTextInputUpdate(object sender, TextCompositionEventArgs e)
        {
            if (e.TextComposition.CompositionText.Length == 0)
            {
                _imeFlag = false;
            }
        }

        //todo : And以外の検索の実装。せめてNOT検索ぐらいまでは…
        //todo : 検索履歴の保管条件（おそらくヒット：ゼロ件超で保管）確認＆修正
        //todo : タグバー代替（保管済み検索条件）の実装
        //stack : プロパティ表示ウィンドウの作成。
        //todo : 重複チェック。本家は恐らくファイル名もチェックで使ってる模様。
        //       こっちで登録しても再度本家に登録されるケースがあったのは、ファイル名の大文字小文字が違ってたから。
        //       movie_name と Hash で重複チェックかなぁ。
        //       本家のmovie_nameは小文字変換かけてる模様。合わせてみたら再登録されなかったので恐らく正解。

        // changed paths の局所更新判定は ReadModel builder へ寄せ、MainWindow は互換入口だけを残す。
        internal static bool TryBuildChangedMovieRefreshSource(
            IEnumerable<MovieRecords> sourceMovies,
            IEnumerable<MovieRecords> currentFilteredMovies,
            string searchKeyword,
            string sortId,
            IEnumerable<WatchChangedMovie> changedMovies,
            Func<IEnumerable<MovieRecords>, string, IEnumerable<MovieRecords>> filterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        )
        {
            return TryBuildChangedMovieRefreshSourceWithReason(
                sourceMovies,
                currentFilteredMovies,
                searchKeyword,
                sortId,
                changedMovies,
                filterMovies,
                out nextFilteredMovies,
                out canReuseCurrentOrder,
                out _
            );
        }

        internal static bool TryBuildChangedMovieRefreshSourceWithReason(
            IEnumerable<MovieRecords> sourceMovies,
            IEnumerable<MovieRecords> currentFilteredMovies,
            string searchKeyword,
            string sortId,
            IEnumerable<WatchChangedMovie> changedMovies,
            Func<IEnumerable<MovieRecords>, string, IEnumerable<MovieRecords>> filterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder,
            out string fallbackReason
        )
        {
            MovieRecords[] sourceSnapshot = sourceMovies?
                .Where(movie => movie != null)
                .ToArray() ?? [];
            WatchChangedMovie[] changedSnapshot = changedMovies?.ToArray() ?? [];
            ApplyObservedStatesToMovieRecords(sourceSnapshot, changedSnapshot);

            return MovieViewReadModelBuilder.TryBuildChangedMovieRefreshSourceWithReason(
                sourceSnapshot,
                currentFilteredMovies,
                searchKeyword,
                sortId,
                changedSnapshot,
                filterMovies,
                out nextFilteredMovies,
                out canReuseCurrentOrder,
                out fallbackReason
            );
        }

        internal static Dictionary<string, MovieRecords> BuildChangedSourceMovieLookup(
            IEnumerable<MovieRecords> sourceMovies,
            IEnumerable<WatchChangedMovie> changedMovies
        )
        {
            return MovieViewReadModelBuilder.BuildChangedSourceMovieLookup(sourceMovies, changedMovies);
        }

        // watch で拾った観測値は UI スレッド側で当ててから、ReadModel builder へ読むだけの source を渡す。
        internal static void ApplyObservedStateToMovieRecord(
            MovieRecords target,
            WatchMovieObservedState? observedState
        )
        {
            if (target == null || !observedState.HasValue)
            {
                return;
            }

            WatchMovieObservedState currentObservedState = observedState.Value;
            if (
                !string.IsNullOrWhiteSpace(currentObservedState.FileDateText)
                && !string.Equals(
                    target.File_Date ?? "",
                    currentObservedState.FileDateText,
                    StringComparison.Ordinal
                )
            )
            {
                target.File_Date = currentObservedState.FileDateText;
            }

            if (target.Movie_Size != currentObservedState.MovieSizeKb)
            {
                target.Movie_Size = currentObservedState.MovieSizeKb;
            }

            if (
                currentObservedState.MovieLengthSeconds.HasValue
                && TryFormatObservedMovieLength(
                    currentObservedState.MovieLengthSeconds.Value,
                    out string movieLengthText
                )
                && !string.Equals(target.Movie_Length ?? "", movieLengthText, StringComparison.Ordinal)
            )
            {
                target.Movie_Length = movieLengthText;
            }
        }

        private static bool TryFormatObservedMovieLength(
            long movieLengthSeconds,
            out string movieLengthText
        )
        {
            movieLengthText = "";
            if (movieLengthSeconds < 0)
            {
                return false;
            }

            movieLengthText = TimeSpan.FromSeconds(movieLengthSeconds).ToString(@"hh\:mm\:ss");
            return true;
        }

        /// <summary>
        /// 絞り込み済みのリスト(filterList)に対して、指定されたソートの魔法だけをサクッとかけるぜ！🪄
        /// </summary>
        private void SetSortData(string id)
        {
            // 並び替えロジックは ViewModel に寄せ、追加ソートの差分を 1 箇所へ閉じ込める。
            filterList = MainVM.SortMovies(filterList ?? [], id).ToArray();
        }

        // 一覧差し替え後の互換 Refresh は、選択が実際に変わった時だけ詳細/タグへ流す。
        private bool RefreshSelectionDetailAfterCollectionApplyIfNeeded(
            MovieRecords selectedBeforeApply,
            FilteredMovieRecsUpdateResult applyResult,
            int currentTabIndex,
            FilteredMovieRecsUpdateMode updateMode
        )
        {
            bool requiresCompatibilityRefresh =
                applyResult.HasChanges
                && UpperTabCollectionUpdatePolicy.ShouldRefreshAfterCollectionApply(
                    currentTabIndex,
                    updateMode
                );
            if (!requiresCompatibilityRefresh)
            {
                return false;
            }

            MovieRecords selectedAfterApply = GetSelectedItemByTabIndex();
            if (
                selectedAfterApply != null
                && ReferenceEquals(selectedBeforeApply, selectedAfterApply)
            )
            {
                return false;
            }

            Refresh();
            return true;
        }

        /// <summary>
        /// サムネイル画像上のクリック位置から、対応する動画の再生開始秒（ミリ秒）を計算する。
        /// サムネイルのグリッド構造（行×列）から、どのフレームがクリックされたかを逆算する。
        /// </summary>
        private int GetPlayPosition(int tabIndex, MovieRecords mv, ref int returnPos)
        {
            string currentThumbPath = ResolveMovieThumbnailPathByTabIndex(tabIndex, mv);
            if (
                TryResolveThumbnailPlaybackPosition(
                    currentThumbPath,
                    lbClickPoint,
                    out int msec,
                    out int secPos
                )
            )
            {
                returnPos = secPos;
                return msec;
            }

            return 0;
        }

        // タブとサムネイルパスの対応はここへ集約し、UI側呼び出し元に分岐を散らさない。
        private static string ResolveMovieThumbnailPathByTabIndex(int tabIndex, MovieRecords mv)
        {
            if (mv == null)
            {
                return "";
            }

            return tabIndex switch
            {
                0 => mv.ThumbPathSmall,
                1 => mv.ThumbPathBig,
                2 => mv.ThumbPathGrid,
                3 => mv.ThumbPathList,
                4 => mv.ThumbPathBig10,
                _ => "",
            };
        }

        // サムネイルシート解析はUIスレッド外でも使えるように純粋関数化する。
        private static bool TryResolveThumbnailPlaybackPosition(
            string thumbPath,
            System.Windows.Point clickPoint,
            out int playbackMilliseconds,
            out int frameIndex
        )
        {
            playbackMilliseconds = 0;
            frameIndex = 0;

            if (string.IsNullOrWhiteSpace(thumbPath) || !Path.Exists(thumbPath))
            {
                return false;
            }

            ThumbInfo thumbInfo = new();
            thumbInfo.GetThumbInfo(thumbPath);
            if (
                thumbInfo.IsThumbnail != true
                || thumbInfo.ThumbRows <= 0
                || thumbInfo.ThumbColumns <= 0
                || thumbInfo.ThumbSec == null
                || thumbInfo.ThumbSec.Count == 0
            )
            {
                return false;
            }

            int totalCells = thumbInfo.ThumbRows * thumbInfo.ThumbColumns;
            int selectedIndex = totalCells;

            bool found = false;
            for (int row = 1; row <= thumbInfo.ThumbRows && !found; row++)
            for (int column = 1; column <= thumbInfo.ThumbColumns; column++)
            {
                int cellBoundaryX = column * thumbInfo.ThumbWidth;
                int cellBoundaryY = row * thumbInfo.ThumbHeight;
                if (clickPoint.X < cellBoundaryX && clickPoint.Y < cellBoundaryY)
                {
                    selectedIndex = (row - 1) * thumbInfo.ThumbColumns + (column - 1);
                    found = true;
                    break;
                }
            }

            selectedIndex = Math.Max(0, Math.Min(selectedIndex, thumbInfo.ThumbSec.Count - 1));
            playbackMilliseconds = thumbInfo.ThumbSec[selectedIndex] * 1000;
            frameIndex = selectedIndex;
            return true;
        }

        /// <summary>
        /// 一覧タブ上のショートカットキー（Enter/F6/C/V/+/-/F2/F12/Delete等）を
        /// 各機能ハンドラへ振り分けるキーディスパッチャ。
        /// </summary>
        private void Tab_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Tabs.SelectedIndex == -1)
            {
                return;
            }
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            if (TryHandleUpperTabPageScroll(e))
            {
                return;
            }

            if (e.Key == Key.Delete)
            {
                // Delete系ショートカットは修飾キーごとに別設定へ振り分ける。
                if (TryHandleDeleteShortcut(e))
                {
                    return;
                }
            }

            switch (e.Key)
            {
                case Key.Enter: //再生
                    PlayMovie_Click(sender, e);
                    break;
                case Key.F6: //タグ編集
                    TagEdit_Click(sender, e);
                    break;
                case Key.C: //タグのコピー
                    TagCopy_Click(sender, e);
                    break;
                case Key.V: //タグの貼り付け
                    TagPaste_Click(sender, e);
                    break;
                case Key.Add: //スコアプラス
                case Key.Subtract: //スコアマイナス
                    MenuScore_Click(sender, e);
                    break;
                case Key.F2: //名前の変更
                    RenameFile_Click(sender, e);
                    break;
                case Key.F12: //親フォルダ
                    OpenParentFolder_Click(sender, e);
                    break;
                case Key.P: //プロパティ
                    break;
                default:
                    return;
            }
        }

        /// <summary>
        /// ソートコンボボックスの選択変更ハンドラ。
        /// 段階ロード中は全件再取得付き FilterAndSort、通常時はインメモリ SortData で並び替えて先頭を選択する。
        /// </summary>
        private async void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath))
            {
                return;
            }
            if (_suppressSortComboSelectionChangedHandling)
            {
                return;
            }
            if (sender is ComboBox senderObj)
            {
                if (MainVM.MovieRecs.Count > 0)
                {
                    if (senderObj.SelectedValue != null)
                    {
                        var id = senderObj.SelectedValue;
                        bool shouldSelectFirstItem = true;
                        if (IsStartupFeedPartialActive)
                        {
                            FilterAndSort(id.ToString(), true);
                        }
                        else
                        {
                            shouldSelectFirstItem = await SortDataAsync(id.ToString());
                        }
                        if (id.ToString() == "28")
                        {
                            RefreshThumbnailErrorRecords(force: true);
                        }
                        if (shouldSelectFirstItem)
                        {
                            SelectFirstItem();
                        }
                    }
                }
            }
        }

    }
}

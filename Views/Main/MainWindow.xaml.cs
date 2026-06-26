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
                    bool isDeferredByRecentViewport = false;
                    UiWorkRequest everythingPollRequest =
                        UiWorkRequestPolicy.CreateEverythingWatchPollRequest();
                    bool isRecentViewportInteractionActive = IsRecentViewportInteractionActive();
                    if (IsWatchSuppressedByUi())
                    {
                        MarkWatchWorkDeferredWhileSuppressed("everything-poll");
                        isDeferredByUiSuppression = true;
                        DebugRuntimeLog.Write(
                            "watch-check",
                            BuildEverythingWatchPollDeferredLogMessage(
                                UiOperationPriorityPolicy.DeferReasonUiSuppression,
                                UiOperationPriorityPolicy.DeferReasonUiSuppression,
                                isRecentViewportInteractionActive,
                                shouldQueueCatchUp: true,
                                everythingPollRequest
                            )
                        );
                    }
                    else if (
                        TryDeferEverythingWatchPollForUserPriorityCore(
                            isRecentViewportInteractionActive
                        )
                    )
                    {
                        // 明示操作が終わった後の catch-up へ任せ、この周回では入口判定まで進めない。
                        isDeferredByUserPriority = true;
                    }
                    else if (
                        TryDeferEverythingWatchPollForRecentViewport(
                            isRecentViewportInteractionActive
                        )
                    )
                    {
                        isDeferredByRecentViewport = true;
                    }
                    else if (await ShouldRunEverythingWatchPollPolicyAsync(cts).ConfigureAwait(false))
                    {
                        if (TryAdmitEverythingWatchPollWork(everythingPollRequest, out _))
                        {
                            await QueueCheckFolderAsync(CheckMode.Watch, "EverythingPoll")
                                .ConfigureAwait(false);
                        }
                    }

                    int delayMs = ResolveEverythingWatchPollDelayMsCore(
                        shouldProbeQueueLoad: ShouldProbeEverythingWatchPollQueueLoad(
                            isDeferredByUiSuppression,
                            isDeferredByUserPriority,
                            isDeferredByRecentViewport
                        ),
                        isDeferredByUiSuppression: isDeferredByUiSuppression,
                        isDeferredByUserPriority: isDeferredByUserPriority,
                        isPlayerPlaybackActive: IsPlayerPlaybackActive(),
                        isRecentViewportInteractionActive: isRecentViewportInteractionActive
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

        private bool TryAdmitEverythingWatchPollWork(
            UiWorkRequest request,
            out UiWorkRequest queuedRequest
        )
        {
            queuedRequest = default;
            UiWorkSchedulerRuntimeQueueResult queueResult;
            UiWorkSchedulerRuntimeTakeResult takeResult = default;

            // runtimeは実行器にせず、既存のwatch scan入口へ渡す1件を選ぶだけに留める。
            lock (_uiWorkSchedulerRuntimeSyncRoot)
            {
                queueResult = _uiWorkSchedulerRuntime.Queue(request);
                if (queueResult.Decision.Accepted)
                {
                    takeResult = _uiWorkSchedulerRuntime.TryTakeNext();
                }
            }

            if (!queueResult.Decision.Accepted)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll scheduler rejected: {UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, queueResult.Decision)}"
                );
                return false;
            }

            if (!takeResult.HasRequest)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll scheduler empty: {UiWorkRequestPolicy.BuildRequestAdmissionLogFields(request, UiWorkRequestPolicy.ReleaseReasonRejected)} next_reason={takeResult.Decision.Reason}"
                );
                return false;
            }

            DebugRuntimeLog.Write(
                "watch-check",
                $"everything poll scheduler admitted: {UiWorkSchedulerPolicy.BuildAdmissionLogFields(request, queueResult.Decision)} pending_count={queueResult.PendingCount}"
            );
            DebugRuntimeLog.Write(
                "watch-check",
                $"everything poll scheduler released: {UiWorkSchedulerPolicy.BuildTakeLogFields(takeResult.PendingRequest, takeResult.Decision, takeResult.PendingCount, UiWorkRequestPolicy.ReleaseReasonReleased)}"
            );

            queuedRequest = takeResult.PendingRequest.Request;
            return true;
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
            return ResolveEverythingWatchPollDelayMsCore(
                shouldProbeQueueLoad,
                isDeferredByUiSuppression,
                isDeferredByUserPriority,
                isPlayerPlaybackActive,
                isRecentViewportInteractionActive: false
            );
        }

        private int ResolveEverythingWatchPollDelayMsCore(
            bool shouldProbeQueueLoad,
            bool isDeferredByUiSuppression,
            bool isDeferredByUserPriority,
            bool isPlayerPlaybackActive,
            bool isRecentViewportInteractionActive
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
                    isPlayerPlaybackActive,
                    isRecentViewportInteractionActive
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
                string deferReason = ResolveEverythingWatchPollDeferReason(
                    isDeferredByUiSuppression,
                    isDeferredByUserPriority,
                    isRecentViewportInteractionActive
                );
                string operationReason = ResolveEverythingWatchPollOperationReason(
                    isDeferredByUiSuppression,
                    isDeferredByUserPriority,
                    isRecentViewportInteractionActive,
                    isPlayerPlaybackActive
                );
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"everything poll interval changed: {_lastEverythingPollDelayMs} -> {delayMs} "
                        + $"last_updates={Volatile.Read(ref _lastEverythingPollUpdateCount)} "
                        + $"calm_cycles={Volatile.Read(ref _consecutiveCalmEverythingPollCount)} "
                        + $"operation_reason={operationReason} "
                        + $"defer_reason={deferReason} "
                        + $"recent_viewport={FormatLogBool(isRecentViewportInteractionActive)} "
                        + $"catch_up={FormatLogBool(ShouldQueueEverythingWatchPollCatchUp(deferReason))} "
                        + $"poll_delay_ms={delayMs}"
                );
                _lastEverythingPollDelayMs = delayMs;
            }
            return delayMs;
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

    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
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
                    return;
                }
            }

            Volatile.Write(ref _mainWindowClosingStarted, 1);
            ReleasePendingPlayerUserPriorityWork("shutdown");
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
                LogUiWorkSchedulerPendingWorkForShutdown("window-closing");

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

        private void LogUiWorkSchedulerPendingWorkForShutdown(string reason)
        {
            UiWorkSchedulerPendingRequest[] pendingRequests;
            lock (_uiWorkSchedulerRuntimeSyncRoot)
            {
                pendingRequests = [.. _uiWorkSchedulerRuntime.PendingRequests];
            }

            if (pendingRequests.Length == 0)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "lifecycle",
                $"ui work scheduler shutdown pending: reason={reason ?? ""} pending_count={pendingRequests.Length} {UiWorkSchedulerPolicy.SchedulerContractLogField}"
            );
            foreach (UiWorkSchedulerPendingRequest pendingRequest in pendingRequests)
            {
                DebugRuntimeLog.Write(
                    "lifecycle",
                    $"ui work scheduler shutdown pending item: reason={reason ?? ""} sequence={pendingRequest.Sequence} {UiWorkRequestPolicy.BuildRequestSchedulerLogFields(pendingRequest.Request, UiWorkRequestPolicy.ReleaseReasonCanceled)}"
                );
            }
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

    }
}

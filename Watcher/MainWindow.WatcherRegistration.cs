using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.DB;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private int _watcherCreationRevision;
        private readonly object _watcherCreationTaskSync = new();
        private Task _watcherCreationTask = Task.CompletedTask;
        private int _watcherCreationActiveTaskCount;

        /// <summary>
        /// FileSystemWatcherから「新入りが来たぞ！」と報告が上がった時の出迎え処理だぜ！🎉
        /// </summary>
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                var ext = Path.GetExtension(e.FullPath);
                string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
                string[] checkExts = checkExt.Split(",", StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < checkExts.Length; i++)
                {
                    checkExts[i] = checkExts[i].Trim();
                }

                // Created 以外は即 return し、watch event queue へ流す対象だけに絞る。
                if (e.ChangeType != WatcherChangeTypes.Created
                    || !checkExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                _ = QueueWatchEventAsync(
                    new WatchEventRequest(WatchEventKind.Created, e.FullPath, ""),
                    "watch-created"
                );
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"FileChangedで例外発生: {ex.Message}");
#endif
                DebugRuntimeLog.Write(
                    "watch",
                    $"watch event enqueue failed(created): {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 「ファイル名が変わった！」と報告が入ったら、DBもサムネイルも全員まとめて追従改名させる怒涛の連鎖処理！🏃‍♂️💨
        /// </summary>
        private void FileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(",", StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < checkExts.Length; i++)
            {
                checkExts[i] = checkExts[i].Trim();
            }
            var eFullPath = e.FullPath;
            var oldFullPath = e.OldFullPath;

            if (checkExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
#if DEBUG
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                s += $"【{e.ChangeType}】{e.OldName} → {e.FullPath}";
                Debug.WriteLine(s);
#endif
                _ = QueueWatchEventAsync(
                    new WatchEventRequest(WatchEventKind.Renamed, eFullPath, oldFullPath),
                    "watch-renamed"
                );
            }
        }

        /// <summary>
        /// 指定されたフォルダにFileSystemWatcher（監視カメラ）をガッチリ仕掛ける番人の儀式！👁️
        /// </summary>
        private bool RunWatcher(string watchFolder, bool sub)
        {
            if (string.IsNullOrWhiteSpace(watchFolder))
            {
                DebugRuntimeLog.Write("watch", "skip watcher: folder is empty");
                return false;
            }

            FileSystemWatcher item = new()
            {
                Path = watchFolder,
                Filter = "",
                NotifyFilter =
                    NotifyFilters.LastAccess
                    | NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName,
                IncludeSubdirectories = sub,
                InternalBufferSize = 1024 * 32,
            };

            item.Changed += new FileSystemEventHandler(FileChanged);
            item.Created += new FileSystemEventHandler(FileChanged);
            item.Renamed += new RenamedEventHandler(FileRenamed);
            if (!TrySetFileWatcherEnabled(item, enabled: true, "register"))
            {
                item.Dispose();
                return false;
            }

            fileWatchers.Add(item);
            DebugRuntimeLog.Write("watch", $"watcher started: folder='{watchFolder}' sub={sub}");
            return true;
        }

        /// <summary>
        /// DBに眠るすべての監視フォルダ設定を呼び覚まし、各地にFileSystemWatcher部隊を一斉配備する開幕の合図だ！📢
        /// </summary>
        private void CreateWatcher()
        {
            string snapshotDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            Stopwatch sw = Stopwatch.StartNew();
            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            int revision = Interlocked.Increment(ref _watcherCreationRevision);
            DebugRuntimeLog.TaskStart(nameof(CreateWatcher), $"db='{snapshotDbFullPath}'");
            Task creationTask = CreateWatcherAsync(snapshotDbFullPath, integrationMode, revision, sw);
            TrackWatcherCreationTask(creationTask, revision);
        }

        private void InvalidateWatcherCreation(string reason)
        {
            int revision = Interlocked.Increment(ref _watcherCreationRevision);
            DebugRuntimeLog.Write(
                "watch",
                $"watcher creation invalidated: revision={revision} reason={reason}"
            );
        }

        private void TrackWatcherCreationTask(Task creationTask, int revision)
        {
            lock (_watcherCreationTaskSync)
            {
                _watcherCreationTask = creationTask ?? Task.CompletedTask;
                if (creationTask != null && !creationTask.IsCompleted)
                {
                    _watcherCreationActiveTaskCount++;
                }
            }

            _ = ObserveWatcherCreationTaskAsync(creationTask, revision);
        }

        // watcher 作成は fire-and-forget にせず、失敗と完了をここで必ず回収する。
        private async Task ObserveWatcherCreationTaskAsync(Task creationTask, int revision)
        {
            try
            {
                if (creationTask != null)
                {
                    await creationTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create task failed: revision={revision} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
            finally
            {
                lock (_watcherCreationTaskSync)
                {
                    if (creationTask != null)
                    {
                        _watcherCreationActiveTaskCount = Math.Max(
                            0,
                            _watcherCreationActiveTaskCount - 1
                        );
                    }

                    if (ReferenceEquals(_watcherCreationTask, creationTask))
                    {
                        _watcherCreationTask = Task.CompletedTask;
                    }
                }
            }
        }

        private void LogWatcherCreationStateForShutdown(string reason)
        {
            Task creationTask;
            int activeTaskCount;
            lock (_watcherCreationTaskSync)
            {
                creationTask = _watcherCreationTask ?? Task.CompletedTask;
                activeTaskCount = _watcherCreationActiveTaskCount;
            }

            if (creationTask.IsCompleted && activeTaskCount < 1)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "lifecycle",
                $"watcher-creation shutdown handoff: reason={reason} latest_status={creationTask.Status} active={activeTaskCount}"
            );
        }

        // watch table 読み込みと Everything 判定は背景で済ませ、UI は登録だけを短く行う。
        private async Task CreateWatcherAsync(
            string snapshotDbFullPath,
            IntegrationMode integrationMode,
            int revision,
            Stopwatch sw
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                DebugRuntimeLog.Write("watch", "watcher create canceled: db is empty");
                WriteWatcherCreationTaskEnd(sw, "status=empty-db");
                return;
            }

            WatcherCreationPlan plan;
            try
            {
                plan = await Task.Run(
                        () => BuildWatcherCreationPlan(snapshotDbFullPath, integrationMode)
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create failed: db='{snapshotDbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
                WriteWatcherCreationTaskEnd(sw, $"status=build-failed err={ex.GetType().Name}");
                return;
            }

            if (plan.LoadFailed)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create canceled: watch table load failed. db='{snapshotDbFullPath}'"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=load-failed mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            int currentRevision = Volatile.Read(ref _watcherCreationRevision);
            if (revision != currentRevision)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create skipped by stale revision: db='{snapshotDbFullPath}' revision={revision} current={currentRevision}"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=stale-revision revision={revision} current={currentRevision} mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=dispatcher-shutdown mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync(
                    () => ApplyWatcherCreationPlan(snapshotDbFullPath, revision, sw, plan)
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create apply failed: db='{snapshotDbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=apply-failed err={ex.GetType().Name} mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
            }
        }

        private WatcherCreationPlan BuildWatcherCreationPlan(
            string snapshotDbFullPath,
            IntegrationMode integrationMode
        )
        {
            Stopwatch availabilitySw = Stopwatch.StartNew();
            AvailabilityResult availability = _indexProviderFacade.CheckAvailability(integrationMode);
            availabilitySw.Stop();
            string availabilityCategory = FileIndexReasonTable.ToCategory(availability.Reason);
            string availabilityAxis = FileIndexReasonTable.ToLogAxis(availability.Reason);

            Stopwatch watchTableLoadSw = Stopwatch.StartNew();
            string sql = $"SELECT * FROM watch where watch = 1";
            DataTable loadedWatchData = SQLite.GetData(snapshotDbFullPath, sql);
            WatchTableRowNormalizer.Normalize(loadedWatchData);
            watchTableLoadSw.Stop();
            int skippedByEverythingOnlyCount = 0;
            List<WatcherRegistrationPlanItem> items = [];

            if (loadedWatchData == null)
            {
                return WatcherCreationPlan.Failed(
                    integrationMode,
                    availability,
                    availabilityCategory,
                    availabilityAxis,
                    availabilitySw.ElapsedMilliseconds,
                    watchTableLoadSw.ElapsedMilliseconds
                );
            }

            Stopwatch folderPlanSw = Stopwatch.StartNew();
            int existingFolderCount = 0;
            foreach (DataRow row in loadedWatchData.Rows)
            {
                string checkFolder = row["dir"]?.ToString() ?? "";
                if (!Path.Exists(checkFolder))
                {
                    continue;
                }

                existingFolderCount++;
                bool sub = Convert.ToInt64(row["sub"]) == 1;
                string watcherDecisionReason;
                bool skipByEverything = ShouldSkipFileSystemWatcherByEverything(
                    checkFolder,
                    integrationMode,
                    availability,
                    out watcherDecisionReason
                );

                if (skipByEverything)
                {
                    skippedByEverythingOnlyCount++;
                }

                items.Add(
                    new WatcherRegistrationPlanItem(
                        checkFolder,
                        sub,
                        skipByEverything,
                        watcherDecisionReason
                    )
                );
            }
            folderPlanSw.Stop();

            DebugRuntimeLog.Write(
                "watch",
                $"watcher creation plan built: rows={loadedWatchData.Rows.Count} existing_folders={existingFolderCount} planned={items.Count} skipped={skippedByEverythingOnlyCount} mode={integrationMode} availability_axis={availabilityAxis} availability_category={availabilityCategory} availability={availability.Reason} availability_ms={availabilitySw.ElapsedMilliseconds} watch_table_load_ms={watchTableLoadSw.ElapsedMilliseconds} folder_plan_ms={folderPlanSw.ElapsedMilliseconds}"
            );

            return new WatcherCreationPlan(
                loadedWatchData,
                integrationMode,
                availability,
                availabilityCategory,
                availabilityAxis,
                skippedByEverythingOnlyCount,
                items,
                false,
                loadedWatchData.Rows.Count,
                existingFolderCount,
                availabilitySw.ElapsedMilliseconds,
                watchTableLoadSw.ElapsedMilliseconds,
                folderPlanSw.ElapsedMilliseconds
            );
        }

        private void ApplyWatcherCreationPlan(
            string snapshotDbFullPath,
            int revision,
            Stopwatch sw,
            WatcherCreationPlan plan
        )
        {
            int currentRevision = Volatile.Read(ref _watcherCreationRevision);
            if (revision != currentRevision)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create apply skipped by stale revision: db='{snapshotDbFullPath}' revision={revision} current={currentRevision}"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=apply-stale-revision revision={revision} current={currentRevision} mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            if (!AreSameMainDbPath(snapshotDbFullPath, MainVM?.DbInfo?.DBFullPath ?? ""))
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create skipped by db change: planned='{snapshotDbFullPath}' current='{MainVM?.DbInfo?.DBFullPath ?? ""}'"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=db-changed mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            watchData = plan.WatchData;
            if (watchData == null)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create canceled: watch table load failed. db='{snapshotDbFullPath}'"
                );
                WriteWatcherCreationTaskEnd(
                    sw,
                    $"status=apply-watchdata-null mode={plan.IntegrationMode} availability_axis={plan.AvailabilityAxis} availability_category={plan.AvailabilityCategory} availability={plan.Availability.Reason}"
                );
                return;
            }

            int watcherCount = 0;
            int skippedByEverythingOnlyCount = 0;
            AvailabilityResult applyAvailability = plan.Availability;
            string applyAvailabilityCategory = plan.AvailabilityCategory;
            string applyAvailabilityAxis = plan.AvailabilityAxis;
            long applyAvailabilityElapsedMs = 0;
            Stopwatch applySw = Stopwatch.StartNew();
            if (plan.IntegrationMode == IntegrationMode.On)
            {
                // 計画作成から登録までの間に Everything が落ちた場合は FSW 側へ戻す。
                Stopwatch applyAvailabilitySw = Stopwatch.StartNew();
                applyAvailability = _indexProviderFacade.CheckAvailability(plan.IntegrationMode);
                applyAvailabilitySw.Stop();
                applyAvailabilityElapsedMs = applyAvailabilitySw.ElapsedMilliseconds;
                applyAvailabilityCategory = FileIndexReasonTable.ToCategory(applyAvailability.Reason);
                applyAvailabilityAxis = FileIndexReasonTable.ToLogAxis(applyAvailability.Reason);
            }

            Stopwatch registrationSw = Stopwatch.StartNew();
            foreach (WatcherRegistrationPlanItem item in plan.Items)
            {
                bool skipByEverything = ShouldSkipFileSystemWatcherByEverything(
                    item.WatchFolder,
                    plan.IntegrationMode,
                    applyAvailability,
                    out string watcherDecisionReason
                );
                if (skipByEverything)
                {
                    skippedByEverythingOnlyCount++;
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher skipped by everything-only: category={applyAvailabilityAxis} folder='{item.WatchFolder}' reason_category={applyAvailabilityCategory} reason={watcherDecisionReason}"
                    );
                    continue;
                }

                if (plan.IntegrationMode == IntegrationMode.On)
                {
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher keep: category={applyAvailabilityAxis} folder='{item.WatchFolder}' reason_category={applyAvailabilityCategory} reason={watcherDecisionReason}"
                    );
                }
                if (RunWatcher(item.WatchFolder, item.IncludeSubdirectories))
                {
                    watcherCount++;
                }
            }
            registrationSw.Stop();
            applySw.Stop();

            DebugRuntimeLog.Write(
                "watch",
                $"watcher creation apply summary: rows={plan.WatchRowCount} existing_folders={plan.ExistingFolderCount} planned={plan.Items.Count} registered={watcherCount} skipped={skippedByEverythingOnlyCount} mode={plan.IntegrationMode} availability_axis={applyAvailabilityAxis} availability_category={applyAvailabilityCategory} availability={applyAvailability.Reason} plan_availability_ms={plan.AvailabilityElapsedMs} watch_table_load_ms={plan.WatchTableLoadElapsedMs} folder_plan_ms={plan.FolderPlanElapsedMs} apply_availability_ms={applyAvailabilityElapsedMs} registration_ms={registrationSw.ElapsedMilliseconds} apply_ms={applySw.ElapsedMilliseconds}"
            );

            WriteWatcherCreationTaskEnd(
                sw,
                $"status=applied count={watcherCount} skipped={skippedByEverythingOnlyCount} mode={plan.IntegrationMode} availability_axis={applyAvailabilityAxis} availability_category={applyAvailabilityCategory} availability={applyAvailability.Reason} plan_availability_ms={plan.AvailabilityElapsedMs} watch_table_load_ms={plan.WatchTableLoadElapsedMs} folder_plan_ms={plan.FolderPlanElapsedMs} apply_availability_ms={applyAvailabilityElapsedMs} registration_ms={registrationSw.ElapsedMilliseconds} apply_ms={applySw.ElapsedMilliseconds}"
            );
        }

        private static void WriteWatcherCreationTaskEnd(Stopwatch sw, string detail)
        {
            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CreateWatcher),
                $"{detail} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        // Everything専用監視を有効にできる条件を満たす場合、FileSystemWatcher作成をスキップする。
        private static bool ShouldSkipFileSystemWatcherByEverything(
            string watchFolder,
            IntegrationMode mode,
            AvailabilityResult availability,
            out string reason
        )
        {
            if (mode != IntegrationMode.On)
            {
                reason = "mode_not_on";
                return false;
            }

            if (!availability.CanUse)
            {
                reason = $"everything_unavailable:{availability.Reason}";
                return false;
            }

            if (!IsEverythingEligiblePath(watchFolder, out string eligibilityReason))
            {
                reason = $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}";
                return false;
            }

            reason = "everything_only_enabled";
            return true;
        }

        private sealed record WatcherCreationPlan(
            DataTable WatchData,
            IntegrationMode IntegrationMode,
            AvailabilityResult Availability,
            string AvailabilityCategory,
            string AvailabilityAxis,
            int SkippedByEverythingOnlyCount,
            List<WatcherRegistrationPlanItem> Items,
            bool LoadFailed,
            int WatchRowCount,
            int ExistingFolderCount,
            long AvailabilityElapsedMs,
            long WatchTableLoadElapsedMs,
            long FolderPlanElapsedMs
        )
        {
            internal static WatcherCreationPlan Failed(
                IntegrationMode integrationMode,
                AvailabilityResult availability,
                string availabilityCategory,
                string availabilityAxis,
                long availabilityElapsedMs,
                long watchTableLoadElapsedMs
            )
            {
                return new WatcherCreationPlan(
                    null,
                    integrationMode,
                    availability,
                    availabilityCategory,
                    availabilityAxis,
                    0,
                    [],
                    true,
                    0,
                    0,
                    availabilityElapsedMs,
                    watchTableLoadElapsedMs,
                    0
                );
            }
        }

        private sealed record WatcherRegistrationPlanItem(
            string WatchFolder,
            bool IncludeSubdirectories,
            bool SkipByEverything,
            string DecisionReason
        );
    }
}

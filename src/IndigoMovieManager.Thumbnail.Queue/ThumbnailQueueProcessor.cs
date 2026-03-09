using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【キュー処理の心臓部（コンシューマー）】🔥
    /// QueueDB（SQLite）を絶対の掟とし、裏でひたすらサムネ生成ジョブをさばき続ける最強の戦士だ！
    ///
    /// ＜アツい全体の流れ＞
    /// 1. DBから「未処理(Pending)」のジョブをごっそり取得し、誰にも触らせないようガッチリロック！🔒
    /// 2. 取ってきたジョブを Parallel.ForEachAsync で並列にブン回す（ThumbnailCreationServiceにバトンタッチ）！🏃‍♂️💨
    /// 3. 長丁場になりそうなら、定期的に「まだまだ処理中だぜ！」とDBに叫んで（ハートビート）ロックを延長！💓
    /// 4. 成功すれば「Done」の勲章を、失敗時は再試行回数を盛って「Pending」か「Failed」に叩き込む！💥
    /// </summary>
    public sealed class ThumbnailQueueProcessor
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string ThumbFileLogEnvName = "IMM_THUMB_FILE_LOG";
        private const string ProcessPriorityEnvName = "IMM_THUMB_PROCESS_PRIORITY";
        private static readonly object PerfLogLock = new();
        private static long _totalProcessedCount = 0;
        private static long _totalElapsedMs = 0;

        private const int DefaultMaxAttemptCount = 5;
        private const int LeaseHeartbeatSeconds = 30;
        private const int SlowLaneThrottleMinParallelism = 3;
        private const int RecoveryLaneReservedMinParallelism = 2;
        private const int RecoveryLaneConcurrency = 1;
        private const int SlowLaneConcurrency = 1;
        private const int RegularLaneBufferedLeaseCount = 4;
        private static readonly ProcessPriorityClass DefaultThumbnailWorkerPriorityClass =
            ProcessPriorityClass.BelowNormal;

        /// <summary>
        /// 全闘争の幕開け！キューの監視と処理を絶え間なく回し続けるメインループだ！
        /// アプリが息をしている限り、バックグラウンドで果てしなく働き続ける不眠不休のエンジン！⚙️
        /// </summary>
        public async Task RunAsync(
            Func<QueueDbService> queueDbServiceResolver,
            string ownerInstanceId,
            Func<QueueObj, CancellationToken, Task> createThumbAsync,
            int maxParallelism = 8,
            Func<int> maxParallelismResolver = null,
            Func<int> dynamicMinimumParallelismResolver = null,
            Func<bool> allowScaleUpResolver = null,
            Func<int> scaleUpDemandFactorResolver = null,
            int pollIntervalMs = 3000,
            int batchCooldownMs = 0,
            int leaseMinutes = 5,
            int leaseBatchSize = 8,
            Func<int?> preferredTabIndexResolver = null,
            Func<string> thermalDiskIdResolver = null,
            Func<string> usnMftVolumeResolver = null,
            Action<string> log = null,
            Func<CancellationToken, Task> onQueueDrainedAsync = null,
            Action<int, int, int, int> progressSnapshot = null,
            Action<QueueObj> onJobStarted = null,
            Action<QueueObj> onJobCompleted = null,
            IThumbnailQueueProgressPresenter progressPresenter = null,
            IAdminTelemetryClient adminTelemetryClient = null,
            ThumbnailQueueWorkerRole workerRole = ThumbnailQueueWorkerRole.All,
            CancellationToken cts = default
        )
        {
            if (queueDbServiceResolver == null)
            {
                throw new ArgumentNullException(nameof(queueDbServiceResolver));
            }
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException(
                    "ownerInstanceId is required.",
                    nameof(ownerInstanceId)
                );
            }
            if (createThumbAsync == null)
            {
                throw new ArgumentNullException(nameof(createThumbAsync));
            }

            string title = ResolveProgressTitle(workerRole);
            int safePollIntervalMs = pollIntervalMs < 100 ? 100 : pollIntervalMs;
            int safeBatchCooldownMs = batchCooldownMs < 0 ? 0 : batchCooldownMs;
            int safeLeaseMinutes = leaseMinutes < 1 ? 1 : leaseMinutes;
            Action<string> safeLog = log ?? (_ => { });
            IThumbnailQueueProgressPresenter safeProgressPresenter =
                progressPresenter ?? NoOpThumbnailQueueProgressPresenter.Instance;
            IAdminTelemetryClient safeAdminTelemetryClient =
                adminTelemetryClient ?? NoOpAdminTelemetryClient.Instance;
            ProcessPriorityClass? originalProcessPriorityClass = null;
            bool backgroundPriorityApplied = false;
            AdminTelemetryRequestContext adminTelemetryRequestContext =
                AdminTelemetryRuntimeResolver.CreateThumbnailRequestContext(ownerInstanceId);
            string lastAdminTelemetryLogKey = "";
            int initialConfiguredParallelism = ResolveConfiguredParallelism(
                maxParallelism,
                maxParallelismResolver
            );
            ThumbnailParallelController parallelController = new(initialConfiguredParallelism);
            int ResolveLatestConfiguredParallelism()
            {
                return ResolveConfiguredParallelism(maxParallelism, maxParallelismResolver);
            }
            int ResolveDynamicMinimumParallelism()
            {
                int configured = ResolveLatestConfiguredParallelism();
                if (dynamicMinimumParallelismResolver == null)
                {
                    return Math.Min(4, configured);
                }

                try
                {
                    int resolved = dynamicMinimumParallelismResolver();
                    if (resolved > configured)
                    {
                        resolved = configured;
                    }
                    return ThumbnailParallelController.Clamp(resolved);
                }
                catch
                {
                    return Math.Min(4, configured);
                }
            }
            bool ResolveAllowScaleUp()
            {
                if (allowScaleUpResolver == null)
                {
                    return true;
                }

                try
                {
                    return allowScaleUpResolver();
                }
                catch
                {
                    return true;
                }
            }
            int ResolveScaleUpDemandFactor()
            {
                if (scaleUpDemandFactorResolver == null)
                {
                    return 2;
                }

                try
                {
                    int resolved = scaleUpDemandFactorResolver();
                    return resolved < 1 ? 1 : resolved;
                }
                catch
                {
                    return 2;
                }
            }
            void EnsureBackgroundPriority()
            {
                if (backgroundPriorityApplied)
                {
                    return;
                }

                ProcessPriorityClass targetPriorityClass = ResolveThumbnailWorkerPriorityClass();
                if (
                    TryApplyProcessPriority(
                        targetPriorityClass,
                        safeLog,
                        out ProcessPriorityClass originalPriorityClass
                    )
                )
                {
                    originalProcessPriorityClass = originalPriorityClass;
                    backgroundPriorityApplied = true;
                }
            }
            void RestoreBackgroundPriority()
            {
                if (!backgroundPriorityApplied)
                {
                    return;
                }

                RestoreProcessPriority(originalProcessPriorityClass, safeLog);
                originalProcessPriorityClass = null;
                backgroundPriorityApplied = false;
            }

            try
            {
                // アプリ終了要求（cts）が来るまで無限ループで監視を続ける
                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    QueueDbService queueDbService = queueDbServiceResolver();
                    if (queueDbService == null)
                    {
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    // 【STEP 1: 処理対象の取得（リース）】
                    // DBから未処理のジョブを取得し、「自分が処理する」という印をつける
                    int configuredParallelism = ResolveConfiguredParallelism(
                        maxParallelism,
                        maxParallelismResolver
                    );
                    int currentParallelism = parallelController.EnsureWithinConfigured(
                        configuredParallelism
                    );
                    ReportProgressSnapshot(
                        progressSnapshot,
                        0,
                        0,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    int runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                        leaseBatchSize,
                        currentParallelism
                    );
                    List<QueueDbLeaseItem> leasedItems = AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        runtimeLeaseBatchSize,
                        safeLeaseMinutes,
                        preferredTabIndexResolver,
                        safeLog,
                        workerRole
                    );

                    // DB上で処理対象が無い時だけ待機し、後回しジョブ確認を呼ぶ。
                    if (leasedItems.Count < 1)
                    {
                        RestoreBackgroundPriority();
                        if (onQueueDrainedAsync != null)
                        {
                            await onQueueDrainedAsync(cts).ConfigureAwait(false);
                        }
                        ReportProgressSnapshot(
                            progressSnapshot,
                            0,
                            0,
                            currentParallelism,
                            ResolveLatestConfiguredParallelism()
                        );
                        await Task.Delay(safePollIntervalMs, cts).ConfigureAwait(false);
                        continue;
                    }

                    EnsureBackgroundPriority();

                    object progressLock = new();
                    int sessionCompletedCount = 0;
                    int sessionTotalCount = 0;
                    ReportProgressSnapshot(
                        progressSnapshot,
                        sessionCompletedCount,
                        sessionTotalCount,
                        currentParallelism,
                        ResolveLatestConfiguredParallelism()
                    );
                    IThumbnailQueueProgressHandle progress = NoOpThumbnailQueueProgressHandle.Instance;
                    try
                    {
                        // 表示層の失敗でキュー処理本体を止めない。
                        progress =
                            safeProgressPresenter.Show(title)
                            ?? NoOpThumbnailQueueProgressHandle.Instance;
                    }
                    catch (Exception ex)
                    {
                        safeLog($"consumer progress open failed: {ex.Message}");
                    }
                    safeLog("consumer progress opened.");

                    try
                    {
                        while (true)
                        {
                            if (leasedItems.Count < 1)
                            {
                                if (onQueueDrainedAsync != null)
                                {
                                    await onQueueDrainedAsync(cts).ConfigureAwait(false);
                                }

                                int activeCount = queueDbService.GetActiveQueueCount(
                                    ownerInstanceId
                                );
                                if (activeCount < 1)
                                {
                                    RestoreBackgroundPriority();
                                    safeLog(
                                        $"consumer progress close: reason=queue_empty session_done={sessionCompletedCount}"
                                    );
                                    ReportProgressSnapshot(
                                        progressSnapshot,
                                        sessionCompletedCount,
                                        sessionTotalCount,
                                        currentParallelism,
                                        ResolveLatestConfiguredParallelism()
                                    );
                                    break;
                                }

                                await Task.Delay(Math.Min(500, safePollIntervalMs), cts)
                                    .ConfigureAwait(false);
                                configuredParallelism = ResolveConfiguredParallelism(
                                    maxParallelism,
                                    maxParallelismResolver
                                );
                                currentParallelism = parallelController.EnsureWithinConfigured(
                                    configuredParallelism
                                );
                                ReportProgressSnapshot(
                                    progressSnapshot,
                                    sessionCompletedCount,
                                    sessionTotalCount,
                                    currentParallelism,
                                    ResolveLatestConfiguredParallelism()
                                );
                                runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                    leaseBatchSize,
                                    currentParallelism
                                );
                                leasedItems = AcquireLeasedItems(
                                    queueDbService,
                                    ownerInstanceId,
                                    runtimeLeaseBatchSize,
                                    safeLeaseMinutes,
                                    preferredTabIndexResolver,
                                    safeLog,
                                    workerRole
                                );
                                continue;
                            }

                            Stopwatch batchSw = Stopwatch.StartNew();
                            int completedCount = 0;
                            int failedCount = 0;
                            int recoveryCompletedCount = 0;
                            int recoveryFailedCount = 0;
                            // バッチ開始時点のアクティブ件数から、セッション総数(完了+残件)を更新する。
                            int activeCountAtBatchStart = queueDbService.GetActiveQueueCount(
                                ownerInstanceId
                            );
                            int estimatedTotal = sessionCompletedCount + activeCountAtBatchStart;
                            if (estimatedTotal > sessionTotalCount)
                            {
                                sessionTotalCount = estimatedTotal;
                            }
                            if (sessionTotalCount < 1)
                            {
                                sessionTotalCount = sessionCompletedCount + leasedItems.Count;
                            }

                            // 【STEP 2: 並列処理の実行】
                            // 取得したジョブリストを、指定された並列数（maxParallelism）で並列に生成する
                            configuredParallelism = ResolveConfiguredParallelism(
                                maxParallelism,
                                maxParallelismResolver
                            );
                            currentParallelism = parallelController.EnsureWithinConfigured(
                                configuredParallelism
                            );
                            if (activeCountAtBatchStart >= 2)
                            {
                                // 巨大ファイル混在時の1並列貼り付きを避けるため、
                                // バックログがある間は最低2並列を即時に確保する。
                                currentParallelism = parallelController.EnsureMinimum(
                                    configuredParallelism,
                                    2
                                );
                            }

                            int maxWorkerPoolParallelism = ThumbnailParallelController.Clamp(
                                int.MaxValue
                            );
                            DynamicParallelGate parallelGate = new(
                                currentParallelism,
                                maxWorkerPoolParallelism
                            );
                            bool hasRecoveryDemand =
                                workerRole == ThumbnailQueueWorkerRole.All
                                    && (
                                        leasedItems.Any(IsRecoveryLeaseItem)
                                        || queueDbService.HasRecoveryQueueDemand(ownerInstanceId)
                                    );
                            bool hasSlowDemand =
                                workerRole == ThumbnailQueueWorkerRole.All
                                    && (
                                        leasedItems.Any(IsSlowNonRecoveryLeaseItem)
                                        || queueDbService.HasSlowQueueDemand(
                                            ownerInstanceId,
                                            ThumbnailLaneClassifier.ResolveSlowLaneMinBytes(),
                                            maxAttemptCount: 0
                                        )
                                    );
                            bool enableRecoveryLane =
                                workerRole == ThumbnailQueueWorkerRole.All
                                && currentParallelism >= RecoveryLaneReservedMinParallelism
                                && hasRecoveryDemand;
                            bool enableSlowLane =
                                workerRole == ThumbnailQueueWorkerRole.All
                                && currentParallelism >= SlowLaneThrottleMinParallelism
                                && hasSlowDemand;
                            DynamicParallelGate regularLaneGate = new(
                                ResolveRegularLaneLimit(
                                    currentParallelism,
                                    enableRecoveryLane,
                                    enableSlowLane
                                ),
                                maxWorkerPoolParallelism
                            );
                            DynamicParallelGate recoveryLaneGate = enableRecoveryLane
                                ? new DynamicParallelGate(
                                    RecoveryLaneConcurrency,
                                    RecoveryLaneConcurrency
                                )
                                : null;
                            DynamicParallelGate slowLaneGate = enableSlowLane
                                ? new DynamicParallelGate(SlowLaneConcurrency, SlowLaneConcurrency)
                                : null;

                            void ApplyRegularLaneLimit(int liveParallelism, string reason)
                            {
                                int before = regularLaneGate.CurrentLimit;
                                int next = ResolveRegularLaneLimit(
                                    liveParallelism,
                                    enableRecoveryLane,
                                    enableSlowLane
                                );
                                regularLaneGate.SetLimit(next);
                                int after = regularLaneGate.CurrentLimit;
                                if (after != before)
                                {
                                    safeLog(
                                        $"lane throttle: regular_limit {before} -> {after} "
                                            + $"reason={reason} parallel={liveParallelism} recovery={enableRecoveryLane} slow={enableSlowLane}"
                                    );
                                }
                            }

                            ApplyRegularLaneLimit(currentParallelism, "batch-start");

                            using CancellationTokenSource parallelMonitorCts =
                                CancellationTokenSource.CreateLinkedTokenSource(cts);
                            Task parallelMonitorTask = RunParallelLimitMonitorAsync(
                                parallelGate,
                                ResolveLatestConfiguredParallelism,
                                parallelController,
                                safeLog,
                                applied => ApplyRegularLaneLimit(applied, "parallel-change"),
                                parallelMonitorCts.Token
                            );

                            int GetLiveParallelism()
                            {
                                return parallelGate.CurrentLimit;
                            }

                            Queue<QueueDbLeaseItem> pendingLeasedItems = new();
                            EnqueueLeasedItems(pendingLeasedItems, leasedItems);

                            static void EnqueueLeasedItems(
                                Queue<QueueDbLeaseItem> destination,
                                IEnumerable<QueueDbLeaseItem> source
                            )
                            {
                                if (destination == null || source == null)
                                {
                                    return;
                                }

                                foreach (QueueDbLeaseItem item in source)
                                {
                                    if (item != null)
                                    {
                                        destination.Enqueue(item);
                                    }
                                }
                            }

                            async Task ProcessLeasedItemAsync(
                                QueueDbLeaseItem leasedItem,
                                CancellationToken token
                            )
                            {
                                QueueObj queueObj = new()
                                {
                                    MainDbFullPath = leasedItem.MainDbFullPath,
                                    MovieFullPath = leasedItem.MoviePath,
                                    MovieSizeBytes = leasedItem.MovieSizeBytes,
                                    AttemptCount = leasedItem.AttemptCount,
                                    Tabindex = leasedItem.TabIndex,
                                    ThumbPanelPos = leasedItem.ThumbPanelPos,
                                    ThumbTimePos = leasedItem.ThumbTimePos,
                                    IsRescueRequest = leasedItem.IsRescueRequest,
                                };
                                ThumbnailExecutionLane lane = ThumbnailLaneClassifier.ResolveLane(
                                    leasedItem.MovieSizeBytes
                                );
                                bool isRecoveryItem = IsRecoveryLeaseItem(leasedItem);
                                bool isSlowNonRecoveryItem =
                                    !isRecoveryItem && lane == ThumbnailExecutionLane.Slow;
                                bool leaseEntered = false;
                                bool regularLaneEntered = false;
                                bool recoveryLaneEntered = false;
                                bool slowLaneEntered = false;
                                bool startedNotified = false;

                                try
                                {
                                    await parallelGate.WaitAsync(token).ConfigureAwait(false);
                                    leaseEntered = true;
                                    if (isRecoveryItem && recoveryLaneGate != null)
                                    {
                                        await recoveryLaneGate.WaitAsync(token)
                                            .ConfigureAwait(false);
                                        recoveryLaneEntered = true;
                                    }
                                    else
                                    {
                                        if (isSlowNonRecoveryItem && slowLaneGate != null)
                                        {
                                            await slowLaneGate.WaitAsync(token)
                                                .ConfigureAwait(false);
                                            slowLaneEntered = true;
                                        }
                                        else
                                        {
                                            await regularLaneGate.WaitAsync(token)
                                                .ConfigureAwait(false);
                                            regularLaneEntered = true;
                                        }
                                    }
                                    NotifyJobCallback(onJobStarted, queueObj);
                                    startedNotified = true;
                                    if (queueObj.IsRescueRequest)
                                    {
                                        safeLog(
                                            $"repair start: queue_id={leasedItem.QueueId} tab={queueObj.Tabindex} attempt={queueObj.AttemptCount} movie='{queueObj.MovieFullPath}'"
                                        );
                                    }

                                    try
                                    {
                                        // ハング1件で全補充が止まらないよう、各ワーカー完了ごとに次リースを回せる形で実行する。
                                        await ExecuteWithLeaseHeartbeatAsync(
                                                queueDbService,
                                                leasedItem,
                                                ownerInstanceId,
                                                safeLeaseMinutes,
                                                () => createThumbAsync(queueObj, token),
                                                safeLog,
                                                token
                                            )
                                            .ConfigureAwait(false);

                                        int updated = queueDbService.UpdateStatus(
                                            leasedItem.QueueId,
                                            ownerInstanceId,
                                            ThumbnailQueueStatus.Done,
                                            DateTime.UtcNow
                                        );
                                        if (updated < 1)
                                        {
                                            safeLog(
                                                $"consumer done skipped: queue_id={leasedItem.QueueId} owner={ownerInstanceId}"
                                            );
                                        }
                                        else if (queueObj.IsRescueRequest)
                                        {
                                            safeLog(
                                                $"repair success: queue_id={leasedItem.QueueId} tab={queueObj.Tabindex} movie='{queueObj.MovieFullPath}'"
                                            );
                                        }
                                    }
                                    catch (OperationCanceledException ex)
                                    {
                                        if (cts.IsCancellationRequested)
                                        {
                                            HandleCanceledItem(
                                                queueDbService,
                                                leasedItem,
                                                ownerInstanceId,
                                                ex,
                                                safeLog
                                            );
                                            throw;
                                        }

                                        HandleFailedItem(
                                            queueDbService,
                                            leasedItem,
                                            ownerInstanceId,
                                            ex,
                                            safeLog
                                        );
                                        if (!IsMainDbScopeChangedException(ex))
                                        {
                                            _ = Interlocked.Increment(ref failedCount);
                                            if (isRecoveryItem)
                                            {
                                                _ = Interlocked.Increment(ref recoveryFailedCount);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        HandleFailedItem(
                                            queueDbService,
                                            leasedItem,
                                            ownerInstanceId,
                                            ex,
                                            safeLog
                                        );
                                        if (!IsMainDbScopeChangedException(ex))
                                        {
                                            _ = Interlocked.Increment(ref failedCount);
                                            if (isRecoveryItem)
                                            {
                                                _ = Interlocked.Increment(ref recoveryFailedCount);
                                            }
                                        }
                                    }

                                    _ = Interlocked.Increment(ref completedCount);
                                    if (isRecoveryItem)
                                    {
                                        _ = Interlocked.Increment(ref recoveryCompletedCount);
                                    }
                                    int doneInSession = Interlocked.Increment(
                                        ref sessionCompletedCount
                                    );
                                    string reportTitle =
                                        $"{GetTabProgressTitle(leasedItem.TabIndex)} ({doneInSession}/{sessionTotalCount})";
                                    string message = leasedItem.MoviePath;
                                    int safeSessionTotalCount =
                                        sessionTotalCount < 1 ? 1 : sessionTotalCount;
                                    double totalProgress =
                                        (double)doneInSession * 100d / safeSessionTotalCount;
                                    if (totalProgress > 100d)
                                    {
                                        totalProgress = 100d;
                                    }

                                    lock (progressLock)
                                    {
                                        progress.Report(totalProgress, message, reportTitle, false);
                                    }

                                    ReportProgressSnapshot(
                                        progressSnapshot,
                                        doneInSession,
                                        sessionTotalCount,
                                        GetLiveParallelism(),
                                        ResolveLatestConfiguredParallelism()
                                    );
                                }
                                finally
                                {
                                    if (startedNotified)
                                    {
                                        NotifyJobCallback(onJobCompleted, queueObj);
                                    }
                                    if (slowLaneEntered)
                                    {
                                        slowLaneGate?.Release();
                                    }
                                    if (recoveryLaneEntered)
                                    {
                                        recoveryLaneGate?.Release();
                                    }
                                    if (regularLaneEntered)
                                    {
                                        regularLaneGate.Release();
                                    }
                                    if (leaseEntered)
                                    {
                                        parallelGate.Release();
                                    }
                                }
                            }

                            try
                            {
                                List<Task> activeWorkerTasks = [];

                                while (true)
                                {
                                    cts.ThrowIfCancellationRequested();

                                    int refillThreshold = Math.Max(1, GetLiveParallelism());
                                    while (pendingLeasedItems.Count < refillThreshold)
                                    {
                                        List<QueueDbLeaseItem> nextItems = AcquireLeasedItems(
                                            queueDbService,
                                            ownerInstanceId,
                                            runtimeLeaseBatchSize,
                                            safeLeaseMinutes,
                                            preferredTabIndexResolver,
                                            safeLog,
                                            workerRole
                                        );
                                        if (nextItems.Count < 1)
                                        {
                                            break;
                                        }

                                        EnqueueLeasedItems(pendingLeasedItems, nextItems);
                                    }

                                    int dispatchLimit = Math.Min(
                                        maxWorkerPoolParallelism,
                                        Math.Max(4, GetLiveParallelism() * 2)
                                    );
                                    while (
                                        pendingLeasedItems.Count > 0
                                        && activeWorkerTasks.Count < dispatchLimit
                                    )
                                    {
                                        QueueDbLeaseItem nextItem = pendingLeasedItems.Dequeue();
                                        activeWorkerTasks.Add(ProcessLeasedItemAsync(nextItem, cts));
                                    }

                                    if (activeWorkerTasks.Count < 1)
                                    {
                                        int activeCount = queueDbService.GetActiveQueueCount(
                                            ownerInstanceId
                                        );
                                        if (pendingLeasedItems.Count < 1 && activeCount < 1)
                                        {
                                            break;
                                        }

                                        await Task.Delay(250, cts).ConfigureAwait(false);
                                        continue;
                                    }

                                    Task completedTask = await Task
                                        .WhenAny(activeWorkerTasks)
                                        .ConfigureAwait(false);
                                    activeWorkerTasks.Remove(completedTask);
                                    await completedTask.ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                parallelMonitorCts.Cancel();
                                try
                                {
                                    await parallelMonitorTask.ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    // monitor停止時のキャンセルは想定内。
                                }
                            }

                            batchSw.Stop();
                            long batchMs = batchSw.ElapsedMilliseconds;
                            long totalCountAfter = Interlocked.Add(
                                ref _totalProcessedCount,
                                completedCount
                            );
                            long totalMsAfter = Interlocked.Add(ref _totalElapsedMs, batchMs);
                            ThumbnailEngineRuntimeSnapshot engineSnapshot =
                                ThumbnailEngineRuntimeStats.ConsumeWindow();
                            int activeCountAfterBatch = queueDbService.GetActiveQueueCount(
                                ownerInstanceId
                            );
                            int latestConfiguredParallelism = ResolveLatestConfiguredParallelism();
                            ThumbnailHighLoadInput highLoadInput = new(
                                batchProcessedCount: completedCount,
                                batchFailedCount: failedCount,
                                batchElapsedMs: batchMs,
                                queueActiveCount: activeCountAfterBatch,
                                currentParallelism: GetLiveParallelism(),
                                configuredParallelism: latestConfiguredParallelism,
                                hasSlowDemand: hasSlowDemand,
                                hasRecoveryDemand: hasRecoveryDemand,
                                engineSnapshot: engineSnapshot
                            );
                            string thermalDiskId = ResolveThermalDiskId(thermalDiskIdResolver);
                            string usnMftVolumeName = ResolveUsnMftVolumeName(
                                usnMftVolumeResolver
                            );
                            AdminTelemetryRuntimeSnapshot adminTelemetryRuntime =
                                await AdminTelemetryRuntimeResolver.ResolveAsync(
                                    safeAdminTelemetryClient,
                                    adminTelemetryRequestContext,
                                    highLoadInput,
                                    thermalDiskId,
                                    usnMftVolumeName,
                                    cts
                                ).ConfigureAwait(false);
                            highLoadInput = new ThumbnailHighLoadInput(
                                batchProcessedCount: completedCount,
                                batchFailedCount: failedCount,
                                batchElapsedMs: batchMs,
                                queueActiveCount: activeCountAfterBatch,
                                currentParallelism: GetLiveParallelism(),
                                configuredParallelism: latestConfiguredParallelism,
                                hasSlowDemand: hasSlowDemand,
                                hasRecoveryDemand: hasRecoveryDemand,
                                engineSnapshot: engineSnapshot,
                                thermalState: ConvertThermalSignalLevel(
                                    adminTelemetryRuntime.DiskThermalSnapshot.ThermalState
                                ),
                                usnMftState: ConvertUsnMftSignalLevel(
                                    adminTelemetryRuntime.UsnMftStatus.StatusKind
                                ),
                                usnMftLastScanLatencyMs:
                                    adminTelemetryRuntime.UsnMftStatus.LastScanLatencyMs,
                                usnMftJournalBacklogCount:
                                    adminTelemetryRuntime.UsnMftStatus.JournalBacklogCount
                            );
                            ThumbnailHighLoadScoreResult highLoadScore =
                                ThumbnailParallelController.CalculateHighLoadScore(highLoadInput);
                            string adminTelemetryLogKey =
                                $"{adminTelemetryRuntime.Mode}|{adminTelemetryRuntime.SystemLoadSource}|"
                                + $"{adminTelemetryRuntime.DiskThermalSource}|"
                                + $"{adminTelemetryRuntime.UsnMftSource}|"
                                + $"{adminTelemetryRuntime.FallbackKind}|"
                                + $"{adminTelemetryRuntime.FallbackReason}|"
                                + $"{adminTelemetryRuntime.DiskThermalFallbackKind}|"
                                + $"{adminTelemetryRuntime.DiskThermalFallbackReason}|"
                                + $"{adminTelemetryRuntime.UsnMftFallbackKind}|"
                                + $"{adminTelemetryRuntime.UsnMftFallbackReason}|"
                                + $"{(adminTelemetryRuntime.Capabilities.SupportsSystemLoad ? 1 : 0)}|"
                                + $"{(adminTelemetryRuntime.Capabilities.SupportsDiskThermal ? 1 : 0)}|"
                                + $"{(adminTelemetryRuntime.Capabilities.SupportsUsnMftStatus ? 1 : 0)}|"
                                + $"{adminTelemetryRuntime.UsnMftStatus.StatusKind}";
                            if (
                                !string.Equals(
                                    lastAdminTelemetryLogKey,
                                    adminTelemetryLogKey,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                string adminMode = adminTelemetryRuntime.Mode switch
                                {
                                    AdminTelemetryRuntimeMode.Service => "service",
                                    _ => "internal-only",
                                };
                                string systemLoadSource =
                                    adminTelemetryRuntime.SystemLoadSource
                                    == AdminTelemetrySignalSourceKind.Service
                                        ? "service"
                                        : "internal";
                                string diskThermalSource =
                                    adminTelemetryRuntime.DiskThermalSource
                                    == AdminTelemetrySignalSourceKind.Service
                                        ? "service"
                                        : "internal";
                                string usnMftSource =
                                    adminTelemetryRuntime.UsnMftSource
                                    == AdminTelemetrySignalSourceKind.Service
                                        ? "service"
                                        : "internal";
                                string adminTelemetryCategory =
                                    adminTelemetryRuntime.Mode == AdminTelemetryRuntimeMode.Service
                                        ? "info"
                                        : "fallback";
                                safeLog(
                                    $"admin telemetry state: category={adminTelemetryCategory} "
                                        + $"mode={adminMode} system_load_source={systemLoadSource} "
                                        + $"disk_thermal_source={diskThermalSource} "
                                        + $"usnmft_source={usnMftSource} "
                                        + $"fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.FallbackKind)} "
                                        + $"fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.FallbackReason)} "
                                        + $"disk_thermal_fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.DiskThermalFallbackKind)} "
                                        + $"disk_thermal_fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.DiskThermalFallbackReason)} "
                                        + $"usnmft_fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.UsnMftFallbackKind)} "
                                        + $"usnmft_fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.UsnMftFallbackReason)} "
                                        + $"supports_system_load={(adminTelemetryRuntime.Capabilities.SupportsSystemLoad ? 1 : 0)} "
                                        + $"supports_disk_thermal={(adminTelemetryRuntime.Capabilities.SupportsDiskThermal ? 1 : 0)} "
                                        + $"supports_usnmft={(adminTelemetryRuntime.Capabilities.SupportsUsnMftStatus ? 1 : 0)} "
                                        + $"thermal_state={adminTelemetryRuntime.DiskThermalSnapshot.ThermalState} "
                                        + $"usnmft_status={adminTelemetryRuntime.UsnMftStatus.StatusKind}"
                                );
                                lastAdminTelemetryLogKey = adminTelemetryLogKey;
                            }
                            int nextParallelism = parallelController.EvaluateNext(
                                latestConfiguredParallelism,
                                completedCount,
                                failedCount,
                                activeCountAfterBatch,
                                engineSnapshot,
                                safeLog,
                                dynamicMinimumParallelism: ResolveDynamicMinimumParallelism(),
                                allowScaleUp: ResolveAllowScaleUp(),
                                scaleUpDemandFactor: ResolveScaleUpDemandFactor(),
                                highLoadInput: highLoadInput
                            );
                            string gpuMode =
                                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName) ?? "off";
                            WritePerfLog(
                                $"thumb queue summary: gpu={gpuMode}, parallel={GetLiveParallelism()}, "
                                    + $"parallel_next={nextParallelism}, parallel_configured={latestConfiguredParallelism}, "
                                    + $"regular_parallel={regularLaneGate.CurrentLimit}, recovery_lane={(enableRecoveryLane ? 1 : 0)}, recovery_demand={(hasRecoveryDemand ? 1 : 0)}, slow_lane={(enableSlowLane ? 1 : 0)}, slow_demand={(hasSlowDemand ? 1 : 0)}, "
                                    + $"admin_mode={(adminTelemetryRuntime.Mode == AdminTelemetryRuntimeMode.Service ? "service" : "internal-only")}, "
                                    + $"admin_system_load_source={(adminTelemetryRuntime.SystemLoadSource == AdminTelemetrySignalSourceKind.Service ? "service" : "internal")}, "
                                    + $"admin_disk_thermal_source={(adminTelemetryRuntime.DiskThermalSource == AdminTelemetrySignalSourceKind.Service ? "service" : "internal")}, "
                                    + $"admin_usnmft_source={(adminTelemetryRuntime.UsnMftSource == AdminTelemetrySignalSourceKind.Service ? "service" : "internal")}, "
                                    + $"admin_fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.FallbackKind)}, "
                                    + $"admin_fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.FallbackReason)}, "
                                    + $"admin_disk_thermal_fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.DiskThermalFallbackKind)}, "
                                    + $"admin_disk_thermal_fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.DiskThermalFallbackReason)}, "
                                    + $"admin_usnmft_fallback_kind={FormatAdminTelemetryFallbackKind(adminTelemetryRuntime.UsnMftFallbackKind)}, "
                                    + $"admin_usnmft_fallback_reason={FormatAdminTelemetryReason(adminTelemetryRuntime.UsnMftFallbackReason)}, "
                                    + $"admin_supports_system_load={(adminTelemetryRuntime.Capabilities.SupportsSystemLoad ? 1 : 0)}, "
                                    + $"admin_supports_disk_thermal={(adminTelemetryRuntime.Capabilities.SupportsDiskThermal ? 1 : 0)}, "
                                    + $"admin_supports_usnmft={(adminTelemetryRuntime.Capabilities.SupportsUsnMftStatus ? 1 : 0)}, "
                                    + $"thermal_state={adminTelemetryRuntime.DiskThermalSnapshot.ThermalState}, "
                                    + $"thermal_temp_c={adminTelemetryRuntime.DiskThermalSnapshot.TemperatureCelsius}, "
                                    + $"usnmft_status={adminTelemetryRuntime.UsnMftStatus.StatusKind}, "
                                    + $"usnmft_available={(adminTelemetryRuntime.UsnMftStatus.Available ? 1 : 0)}, "
                                    + $"usnmft_latency_ms={adminTelemetryRuntime.UsnMftStatus.LastScanLatencyMs}, "
                                    + $"usnmft_backlog={adminTelemetryRuntime.UsnMftStatus.JournalBacklogCount}, "
                                    + $"system_queue_backlog={adminTelemetryRuntime.SystemLoadSnapshot.QueueBacklogCount}, "
                                    + $"system_slow_backlog={adminTelemetryRuntime.SystemLoadSnapshot.SlowLaneBacklogCount}, "
                                    + $"system_recovery_backlog={adminTelemetryRuntime.SystemLoadSnapshot.RecoveryLaneBacklogCount}, "
                                    + $"system_sample_ms={adminTelemetryRuntime.SystemLoadSnapshot.SampleWindowMs}, "
                                    + $"high_load={highLoadScore.HighLoadScore:0.000}, error_score={highLoadScore.ErrorScore:0.000}, queue_score={highLoadScore.QueuePressureScore:0.000}, slow_score={highLoadScore.SlowBacklogScore:0.000}, recovery_score={highLoadScore.RecoveryBacklogScore:0.000}, throughput_score={highLoadScore.ThroughputPenaltyScore:0.000}, thermal_score={highLoadScore.ThermalScore:0.000}, usnmft_score={highLoadScore.UsnMftScore:0.000}, "
                                    + $"batch_count={completedCount}, batch_ms={batchMs}, "
                                    + $"batch_failed={failedCount}, recovery_done={recoveryCompletedCount}, recovery_failed={recoveryFailedCount}, "
                                    + $"active={activeCountAfterBatch}, "
                                    + $"autogen_transient_fail={engineSnapshot.AutogenTransientFailureCount}, "
                                    + $"autogen_retry_success={engineSnapshot.AutogenRetrySuccessCount}, "
                                    + $"fallback_1pass={engineSnapshot.FallbackToFfmpegOnePassCount}, "
                                    + $"total_count={totalCountAfter}, total_ms={totalMsAfter}, "
                                    + $"{ThumbnailQueueMetrics.CreateSummary()}"
                            );

                            currentParallelism = nextParallelism;
                            configuredParallelism = latestConfiguredParallelism;
                            ReportProgressSnapshot(
                                progressSnapshot,
                                sessionCompletedCount,
                                sessionTotalCount,
                                GetLiveParallelism(),
                                ResolveLatestConfiguredParallelism()
                            );
                            runtimeLeaseBatchSize = ResolveLeaseBatchSize(
                                leaseBatchSize,
                                currentParallelism
                            );
                            leasedItems = AcquireLeasedItems(
                                queueDbService,
                                ownerInstanceId,
                                runtimeLeaseBatchSize,
                                safeLeaseMinutes,
                                preferredTabIndexResolver,
                                safeLog,
                                workerRole
                            );
                            if (safeBatchCooldownMs > 0 && leasedItems.Count > 0)
                            {
                                // slow 用の低負荷モードでは、バッチごとに小休止して
                                // CPU/I/O の貼り付きを少し緩める。
                                safeLog(
                                    $"consumer cooldown: wait_ms={safeBatchCooldownMs} next_batch={leasedItems.Count}"
                                );
                                await Task.Delay(safeBatchCooldownMs, cts).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        lock (progressLock)
                        {
                            progress.Dispose();
                        }
                        safeLog("consumer progress closed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                string msg =
                    $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : サムネイルキュー処理をキャンセルしました。";
                Debug.WriteLine(msg);
                safeLog(msg);
                throw;
            }
            catch (Exception e)
            {
                string msg = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss} : {e.Message}";
                Debug.WriteLine(msg);
                safeLog(msg);
                throw;
            }
            finally
            {
                RestoreBackgroundPriority();
            }
        }

        /// <summary>
        /// リース取得の共通窓口だ！タブ優先度の解決やログ出力をここで一手に引き受け、コードをスッキリ保つぜ！🧹
        /// </summary>
        private static List<QueueDbLeaseItem> AcquireLeasedItems(
            QueueDbService queueDbService,
            string ownerInstanceId,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Action<string> log,
            ThumbnailQueueWorkerRole workerRole
        )
        {
            int? preferredTabIndex = null;
            if (preferredTabIndexResolver != null)
            {
                try
                {
                    int? resolved = preferredTabIndexResolver();
                    preferredTabIndex =
                        (resolved.HasValue && resolved.Value >= 0) ? resolved : null;
                }
                catch (Exception ex)
                {
                    log?.Invoke($"preferred tab resolver failed: {ex.Message}");
                }
            }

            DateTime utcNow = DateTime.UtcNow;
            TimeSpan leaseDuration = TimeSpan.FromMinutes(leaseMinutes);
            List<QueueDbLeaseItem> leasedItems = [];
            long slowLaneMinMovieSizeBytes = ThumbnailLaneClassifier.ResolveSlowLaneMinBytes();
            if (workerRole == ThumbnailQueueWorkerRole.Normal)
            {
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: leaseBatchSize,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        maxAttemptCount: 0,
                        maxMovieSizeBytes: Math.Max(0, slowLaneMinMovieSizeBytes - 1),
                        leaseOrder: QueueDbLeaseOrder.MovieSizeAsc
                    )
                );
                if (
                    leasedItems.Count < 1
                    && !queueDbService.HasNormalQueueDemand(
                        ownerInstanceId,
                        slowLaneMinMovieSizeBytes,
                        maxAttemptCount: 0
                    )
                )
                {
                    leasedItems.AddRange(
                        queueDbService.GetPendingAndLease(
                            ownerInstanceId,
                            takeCount: leaseBatchSize,
                            leaseDuration: leaseDuration,
                            utcNow: utcNow,
                            preferredTabIndex: preferredTabIndex,
                            maxAttemptCount: 0,
                            minMovieSizeBytes: slowLaneMinMovieSizeBytes,
                            leaseOrder: QueueDbLeaseOrder.MovieSizeAsc
                        )
                    );
                    if (leasedItems.Count > 0)
                    {
                        log?.Invoke(
                            $"consumer lease: normal delegated slow-initial because regular queue is empty acquired={leasedItems.Count}"
                        );
                    }
                }
                SortLeasedItemsByLane(leasedItems);
                long dedicatedLeaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(
                    leasedItems.Count
                );
                if (leasedItems.Count > 0)
                {
                    log?.Invoke(
                        $"consumer lease: acquired={leasedItems.Count} total={dedicatedLeaseTotal} role={workerRole}"
                    );
                }
                return leasedItems;
            }

            if (workerRole == ThumbnailQueueWorkerRole.Idle)
            {
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: leaseBatchSize,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        minAttemptCount: 1,
                        leaseOrder: QueueDbLeaseOrder.MovieSizeDesc
                    )
                );

                int remainingIdleCount = leaseBatchSize - leasedItems.Count;
                if (remainingIdleCount > 0)
                {
                    leasedItems.AddRange(
                        queueDbService.GetPendingAndLease(
                            ownerInstanceId,
                            takeCount: remainingIdleCount,
                            leaseDuration: leaseDuration,
                            utcNow: utcNow,
                            preferredTabIndex: preferredTabIndex,
                            maxAttemptCount: 0,
                            minMovieSizeBytes: slowLaneMinMovieSizeBytes,
                            leaseOrder: QueueDbLeaseOrder.MovieSizeDesc
                        )
                    );
                }

                SortLeasedItemsByLane(leasedItems);
                long dedicatedLeaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(
                    leasedItems.Count
                );
                if (leasedItems.Count > 0)
                {
                    log?.Invoke(
                        $"consumer lease: acquired={leasedItems.Count} total={dedicatedLeaseTotal} role={workerRole}"
                    );
                }
                return leasedItems;
            }

            bool reserveRecoveryLease =
                leaseBatchSize >= RecoveryLaneReservedMinParallelism
                && queueDbService.HasRecoveryQueueDemand(ownerInstanceId);

            if (reserveRecoveryLease)
            {
                // 再試行ジョブが待っている時だけ、1件を先に確保して専用枠へ流しやすくする。
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: 1,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        minAttemptCount: 1,
                        leaseOrder: QueueDbLeaseOrder.MovieSizeDesc
                    )
                );
            }

            int remainingCount = leaseBatchSize - leasedItems.Count;
            bool reserveSlowLease =
                remainingCount > 0
                && leaseBatchSize >= SlowLaneThrottleMinParallelism
                && queueDbService.HasSlowQueueDemand(
                    ownerInstanceId,
                    slowLaneMinMovieSizeBytes,
                    maxAttemptCount: 0
                );
            if (reserveSlowLease)
            {
                // 巨大動画が待っている時だけ、1件を先に確保して低速枠へ流しやすくする。
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: 1,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        maxAttemptCount: 0,
                        minMovieSizeBytes: slowLaneMinMovieSizeBytes,
                        leaseOrder: QueueDbLeaseOrder.MovieSizeDesc
                    )
                );
                remainingCount = leaseBatchSize - leasedItems.Count;
            }

            if (remainingCount > 0)
            {
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: remainingCount,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        maxAttemptCount: 0,
                        maxMovieSizeBytes: Math.Max(0, slowLaneMinMovieSizeBytes - 1),
                        leaseOrder: QueueDbLeaseOrder.MovieSizeAsc
                    )
                );
                remainingCount = leaseBatchSize - leasedItems.Count;
            }

            if (remainingCount > 0)
            {
                leasedItems.AddRange(
                    queueDbService.GetPendingAndLease(
                        ownerInstanceId,
                        takeCount: remainingCount,
                        leaseDuration: leaseDuration,
                        utcNow: utcNow,
                        preferredTabIndex: preferredTabIndex,
                        maxAttemptCount: 0,
                        minMovieSizeBytes: slowLaneMinMovieSizeBytes,
                        leaseOrder: QueueDbLeaseOrder.MovieSizeAsc
                    )
                );
            }
            SortLeasedItemsByLane(leasedItems);
            long leaseTotal = ThumbnailQueueMetrics.RecordLeaseAcquired(leasedItems.Count);
            if (leasedItems.Count > 0)
            {
                log?.Invoke(
                    $"consumer lease: acquired={leasedItems.Count} total={leaseTotal} recovery_reserved={reserveRecoveryLease} slow_reserved={reserveSlowLease}"
                );
            }
            return leasedItems;
        }

        private static string ResolveProgressTitle(ThumbnailQueueWorkerRole workerRole)
        {
            return workerRole switch
            {
                ThumbnailQueueWorkerRole.Normal => "サムネイル作成中(通常)",
                ThumbnailQueueWorkerRole.Idle => "サムネイル作成中(ゆっくり)",
                _ => "サムネイル作成中",
            };
        }

        // バックグラウンド処理中だけ通常系のプロセス優先度を下げ、UI操作への食い込みを少し抑える。
        private static bool TryApplyProcessPriority(
            ProcessPriorityClass targetPriorityClass,
            Action<string> log,
            out ProcessPriorityClass originalPriorityClass
        )
        {
            originalPriorityClass = ProcessPriorityClass.Normal;
            try
            {
                Process current = Process.GetCurrentProcess();
                originalPriorityClass = current.PriorityClass;
                if (current.PriorityClass != targetPriorityClass)
                {
                    current.PriorityClass = targetPriorityClass;
                }

                log?.Invoke(
                    $"consumer priority applied: current={current.PriorityClass} original={originalPriorityClass}"
                );
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"consumer priority apply skipped: {ex.Message}");
                return false;
            }
        }

        // UIと同居する通常系は BelowNormal に固定し、ffmpeg.exe 側だけ Idle へ落とす。
        private static ProcessPriorityClass ResolveThumbnailWorkerPriorityClass()
        {
            string configured = Environment.GetEnvironmentVariable(ProcessPriorityEnvName)?.Trim()
                ?? "";
            if (string.IsNullOrWhiteSpace(configured))
            {
                return DefaultThumbnailWorkerPriorityClass;
            }

            return Enum.TryParse(configured, ignoreCase: true, out ProcessPriorityClass parsed)
                ? parsed
                : DefaultThumbnailWorkerPriorityClass;
        }

        // キューが空になったら元の優先度へ戻し、通常操作の足を引っ張らないようにする。
        private static void RestoreProcessPriority(
            ProcessPriorityClass? originalPriorityClass,
            Action<string> log
        )
        {
            if (!originalPriorityClass.HasValue)
            {
                return;
            }

            try
            {
                Process current = Process.GetCurrentProcess();
                if (current.PriorityClass != originalPriorityClass.Value)
                {
                    current.PriorityClass = originalPriorityClass.Value;
                }

                log?.Invoke($"consumer priority restored: current={current.PriorityClass}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"consumer priority restore skipped: {ex.Message}");
            }
        }

        // バッチ先頭へ再試行を寄せ、その次に小さい通常動画、最後に巨大動画を並べる。
        // これにより通常系は小さい順で回り、巨大/再試行は専用枠へ自然に吸い込まれる。
        private static void SortLeasedItemsByLane(List<QueueDbLeaseItem> leasedItems)
        {
            if (leasedItems == null || leasedItems.Count < 2)
            {
                return;
            }

            leasedItems.Sort((left, right) =>
            {
                bool leftRecovery = IsRecoveryLeaseItem(left);
                bool rightRecovery = IsRecoveryLeaseItem(right);
                if (leftRecovery != rightRecovery)
                {
                    return leftRecovery ? -1 : 1;
                }

                ThumbnailExecutionLane leftLane = ThumbnailLaneClassifier.ResolveLane(
                    left?.MovieSizeBytes ?? 0
                );
                ThumbnailExecutionLane rightLane = ThumbnailLaneClassifier.ResolveLane(
                    right?.MovieSizeBytes ?? 0
                );
                int rankDiff = ThumbnailLaneClassifier.ResolveRank(leftLane)
                    - ThumbnailLaneClassifier.ResolveRank(rightLane);
                if (rankDiff != 0)
                {
                    return rankDiff;
                }

                long leftSize = Math.Max(0, left?.MovieSizeBytes ?? 0);
                long rightSize = Math.Max(0, right?.MovieSizeBytes ?? 0);
                return leftLane switch
                {
                    ThumbnailExecutionLane.Slow => rightSize.CompareTo(leftSize),
                    _ => leftSize.CompareTo(rightSize),
                };
            });
        }

        /// <summary>
        /// 長時間ジョブの命綱！定期的にリース期限を延長し、他のプロセスにジョブを横取りされないように死守するぜ！🛡️
        /// </summary>
        private static async Task ExecuteWithLeaseHeartbeatAsync(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            int leaseMinutes,
            Func<Task> processingAction,
            Action<string> log,
            CancellationToken cts
        )
        {
            Task processingTask = processingAction();

            while (true)
            {
                // キャンセル時は即座にループを抜け、終了処理を優先する。
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(LeaseHeartbeatSeconds), cts);
                Task completed = await Task.WhenAny(processingTask, delayTask)
                    .ConfigureAwait(false);

                if (completed == processingTask)
                {
                    await processingTask.ConfigureAwait(false);
                    return;
                }

                // 終了要求時はジョブ完了待ちせず、外側へキャンセルを伝播させる。
                cts.ThrowIfCancellationRequested();

                DateTime nowUtc = DateTime.UtcNow;
                try
                {
                    queueDbService.ExtendLease(
                        leasedItem.QueueId,
                        ownerInstanceId,
                        nowUtc.AddMinutes(leaseMinutes),
                        nowUtc
                    );
                }
                catch (Exception ex)
                {
                    log?.Invoke(
                        $"lease extend failed: queue_id={leasedItem.QueueId} message={ex.Message}"
                    );
                }
            }
        }

        /// <summary>
        /// 失敗時の駆け込み寺！まだ再試行の余地があるか判定し、Pendingに戻すかFailedの烙印を押す運命の分かれ道だ！⚖️
        /// </summary>
        private static void HandleFailedItem(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            Exception ex,
            Action<string> log
        )
        {
            if (IsMainDbScopeChangedException(ex))
            {
                int restored = queueDbService.UpdateStatus(
                    leasedItem.QueueId,
                    ownerInstanceId,
                    ThumbnailQueueStatus.Pending,
                    DateTime.UtcNow,
                    ex.Message,
                    incrementAttemptCount: false
                );
                if (restored < 1)
                {
                    log?.Invoke(
                        $"consumer status skipped: category=db-scope-changed queue_id={leasedItem.QueueId} message={ex.Message}"
                    );
                }
                else
                {
                    log?.Invoke(
                        $"consumer deferred: category=db-scope-changed queue_id={leasedItem.QueueId} next={ThumbnailQueueStatus.Pending}"
                    );
                }
                return;
            }

            bool exceeded = leasedItem.AttemptCount + 1 >= DefaultMaxAttemptCount;
            bool missingFile =
                string.IsNullOrWhiteSpace(leasedItem.MoviePath)
                || !Path.Exists(leasedItem.MoviePath);
            bool retryable = !exceeded && !missingFile;
            ThumbnailQueueStatus nextStatus = retryable
                ? ThumbnailQueueStatus.Pending
                : ThumbnailQueueStatus.Failed;
            long failedTotal = ThumbnailQueueMetrics.RecordFailed();

            int updated = queueDbService.UpdateStatus(
                leasedItem.QueueId,
                ownerInstanceId,
                nextStatus,
                DateTime.UtcNow,
                ex.Message,
                incrementAttemptCount: retryable
            );

            if (updated < 1)
            {
                log?.Invoke(
                    $"consumer status skipped: category=error queue_id={leasedItem.QueueId} next={nextStatus} message={ex.Message}"
                );
                return;
            }

            if (failedTotal <= 20 || failedTotal % 50 == 0)
            {
                log?.Invoke(
                    $"consumer failed: category=error queue_id={leasedItem.QueueId} next={nextStatus} "
                        + $"retryable={retryable} failed_total={failedTotal}"
                );
            }

            if (leasedItem.IsRescueRequest)
            {
                log?.Invoke(
                    $"repair failed: queue_id={leasedItem.QueueId} next={nextStatus} retryable={retryable} "
                        + $"attempt={leasedItem.AttemptCount + (retryable ? 1 : 0)} movie='{leasedItem.MoviePath}' message={ex.Message}"
                );
            }
        }

        // 停止要求で中断したジョブは失敗扱いにせず、AttemptCountを増やさずPendingへ戻す。
        private static void HandleCanceledItem(
            QueueDbService queueDbService,
            QueueDbLeaseItem leasedItem,
            string ownerInstanceId,
            OperationCanceledException ex,
            Action<string> log
        )
        {
            int restored = queueDbService.UpdateStatus(
                leasedItem.QueueId,
                ownerInstanceId,
                ThumbnailQueueStatus.Pending,
                DateTime.UtcNow,
                "operation canceled by stop request",
                incrementAttemptCount: false
            );

            if (restored < 1)
            {
                log?.Invoke(
                    $"consumer cancel skipped: queue_id={leasedItem.QueueId} owner={ownerInstanceId} message={ex.Message}"
                );
                return;
            }

            log?.Invoke(
                $"consumer canceled: queue_id={leasedItem.QueueId} next={ThumbnailQueueStatus.Pending} attempt={leasedItem.AttemptCount}"
            );

            if (leasedItem.IsRescueRequest)
            {
                log?.Invoke(
                    $"repair canceled: queue_id={leasedItem.QueueId} next={ThumbnailQueueStatus.Pending} attempt={leasedItem.AttemptCount} movie='{leasedItem.MoviePath}'"
                );
            }
        }

        private static bool IsMainDbScopeChangedException(Exception ex)
        {
            return ex is ThumbnailMainDbScopeChangedException;
        }

        private static void ReportProgressSnapshot(
            Action<int, int, int, int> progressSnapshot,
            int completedCount,
            int totalCount,
            int currentParallelism,
            int configuredParallelism
        )
        {
            if (progressSnapshot == null)
            {
                return;
            }

            try
            {
                progressSnapshot(
                    Math.Max(0, completedCount),
                    Math.Max(0, totalCount),
                    Math.Max(0, currentParallelism),
                    Math.Max(0, configuredParallelism)
                );
            }
            catch
            {
                // 進捗通知失敗はキュー処理本体を止めない。
            }
        }

        private static void NotifyJobCallback(Action<QueueObj> callback, QueueObj queueObj)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(queueObj);
            }
            catch
            {
                // UI通知失敗でジョブ処理を止めない。
            }
        }

        private static string GetTabProgressTitle(int tabIndex)
        {
            return tabIndex switch
            {
                0 => "サムネイル作成中(Small)",
                1 => "サムネイル作成中(Big)",
                2 => "サムネイル作成中(Grid)",
                3 => "サムネイル作成中(List)",
                4 => "サムネイル作成中(Big10)",
                _ => "サムネイル作成中",
            };
        }

        /// <summary>
        /// 処理速度の計測用！バッチ単位や累計のイケてる数値をログにバシッと刻み込むぜ！📊
        /// </summary>
        private static void WritePerfLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);
            if (!IsThumbFileLogEnabled())
            {
                return;
            }

            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
                Directory.CreateDirectory(baseDir);
                string logPath = Path.Combine(baseDir, "thumb_decode.log");

                lock (PerfLogLock)
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // ログ書き込み失敗時も処理継続を優先する。
            }
        }

        private static string FormatAdminTelemetryReason(string reason)
        {
            return string.IsNullOrWhiteSpace(reason) ? "none" : reason.Replace(' ', '_');
        }

        private static string FormatAdminTelemetryFallbackKind(
            AdminTelemetryFallbackKind fallbackKind
        )
        {
            return fallbackKind switch
            {
                AdminTelemetryFallbackKind.None => "none",
                AdminTelemetryFallbackKind.AccessDenied => "access-denied",
                AdminTelemetryFallbackKind.Timeout => "timeout",
                _ => "unavailable",
            };
        }

        private static string ResolveThermalDiskId(Func<string> thermalDiskIdResolver)
        {
            if (thermalDiskIdResolver == null)
            {
                return "";
            }

            try
            {
                return thermalDiskIdResolver() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string ResolveUsnMftVolumeName(Func<string> usnMftVolumeResolver)
        {
            if (usnMftVolumeResolver == null)
            {
                return "";
            }

            try
            {
                return usnMftVolumeResolver() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static ThumbnailThermalSignalLevel ConvertThermalSignalLevel(
            DiskThermalState thermalState
        )
        {
            return thermalState switch
            {
                DiskThermalState.Warning => ThumbnailThermalSignalLevel.Warning,
                DiskThermalState.Critical => ThumbnailThermalSignalLevel.Critical,
                DiskThermalState.Normal => ThumbnailThermalSignalLevel.Normal,
                _ => ThumbnailThermalSignalLevel.Unavailable,
            };
        }

        private static ThumbnailUsnMftSignalLevel ConvertUsnMftSignalLevel(
            UsnMftStatusKind statusKind
        )
        {
            return statusKind switch
            {
                UsnMftStatusKind.Ready => ThumbnailUsnMftSignalLevel.Ready,
                UsnMftStatusKind.Busy => ThumbnailUsnMftSignalLevel.Busy,
                UsnMftStatusKind.AccessDenied => ThumbnailUsnMftSignalLevel.AccessDenied,
                _ => ThumbnailUsnMftSignalLevel.Unavailable,
            };
        }

        /// <summary>
        /// 隠しコマンド発動判定！環境変数「IMM_THUMB_FILE_LOG」があればファイルログを有効化する、普段は静かな眠れる獅子だ🤫
        /// </summary>
        private static bool IsThumbFileLogEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(ThumbFileLogEnvName);
            if (string.IsNullOrWhiteSpace(mode))
            {
                return false;
            }
            string normalized = mode.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes";
        }

        // 設定値と実行中設定変更を吸収して、今回バッチの上限並列を決める。
        private static int ResolveConfiguredParallelism(
            int defaultParallelism,
            Func<int> maxParallelismResolver
        )
        {
            int resolved = defaultParallelism;
            if (maxParallelismResolver != null)
            {
                try
                {
                    resolved = maxParallelismResolver();
                }
                catch
                {
                    resolved = defaultParallelism;
                }
            }

            return ThumbnailParallelController.Clamp(resolved);
        }

        // リース取得件数は、通常系4件 + ゆっくり1件 + 再試行1件を先読みできる数を既定にする。
        private static int ResolveLeaseBatchSize(int configuredLeaseBatchSize, int currentParallelism)
        {
            if (configuredLeaseBatchSize > 0)
            {
                return configuredLeaseBatchSize;
            }

            int safeParallelism = Math.Max(1, currentParallelism);
            int reservedLaneCount = 0;
            if (safeParallelism >= RecoveryLaneReservedMinParallelism)
            {
                reservedLaneCount++;
            }
            if (safeParallelism >= SlowLaneThrottleMinParallelism)
            {
                reservedLaneCount++;
            }

            int bufferedLeaseCount = RegularLaneBufferedLeaseCount + reservedLaneCount;
            return Math.Max(safeParallelism, bufferedLeaseCount);
        }

        // RecoveryレーンやSlowレーンを有効化した場合、通常系へ割り当てる並列枠を減らす。
        private static int ResolveRegularLaneLimit(
            int liveParallelism,
            bool enableRecoveryLane,
            bool enableSlowLane
        )
        {
            int safeParallelism = liveParallelism < 1 ? 1 : liveParallelism;
            int reservedCount = 0;
            if (enableRecoveryLane)
            {
                reservedCount++;
            }
            if (enableSlowLane)
            {
                reservedCount++;
            }
            if (reservedCount < 1)
            {
                return safeParallelism;
            }

            return Math.Max(1, safeParallelism - reservedCount);
        }

        // 再キュー（再試行）に入ったジョブだけをRecoveryレーンへ流す。
        private static bool IsRecoveryLeaseItem(QueueDbLeaseItem leasedItem)
        {
            return leasedItem != null && leasedItem.AttemptCount > 0;
        }

        // Recovery以外で巨大動画に入るジョブだけをSlowレーン扱いにする。
        private static bool IsSlowNonRecoveryLeaseItem(QueueDbLeaseItem leasedItem)
        {
            if (leasedItem == null || IsRecoveryLeaseItem(leasedItem))
            {
                return false;
            }

            return ThumbnailLaneClassifier.ResolveLane(leasedItem.MovieSizeBytes)
                == ThumbnailExecutionLane.Slow;
        }

        // 処理中にバッファが尽きても、その場で次のリースを取りに行けるようにする。
        // これで巨大ファイル1件が残っている間も、空いたワーカーへ次ジョブを継続投入できる。
        private static async IAsyncEnumerable<QueueDbLeaseItem> EnumerateLeasedItemsAsync(
            QueueDbService queueDbService,
            string ownerInstanceId,
            IReadOnlyList<QueueDbLeaseItem> initialItems,
            int leaseBatchSize,
            int leaseMinutes,
            Func<int?> preferredTabIndexResolver,
            Action<string> log,
            ThumbnailQueueWorkerRole workerRole,
            [EnumeratorCancellation] CancellationToken cts = default
        )
        {
            Queue<QueueDbLeaseItem> buffer = new();
            if (initialItems != null)
            {
                for (int i = 0; i < initialItems.Count; i++)
                {
                    buffer.Enqueue(initialItems[i]);
                }
            }

            while (!cts.IsCancellationRequested)
            {
                if (buffer.Count < 1)
                {
                    List<QueueDbLeaseItem> nextItems = AcquireLeasedItems(
                        queueDbService,
                        ownerInstanceId,
                        leaseBatchSize,
                        leaseMinutes,
                        preferredTabIndexResolver,
                        log,
                        workerRole
                    );
                    if (nextItems.Count > 0)
                    {
                        for (int i = 0; i < nextItems.Count; i++)
                        {
                            buffer.Enqueue(nextItems[i]);
                        }
                    }
                    else
                    {
                        int activeCount = queueDbService.GetActiveQueueCount(ownerInstanceId);
                        if (activeCount < 1)
                        {
                            yield break;
                        }

                        // 実行中ジョブが残っている間は短い間隔で再取得を試みる。
                        await Task.Delay(250, cts).ConfigureAwait(false);
                        continue;
                    }
                }

                if (buffer.Count < 1)
                {
                    continue;
                }

                yield return buffer.Dequeue();
            }
        }

        // 実行中バッチでも設定値変更へ追従できるよう、並列上限ゲートを周期更新する。
        private static async Task RunParallelLimitMonitorAsync(
            DynamicParallelGate parallelGate,
            Func<int> resolveConfiguredParallelism,
            ThumbnailParallelController parallelController,
            Action<string> log,
            Action<int> onAppliedParallelism,
            CancellationToken cts
        )
        {
            if (parallelGate == null || resolveConfiguredParallelism == null || parallelController == null)
            {
                return;
            }

            int lastApplied = parallelGate.CurrentLimit;
            while (!cts.IsCancellationRequested)
            {
                int configured = resolveConfiguredParallelism();
                int next = parallelController.EnsureWithinConfigured(configured);
                parallelGate.SetLimit(next);
                int applied = parallelGate.CurrentLimit;
                if (applied != lastApplied)
                {
                    log?.Invoke(
                        $"parallel apply: {lastApplied} -> {applied} configured={configured}"
                    );
                    lastApplied = applied;
                }

                if (onAppliedParallelism != null)
                {
                    try
                    {
                        onAppliedParallelism(applied);
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"parallel apply callback failed: {ex.Message}");
                    }
                }

                await Task.Delay(200, cts).ConfigureAwait(false);
            }
        }

        // 並列数の上限を動的に調整する軽量ゲート。
        // ForEach自体は最大プール(24)で回し、実行許可数だけここで制御する。
        private sealed class DynamicParallelGate
        {
            private readonly object syncRoot = new();
            private readonly SemaphoreSlim semaphore;
            private readonly int maxLimit;
            private int targetLimit;
            private int pendingReduction;

            public DynamicParallelGate(int initialLimit, int maxLimit)
            {
                this.maxLimit = maxLimit < 1 ? 1 : maxLimit;
                int clampedInitial = initialLimit;
                if (clampedInitial < 1)
                {
                    clampedInitial = 1;
                }
                if (clampedInitial > this.maxLimit)
                {
                    clampedInitial = this.maxLimit;
                }

                targetLimit = clampedInitial;
                semaphore = new SemaphoreSlim(clampedInitial, this.maxLimit);
            }

            public int CurrentLimit
            {
                get
                {
                    lock (syncRoot)
                    {
                        return targetLimit;
                    }
                }
            }

            public async Task WaitAsync(CancellationToken cts)
            {
                await semaphore.WaitAsync(cts).ConfigureAwait(false);
            }

            public void Release()
            {
                lock (syncRoot)
                {
                    if (pendingReduction > 0)
                    {
                        pendingReduction--;
                        return;
                    }
                }

                semaphore.Release();
            }

            public void SetLimit(int requestedLimit)
            {
                int clamped = requestedLimit;
                if (clamped < 1)
                {
                    clamped = 1;
                }
                if (clamped > maxLimit)
                {
                    clamped = maxLimit;
                }

                lock (syncRoot)
                {
                    if (clamped == targetLimit)
                    {
                        return;
                    }

                    if (clamped > targetLimit)
                    {
                        int deltaUp = clamped - targetLimit;
                        targetLimit = clamped;

                        int consumePending = Math.Min(deltaUp, pendingReduction);
                        pendingReduction -= consumePending;
                        int releaseCount = deltaUp - consumePending;
                        if (releaseCount > 0)
                        {
                            semaphore.Release(releaseCount);
                        }
                        return;
                    }

                    int deltaDown = targetLimit - clamped;
                    targetLimit = clamped;
                    pendingReduction += deltaDown;
                    while (pendingReduction > 0 && semaphore.Wait(0))
                    {
                        pendingReduction--;
                    }
                }
            }
        }
    }
}

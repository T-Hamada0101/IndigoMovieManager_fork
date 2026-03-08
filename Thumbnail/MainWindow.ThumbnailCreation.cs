using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.Ipc;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // サムネイル監視タスクを再起動する。
        private void RestartThumbnailTask()
        {
            DebugRuntimeLog.TaskStart(nameof(RestartThumbnailTask));
            ClearThumbnailQueue();

            if (ShouldUseThumbnailCoordinatorMode())
            {
                RestartThumbnailCoordinatorSupervisor();
                DebugRuntimeLog.TaskEnd(nameof(RestartThumbnailTask));
                return;
            }

            // 既存タスクのキャンセル
            _thumbCheckCts.Cancel();
            DebugRuntimeLog.Write("task", "thumbnail token canceled for restart.");

            // 新しいCancellationTokenSourceを生成
            _thumbCheckCts = new CancellationTokenSource();

            // 新しいトークンでタスクを再起動
            DebugRuntimeLog.TaskStart(nameof(CheckThumbAsync), "trigger=RestartThumbnailTask");
            _thumbCheckTask = CheckThumbAsync(_thumbCheckCts.Token);
            DebugRuntimeLog.TaskEnd(nameof(RestartThumbnailTask));
        }

        /// <summary>
        /// CheckThumbAsync サムネイル作成用に起動時にぶん投げるタスク。常時起動。終了条件はねぇ。
        /// </summary>
        private async Task CheckThumbAsync(CancellationToken cts = default)
        {
            string endStatus = "completed";
            DebugRuntimeLog.TaskStart(
                nameof(CheckThumbAsync),
                $"parallel={GetThumbnailQueueMaxParallelism()} poll_ms={GetThumbnailQueuePollIntervalMs()} cooldown_ms={GetThumbnailQueueBatchCooldownMs()}"
            );
            using CancellationTokenSource serviceSupervisorCts =
                CancellationTokenSource.CreateLinkedTokenSource(cts);
            Task adminTelemetryServiceTask = RunAdminTelemetryServiceSupervisorAsync(
                serviceSupervisorCts.Token
            );
            try
            {
                if (ShouldUseThumbnailCoordinatorMode())
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-coordinator",
                        "coordinator mode enabled: MainWindow worker supervisor is bypassed."
                    );
                    await WaitUntilCoordinatorModeChangesAsync(cts).ConfigureAwait(false);
                    return;
                }

                if (_thumbnailWorkerProcessManager.IsWorkerAvailable())
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-worker",
                        "worker mode enabled: external worker supervisor started."
                    );
                    await _thumbnailWorkerProcessManager
                        .RunSupervisorAsync(
                            ResolveThumbnailWorkerLaunchConfigs,
                            message => DebugRuntimeLog.Write("thumbnail-worker", message),
                            cts
                        )
                        .ConfigureAwait(false);
                    return;
                }

                ThumbnailFallbackModeDecision fallbackDecision =
                    ThumbnailFallbackModeResolver.Resolve();
                if (!fallbackDecision.AllowInProcessFallback)
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-worker",
                        $"worker exe not found and in-process fallback is disabled. reason={fallbackDecision.Reason}"
                    );
                    await WaitForExternalWorkerAvailabilityAsync(cts).ConfigureAwait(false);
                    return;
                }

                DebugRuntimeLog.Write(
                    "thumbnail-worker",
                    $"worker exe not found. fallback to in-process consumer. reason={fallbackDecision.Reason}"
                );
                await RunThumbnailQueueConsumerInProcessAsync(cts).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                endStatus = "canceled";
                throw;
            }
            catch (Exception ex)
            {
                endStatus = $"fault message='{ex.Message}'";
                throw;
            }
            finally
            {
                serviceSupervisorCts.Cancel();
                // 裏の監視タスク終了はここで待ち、キャンセルは正常系として飲み込む。
                if (adminTelemetryServiceTask != null)
                {
                    try
                    {
                        await adminTelemetryServiceTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 明示停止時のキャンセルは想定内。
                    }
                }
                DebugRuntimeLog.TaskEnd(nameof(CheckThumbAsync), $"status={endStatus}");
            }
        }

        private async Task RunAdminTelemetryServiceSupervisorAsync(CancellationToken cts)
        {
            if (!_adminTelemetryServiceProcessManager.IsServiceAvailable())
            {
                return;
            }

            await _adminTelemetryServiceProcessManager
                .RunSupervisorAsync(
                    message => DebugRuntimeLog.Write("admin-telemetry-service", message),
                    cts
                )
                .ConfigureAwait(false);
        }

        private bool ShouldUseThumbnailCoordinatorMode()
        {
            return _thumbnailCoordinatorProcessManager.IsCoordinatorAvailable();
        }

        private async Task WaitUntilCoordinatorModeChangesAsync(CancellationToken cts)
        {
            while (ShouldUseThumbnailCoordinatorMode() && !cts.IsCancellationRequested)
            {
                await Task.Delay(5000, cts).ConfigureAwait(false);
            }
        }

        // fallback を許可しない運用では、worker 配置を待ってから supervisor 本線へ戻す。
        private async Task WaitForExternalWorkerAvailabilityAsync(CancellationToken cts)
        {
            while (!_thumbnailWorkerProcessManager.IsWorkerAvailable())
            {
                await Task.Delay(5000, cts).ConfigureAwait(false);
            }

            DebugRuntimeLog.Write(
                "thumbnail-worker",
                "worker exe detected. external worker supervisor will start."
            );
            await _thumbnailWorkerProcessManager
                .RunSupervisorAsync(
                    ResolveThumbnailWorkerLaunchConfigs,
                    message => DebugRuntimeLog.Write("thumbnail-worker", message),
                    cts
                )
                .ConfigureAwait(false);
        }

        // Worker未配置時だけ従来consumerへ戻し、移行途中でも処理が止まらないようにする。
        private async Task RunThumbnailQueueConsumerInProcessAsync(CancellationToken cts)
        {
            while (true)
            {
                cts.ThrowIfCancellationRequested();
                try
                {
                    _thumbnailProgressRuntime.SetPersistentMainDbFullPath(
                        MainVM?.DbInfo?.DBFullPath ?? ""
                    );
                    ThumbnailWorkerResolvedSettings fallbackSettings =
                        ResolveCurrentThumbnailQueueSettings(ThumbnailQueueWorkerRole.All);
                    ThumbnailWorkerExecutionEnvironment.Apply(
                        fallbackSettings,
                        message => DebugRuntimeLog.Write("thumbnail-worker", message)
                    );
                    await _thumbnailQueueProcessor
                        .RunAsync(
                            ResolveCurrentQueueDbService,
                            thumbnailQueueOwnerInstanceId,
                            (queueObj, token) => CreateThumbAsync(queueObj, false, token),
                            maxParallelism: fallbackSettings.MaxParallelism,
                            maxParallelismResolver: () =>
                                ResolveCurrentThumbnailQueueSettings(ThumbnailQueueWorkerRole.All)
                                    .MaxParallelism,
                            dynamicMinimumParallelismResolver: () =>
                                ResolveCurrentThumbnailQueueSettings(ThumbnailQueueWorkerRole.All)
                                    .DynamicMinimumParallelism,
                            allowScaleUpResolver: () =>
                                ResolveCurrentThumbnailQueueSettings(ThumbnailQueueWorkerRole.All)
                                    .AllowDynamicScaleUp,
                            scaleUpDemandFactorResolver: () =>
                                ResolveCurrentThumbnailQueueSettings(ThumbnailQueueWorkerRole.All)
                                    .ScaleUpDemandFactor,
                            pollIntervalMs: fallbackSettings.PollIntervalMs,
                            batchCooldownMs: fallbackSettings.BatchCooldownMs,
                            leaseMinutes: 5,
                            leaseBatchSize: 0,
                            preferredTabIndexResolver: ResolvePreferredThumbnailTabIndex,
                            thermalDiskIdResolver: ResolveCurrentAdminTelemetryDiskId,
                            usnMftVolumeResolver: ResolveCurrentAdminTelemetryVolumeName,
                            log: message => DebugRuntimeLog.Write("queue-consumer", message),
                            progressSnapshot: (
                                completed,
                                total,
                                currentParallel,
                                configuredParallel
                            ) =>
                            {
                                int configuredParallelForUi = GetThumbnailQueueMaxParallelism();
                                _thumbnailProgressRuntime.UpdateSessionProgress(
                                    completed,
                                    total,
                                    currentParallel,
                                    configuredParallelForUi
                                );
                                RequestThumbnailProgressSnapshotRefresh();
                            },
                            onJobStarted: queueObj =>
                            {
                                _thumbnailProgressRuntime.MarkJobStarted(queueObj);
                                RequestThumbnailProgressSnapshotRefresh();
                            },
                            onJobCompleted: queueObj =>
                            {
                                _thumbnailProgressRuntime.MarkJobCompleted(queueObj);
                                RequestThumbnailProgressSnapshotRefresh();
                                MarkThumbnailFailedListDirty(
                                    incrementRevision: false,
                                    reason: "queue-job-completed"
                                );
                            },
                            progressPresenter: _thumbnailQueueProgressPresenter,
                            adminTelemetryClient: CreateAdminTelemetryClient(),
                            cts: cts
                        )
                        .ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-consumer",
                        $"consumer restart scheduled: {ex.Message}"
                    );
                    await Task.Delay(500, cts).ConfigureAwait(false);
                }
            }
        }

        // 現在のDBと設定から、外部Workerの起動構成を組み立てる。
        private IReadOnlyList<ThumbnailWorkerProcessManager.ThumbnailWorkerLaunchConfig>
            ResolveThumbnailWorkerLaunchConfigs()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return [];
            }

            ThumbnailWorkerSettingsSaveResult settingsRef = SaveThumbnailWorkerSettingsSnapshot();
            if (settingsRef == null || string.IsNullOrWhiteSpace(settingsRef.SnapshotFilePath))
            {
                return [];
            }

            return
            [
                new ThumbnailWorkerProcessManager.ThumbnailWorkerLaunchConfig
                {
                    WorkerRole = ThumbnailQueueWorkerRole.Normal,
                    MainDbFullPath = mainDbFullPath,
                    OwnerInstanceId = thumbnailNormalWorkerOwnerInstanceId,
                    SettingsSnapshotPath = settingsRef.SnapshotFilePath,
                    SettingsVersionToken = settingsRef.VersionToken,
                },
                new ThumbnailWorkerProcessManager.ThumbnailWorkerLaunchConfig
                {
                    WorkerRole = ThumbnailQueueWorkerRole.Idle,
                    MainDbFullPath = mainDbFullPath,
                    OwnerInstanceId = thumbnailIdleWorkerOwnerInstanceId,
                    SettingsSnapshotPath = settingsRef.SnapshotFilePath,
                    SettingsVersionToken = settingsRef.VersionToken,
                },
            ];
        }

        // Worker が読む設定の生値を snapshot へ保存し、起動参照を返す。
        private ThumbnailWorkerSettingsSaveResult SaveThumbnailWorkerSettingsSnapshot()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            string thumbFolder = ResolveWorkerThumbFolder(dbName, MainVM?.DbInfo?.ThumbFolder ?? "");
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || string.IsNullOrWhiteSpace(dbName))
            {
                return null;
            }

            ThumbnailWorkerSettingsSnapshot snapshot = BuildThumbnailWorkerSettingsSnapshot(
                mainDbFullPath: mainDbFullPath,
                dbName: dbName,
                thumbFolder: thumbFolder,
                leaseMinutes: 5
            );

            return ThumbnailWorkerSettingsStore.SaveSnapshot(snapshot);
        }

        // 進捗ViewerはDB接続中だけ起動し、Worker owner単位で同じスナップショットを見る。
        private ThumbnailProgressViewerProcessManager.ThumbnailProgressViewerLaunchConfig ResolveThumbnailProgressViewerLaunchConfig()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || string.IsNullOrWhiteSpace(dbName))
            {
                return null;
            }

            return new ThumbnailProgressViewerProcessManager.ThumbnailProgressViewerLaunchConfig
            {
                MainDbFullPath = mainDbFullPath,
                DbName = dbName,
                NormalOwnerInstanceId = thumbnailNormalWorkerOwnerInstanceId,
                IdleOwnerInstanceId = thumbnailIdleWorkerOwnerInstanceId,
                CoordinatorOwnerInstanceId = thumbnailCoordinatorOwnerInstanceId,
            };
        }

        // Coordinator 接続点を先に固定し、本体と外側運転席を並列で実装できるようにする。
        private ThumbnailCoordinatorProcessManager.ThumbnailCoordinatorLaunchConfig ResolveThumbnailCoordinatorLaunchConfig()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath) || string.IsNullOrWhiteSpace(dbName))
            {
                return null;
            }

            ThumbnailWorkerSettingsSaveResult settingsRef = SaveThumbnailWorkerSettingsSnapshot();
            if (settingsRef == null || string.IsNullOrWhiteSpace(settingsRef.SnapshotFilePath))
            {
                return null;
            }

            return new ThumbnailCoordinatorProcessManager.ThumbnailCoordinatorLaunchConfig
            {
                MainDbFullPath = mainDbFullPath,
                DbName = dbName,
                OwnerInstanceId = thumbnailCoordinatorOwnerInstanceId,
                NormalWorkerOwnerInstanceId = thumbnailNormalWorkerOwnerInstanceId,
                IdleWorkerOwnerInstanceId = thumbnailIdleWorkerOwnerInstanceId,
                InitialSettingsSnapshotPath = settingsRef.SnapshotFilePath,
            };
        }

        // DBのthumb設定が空でも、既存UIと同じ既定フォルダへ寄せてWorker停止を防ぐ。
        private static string ResolveWorkerThumbFolder(string dbName, string thumbFolder)
        {
            if (!string.IsNullOrWhiteSpace(thumbFolder))
            {
                return thumbFolder;
            }

            if (string.IsNullOrWhiteSpace(dbName))
            {
                return "";
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "Thumb", dbName);
        }

        private IAdminTelemetryClient CreateAdminTelemetryClient()
        {
            return new NamedPipeAdminTelemetryClient();
        }

        // まずは現在DBがあるドライブを代表値として渡し、service 側の取得器を差し替えやすくする。
        private string ResolveCurrentAdminTelemetryDiskId()
        {
            return ResolveDriveRootPath(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private string ResolveCurrentAdminTelemetryVolumeName()
        {
            return ResolveDriveRootPath(MainVM?.DbInfo?.DBFullPath ?? "");
        }

        private static string ResolveDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            }
            catch
            {
                return "";
            }
        }

        // ブックマーク用の単一フレームサムネイルを作成する。
        private async Task CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            bool created = await _thumbnailCreationService.CreateBookmarkThumbAsync(
                movieFullPath,
                saveThumbPath,
                capturePos
            );
            if (!created)
            {
                return;
            }

            await Task.Delay(1000);
            BookmarkList.Items.Refresh();
        }

        // 将来の別UIから再利用するため、動画インデックス修復APIの呼び出し口を先に用意しておく。
        private async Task<VideoIndexRepairResult> RepairVideoIndexFromUiAsync(
            string movieFullPath,
            string outputPath,
            CancellationToken cts = default
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath) || string.IsNullOrWhiteSpace(outputPath))
            {
                return new VideoIndexRepairResult
                {
                    IsSuccess = false,
                    InputPath = movieFullPath ?? "",
                    OutputPath = outputPath ?? "",
                    ErrorMessage = "movie path or output path is empty",
                };
            }

            try
            {
                VideoIndexRepairResult result = await _thumbnailCreationService
                    .RepairVideoIndexAsync(movieFullPath, outputPath, cts)
                    .ConfigureAwait(false);
                DebugRuntimeLog.Write(
                    "index-repair-ui",
                    $"repair ui call: movie='{movieFullPath}', output='{outputPath}', success={result.IsSuccess}, err='{result.ErrorMessage}'"
                );
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "index-repair-ui",
                    $"repair ui call failed: movie='{movieFullPath}', output='{outputPath}', err='{ex.Message}'"
                );
                return new VideoIndexRepairResult
                {
                    IsSuccess = false,
                    InputPath = movieFullPath,
                    OutputPath = outputPath,
                    ErrorMessage = ex.Message,
                };
            }
        }

        /// <summary>
        /// サムネイル作成本体
        /// </summary>
        /// <param name="queueObj">取り出したQueueの中身</param>
        /// <param name="IsManual">マニュアル作成かどうか</param>
        private async Task CreateThumbAsync(
            QueueObj queueObj,
            bool IsManual = false,
            CancellationToken cts = default
        )
        {
            string currentMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            string currentDbName = MainVM?.DbInfo?.DBName ?? "";
            string currentThumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
            string jobId =
                $"movie_id={queueObj?.MovieId} tab={queueObj?.Tabindex} manual={IsManual}";
            DebugRuntimeLog.TaskStart(nameof(CreateThumbAsync), jobId);
            try
            {
                // DB切替後に旧DBジョブが残っていても、新DB側では作らない。
                if (
                    queueObj != null
                    && !string.IsNullOrWhiteSpace(queueObj.MainDbFullPath)
                    && !string.Equals(
                        queueObj.MainDbFullPath,
                        currentMainDbFullPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new ThumbnailMainDbScopeChangedException(
                        queueObj.MainDbFullPath,
                        currentMainDbFullPath,
                        queueObj.MovieFullPath
                    );
                }

                // QueueDBリース経路ではMovieId/Hashが欠落し得るため、UI側一覧から補完する。
                long resolvedMovieId = await ResolveMovieIdByPathAsync(queueObj)
                    .ConfigureAwait(false);
                var result = await _thumbnailCreationService.CreateThumbAsync(
                    queueObj,
                    currentDbName,
                    currentThumbFolder,
                    Properties.Settings.Default.IsResizeThumb,
                    IsManual,
                    cts
                );

                // 生成失敗は例外としてキュー層へ伝播し、Failedで可視化する。
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"thumbnail create failed: movie='{queueObj?.MovieFullPath}', tab={queueObj?.Tabindex}, reason='{result.ErrorMessage}'"
                    );
                }

                var saveThumbFileName = result.SaveThumbFileName;
                if (!Path.Exists(saveThumbFileName))
                {
                    throw new FileNotFoundException(
                        $"thumbnail output not found: '{saveThumbFileName}'",
                        saveThumbFileName
                    );
                }
                if (!IsManual)
                {
                    string previewCacheKey = "";
                    long previewRevision = 0;
                    bool hasMemoryPreview = TryStoreThumbnailProgressPreview(
                        queueObj,
                        result.PreviewFrame,
                        out previewCacheKey,
                        out previewRevision
                    );
                    if (!hasMemoryPreview)
                    {
                        previewCacheKey = ThumbnailProgressRuntime.CreateWorkerKey(queueObj);
                        previewRevision = DateTime.UtcNow.Ticks;
                    }

                    ThumbnailPreviewLatencyTracker.RecordSaved(
                        previewCacheKey,
                        previewRevision,
                        saveThumbFileName
                    );
                    _thumbnailProgressRuntime.MarkThumbnailSaved(
                        queueObj,
                        saveThumbFileName,
                        previewCacheKey,
                        previewRevision
                    );
                    RequestThumbnailProgressSnapshotRefresh();
                }

                // サムネイル作成完了時に保存先パスをログ出力（一時的）
                DebugRuntimeLog.Write(
                    "thumbnail-path",
                    $"Created thumbnail saved to: {saveThumbFileName}"
                );

                // 動画長はDB値とズレることがあるため、作成時の計測値で補正する。
                if (result.DurationSec.HasValue)
                {
                    bool needUpdateDb = false;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var item = MainVM
                            .MovieRecs.Where(x => IsSameMovieForQueue(x, queueObj, resolvedMovieId))
                            .FirstOrDefault();
                        if (item == null)
                        {
                            return;
                        }

                        string tSpan = new TimeSpan(
                            0,
                            0,
                            (int)(long)result.DurationSec.Value
                        ).ToString(@"hh\:mm\:ss");
                        if (item.Movie_Length != tSpan)
                        {
                            item.Movie_Length = tSpan;
                            needUpdateDb = true;
                        }
                    });

                    if (needUpdateDb)
                    {
                        if (
                            resolvedMovieId > 0
                            && !string.IsNullOrWhiteSpace(currentMainDbFullPath)
                        )
                        {
                            UpdateMovieSingleColumn(
                                currentMainDbFullPath,
                                resolvedMovieId,
                                "movie_length",
                                result.DurationSec.Value
                            );
                        }
                    }
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    string liveMainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
                    if (
                        !string.Equals(
                            liveMainDbFullPath,
                            currentMainDbFullPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        DebugRuntimeLog.Write(
                            "thumbnail",
                            $"ui update skipped by db switch: requested='{currentMainDbFullPath}' current='{liveMainDbFullPath}' movie='{queueObj?.MovieFullPath}'"
                        );
                        return;
                    }

                    foreach (
                        var item in MainVM.MovieRecs.Where(x =>
                            IsSameMovieForQueue(x, queueObj, resolvedMovieId)
                        )
                    )
                    {
                        switch (queueObj.Tabindex)
                        {
                            case 0:
                                item.ThumbPathSmall = saveThumbFileName;
                                break;
                            case 1:
                                item.ThumbPathBig = saveThumbFileName;
                                break;
                            case 2:
                                item.ThumbPathGrid = saveThumbFileName;
                                break;
                            case 3:
                                item.ThumbPathList = saveThumbFileName;
                                break;
                            case 4:
                                item.ThumbPathBig10 = saveThumbFileName;
                                break;
                            case 99:
                                item.ThumbDetail = saveThumbFileName;
                                break;
                            default:
                                break;
                        }
                    }
                });
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(nameof(CreateThumbAsync), jobId);
            }
        }

        // QueueDB経由でMovieId/Hashが欠落している場合に、MoviePath一致で補完する。
        private async Task<long> ResolveMovieIdByPathAsync(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return 0;
            }

            bool needMovieId = queueObj.MovieId < 1;
            bool needHash = string.IsNullOrWhiteSpace(queueObj.Hash);
            if (!needMovieId && !needHash)
            {
                return queueObj.MovieId;
            }

            if (string.IsNullOrWhiteSpace(queueObj.MovieFullPath) && needMovieId)
            {
                return queueObj.MovieId;
            }

            long movieId = queueObj.MovieId;
            string hash = queueObj.Hash ?? "";
            await Dispatcher.InvokeAsync(() =>
            {
                MovieRecords item = null;
                if (queueObj.MovieId > 0)
                {
                    item = MainVM
                        .MovieRecs.Where(x => x.Movie_Id == queueObj.MovieId)
                        .FirstOrDefault();
                }

                if (item == null)
                {
                    item = MainVM
                        .MovieRecs.Where(x =>
                            string.Equals(
                                x.Movie_Path,
                                queueObj.MovieFullPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .FirstOrDefault();
                }

                if (item == null)
                {
                    return;
                }

                movieId = item.Movie_Id;
                if (string.IsNullOrWhiteSpace(hash))
                {
                    hash = item.Hash ?? "";
                }
            });

            // UI側の一覧に未展開でも、DBにはhashがあるケースがあるため補完する。
            if (
                (movieId < 1 || string.IsNullOrWhiteSpace(hash))
                && TryResolveMovieIdentityFromDb(
                    queueObj.MovieFullPath,
                    out long dbMovieId,
                    out string dbHash
                )
            )
            {
                if (movieId < 1 && dbMovieId > 0)
                {
                    movieId = dbMovieId;
                }
                if (string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(dbHash))
                {
                    hash = dbHash;
                }
            }

            if (movieId > 0)
            {
                queueObj.MovieId = movieId;
            }
            if (string.IsNullOrWhiteSpace(queueObj.Hash) && !string.IsNullOrWhiteSpace(hash))
            {
                queueObj.Hash = hash;
            }
            return movieId;
        }

        // MovieRecsに無い場合のフォールバックとして、DBからmovie_id/hashを直接引く。
        private bool TryResolveMovieIdentityFromDb(
            string movieFullPath,
            out long movieId,
            out string hash
        )
        {
            movieId = 0;
            hash = "";

            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string dbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return false;
            }

            try
            {
                string escapedMoviePath = movieFullPath.Replace("'", "''");
                var dt = GetData(
                    dbFullPath,
                    $"select movie_id, hash from movie where lower(movie_path) = lower('{escapedMoviePath}') limit 1"
                );
                if (dt == null || dt.Rows.Count < 1)
                {
                    return false;
                }

                var row = dt.Rows[0];
                _ = long.TryParse(row["movie_id"]?.ToString(), out movieId);
                hash = row["hash"]?.ToString() ?? "";
                return movieId > 0 || !string.IsNullOrWhiteSpace(hash);
            }
            catch
            {
                return false;
            }
        }

        // UI反映対象を「MovieId優先、無い場合はMoviePath一致」で判定する。
        private static bool IsSameMovieForQueue(
            MovieRecords item,
            QueueObj queueObj,
            long resolvedMovieId
        )
        {
            if (item == null || queueObj == null)
            {
                return false;
            }
            if (resolvedMovieId > 0 && item.Movie_Id == resolvedMovieId)
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return false;
            }
            return string.Equals(
                item.Movie_Path,
                queueObj.MovieFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // Worker進捗スナップショットから、一覧のサムネパスだけを本体へ戻す。
        private void ApplyWorkerThumbnailResultsToMovieRecords(
            ThumbnailProgressRuntimeSnapshot runtimeSnapshot
        )
        {
            if (runtimeSnapshot?.ActiveWorkers == null || MainVM?.MovieRecs == null)
            {
                return;
            }

            foreach (ThumbnailProgressWorkerSnapshot worker in runtimeSnapshot.ActiveWorkers)
            {
                _ = TryApplyWorkerThumbnailResultToMovieRecords(worker);
            }
        }

        // 外部Workerは本体一覧を直接触れないため、MovieId/Path一致でUI側に戻して反映する。
        private bool TryApplyWorkerThumbnailResultToMovieRecords(
            ThumbnailProgressWorkerSnapshot worker
        )
        {
            if (worker == null || worker.TabIndex < 0)
            {
                return false;
            }

            if (
                string.IsNullOrWhiteSpace(worker.PreviewImagePath)
                || !Path.Exists(worker.PreviewImagePath)
            )
            {
                return false;
            }

            bool applied = false;
            foreach (
                MovieRecords item in MainVM.MovieRecs.Where(x => IsSameMovieForWorkerSnapshot(x, worker))
            )
            {
                applied |= ApplyWorkerThumbnailPathToMovieRecord(
                    item,
                    worker.TabIndex,
                    worker.PreviewImagePath
                );
            }

            return applied;
        }

        private static bool IsSameMovieForWorkerSnapshot(
            MovieRecords item,
            ThumbnailProgressWorkerSnapshot worker
        )
        {
            if (item == null || worker == null)
            {
                return false;
            }

            if (worker.MovieId > 0 && item.Movie_Id == worker.MovieId)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(worker.MovieFullPath))
            {
                return false;
            }

            return string.Equals(
                item.Movie_Path,
                worker.MovieFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool ApplyWorkerThumbnailPathToMovieRecord(
            MovieRecords item,
            int tabIndex,
            string thumbnailPath
        )
        {
            if (item == null || string.IsNullOrWhiteSpace(thumbnailPath))
            {
                return false;
            }

            switch (tabIndex)
            {
                case 0:
                    if (
                        string.Equals(
                            item.ThumbPathSmall,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbPathSmall = thumbnailPath;
                    return true;
                case 1:
                    if (
                        string.Equals(
                            item.ThumbPathBig,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbPathBig = thumbnailPath;
                    return true;
                case 2:
                    if (
                        string.Equals(
                            item.ThumbPathGrid,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbPathGrid = thumbnailPath;
                    return true;
                case 3:
                    if (
                        string.Equals(
                            item.ThumbPathList,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbPathList = thumbnailPath;
                    return true;
                case 4:
                    if (
                        string.Equals(
                            item.ThumbPathBig10,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbPathBig10 = thumbnailPath;
                    return true;
                case 99:
                    if (
                        string.Equals(
                            item.ThumbDetail,
                            thumbnailPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return false;
                    }
                    item.ThumbDetail = thumbnailPath;
                    return true;
                default:
                    return false;
            }
        }

        // エンジンから受けた中立DTOをWPFの画像へ変換し、ミニパネル用キャッシュへ登録する。
        private static bool TryStoreThumbnailProgressPreview(
            QueueObj queueObj,
            ThumbnailPreviewFrame previewFrame,
            out string previewCacheKey,
            out long previewRevision
        )
        {
            previewCacheKey = "";
            previewRevision = 0;

            if (queueObj == null || previewFrame == null || !previewFrame.IsValid())
            {
                return false;
            }

            if (!TryCreatePreviewImageSource(previewFrame, out BitmapSource bitmapSource))
            {
                return false;
            }

            previewCacheKey = ThumbnailProgressRuntime.CreateWorkerKey(queueObj);
            if (string.IsNullOrWhiteSpace(previewCacheKey))
            {
                previewCacheKey = "";
                return false;
            }

            previewRevision = ThumbnailPreviewCache.Shared.Store(previewCacheKey, bitmapSource);
            if (previewRevision < 1)
            {
                previewCacheKey = "";
                return false;
            }

            return true;
        }

        // ピクセル配列の生データからWriteableBitmapを組み立て、UI間共有のためにFreezeする。
        private static bool TryCreatePreviewImageSource(
            ThumbnailPreviewFrame previewFrame,
            out BitmapSource bitmapSource
        )
        {
            bitmapSource = null;
            if (previewFrame == null || !previewFrame.IsValid())
            {
                return false;
            }

            if (!TryResolveWpfPixelFormat(previewFrame.PixelFormat, out PixelFormat pixelFormat))
            {
                return false;
            }

            try
            {
                WriteableBitmap bitmap = new(
                    previewFrame.Width,
                    previewFrame.Height,
                    96,
                    96,
                    pixelFormat,
                    null
                );
                bitmap.WritePixels(
                    new Int32Rect(0, 0, previewFrame.Width, previewFrame.Height),
                    previewFrame.PixelBytes,
                    previewFrame.Stride,
                    0
                );
                bitmap.Freeze();
                bitmapSource = bitmap;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveWpfPixelFormat(
            ThumbnailPreviewPixelFormat previewPixelFormat,
            out PixelFormat pixelFormat
        )
        {
            switch (previewPixelFormat)
            {
                case ThumbnailPreviewPixelFormat.Bgr24:
                    pixelFormat = PixelFormats.Bgr24;
                    return true;
                case ThumbnailPreviewPixelFormat.Bgra32:
                    pixelFormat = PixelFormats.Bgra32;
                    return true;
                default:
                    pixelFormat = default;
                    return false;
            }
        }

        /// <summary>
        /// 手動等間隔サムネイル作成
        /// </summary>
        private void CreateThumb_EqualInterval(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            // 複数選択対応: 選択中の全アイテムを取得
            List<MovieRecords> selectedItems = GetSelectedItemsByTabIndex();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                return;
            }

            foreach (var mv in selectedItems)
            {
                QueueObj tempObj = new()
                {
                    MovieId = mv.Movie_Id,
                    MovieFullPath = mv.Movie_Path,
                    Hash = mv.Hash,
                    Tabindex = Tabs.SelectedIndex,
                };
                _ = TryEnqueueThumbnailJob(tempObj);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using IndigoMovieManager.DB;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int WhiteBrowserSkinStatePersistBatchWindowMs = 100;

        // skin 状態保存は単一ライターへ寄せ、UI からは request を積むだけにする。
        private readonly Channel<WhiteBrowserSkinStatePersistRequest> _whiteBrowserSkinStatePersistChannel =
            Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }
            );

        private readonly WhiteBrowserSkinStatePersister _whiteBrowserSkinStatePersister;
        private Task _whiteBrowserSkinStatePersisterTask;
        private CancellationTokenSource _whiteBrowserSkinStatePersisterCts = new();
        private int _whiteBrowserSkinStatePersistInputOpen = 1;

        private bool TryEnqueueWhiteBrowserSkinStatePersistRequest(
            WhiteBrowserSkinStatePersistRequest request
        )
        {
            if (
                request == null
                || string.IsNullOrWhiteSpace(request.DbFullPath)
                || string.IsNullOrWhiteSpace(request.Key)
            )
            {
                return false;
            }

            if (Volatile.Read(ref _whiteBrowserSkinStatePersistInputOpen) == 0)
            {
                RecordProfilePersistFaultForCache(request);
                if (request.TargetKind == WhiteBrowserSkinStatePersistTargetKind.Profile)
                {
                    DebugRuntimeLog.Write(
                        "skin-db",
                        $"profile persist queue closed: db='{request.DbFullPath}' profile='{request.ProfileName}' key='{request.Key}' {request.BuildWriteFailureResultLogFields("queue-closed", TimeSpan.Zero)}"
                    );
                }

                return false;
            }

            bool queued = _whiteBrowserSkinStatePersistChannel.Writer.TryWrite(request);
            if (!queued)
            {
                RecordProfilePersistFaultForCache(request);
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"persist queue rejected: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}' {request.BuildWriteFailureResultLogFields("queue-rejected", TimeSpan.Zero)}"
                );
            }
            else if (request.TargetKind == WhiteBrowserSkinStatePersistTargetKind.Profile)
            {
                WhiteBrowserSkinProfileValueCache.RecordPending(
                    request.DbFullPath,
                    request.ProfileName,
                    request.Key,
                    request.Value
                );
            }

            if (queued)
            {
                DebugRuntimeLog.RecordSkinDbPersistQueued();
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"persist queued: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}' value='{request.Value ?? ""}'"
                );
            }

            return queued;
        }

        private static void RecordProfilePersistFaultForCache(
            WhiteBrowserSkinStatePersistRequest request
        )
        {
            if (request?.TargetKind != WhiteBrowserSkinStatePersistTargetKind.Profile)
            {
                return;
            }

            // enqueue できなかった profile 値だけ、次の操作で再保存できる失敗 dirty として残す。
            WhiteBrowserSkinProfileValueCache.RecordFault(
                request.DbFullPath,
                request.ProfileName,
                request.Key,
                request.Value
            );
        }

        private bool TryEnqueueExternalSkinProfileWrite(string dbFullPath, string skinName, string key, string value)
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(skinName)
                || string.IsNullOrWhiteSpace(key)
            )
            {
                return false;
            }

            return TryEnqueueWhiteBrowserSkinStatePersistRequest(
                WhiteBrowserSkinStatePersistRequest.CreateProfile(
                    dbFullPath,
                    skinName,
                    key,
                    value ?? "",
                    DebugRuntimeLog.GetCurrentScopeText()
                )
            );
        }

        private bool TryPersistSystemValue(string dbFullPath, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            WhiteBrowserSkinStatePersistRequest request = WhiteBrowserSkinStatePersistRequest.CreateSystem(
                dbFullPath,
                key,
                value ?? "",
                DebugRuntimeLog.GetCurrentScopeText()
            );

            // まずは単一ライターへ流し、通常時の runtime 状態だけ先に揃える。
            if (TryEnqueueWhiteBrowserSkinStatePersistRequest(request))
            {
                ApplyRuntimeSystemValue(dbFullPath, key, value ?? "");
                return true;
            }

            if (Volatile.Read(ref _whiteBrowserSkinStatePersistInputOpen) == 0)
            {
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"system persist dropped after shutdown start: db='{dbFullPath}' key='{key}' {request.BuildWriteFailureResultLogFields("queue-closed", TimeSpan.Zero)}"
                );
                return false;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                // 予期しない queue 拒否時だけ direct write へ戻し、通常時の保存を落とさない。
                SQLite.UpsertSystemTable(dbFullPath, key, value ?? "");
                stopwatch.Stop();
                ApplyRuntimeSystemValue(dbFullPath, key, value ?? "");
                DebugRuntimeLog.RecordSkinDbPersistFallbackApplied();
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"system persist fallback applied: db='{dbFullPath}' key='{key}' value='{value ?? ""}' {request.BuildWriteSuccessResultLogFields("fallback-write", stopwatch.Elapsed)}"
                );
                return true;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"system persist fallback failed: db='{dbFullPath}' key='{key}' {request.BuildWriteFailureResultLogFields("fallback-write", stopwatch.Elapsed)} err='{ex.GetType().Name}: {ex.Message}'"
                );
                return false;
            }
        }

        private void PersistWhiteBrowserSkinStateRequestFallback(
            WhiteBrowserSkinStatePersistRequest request
        )
        {
            if (
                request == null
                || string.IsNullOrWhiteSpace(request.DbFullPath)
                || string.IsNullOrWhiteSpace(request.Key)
            )
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                switch (request.TargetKind)
                {
                    case WhiteBrowserSkinStatePersistTargetKind.System:
                        SQLite.UpsertSystemTable(
                            request.DbFullPath,
                            request.Key,
                            request.Value ?? ""
                        );
                        break;

                    case WhiteBrowserSkinStatePersistTargetKind.Profile:
                        SQLite.UpsertProfileTable(
                            request.DbFullPath,
                            request.ProfileName,
                            request.Key,
                            request.Value ?? ""
                        );
                        WhiteBrowserSkinProfileValueCache.RecordPersisted(
                            request.DbFullPath,
                            request.ProfileName,
                            request.Key,
                            request.Value
                        );
                        break;
                }

                stopwatch.Stop();
                DebugRuntimeLog.RecordSkinDbPersistFallbackApplied();
                DebugRuntimeLog.Write(
                    "skin-db",
                    $"persist fallback applied: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}' value='{request.Value ?? ""}' {request.BuildWriteSuccessResultLogFields("fallback-write", stopwatch.Elapsed)}"
                );
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (request.TargetKind == WhiteBrowserSkinStatePersistTargetKind.Profile)
                {
                    WhiteBrowserSkinProfileValueCache.RecordFault(
                        request.DbFullPath,
                        request.ProfileName,
                        request.Key,
                        request.Value
                    );
                }

                DebugRuntimeLog.Write(
                    "skin-db",
                    $"persist fallback failed: db='{request.DbFullPath}' target={request.TargetKind} profile='{request.ProfileName}' key='{request.Key}' {request.BuildWriteFailureResultLogFields("fallback-write", stopwatch.Elapsed)} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private async Task RunWhiteBrowserSkinStatePersisterSupervisorAsync(CancellationToken cts)
        {
            await Task.Yield();

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await _whiteBrowserSkinStatePersister.RunAsync(cts).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "skin-db",
                        $"skin state persister restart scheduled: {ex.Message}"
                    );
                    try
                    {
                        await Task.Delay(500, cts).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }

        private void BeginWhiteBrowserSkinStatePersisterShutdown()
        {
            Interlocked.Exchange(ref _whiteBrowserSkinStatePersistInputOpen, 0);
            _whiteBrowserSkinStatePersistChannel.Writer.TryComplete();
            DebugRuntimeLog.Write(
                "lifecycle",
                "shutdown: skin state persister input complete requested."
            );
        }

        private void DrainWhiteBrowserSkinStatePersisterForShutdown()
        {
            if (_whiteBrowserSkinStatePersisterTask == null)
            {
                return;
            }

            try
            {
                Task completed = Task
                    .WhenAny(_whiteBrowserSkinStatePersisterTask, Task.Delay(500))
                    .GetAwaiter()
                    .GetResult();
                if (ReferenceEquals(completed, _whiteBrowserSkinStatePersisterTask))
                {
                    if (_whiteBrowserSkinStatePersisterTask.IsFaulted)
                    {
                        string message =
                            _whiteBrowserSkinStatePersisterTask.Exception?.GetBaseException()?.Message
                            ?? "unknown";
                        DebugRuntimeLog.Write(
                            "lifecycle",
                            $"skin-state-persister faulted: {message}"
                        );
                    }

                    return;
                }

                DebugRuntimeLog.Write(
                    "lifecycle",
                    $"skin-state-persister drain timeout: 500ms status={_whiteBrowserSkinStatePersisterTask.Status}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "lifecycle",
                    $"skin-state-persister drain wait failed: {ex.Message}"
                );
            }

            _whiteBrowserSkinStatePersisterCts.Cancel();
            WaitBackgroundTaskForShutdown(_whiteBrowserSkinStatePersisterTask, "skin-state-persister");
        }
    }
}

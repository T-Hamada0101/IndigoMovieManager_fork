using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Windows.Data;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ThumbnailFailedRefreshDebounceMs = 300;
        private int _thumbnailFailedTabSelected;
        private int _thumbnailFailedListDirty = 1;
        private int _thumbnailFailedRefreshRequested;
        private int _thumbnailFailedRefreshQueued;
        private int _thumbnailFailedRefreshRevision;
        private int _thumbnailFailedAppliedRevision = -1;
        private long _thumbnailFailedLastRefreshTickMs = -1;
        private string _thumbnailFailedResultSignatureFilter = "";
        private string _thumbnailFailedRecoveryRouteFilter = "";

        // 失敗一覧の更新を必要状態にする。必要時だけ再読込を予約する。
        private void MarkThumbnailFailedListDirty(bool incrementRevision = false, string reason = "")
        {
            if (incrementRevision)
            {
                _ = Interlocked.Increment(ref _thumbnailFailedRefreshRevision);
            }

            _ = Interlocked.Exchange(ref _thumbnailFailedListDirty, 1);
            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }

            DebugRuntimeLog.Write(
                "thumbnail-failed",
                $"failed list dirty marked: reason={reason} revision={Volatile.Read(ref _thumbnailFailedRefreshRevision)}"
            );
            RequestThumbnailFailedListRefresh();
        }

        // タブ選択状態を更新し、表示中タブならdirtyを即時回収する。
        private void UpdateThumbnailFailedTabSelectionState(bool isSelected)
        {
            _ = Interlocked.Exchange(ref _thumbnailFailedTabSelected, isSelected ? 1 : 0);
            if (!isSelected)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _thumbnailFailedListDirty, 0, 0) == 1)
            {
                RequestThumbnailFailedListRefresh();
            }
        }

        private bool IsThumbnailFailedTabSelected()
        {
            return Interlocked.CompareExchange(ref _thumbnailFailedTabSelected, 0, 0) == 1;
        }

        // 連続イベントを1本化して、失敗一覧の再読込要求を過剰発火させない。
        private void RequestThumbnailFailedListRefresh()
        {
            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Interlocked.Exchange(ref _thumbnailFailedRefreshRequested, 1);
            if (Interlocked.Exchange(ref _thumbnailFailedRefreshQueued, 1) == 1)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(ProcessThumbnailFailedRefreshQueue)
            );
        }

        private async void ProcessThumbnailFailedRefreshQueue()
        {
            try
            {
                if (Interlocked.Exchange(ref _thumbnailFailedRefreshRequested, 0) == 1)
                {
                    await WaitThumbnailFailedRefreshDebounceAsync();
                    await RefreshThumbnailFailedListCoreAsync();
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail-failed",
                    $"failed list refresh queue failed: {ex.Message}"
                );
            }
            finally
            {
                _ = Interlocked.Exchange(ref _thumbnailFailedRefreshQueued, 0);
                if (
                    IsThumbnailFailedTabSelected()
                    && Interlocked.CompareExchange(ref _thumbnailFailedRefreshRequested, 0, 0) == 1
                )
                {
                    RequestThumbnailFailedListRefresh();
                }
            }
        }

        // 直近反映から一定時間は待機し、完了イベント連打時の読み直し頻度を制限する。
        private async Task WaitThumbnailFailedRefreshDebounceAsync()
        {
            long lastTickMs = Interlocked.Read(ref _thumbnailFailedLastRefreshTickMs);
            if (lastTickMs < 0)
            {
                return;
            }

            long nowTickMs = Environment.TickCount64;
            long elapsedMs = nowTickMs - lastTickMs;
            if (elapsedMs < ThumbnailFailedRefreshDebounceMs)
            {
                int delayMs = (int)(ThumbnailFailedRefreshDebounceMs - elapsedMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }

        // 失敗専用DBから失敗一覧を取得し、最新要求と一致する場合だけUIへ反映する。
        private async Task RefreshThumbnailFailedListCoreAsync()
        {
            if (MainVM?.ThumbnailFailedRecs == null)
            {
                return;
            }

            string requestedMainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int requestedRevision = Volatile.Read(ref _thumbnailFailedRefreshRevision);

            List<ThumbnailFailureRecord> failureRecords = [];
            if (!string.IsNullOrWhiteSpace(requestedMainDbPath))
            {
                try
                {
                    failureRecords = await Task.Run(() =>
                        new ThumbnailFailureDebugDbService(requestedMainDbPath).GetFailureRecords()
                    );
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "thumbnail-failed",
                        $"failed list load failed: {ex.Message}"
                    );
                }
            }

            if (!IsThumbnailFailedTabSelected())
            {
                return;
            }

            string currentMainDbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            int currentRevision = Volatile.Read(ref _thumbnailFailedRefreshRevision);
            if (
                !string.Equals(
                    requestedMainDbPath,
                    currentMainDbPath,
                    StringComparison.OrdinalIgnoreCase
                )
                || currentRevision != requestedRevision
            )
            {
                return;
            }

            MainVM.ThumbnailFailedRecs.Clear();
            foreach (ThumbnailFailureRecord failureRecord in failureRecords)
            {
                MainVM.ThumbnailFailedRecs.Add(ToThumbnailFailedRecordViewModel(failureRecord));
            }

            ApplyThumbnailFailedListFilter();

            _ = Interlocked.Exchange(ref _thumbnailFailedListDirty, 0);
            _ = Interlocked.Exchange(ref _thumbnailFailedAppliedRevision, requestedRevision);
            Interlocked.Exchange(ref _thumbnailFailedLastRefreshTickMs, Environment.TickCount64);

            DebugRuntimeLog.Write(
                "thumbnail-failed",
                $"failed list applied: count={failureRecords.Count} revision={requestedRevision} source=FailureDb applied={Interlocked.CompareExchange(ref _thumbnailFailedAppliedRevision, 0, 0)}"
            );
        }

        // FailureDbの生情報を、表示専用ViewModelへ詰め替える。
        private static ThumbnailFailedRecordViewModel ToThumbnailFailedRecordViewModel(
            ThumbnailFailureRecord item
        )
        {
            if (item == null)
            {
                return new ThumbnailFailedRecordViewModel();
            }

            return new ThumbnailFailedRecordViewModel
            {
                QueueId = TryReadQueueId(item.ExtraJson, item.RecordId),
                MainDbPathHash = item.MainDbPathHash ?? "",
                MoviePath = item.MoviePath ?? "",
                MoviePathKey = item.MoviePathKey ?? "",
                TabIndex = item.TabIndex ?? -1,
                MovieSizeBytes = item.MovieSizeBytes,
                ThumbPanelPos = TryReadNullableIntAny(
                    item.ExtraJson,
                    "ThumbPanelPos",
                    "thumb_panel_pos"
                ),
                ThumbTimePos = TryReadNullableIntAny(
                    item.ExtraJson,
                    "ThumbTimePos",
                    "thumb_time_pos"
                ),
                PanelType = item.PanelType ?? "",
                Status = item.QueueStatus ?? "",
                FailureKind = item.FailureKind.ToString(),
                Reason = item.Reason ?? "",
                AttemptCount = item.AttemptCount,
                LastError = item.LastError ?? "",
                OwnerInstanceId = item.OwnerInstanceId ?? "",
                WorkerRole = item.WorkerRole ?? "",
                EngineId = item.EngineId ?? "",
                LeaseUntilUtc = item.LeaseUntilUtc ?? "",
                StartedAtUtc = item.StartedAtUtc ?? "",
                FailureKindSource = TryReadJsonStringAny(
                    item.ExtraJson,
                    "FailureKindSource",
                    "failure_kind_source"
                ),
                MaterialDurationSec = TryReadNullableDoubleAny(
                    item.ExtraJson,
                    "MaterialDurationSec",
                    "material_duration_sec"
                ),
                EngineAttempted = TryReadJsonStringAny(
                    item.ExtraJson,
                    "EngineAttempted",
                    "engine_attempted"
                ),
                EngineSucceeded = TryReadJsonBoolAny(
                    item.ExtraJson,
                    "EngineSucceeded",
                    "engine_succeeded"
                ),
                SeekStrategy = TryReadJsonStringAny(
                    item.ExtraJson,
                    "SeekStrategy",
                    "seek_strategy"
                ),
                SeekSec = TryReadNullableDoubleAny(
                    item.ExtraJson,
                    "SeekSec",
                    "seek_sec",
                    "ThumbSec"
                ),
                RepairAttempted = TryReadJsonBoolAny(
                    item.ExtraJson,
                    "RepairAttempted",
                    "repair_attempted"
                ),
                RepairSucceeded = TryReadJsonBoolAny(
                    item.ExtraJson,
                    "RepairSucceeded",
                    "repair_succeeded"
                ),
                PreflightBranch = TryReadJsonStringAny(
                    item.ExtraJson,
                    "PreflightBranch",
                    "preflight_branch"
                ),
                ResultSignature = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultSignature",
                    "result_signature"
                ),
                ReproConfirmed = TryReadJsonBoolAny(
                    item.ExtraJson,
                    "ReproConfirmed",
                    "repro_confirmed"
                ),
                RecoveryRoute = TryReadJsonStringAny(
                    item.ExtraJson,
                    "RecoveryRoute",
                    "recovery_route"
                ),
                DecisionBasis = TryReadJsonStringAny(
                    item.ExtraJson,
                    "DecisionBasis",
                    "decision_basis"
                ),
                WasRunning = TryReadJsonBoolAny(item.ExtraJson, "WasRunning", "was_running"),
                AttemptCountAfter = TryReadNullableIntAny(
                    item.ExtraJson,
                    "AttemptCountAfter",
                    "attempt_count_after"
                ),
                MovieExists = TryReadJsonBoolAny(item.ExtraJson, "MovieExists", "movie_exists"),
                ResultFailureStage = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultFailureStage",
                    "result_failure_stage"
                ),
                ResultPolicyDecision = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultPolicyDecision",
                    "result_policy_decision"
                ),
                ResultPlaceholderAction = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultPlaceholderAction",
                    "result_placeholder_action"
                ),
                ResultPlaceholderKind = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultPlaceholderKind",
                    "result_placeholder_kind"
                ),
                ResultFinalizerAction = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultFinalizerAction",
                    "result_finalizer_action"
                ),
                ResultFinalizerDetail = TryReadJsonStringAny(
                    item.ExtraJson,
                    "ResultFinalizerDetail",
                    "result_finalizer_detail"
                ),
                CreatedAtUtc = item.OccurredAtUtc == DateTime.MinValue
                    ? ""
                    : item.OccurredAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                UpdatedAtUtc = item.UpdatedAtUtc == DateTime.MinValue
                    ? ""
                    : item.UpdatedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            };
        }

        // FailureDbの補助JSONからQueueIdを戻し、無ければRecordIdで代用する。
        private static long TryReadQueueId(string extraJson, long fallbackRecordId)
        {
            if (
                TryReadJsonInt64(extraJson, "QueueId", out long queueId)
                || TryReadJsonInt64(extraJson, "queueId", out queueId)
            )
            {
                return queueId;
            }

            return fallbackRecordId;
        }

        // 数値補助列はJSONから復元する。無ければ空表示にする。
        private static int? TryReadNullableInt(string extraJson, string propertyName)
        {
            return TryReadJsonInt32(extraJson, propertyName, out int value) ? value : null;
        }

        private static int? TryReadNullableIntAny(string extraJson, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                int? value = TryReadNullableInt(extraJson, propertyName);
                if (value.HasValue)
                {
                    return value;
                }
            }

            return null;
        }

        private static bool TryReadJsonInt32(string extraJson, string propertyName, out int value)
        {
            value = 0;
            if (!TryReadJsonInt64(extraJson, propertyName, out long parsed))
            {
                return false;
            }

            if (parsed < int.MinValue || parsed > int.MaxValue)
            {
                return false;
            }

            value = (int)parsed;
            return true;
        }

        private static bool TryReadJsonInt64(string extraJson, string propertyName, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(extraJson) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (
                    !document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind == JsonValueKind.Null
                )
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.Number)
                {
                    return property.TryGetInt64(out value);
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    return long.TryParse(property.GetString(), out value);
                }
            }
            catch
            {
                // 調査用JSONの破損では一覧表示自体を止めない。
            }

            return false;
        }

        private static string TryReadJsonString(string extraJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(extraJson) || string.IsNullOrWhiteSpace(propertyName))
            {
                return "";
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (
                    !document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind == JsonValueKind.Null
                )
                {
                    return "";
                }

                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => property.GetRawText(),
                    _ => property.GetRawText(),
                };
            }
            catch
            {
                return "";
            }
        }

        private static bool TryReadJsonBool(string extraJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(extraJson) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (
                    !document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind == JsonValueKind.Null
                )
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (property.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    return bool.TryParse(property.GetString(), out bool parsed) && parsed;
                }
            }
            catch
            {
                // 調査用JSONの破損では一覧表示自体を止めない。
            }

            return false;
        }

        private static string TryReadJsonStringAny(string extraJson, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                string value = TryReadJsonString(extraJson, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        // 失敗タブ専用の絞り込み条件をCollectionViewへ反映する。
        private void ApplyThumbnailFailedListFilter()
        {
            if (MainVM?.ThumbnailFailedRecs == null)
            {
                return;
            }

            ICollectionView view = CollectionViewSource.GetDefaultView(MainVM.ThumbnailFailedRecs);
            if (view == null)
            {
                return;
            }

            bool hasFilter =
                !string.IsNullOrWhiteSpace(_thumbnailFailedResultSignatureFilter)
                || !string.IsNullOrWhiteSpace(_thumbnailFailedRecoveryRouteFilter);
            view.Filter = hasFilter ? FilterThumbnailFailedRecord : null;
            view.Refresh();
        }

        private bool FilterThumbnailFailedRecord(object item)
        {
            return item is ThumbnailFailedRecordViewModel record
                && IsThumbnailFailedRecordMatched(
                    record,
                    _thumbnailFailedResultSignatureFilter,
                    _thumbnailFailedRecoveryRouteFilter
                );
        }

        // result_signature と recovery_route の部分一致だけで候補を絞る。
        private static bool IsThumbnailFailedRecordMatched(
            ThumbnailFailedRecordViewModel item,
            string resultSignatureFilter,
            string recoveryRouteFilter
        )
        {
            if (item == null)
            {
                return false;
            }

            if (
                !ContainsFilter(item.ResultSignature, resultSignatureFilter)
                || !ContainsFilter(item.RecoveryRoute, recoveryRouteFilter)
            )
            {
                return false;
            }

            return true;
        }

        private static bool ContainsFilter(string actualValue, string filterValue)
        {
            if (string.IsNullOrWhiteSpace(filterValue))
            {
                return true;
            }

            return (actualValue ?? "").Contains(
                filterValue.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private void ThumbnailFailedResultSignatureFilterTextBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e
        )
        {
            _thumbnailFailedResultSignatureFilter =
                ThumbnailFailedResultSignatureFilterTextBox?.Text?.Trim() ?? "";
            ApplyThumbnailFailedListFilter();
        }

        private void ThumbnailFailedRecoveryRouteFilterTextBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e
        )
        {
            _thumbnailFailedRecoveryRouteFilter =
                ThumbnailFailedRecoveryRouteFilterTextBox?.Text?.Trim() ?? "";
            ApplyThumbnailFailedListFilter();
        }

        private void ClearThumbnailFailedFilterButton_Click(
            object sender,
            System.Windows.RoutedEventArgs e
        )
        {
            _thumbnailFailedResultSignatureFilter = "";
            _thumbnailFailedRecoveryRouteFilter = "";

            if (ThumbnailFailedResultSignatureFilterTextBox != null)
            {
                ThumbnailFailedResultSignatureFilterTextBox.Text = "";
            }

            if (ThumbnailFailedRecoveryRouteFilterTextBox != null)
            {
                ThumbnailFailedRecoveryRouteFilterTextBox.Text = "";
            }

            ApplyThumbnailFailedListFilter();
        }

        private static bool TryReadJsonBoolAny(string extraJson, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (TryReadJsonBool(extraJson, propertyName))
                {
                    return true;
                }
            }

            return false;
        }

        private static double? TryReadNullableDoubleAny(string extraJson, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (TryReadJsonDouble(extraJson, propertyName, out double value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool TryReadJsonDouble(string extraJson, string propertyName, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(extraJson) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(extraJson);
                if (
                    !document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind == JsonValueKind.Null
                )
                {
                    return false;
                }

                if (property.ValueKind == JsonValueKind.Number)
                {
                    return property.TryGetDouble(out value);
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    return double.TryParse(property.GetString(), out value);
                }
            }
            catch
            {
                // 調査用JSONの破損では一覧表示自体を止めない。
            }

            return false;
        }
    }
}

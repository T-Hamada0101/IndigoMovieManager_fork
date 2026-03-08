using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IndigoMovieManager.ModelViews;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class ThumbnailProgressViewerWindow : Window, INotifyPropertyChanged
    {
        private static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan HealthMaxAge = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ControlMaxAge = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan CommandMaxAge = TimeSpan.FromDays(1);
        private readonly ThumbnailProgressViewerRuntimeOptions options;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer parentWatchTimer;
        private string headerTitle = "サムネイル ビューアー";
        private string headerStatus = "接続待機中";
        private string updatedAtText = "";
        private ThumbnailCoordinatorControlSnapshot latestCoordinatorControl;
        private ThumbnailCoordinatorCommandSnapshot currentCoordinatorCommand;
        private int temporaryParallelismDelta;
        private bool editorInitialized;

        public ThumbnailProgressViewerWindow(ThumbnailProgressViewerRuntimeOptions options)
        {
            this.options = options ?? new ThumbnailProgressViewerRuntimeOptions();
            Progress = new ThumbnailProgressViewState();

            InitializeComponent();
            DataContext = this;

            HeaderTitle = string.IsNullOrWhiteSpace(this.options.DbName)
                ? "サムネイル 運転席"
                : $"サムネイル 運転席 - {this.options.DbName}";

            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            refreshTimer.Tick += (_, _) => RefreshSnapshot();

            parentWatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            parentWatchTimer.Tick += (_, _) => CloseIfParentExited();

            Loaded += (_, _) =>
            {
                RefreshSnapshot();
                refreshTimer.Start();
                parentWatchTimer.Start();
            };
            Closing += (_, _) =>
            {
                refreshTimer.Stop();
                parentWatchTimer.Stop();
            };
        }

        public ThumbnailProgressViewState Progress { get; }
        public ObservableCollection<ThumbnailCoordinatorDecisionHistoryItem> CoordinatorDecisionHistory { get; } = [];

        public string HeaderTitle
        {
            get => headerTitle;
            private set => SetField(ref headerTitle, value);
        }

        public string HeaderStatus
        {
            get => headerStatus;
            private set => SetField(ref headerStatus, value);
        }

        public string UpdatedAtText
        {
            get => updatedAtText;
            private set => SetField(ref updatedAtText, value);
        }

        private void RefreshSnapshot()
        {
            ThumbnailProgressRuntimeSnapshot snapshot =
                ThumbnailProgressExternalSnapshotStore.CreateMergedSnapshot(
                    options.MainDbFullPath,
                    new ThumbnailProgressRuntimeSnapshot(),
                    [options.NormalOwnerInstanceId, options.IdleOwnerInstanceId],
                    SnapshotMaxAge
                );
            IReadOnlyList<ThumbnailWorkerHealthSnapshot> healthSnapshots =
                ThumbnailWorkerHealthStore.LoadSnapshots(
                    options.MainDbFullPath,
                    [options.NormalOwnerInstanceId, options.IdleOwnerInstanceId],
                    HealthMaxAge
                );

            Progress.ApplySnapshot(
                snapshot,
                dbPendingCount: 0,
                dbTotalCount: 0,
                logicalCoreCount: Environment.ProcessorCount
            );
            Progress.ApplyDbPendingPaused();
            Progress.ApplyMetersPaused();

            int activeCount = snapshot.ActiveWorkers.Count(x => x.IsActive);
            RefreshCoordinatorControl();
            HeaderStatus = BuildHeaderStatus(
                snapshot,
                healthSnapshots,
                latestCoordinatorControl,
                activeCount
            );
            UpdatedAtText = $"更新 {DateTime.Now:HH:mm:ss}";
        }

        private static string BuildHeaderStatus(
            ThumbnailProgressRuntimeSnapshot snapshot,
            IReadOnlyList<ThumbnailWorkerHealthSnapshot> healthSnapshots,
            ThumbnailCoordinatorControlSnapshot coordinatorControl,
            int activeCount
        )
        {
            string panelStatus = activeCount > 0
                ? $"稼働中 {activeCount} / パネル {snapshot.ActiveWorkers.Count}"
                : "待機中";
            string coordinatorStatus = coordinatorControl == null
                ? "運転席未取得"
                : $"運転席={ThumbnailCoordinatorStateResolver.ToDisplayText(coordinatorControl.CoordinatorState)}";
            if (healthSnapshots == null || healthSnapshots.Count < 1)
            {
                return $"{panelStatus} | {coordinatorStatus} | health未取得";
            }

            string workerStatus = string.Join(
                " / ",
                healthSnapshots.Select(BuildWorkerHealthText)
            );
            return $"{panelStatus} | {coordinatorStatus} | {workerStatus}";
        }

        private static string BuildWorkerHealthText(ThumbnailWorkerHealthSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "worker:不明";
            }

            string roleText = string.Equals(snapshot.WorkerRole, "Idle", StringComparison.OrdinalIgnoreCase)
                ? "ゆっくり"
                : "通常";
            string stateText = ResolveHealthStateText(snapshot.State);
            string reasonText = ThumbnailWorkerHealthReasonResolver.ToDisplayText(snapshot.ReasonCode);
            if (string.IsNullOrWhiteSpace(snapshot.CurrentPriority))
            {
                return string.IsNullOrWhiteSpace(reasonText)
                    ? $"{roleText}:{stateText}"
                    : $"{roleText}:{stateText}[{reasonText}]";
            }

            return string.IsNullOrWhiteSpace(reasonText)
                ? $"{roleText}:{stateText}({snapshot.CurrentPriority})"
                : $"{roleText}:{stateText}[{reasonText}]({snapshot.CurrentPriority})";
        }

        private static string ResolveHealthStateText(string state)
        {
            return (state ?? "").ToLowerInvariant() switch
            {
                ThumbnailWorkerHealthState.Starting => "起動中",
                ThumbnailWorkerHealthState.Running => "稼働",
                ThumbnailWorkerHealthState.Stopped => "停止",
                ThumbnailWorkerHealthState.Exited => "終了",
                ThumbnailWorkerHealthState.StartFailed => "起動失敗",
                ThumbnailWorkerHealthState.Missing => "未配置",
                _ => "不明",
            };
        }

        // control / command の読み書きはこの画面で閉じ、MainWindow と独立して開発できるようにする。
        private void RefreshCoordinatorControl()
        {
            latestCoordinatorControl = ThumbnailCoordinatorControlStore.LoadLatest(
                options.MainDbFullPath,
                options.CoordinatorOwnerInstanceId,
                ControlMaxAge
            );
            ThumbnailCoordinatorCommandSnapshot latestCommand =
                ThumbnailCoordinatorCommandStore.LoadLatest(
                    options.MainDbFullPath,
                    options.CoordinatorOwnerInstanceId,
                    CommandMaxAge
                );

            if (!editorInitialized)
            {
                InitializeCoordinatorEditor(latestCoordinatorControl, latestCommand);
            }

            CoordinatorStateText.Text = latestCoordinatorControl == null
                ? "取得待ち"
                : ThumbnailCoordinatorStateResolver.ToDisplayText(
                    latestCoordinatorControl.CoordinatorState
                );
            CoordinatorParallelismText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.RequestedParallelism} / {latestCoordinatorControl.EffectiveParallelism}";
            CoordinatorSlotText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.FastSlotCount} / {latestCoordinatorControl.SlowSlotCount}";
            CoordinatorQueueText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.QueuedNormalCount}/{latestCoordinatorControl.QueuedSlowCount}/{latestCoordinatorControl.QueuedRecoveryCount}"
                    + $" / {latestCoordinatorControl.RunningNormalCount}/{latestCoordinatorControl.RunningSlowCount}/{latestCoordinatorControl.RunningRecoveryCount}";
            CoordinatorDemandText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.DemandNormalCount}/{latestCoordinatorControl.DemandSlowCount}/{latestCoordinatorControl.DemandRecoveryCount}";
            CoordinatorWeightText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.WeightedNormalDemand}/{latestCoordinatorControl.WeightedSlowDemand}";
            CoordinatorSlowRangeText.Text = latestCoordinatorControl == null
                ? "-"
                : $"{latestCoordinatorControl.SlowSlotMinimum} - {latestCoordinatorControl.SlowSlotMaximum}";
            CoordinatorDecisionCategoryText.Text = latestCoordinatorControl == null
                ? "-"
                : ThumbnailCoordinatorDecisionCategoryResolver.ToDisplayText(
                    latestCoordinatorControl.DecisionCategory
                );
            ApplyDecisionCategoryStyle(latestCoordinatorControl?.DecisionCategory);
            SyncDecisionHistory(latestCoordinatorControl);
            CoordinatorDecisionSummaryText.Text = latestCoordinatorControl == null
                ? "control未取得"
                : string.IsNullOrWhiteSpace(latestCoordinatorControl.DecisionSummary)
                    ? "判断要約なし"
                    : latestCoordinatorControl.DecisionSummary;
            CoordinatorReasonText.Text = latestCoordinatorControl == null
                ? "control未取得"
                : string.IsNullOrWhiteSpace(latestCoordinatorControl.Reason)
                    ? "理由なし"
                    : latestCoordinatorControl.Reason;
        }

        private void ApplyDecisionCategoryStyle(string decisionCategory)
        {
            Brush foreground = Brushes.DimGray;
            if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.RecoveryBiased,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                foreground = Brushes.Firebrick;
            }
            else if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.DelegationCapped,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                foreground = Brushes.DarkGoldenrod;
            }
            else if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.Minimum,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.Steady,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                foreground = Brushes.SeaGreen;
            }
            else if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.DemandBiased,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                foreground = Brushes.SteelBlue;
            }

            CoordinatorDecisionCategoryText.Foreground = foreground;
            CoordinatorDecisionSummaryText.Foreground = foreground;
        }

        private void SyncDecisionHistory(ThumbnailCoordinatorControlSnapshot controlSnapshot)
        {
            CoordinatorDecisionHistory.Clear();
            if (controlSnapshot == null)
            {
                return;
            }

            IReadOnlyList<ThumbnailCoordinatorDecisionHistoryEntry> entries =
                controlSnapshot.DecisionHistory;
            if (entries == null || entries.Count < 1)
            {
                entries =
                [
                    new ThumbnailCoordinatorDecisionHistoryEntry
                    {
                        UpdatedAtUtc = controlSnapshot.UpdatedAtUtc,
                        OperationMode = controlSnapshot.OperationMode,
                        DecisionCategory = controlSnapshot.DecisionCategory,
                        DecisionSummary = controlSnapshot.DecisionSummary,
                        FastSlotCount = controlSnapshot.FastSlotCount,
                        SlowSlotCount = controlSnapshot.SlowSlotCount,
                    },
                ];
            }

            foreach (ThumbnailCoordinatorDecisionHistoryEntry entry in entries)
            {
                CoordinatorDecisionHistory.Add(
                    new ThumbnailCoordinatorDecisionHistoryItem
                    {
                        UpdatedAtText = entry.UpdatedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                        CategoryText = ThumbnailCoordinatorDecisionCategoryResolver.ToDisplayText(
                            entry.DecisionCategory
                        ),
                        SummaryText = BuildDecisionHistorySummary(entry),
                        AccentBrush = ResolveDecisionHistoryBrush(entry.DecisionCategory),
                    }
                );
            }
        }

        private static string BuildDecisionHistorySummary(
            ThumbnailCoordinatorDecisionHistoryEntry entry
        )
        {
            if (entry == null)
            {
                return "判断要約なし";
            }

            string summary = string.IsNullOrWhiteSpace(entry.DecisionSummary)
                ? "判断要約なし"
                : entry.DecisionSummary;
            string modeText = ThumbnailCoordinatorOperationModeResolver.ToDisplayText(
                entry.OperationMode
            );
            if (string.IsNullOrWhiteSpace(modeText) || string.Equals(modeText, "未設定", StringComparison.Ordinal))
            {
                return $"{summary} / slot={entry.FastSlotCount}/{entry.SlowSlotCount}";
            }

            return $"{modeText} / slot={entry.FastSlotCount}/{entry.SlowSlotCount} / {summary}";
        }

        private static Brush ResolveDecisionHistoryBrush(string decisionCategory)
        {
            if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.RecoveryBiased,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Brushes.Firebrick;
            }

            if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.DelegationCapped,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Brushes.DarkGoldenrod;
            }

            if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.Minimum,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.Steady,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Brushes.SeaGreen;
            }

            if (
                string.Equals(
                    decisionCategory,
                    ThumbnailCoordinatorDecisionCategory.DemandBiased,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Brushes.SteelBlue;
            }

            return Brushes.DimGray;
        }

        private void InitializeCoordinatorEditor(
            ThumbnailCoordinatorControlSnapshot controlSnapshot,
            ThumbnailCoordinatorCommandSnapshot commandSnapshot
        )
        {
            currentCoordinatorCommand = commandSnapshot;
            int requestedParallelism =
                commandSnapshot?.RequestedParallelism
                ?? controlSnapshot?.RequestedParallelism
                ?? Math.Max(2, Environment.ProcessorCount / 2);
            int largeMovieThresholdGb =
                commandSnapshot?.LargeMovieThresholdGb
                ?? controlSnapshot?.LargeMovieThresholdGb
                ?? 50;
            bool gpuDecodeEnabled =
                commandSnapshot?.GpuDecodeEnabled
                ?? controlSnapshot?.GpuDecodeEnabled
                ?? false;
            string operationMode =
                commandSnapshot?.OperationMode
                ?? controlSnapshot?.OperationMode
                ?? ThumbnailCoordinatorOperationMode.NormalFirst;
            temporaryParallelismDelta =
                commandSnapshot?.TemporaryParallelismDelta
                ?? controlSnapshot?.TemporaryParallelismDelta
                ?? 0;

            RequestedParallelismTextBox.Text = requestedParallelism.ToString();
            LargeMovieThresholdTextBox.Text = largeMovieThresholdGb.ToString();
            GpuDecodeEnabledCheckBox.IsChecked = gpuDecodeEnabled;
            SelectOperationMode(operationMode);
            UpdateTemporaryDeltaText();
            CoordinatorCommandStatusText.Text = commandSnapshot == null
                ? "command未送信"
                : $"最終command: {commandSnapshot.IssuedAtUtc.ToLocalTime():HH:mm:ss} by {commandSnapshot.IssuedBy}";
            editorInitialized = true;
        }

        private void SelectOperationMode(string operationMode)
        {
            string targetMode = string.IsNullOrWhiteSpace(operationMode)
                ? ThumbnailCoordinatorOperationMode.NormalFirst
                : operationMode;
            foreach (ComboBoxItem item in OperationModeComboBox.Items)
            {
                if (
                    string.Equals(
                        item.Tag as string,
                        targetMode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    OperationModeComboBox.SelectedItem = item;
                    return;
                }
            }

            OperationModeComboBox.SelectedIndex = 0;
        }

        private void UpdateTemporaryDeltaText()
        {
            TemporaryDeltaText.Text = temporaryParallelismDelta.ToString("+0;-0;0");
        }

        private bool TryWriteCoordinatorCommand(string issuedBy)
        {
            if (string.IsNullOrWhiteSpace(options.MainDbFullPath))
            {
                CoordinatorCommandStatusText.Text = "DB未接続のため command を送れません。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.CoordinatorOwnerInstanceId))
            {
                CoordinatorCommandStatusText.Text = "Coordinator owner が未指定です。";
                return false;
            }

            if (
                !int.TryParse(RequestedParallelismTextBox.Text, out int requestedParallelism)
                || requestedParallelism < 1
            )
            {
                CoordinatorCommandStatusText.Text = "希望並列数は 1 以上の整数で指定してください。";
                return false;
            }

            if (
                !int.TryParse(LargeMovieThresholdTextBox.Text, out int largeMovieThresholdGb)
                || largeMovieThresholdGb < 1
            )
            {
                CoordinatorCommandStatusText.Text = "巨大動画閾値は 1 以上の整数で指定してください。";
                return false;
            }

            string operationMode = (OperationModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(operationMode))
            {
                operationMode = ThumbnailCoordinatorOperationMode.NormalFirst;
            }

            currentCoordinatorCommand = new ThumbnailCoordinatorCommandSnapshot
            {
                MainDbFullPath = options.MainDbFullPath,
                DbName = options.DbName,
                OwnerInstanceId = options.CoordinatorOwnerInstanceId,
                RequestedParallelism = requestedParallelism,
                TemporaryParallelismDelta = temporaryParallelismDelta,
                LargeMovieThresholdGb = largeMovieThresholdGb,
                GpuDecodeEnabled = GpuDecodeEnabledCheckBox.IsChecked == true,
                OperationMode = operationMode,
                IssuedBy = issuedBy ?? "progress-viewer",
                IssuedAtUtc = DateTime.UtcNow,
            };
            ThumbnailCoordinatorCommandStore.Save(currentCoordinatorCommand);
            CoordinatorCommandStatusText.Text =
                $"command送信: {DateTime.Now:HH:mm:ss} / delta={temporaryParallelismDelta:+0;-0;0}";
            return true;
        }

        private void ApplyCoordinatorCommandButton_Click(object sender, RoutedEventArgs e)
        {
            _ = TryWriteCoordinatorCommand("progress-viewer");
        }

        private void TemporaryDeltaMinusButton_Click(object sender, RoutedEventArgs e)
        {
            temporaryParallelismDelta = Math.Max(-8, temporaryParallelismDelta - 1);
            UpdateTemporaryDeltaText();
            _ = TryWriteCoordinatorCommand("progress-viewer-temp");
        }

        private void TemporaryDeltaResetButton_Click(object sender, RoutedEventArgs e)
        {
            temporaryParallelismDelta = 0;
            UpdateTemporaryDeltaText();
            _ = TryWriteCoordinatorCommand("progress-viewer-temp");
        }

        private void TemporaryDeltaPlusButton_Click(object sender, RoutedEventArgs e)
        {
            temporaryParallelismDelta = Math.Min(8, temporaryParallelismDelta + 1);
            UpdateTemporaryDeltaText();
            _ = TryWriteCoordinatorCommand("progress-viewer-temp");
        }

        // 親プロセスが消えたら Viewer も残さない。
        private void CloseIfParentExited()
        {
            if (options.ParentProcessId <= 0)
            {
                return;
            }

            try
            {
                using Process parent = Process.GetProcessById(options.ParentProcessId);
                if (parent.HasExited)
                {
                    Close();
                }
            }
            catch
            {
                Close();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ThumbnailCoordinatorDecisionHistoryItem
    {
        public string UpdatedAtText { get; init; } = "";
        public string CategoryText { get; init; } = "";
        public string SummaryText { get; init; } = "";
        public Brush AccentBrush { get; init; } = Brushes.DimGray;
    }
}

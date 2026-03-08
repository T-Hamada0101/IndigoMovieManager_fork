using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    /// <summary>
    /// Settings.xaml の相互作用ロジック
    /// </summary>
    public partial class CommonSettingsWindow : Window
    {
        private bool _isThumbnailParallelismSyncing;
        private bool _isThumbnailLaneThresholdSyncing;
        private bool _isThumbnailThreadPresetSyncing;

        // 共通設定画面の初期化。
        // 閉じるイベントで設定保存するため、ここでイベントを接続する。
        public CommonSettingsWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
            Closed += CommonSettingsWindow_Closed;
            PreviewKeyDown += CommonSettingsWindow_PreviewKeyDown;
            ThumbnailThreadPresetSelector.SelectionChanged +=
                ThumbnailThreadPresetSelector_SelectionChanged;
            sliderThumbnailParallelism.ValueChanged += SliderThumbnailParallelism_ValueChanged;
            sliderThumbnailSlowLaneMinGb.ValueChanged +=
                SliderThumbnailSlowLaneMinGb_ValueChanged;
            ThumbnailGpuDecodeEnabled.Click += ThumbnailGpuDecodeEnabled_Click;
            Properties.Settings.Default.PropertyChanged += SettingsDefault_PropertyChanged;
            DefaultPlayerParam.ItemsSource = new string[]
            {
                "/start <ms>",
                "<file> player -seek pos=<ms>"
            };
            string normalizedProvider = FileIndexProviderFactory.NormalizeProviderKey(
                Properties.Settings.Default.FileIndexProvider
            );
            FileIndexProviderSelector.SelectedValue = normalizedProvider;
            ThumbnailThreadPresetSelector.SelectedValue = ThumbnailThreadPresetResolver.NormalizePresetKey(
                Properties.Settings.Default.ThumbnailThreadPreset
            );
            SyncThumbnailThreadPresetEditingState();
            SyncThumbnailParallelismSliderFromSettings();
            SyncThumbnailLaneThresholdSlidersFromSettings();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // 画面上の値を Properties.Settings へ反映して永続化する。
            Properties.Settings.Default.AutoOpen = (bool)AutoOpen.IsChecked;
            Properties.Settings.Default.ConfirmExit = (bool)ConfirmExit.IsChecked;
            Properties.Settings.Default.DefaultPlayerPath = DefaultPlayerPath.Text;
            Properties.Settings.Default.DefaultPlayerParam = DefaultPlayerParam.Text;
            Properties.Settings.Default.RecentFilesCount = (int)slider.Value;
            Properties.Settings.Default.ThumbnailGpuDecodeEnabled = (bool)ThumbnailGpuDecodeEnabled.IsChecked;
            // Delキー押下時の動作を保存する。範囲外は既存互換の「登録解除」に戻す。
            int deleteKeyActionMode = DeleteKeyActionMode.SelectedIndex;
            if (deleteKeyActionMode < 0 || deleteKeyActionMode > 1)
            {
                deleteKeyActionMode = 0;
            }
            Properties.Settings.Default.DeleteKeyActionMode = deleteKeyActionMode;
            // OFF/AUTO/ONの3値設定を保存する。範囲外はAUTOへ丸める。
            int integrationMode = EverythingIntegrationMode.SelectedIndex;
            if (integrationMode < 0 || integrationMode > 2)
            {
                integrationMode = 1;
            }
            Properties.Settings.Default.EverythingIntegrationMode = integrationMode;
            // 旧設定との互換のため、OFF以外をtrueとして同期する。
            Properties.Settings.Default.EverythingIntegrationEnabled = integrationMode != 0;
            string selectedProvider = FileIndexProviderSelector.SelectedValue as string;
            Properties.Settings.Default.FileIndexProvider = FileIndexProviderFactory.NormalizeProviderKey(
                selectedProvider
            );
            string selectedThumbnailThreadPreset =
                ThumbnailThreadPresetSelector.SelectedValue as string;
            Properties.Settings.Default.ThumbnailThreadPreset = ThumbnailThreadPresetResolver.NormalizePresetKey(
                selectedThumbnailThreadPreset
            );
            // サムネイル作成の並列数を保存する（2〜24）。
            Properties.Settings.Default.ThumbnailParallelism = (int)sliderThumbnailParallelism.Value;
            // 巨大動画判定閾値だけを保存し、旧優先レーン閾値は新方式では使わない。
            Properties.Settings.Default.ThumbnailSlowLaneMinGb = ClampThumbnailSlowLaneMinGb(
                (int)System.Math.Round(sliderThumbnailSlowLaneMinGb.Value)
            );
            Properties.Settings.Default.Save();
        }

        // 共通設定を閉じる時にイベント購読を解除する。
        private void CommonSettingsWindow_Closed(object sender, System.EventArgs e)
        {
            PreviewKeyDown -= CommonSettingsWindow_PreviewKeyDown;
            ThumbnailThreadPresetSelector.SelectionChanged -=
                ThumbnailThreadPresetSelector_SelectionChanged;
            sliderThumbnailParallelism.ValueChanged -= SliderThumbnailParallelism_ValueChanged;
            sliderThumbnailSlowLaneMinGb.ValueChanged -=
                SliderThumbnailSlowLaneMinGb_ValueChanged;
            ThumbnailGpuDecodeEnabled.Click -= ThumbnailGpuDecodeEnabled_Click;
            Properties.Settings.Default.PropertyChanged -= SettingsDefault_PropertyChanged;
            Closed -= CommonSettingsWindow_Closed;
        }

        // スライダー変更は即座に設定へ反映し、実行中の並列数制御にも即時反映させる。
        private void SliderThumbnailParallelism_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isThumbnailParallelismSyncing)
            {
                return;
            }

            EnsureCustomPresetForManualParallelism();

            int next = ClampThumbnailParallelism((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.ThumbnailParallelism == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailParallelism = next;
            NotifyThumbnailCoordinatorSettingsChanged("common-settings:parallelism-change");
        }

        // 他経路（ショートカット等）で設定値が変わった時、スライダーを即時追従させる。
        private void SettingsDefault_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            string propertyName = e?.PropertyName ?? "";
            if (
                string.Equals(
                    propertyName,
                    nameof(Properties.Settings.Default.ThumbnailThreadPreset),
                    System.StringComparison.Ordinal
                )
            )
            {
                SyncThumbnailThreadPresetSelectorFromSettings();
                SyncThumbnailThreadPresetEditingState();
                return;
            }

            if (
                string.Equals(
                    propertyName,
                    nameof(Properties.Settings.Default.ThumbnailParallelism),
                    System.StringComparison.Ordinal
                )
            )
            {
                if (_isThumbnailParallelismSyncing)
                {
                    return;
                }
                SyncThumbnailParallelismSliderFromSettings();
                return;
            }

            if (
                string.Equals(
                    propertyName,
                    nameof(Properties.Settings.Default.ThumbnailSlowLaneMinGb),
                    System.StringComparison.Ordinal
                )
            )
            {
                if (_isThumbnailLaneThresholdSyncing)
                {
                    return;
                }
                SyncThumbnailLaneThresholdSlidersFromSettings();
                return;
            }
        }

        // 共通設定画面でも Ctrl + / Ctrl - で並列数を即時変更する。
        private void CommonSettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            int delta = key switch
            {
                Key.Add => 1,
                Key.OemPlus => 1,
                Key.Subtract => -1,
                Key.OemMinus => -1,
                _ => 0,
            };
            if (delta == 0)
            {
                return;
            }

            int current = ResolveEffectiveThumbnailParallelism();
            EnsureCustomPresetForManualParallelism();
            int next = ClampThumbnailParallelism(current + delta);
            if (current != next)
            {
                Properties.Settings.Default.ThumbnailParallelism = next;
                NotifyThumbnailCoordinatorSettingsChanged("common-settings:shortcut");
            }

            e.Handled = true;
        }

        // 設定値をスライダーへ同期する。値が同じ場合は何もしない。
        private void SyncThumbnailParallelismSliderFromSettings()
        {
            if (_isThumbnailParallelismSyncing)
            {
                return;
            }

            int next = ClampThumbnailParallelism(Properties.Settings.Default.ThumbnailParallelism);
            if (Properties.Settings.Default.ThumbnailParallelism != next)
            {
                Properties.Settings.Default.ThumbnailParallelism = next;
            }

            if (System.Math.Abs(sliderThumbnailParallelism.Value - next) < 0.0001d)
            {
                return;
            }

            _isThumbnailParallelismSyncing = true;
            try
            {
                sliderThumbnailParallelism.Value = next;
            }
            finally
            {
                _isThumbnailParallelismSyncing = false;
            }
        }

        // プリセット選択は設定値へ即時反映し、手動編集可否もここで切り替える。
        private void ThumbnailThreadPresetSelector_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e
        )
        {
            if (_isThumbnailThreadPresetSyncing)
            {
                return;
            }

            string selectedPreset = ThumbnailThreadPresetSelector.SelectedValue as string;
            string normalizedPreset = ThumbnailThreadPresetResolver.NormalizePresetKey(selectedPreset);
            if (Properties.Settings.Default.ThumbnailThreadPreset != normalizedPreset)
            {
                Properties.Settings.Default.ThumbnailThreadPreset = normalizedPreset;
                NotifyThumbnailCoordinatorSettingsChanged("common-settings:preset-change");
            }

            SyncThumbnailThreadPresetEditingState();
        }

        // 設定値からコンボボックス選択を同期する。
        private void SyncThumbnailThreadPresetSelectorFromSettings()
        {
            string next = ThumbnailThreadPresetResolver.NormalizePresetKey(
                Properties.Settings.Default.ThumbnailThreadPreset
            );
            string current = ThumbnailThreadPresetSelector.SelectedValue as string;
            if (string.Equals(current, next, System.StringComparison.Ordinal))
            {
                return;
            }

            _isThumbnailThreadPresetSyncing = true;
            try
            {
                ThumbnailThreadPresetSelector.SelectedValue = next;
            }
            finally
            {
                _isThumbnailThreadPresetSyncing = false;
            }
        }

        // custum の時だけ手動並列数を前面に出す。
        private void SyncThumbnailThreadPresetEditingState()
        {
            bool isCustom = string.Equals(
                ThumbnailThreadPresetResolver.NormalizePresetKey(
                    Properties.Settings.Default.ThumbnailThreadPreset
                ),
                ThumbnailThreadPresetResolver.PresetCustum,
                System.StringComparison.Ordinal
            );

            sliderThumbnailParallelism.IsEnabled = isCustom;
            ThumbnailParallelismText.Opacity = isCustom ? 1.0d : 0.7d;
            sliderThumbnailParallelism.Opacity = isCustom ? 1.0d : 0.7d;
            sliderThumbnailParallelism.ToolTip = isCustom
                ? "custum 選択時は手動並列数を直接編集できます。"
                : "手動並列数の編集は custum 選択時のみ有効です。";
        }

        // 手動並列数を変更する操作は、明示的に custum 扱いへ寄せる。
        private void EnsureCustomPresetForManualParallelism()
        {
            string currentPreset = ThumbnailThreadPresetResolver.NormalizePresetKey(
                Properties.Settings.Default.ThumbnailThreadPreset
            );
            if (
                string.Equals(
                    currentPreset,
                    ThumbnailThreadPresetResolver.PresetCustum,
                    System.StringComparison.Ordinal
                )
            )
            {
                return;
            }

            Properties.Settings.Default.ThumbnailThreadPreset =
                ThumbnailThreadPresetResolver.PresetCustum;
            SyncThumbnailThreadPresetSelectorFromSettings();
            SyncThumbnailThreadPresetEditingState();
        }

        // 現在有効なプリセット込みの実効並列数を返す。
        private static int ResolveEffectiveThumbnailParallelism()
        {
            return ThumbnailThreadPresetResolver.ResolveParallelism(
                Properties.Settings.Default.ThumbnailThreadPreset,
                Properties.Settings.Default.ThumbnailParallelism,
                System.Environment.ProcessorCount
            );
        }

        // 低速レーン開始(GB)の変更を即時設定へ反映する。
        private void SliderThumbnailSlowLaneMinGb_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isThumbnailLaneThresholdSyncing)
            {
                return;
            }

            int next = ClampThumbnailSlowLaneMinGb((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.ThumbnailSlowLaneMinGb == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailSlowLaneMinGb = next;
            NotifyThumbnailCoordinatorSettingsChanged("common-settings:slow-threshold-change");
        }

        private void ThumbnailGpuDecodeEnabled_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ThumbnailGpuDecodeEnabled =
                ThumbnailGpuDecodeEnabled.IsChecked == true;
            NotifyThumbnailCoordinatorSettingsChanged("common-settings:gpu-change");
        }

        // 巨大動画判定閾値スライダーを設定値へ同期する。
        private void SyncThumbnailLaneThresholdSlidersFromSettings()
        {
            if (_isThumbnailLaneThresholdSyncing)
            {
                return;
            }

            _isThumbnailLaneThresholdSyncing = true;
            try
            {
                int nextSlowGb = ClampThumbnailSlowLaneMinGb(
                    Properties.Settings.Default.ThumbnailSlowLaneMinGb
                );
                if (Properties.Settings.Default.ThumbnailSlowLaneMinGb != nextSlowGb)
                {
                    Properties.Settings.Default.ThumbnailSlowLaneMinGb = nextSlowGb;
                }

                bool sameSlow =
                    System.Math.Abs(sliderThumbnailSlowLaneMinGb.Value - nextSlowGb) < 0.0001d;
                if (sameSlow)
                {
                    return;
                }

                sliderThumbnailSlowLaneMinGb.Value = nextSlowGb;
            }
            finally
            {
                _isThumbnailLaneThresholdSyncing = false;
            }
        }

        // サムネイル並列数は 2〜24 の範囲に制限する。
        private static int ClampThumbnailParallelism(int value)
        {
            if (value < 2)
            {
                return 2;
            }
            if (value > 24)
            {
                return 24;
            }
            return value;
        }

        // 低速レーン開始(GB)は 1〜1024 の範囲に制限する。
        private static int ClampThumbnailSlowLaneMinGb(int value)
        {
            if (value < 1)
            {
                return 1;
            }
            if (value > 1024)
            {
                return 1024;
            }
            return value;
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定画面を閉じてメインへ戻る。
            Close();
        }

        private static void NotifyThumbnailCoordinatorSettingsChanged(string source)
        {
            if (Application.Current?.MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            mainWindow.PublishThumbnailCoordinatorCommandFromCurrentSettings(
                string.IsNullOrWhiteSpace(source) ? "common-settings" : source
            );
        }

        private void OpenDialogPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定の既定プレイヤー実行ファイルを選択する。
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "既定のプレイヤー選択"
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                DefaultPlayerPath.Text = ofd.FileName;
            }
        }
    }
}

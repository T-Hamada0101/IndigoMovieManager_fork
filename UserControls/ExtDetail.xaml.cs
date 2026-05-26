using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using IndigoMovieManager.Converter;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// ExtDetail.xaml の相互作用ロジック
    /// </summary>
    public partial class ExtDetail : UserControl, INotifyPropertyChanged
    {
        private bool _isSyncingDetailThumbnailModeUi;
        private string _appliedDetailThumbnailMode = "";
        private MovieRecords _subscribedRecord;
        private int _detailThumbnailDecodePixelHeight;
        private FileSystemWatcher _detailThumbnailFileWatcher;
        private string _watchedDetailThumbnailPath = "";
        private int _detailThumbnailWatchRevision;

        public event PropertyChangedEventHandler PropertyChanged;

        public int DetailThumbnailDecodePixelHeight
        {
            get => _detailThumbnailDecodePixelHeight;
            private set
            {
                if (_detailThumbnailDecodePixelHeight == value)
                {
                    return;
                }

                _detailThumbnailDecodePixelHeight = value;
                if (
                    _subscribedRecord != null
                    && !string.IsNullOrWhiteSpace(_subscribedRecord.ThumbDetail)
                )
                {
                    NoLockImageConverter.InvalidateFilePath(_subscribedRecord.ThumbDetail);
                }
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(DetailThumbnailDecodePixelHeight))
                );
                // PropertyChanged で MultiBinding の ConverterParameter が変化し、
                // WPF が自動で MultiBinding を再評価する。
                // 明示的な RefreshDetailThumbnailImage() は呼び出し元に集約。
            }
        }

        // MultiBinding（ConverterBindableParameter）を壊さず画像バインドを再評価する。
        // キャッシュ無効化は呼び出し元で NoLockImageConverter.InvalidateFilePath を先に実行済み。
        private void RefreshDetailThumbnailImage()
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // GetBindingExpression は MultiBinding に null を返すため、
                    // Binding 種別を問わない GetBindingExpressionBase を使う。
                    BindingExpressionBase binding =
                        BindingOperations.GetBindingExpressionBase(
                            DetailThumbnailImage,
                            Image.SourceProperty
                        );
                    binding?.UpdateTarget();
                })
            );
        }

        // 詳細ペインの初期化。
        // 選択切替時にMainWindowからDataContextを差し替えて使う。
        public ExtDetail()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
            DataContextChanged += ExtDetail_DataContextChanged;
            Unloaded += ExtDetail_Unloaded;
            UpdateSubscribedRecord(DataContext as MovieRecords);
            ApplyConfiguredDetailThumbnailMode();
        }

        private void ExtDetail_Unloaded(object sender, RoutedEventArgs e)
        {
            StopDetailThumbnailFileWatcher();
        }

        private void LabelExtDetail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainWindow ownerWindow = Window.GetWindow(this) as MainWindow;
            if (ownerWindow != null)
            {
                // 画像クリック時も、前面表示中なら現在の詳細サイズで再評価する。
                ownerWindow.ReevaluateActiveExtensionDetailThumbnail();
            }

            // サムネイルのダブルクリックは、親Windowの再生処理へ委譲する。
            if (e.ClickCount >= 2 && e.LeftButton == MouseButtonState.Pressed)
            {
                ownerWindow.PlayMovie_Click(sender, e);
            }
        }

        private async void DetailThumbnailImage_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e
        )
        {
            try
            {
                if (sender is not System.Windows.FrameworkElement imageElement)
                {
                    return;
                }

                if (imageElement.DataContext is not MovieRecords record)
                {
                    return;
                }

                string thumbDetailSnapshot = record.ThumbDetail;

                // 右クリック時は選択中のパスだけ固定し、存在確認は背景へ逃がす。
                if (record.IsExists && await HasDetailThumbnailFileAsync(thumbDetailSnapshot))
                {
                    return;
                }

                if (
                    Dispatcher.HasShutdownStarted
                    || Dispatcher.HasShutdownFinished
                    || !ReferenceEquals(imageElement.DataContext, record)
                    || !string.Equals(
                        record.ThumbDetail,
                        thumbDetailSnapshot,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                if (Window.GetWindow(this) is not MainWindow ownerWindow)
                {
                    return;
                }

                RefreshDetailThumbnailImage();
                ownerWindow.ReevaluateActiveExtensionDetailThumbnail();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("ext-detail", $"context thumbnail check failed: {ex.Message}");
            }
        }

        private static Task<bool> HasDetailThumbnailFileAsync(string thumbDetailPath)
        {
            if (string.IsNullOrWhiteSpace(thumbDetailPath))
            {
                return Task.FromResult(false);
            }

            if (MainWindow.IsThumbnailErrorPlaceholderPath(thumbDetailPath))
            {
                return Task.FromResult(false);
            }

            return PathExistsInBackgroundAsync(thumbDetailPath);
        }

        public void Refresh()
        {
            if (!IsVisible || DataContext is not MovieRecords)
            {
                return;
            }

            // 詳細タブのタグは選択中1件だけなので、表示中の軽い view-local 更新に閉じる。
            CollectionViewSource.GetDefaultView(ExtDetailTags.ItemsSource)?.Refresh();
        }

        public void ApplyThumbnailDisplaySize(int width, int height)
        {
            // 表示サイズは固定値で持たず、残り領域へ Uniform でフィットさせる。
            DetailThumbnailImage.ClearValue(WidthProperty);
            DetailThumbnailImage.ClearValue(HeightProperty);
        }

        public void ApplyConfiguredDetailThumbnailMode()
        {
            string currentMode = ThumbnailDetailModeRuntime.Normalize(
                IndigoMovieManager.Properties.Settings.Default.DetailThumbnailMode
            );
            ApplyThumbnailDisplaySizeForCurrentContext(currentMode);
            if (
                string.Equals(
                    _appliedDetailThumbnailMode,
                    currentMode,
                    StringComparison.Ordinal
                )
            )
            {
                // 選択切替ごとに同じモードを再適用すると、無駄なレイアウト更新が走るので止める。
                return;
            }

            _isSyncingDetailThumbnailModeUi = true;
            try
            {
                foreach (object item in DetailThumbnailModeComboBox.Items)
                {
                    if (item is ComboBoxItem comboBoxItem)
                    {
                        comboBoxItem.IsSelected = string.Equals(
                            comboBoxItem.Tag?.ToString(),
                            currentMode,
                            StringComparison.Ordinal
                        );
                    }
                }

                _appliedDetailThumbnailMode = currentMode;
            }
            finally
            {
                _isSyncingDetailThumbnailModeUi = false;
            }
        }

        private void DetailThumbnailModeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_isSyncingDetailThumbnailModeUi)
            {
                return;
            }

            if (DetailThumbnailModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            string selectedMode = selectedItem.Tag?.ToString() ?? "";
            ApplyThumbnailDisplaySizeForCurrentContext(selectedMode);
            _appliedDetailThumbnailMode = ThumbnailDetailModeRuntime.Normalize(selectedMode);

            if (Window.GetWindow(this) is MainWindow ownerWindow)
            {
                ownerWindow.ChangeExtensionDetailThumbnailMode(selectedMode);
            }
        }

        private void ExtDetail_DataContextChanged(
            object sender,
            DependencyPropertyChangedEventArgs e
        )
        {
            UpdateSubscribedRecord(e.NewValue as MovieRecords);
            ApplyConfiguredDetailThumbnailMode();
        }

        private void UpdateSubscribedRecord(MovieRecords record)
        {
            if (ReferenceEquals(_subscribedRecord, record))
            {
                return;
            }

            if (_subscribedRecord != null)
            {
                _subscribedRecord.PropertyChanged -= SubscribedRecord_PropertyChanged;
            }

            _subscribedRecord = record;

            if (_subscribedRecord != null)
            {
                _subscribedRecord.PropertyChanged += SubscribedRecord_PropertyChanged;
                ConfigureDetailThumbnailFileWatch();
            }
            else
            {
                StopDetailThumbnailFileWatcher();
            }
        }

        private void SubscribedRecord_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e?.PropertyName, nameof(MovieRecords.ThumbDetail), StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyConfiguredDetailThumbnailMode));
            Dispatcher.BeginInvoke(new Action(ConfigureDetailThumbnailFileWatch));
        }

        private static bool IsDetailThumbnailPlaceholder(string path)
        {
            return MainWindow.IsThumbnailErrorPlaceholderPath(path);
        }

        private string ResolveWatchedDetailThumbnailPath(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
            }
            catch
            {
                return string.IsNullOrWhiteSpace(path) ? "" : path;
            }
        }

        private async void ConfigureDetailThumbnailFileWatch()
        {
            StopDetailThumbnailFileWatcher();
            int watchRevision = ++_detailThumbnailWatchRevision;

            MovieRecords subscribedRecordSnapshot = _subscribedRecord;
            if (subscribedRecordSnapshot == null)
            {
                return;
            }

            string targetPath = subscribedRecordSnapshot.ThumbDetail;
            if (string.IsNullOrWhiteSpace(targetPath) || IsDetailThumbnailPlaceholder(targetPath))
            {
                return;
            }

            string normalizedTargetPath = ResolveWatchedDetailThumbnailPath(targetPath);
            string directoryPath;
            string fileName;
            try
            {
                directoryPath = Path.GetDirectoryName(normalizedTargetPath) ?? "";
                fileName = Path.GetFileName(normalizedTargetPath);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            DetailThumbnailWatchPathState pathState =
                await GetDetailThumbnailWatchPathStateAsync(normalizedTargetPath, directoryPath);

            if (
                Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished
                || watchRevision != _detailThumbnailWatchRevision
                || !ReferenceEquals(_subscribedRecord, subscribedRecordSnapshot)
                || !string.Equals(
                    subscribedRecordSnapshot.ThumbDetail,
                    targetPath,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            if (pathState.TargetExists)
            {
                NoLockImageConverter.InvalidateFilePath(normalizedTargetPath);
                RefreshDetailThumbnailImage();
                return;
            }

            if (!pathState.DirectoryExists)
            {
                return;
            }

            try
            {
                _detailThumbnailFileWatcher = new FileSystemWatcher(directoryPath, fileName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false,
                };
                _detailThumbnailFileWatcher.Created += DetailThumbnailFileWatcher_Changed;
                _detailThumbnailFileWatcher.Changed += DetailThumbnailFileWatcher_Changed;
                _detailThumbnailFileWatcher.Renamed += DetailThumbnailFileWatcher_Renamed;
                _detailThumbnailFileWatcher.Error += DetailThumbnailFileWatcher_Error;
                _watchedDetailThumbnailPath = normalizedTargetPath;
            }
            catch
            {
                StopDetailThumbnailFileWatcher();
            }
        }

        private async void DetailThumbnailFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!string.Equals(
                    ResolveWatchedDetailThumbnailPath(e.FullPath),
                    _watchedDetailThumbnailPath,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return;
                }

                string watchedPathSnapshot = _watchedDetailThumbnailPath;
                MovieRecords subscribedRecordSnapshot = _subscribedRecord;
                if (subscribedRecordSnapshot == null || string.IsNullOrWhiteSpace(watchedPathSnapshot))
                {
                    return;
                }

                // ファイル生成直後の存在確認は背景へ逃がし、UI は画像差し替えだけを受け持つ。
                if (!await PathExistsInBackgroundAsync(watchedPathSnapshot))
                {
                    return;
                }

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _ = Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (
                            Dispatcher.HasShutdownStarted
                            || Dispatcher.HasShutdownFinished
                            || !ReferenceEquals(_subscribedRecord, subscribedRecordSnapshot)
                            || !string.Equals(
                                _watchedDetailThumbnailPath,
                                watchedPathSnapshot,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            return;
                        }

                        NoLockImageConverter.InvalidateFilePath(watchedPathSnapshot);
                        RefreshDetailThumbnailImage();
                        StopDetailThumbnailFileWatcher();
                    }),
                    System.Windows.Threading.DispatcherPriority.Background
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write("ext-detail", $"watch thumbnail change failed: {ex.Message}");
            }
        }

        private void DetailThumbnailFileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            DetailThumbnailFileWatcher_Changed(sender, e);
        }

        private void DetailThumbnailFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            StopDetailThumbnailFileWatcher();
        }

        private void StopDetailThumbnailFileWatcher()
        {
            _detailThumbnailWatchRevision++;
            if (_detailThumbnailFileWatcher != null)
            {
                try
                {
                    _detailThumbnailFileWatcher.EnableRaisingEvents = false;
                    _detailThumbnailFileWatcher.Created -= DetailThumbnailFileWatcher_Changed;
                    _detailThumbnailFileWatcher.Changed -= DetailThumbnailFileWatcher_Changed;
                    _detailThumbnailFileWatcher.Renamed -= DetailThumbnailFileWatcher_Renamed;
                    _detailThumbnailFileWatcher.Error -= DetailThumbnailFileWatcher_Error;
                    _detailThumbnailFileWatcher.Dispose();
                }
                finally
                {
                    _detailThumbnailFileWatcher = null;
                    _watchedDetailThumbnailPath = "";
                }
            }
        }

        private void ApplyThumbnailDisplaySizeForCurrentContext(string mode)
        {
            ApplyThumbnailDisplaySize(0, 0);
            DetailThumbnailDecodePixelHeight = ResolveDetailThumbnailDecodePixelHeight(mode);
            RefreshDetailThumbnailImage();
        }

        private async void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            // 親フォルダ上で対象ファイルを選択状態で開く。
            var item = sender as Hyperlink;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                if (mv != null)
                {
                    string moviePath = mv.Movie_Path;
                    // クリック直後はパスの snapshot だけ取り、存在確認は UI スレッドから外す。
                    if (await PathExistsInBackgroundAsync(moviePath))
                    {
                        Process.Start("explorer.exe", $"/select,{moviePath}");
                    }
                }
            }
        }

        private static Task<bool> PathExistsInBackgroundAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult(false);
            }

            return Task.Run(() =>
            {
                try
                {
                    return Path.Exists(path);
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("ext-detail", $"path exists failed: {ex.Message}");
                    return false;
                }
            });
        }

        private static Task<DetailThumbnailWatchPathState> GetDetailThumbnailWatchPathStateAsync(
            string targetPath,
            string directoryPath
        )
        {
            return Task.Run(() =>
            {
                try
                {
                    return new DetailThumbnailWatchPathState(
                        Path.Exists(targetPath),
                        Directory.Exists(directoryPath)
                    );
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write("ext-detail", $"watch path check failed: {ex.Message}");
                    return new DetailThumbnailWatchPathState(false, false);
                }
            });
        }

        private readonly struct DetailThumbnailWatchPathState
        {
            public DetailThumbnailWatchPathState(bool targetExists, bool directoryExists)
            {
                TargetExists = targetExists;
                DirectoryExists = directoryExists;
            }

            public bool TargetExists { get; }

            public bool DirectoryExists { get; }
        }

        private async void FileNameLink_Click(object sender, RoutedEventArgs e)
        {
            // ファイル名リンクは完全一致検索（"..."）としてSearchBoxへ投入する。
            // DataContext からファイル名を取得
            MainWindow ownerWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (ownerWindow == null)
            {
                return;
            }

            if (DataContext is not MovieRecords record)
            {
                return;
            }

            var quoted = $"\"{record.Movie_Body}\"";
            await ownerWindow.ApplySearchKeywordFromLinkAsync(quoted);
        }

        private async void Ext_Click(object sender, RoutedEventArgs e)
        {
            // 拡張子リンクは拡張子検索としてSearchBoxへ投入する。
            MainWindow ownerWindow = Window.GetWindow(this) as MainWindow;
            if (ownerWindow == null)
            {
                return;
            }

            var item = sender as Hyperlink;
            if (item == null)
            {
                return;
            }

            MovieRecords mv = item.DataContext as MovieRecords;
            if (mv == null)
            {
                return;
            }

            await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Ext);
        }

        private static int ResolveDetailThumbnailDecodePixelHeight(string mode)
        {
            return ThumbnailDetailModeRuntime.GetDisplayHeight(mode);
        }
    }
}

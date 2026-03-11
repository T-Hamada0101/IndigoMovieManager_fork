using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Thumbnail.DropTool
{
    public partial class DropToolWindow : Window
    {
        private static readonly HashSet<string> SupportedExtensions = new(
            [
                ".3g2",
                ".3gp",
                ".avi",
                ".asf",
                ".avs",
                ".bmp",
                ".divx",
                ".flv",
                ".gif",
                ".jpeg",
                ".jpg",
                ".m2ts",
                ".m4v",
                ".mkv",
                ".mov",
                ".mp4",
                ".mpeg",
                ".mpg",
                ".ogm",
                ".png",
                ".swf",
                ".ts",
                ".webm",
                ".webp",
                ".wmv",
            ],
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly IReadOnlyList<ThumbnailSizeOption> ThumbnailSizeOptions =
        [
            new ThumbnailSizeOption(0, "Small", "120x90x3x1"),
            new ThumbnailSizeOption(1, "Big", "200x150x3x1"),
            new ThumbnailSizeOption(2, "Grid", "160x120x1x1"),
            new ThumbnailSizeOption(3, "List", "56x42x5x1"),
            new ThumbnailSizeOption(4, "5x2", "120x90x5x2"),
        ];
        private static readonly IReadOnlyList<ParallelismOption> ParallelismOptions =
            CreateParallelismOptions();

        private readonly HashSet<string> inputRoots = new(StringComparer.OrdinalIgnoreCase);
        private string outputFolderPath = "";
        private bool isRunning;

        public DropToolWindow(DropToolStartupContext startupContext = null)
        {
            InitializeComponent();
            Loaded += DropToolWindow_Loaded;
            ThumbnailSizeComboBox.ItemsSource = ThumbnailSizeOptions;
            ThumbnailSizeComboBox.SelectedIndex = 0;
            ParallelismComboBox.ItemsSource = ParallelismOptions;
            ParallelismComboBox.SelectedItem = ResolveDefaultParallelismOption();
            StatusTextBlock.Text = "待機中";
            ApplyStartupContext(startupContext);
            RefreshView();
            AppendLog("この画面が Worker.exe 本体です。");
            AppendLog("入力待機中です。");
        }

        // 起動直後に背面や最小化へ埋もれにくいよう、通常表示へ戻して前面へ寄せる。
        private void DropToolWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Topmost = true;
            Activate();
            Focus();
            Topmost = false;
        }

        private void InputDropArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void InputDropArea_Drop(object sender, DragEventArgs e)
        {
            AddInputRoots(ExtractDroppedPaths(e));

            RefreshView();
            AppendLog($"入力ドロップを受け取りました: {inputRoots.Count} 件");
        }

        private void OutputDropArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OutputDropArea_Drop(object sender, DragEventArgs e)
        {
            string path = ExtractDroppedPaths(e)
                .Select(Path.GetFullPath)
                .FirstOrDefault(Directory.Exists);
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(
                    this,
                    "出力フォルダにはフォルダを1つドロップしてください。",
                    "出力フォルダ未選択",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            outputFolderPath = path;
            RefreshView();
            AppendLog($"出力フォルダを設定しました: {outputFolderPath}");
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                return;
            }

            List<string> targetFiles = CollectTargetFiles().ToList();
            if (targetFiles.Count < 1)
            {
                MessageBox.Show(
                    this,
                    "入力側に処理対象のファイルまたはフォルダをドロップしてください。",
                    "入力未選択",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(outputFolderPath))
            {
                MessageBox.Show(
                    this,
                    "出力フォルダをドロップするか、入力フォルダから自動設定された値を使ってください。",
                    "出力未選択",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            isRunning = true;
            RefreshView();

            int successCount = 0;
            int failedCount = 0;
            int completedCount = 0;
            string resolvedOutputFolderPath = outputFolderPath;

            try
            {
                Directory.CreateDirectory(resolvedOutputFolderPath);

                string dbName = new DirectoryInfo(resolvedOutputFolderPath).Name;
                if (string.IsNullOrWhiteSpace(dbName))
                {
                    dbName = "drop-output";
                }

                ThumbnailSizeOption selectedSize =
                    ThumbnailSizeComboBox.SelectedItem as ThumbnailSizeOption
                    ?? ThumbnailSizeOptions[0];
                ParallelismOption selectedParallelism =
                    ParallelismComboBox.SelectedItem as ParallelismOption
                    ?? ParallelismOptions[0];

                AppendLog(
                    $"処理開始: 対象={targetFiles.Count} 件, サイズ={selectedSize.DisplayText}, 並列={selectedParallelism.Value}, 出力='{resolvedOutputFolderPath}'"
                );
                SetStatusText($"処理中 0/{targetFiles.Count}");

                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = selectedParallelism.Value,
                };
                await Parallel.ForEachAsync(
                    targetFiles,
                    parallelOptions,
                    async (filePath, cancellationToken) =>
                    {
                        try
                        {
                            QueueObj queueObj = new()
                            {
                                MovieFullPath = filePath,
                                MovieSizeBytes = TryGetFileSize(filePath),
                                Tabindex = selectedSize.TabIndex,
                            };
                            ThumbnailCreationRuntime runtime =
                                ThumbnailCreationRuntimeFactory.CreateDefault(
                                    new DropToolVideoMetadataProvider(),
                                    new DropToolThumbnailLogger(AppendLog)
                                );

                            ThumbnailCreateResult result = await CreateThumbWithRecoveryAsync(
                                runtime,
                                queueObj,
                                dbName,
                                resolvedOutputFolderPath,
                                cancellationToken
                            )
                                .ConfigureAwait(false);

                            if (result?.IsSuccess == true)
                            {
                                Interlocked.Increment(ref successCount);
                                AppendLog(
                                    $"[OK] {Path.GetFileName(filePath)} -> {result.SaveThumbFileName}"
                                );
                            }
                            else
                            {
                                Interlocked.Increment(ref failedCount);
                                AppendLog(
                                    $"[NG] {Path.GetFileName(filePath)} -> {result?.ErrorMessage ?? "unknown"}"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failedCount);
                            AppendLog($"[EX] {Path.GetFileName(filePath)} -> {ex.Message}");
                        }
                        finally
                        {
                            int completed = Interlocked.Increment(ref completedCount);
                            SetStatusText($"処理中 {completed}/{targetFiles.Count}");
                        }
                    }
                )
                    .ConfigureAwait(true);

                StatusTextBlock.Text =
                    $"完了: 成功 {successCount} 件 / 失敗 {failedCount} 件 / 合計 {targetFiles.Count} 件";
                AppendLog(StatusTextBlock.Text);
            }
            finally
            {
                isRunning = false;
                RefreshView();
            }
        }

        // 単発ツールはQueueの自動再試行が無いので、初回失敗時だけ回復レーンでもう一度流す。
        private async Task<ThumbnailCreateResult> CreateThumbWithRecoveryAsync(
            ThumbnailCreationRuntime runtime,
            QueueObj queueObj,
            string dbName,
            string thumbFolderPath,
            CancellationToken cancellationToken = default
        )
        {
            ThumbnailCreateResult firstResult = await runtime
                .CreateThumbAsync(
                    queueObj,
                    dbName,
                    thumbFolderPath,
                    isResizeThumb: true,
                    // このツールは既存サムネ差し替えではなく新規生成なので通常経路で流す。
                    isManual: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (firstResult?.IsSuccess == true)
            {
                return firstResult;
            }

            AppendLog(
                $"[retry] {Path.GetFileName(queueObj.MovieFullPath)} -> 初回失敗のため回復レーンで再試行します: {firstResult?.ErrorMessage ?? "unknown"}"
            );

            queueObj.AttemptCount = Math.Max(queueObj.AttemptCount, 1);
            queueObj.IsRescueRequest = true;

            ThumbnailCreateResult retryResult = await runtime
                .CreateThumbAsync(
                    queueObj,
                    dbName,
                    thumbFolderPath,
                    isResizeThumb: true,
                    isManual: false,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return retryResult ?? firstResult;
        }

        // ドロップされたパスを正規化して受け取る。
        private static IEnumerable<string> ExtractDroppedPaths(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return [];
            }

            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
            return dropped.Where(x => !string.IsNullOrWhiteSpace(x));
        }

        // 入力ファイルとフォルダをまとめてたどり、処理可能な拡張子だけを残す。
        private IEnumerable<string> CollectTargetFiles()
        {
            HashSet<string> results = new(StringComparer.OrdinalIgnoreCase);

            foreach (string root in inputRoots)
            {
                if (File.Exists(root))
                {
                    TryAddTargetFile(root, results);
                    continue;
                }

                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string filePath in EnumerateFilesSafe(root))
                {
                    TryAddTargetFile(filePath, results);
                }
            }

            return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateFilesSafe(string rootDirectoryPath)
        {
            Stack<string> pending = new();
            pending.Push(rootDirectoryPath);

            while (pending.Count > 0)
            {
                string currentDirectoryPath = pending.Pop();

                string[] files = [];
                try
                {
                    files = Directory.GetFiles(currentDirectoryPath);
                }
                catch
                {
                    // 読み取れないフォルダは全体停止せず、その場だけ飛ばす。
                }

                foreach (string filePath in files)
                {
                    yield return filePath;
                }

                string[] directories = [];
                try
                {
                    directories = Directory.GetDirectories(currentDirectoryPath);
                }
                catch
                {
                    // 子フォルダ列挙失敗も、そのフォルダだけ諦めて先へ進む。
                }

                foreach (string directoryPath in directories)
                {
                    pending.Push(directoryPath);
                }
            }
        }

        private static void TryAddTargetFile(string filePath, ISet<string> results)
        {
            string extension = Path.GetExtension(filePath ?? "");
            if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            {
                return;
            }

            results.Add(Path.GetFullPath(filePath));
        }

        private static long TryGetFileSize(string filePath)
        {
            try
            {
                return new FileInfo(filePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        // Worker起動時のmanifest内容をそのまま既存UIへ流し込み、入力欄だけ先に埋める。
        private void ApplyStartupContext(DropToolStartupContext startupContext)
        {
            if (startupContext == null)
            {
                return;
            }

            AddInputRoots(startupContext.InitialInputPaths);
            if (!string.IsNullOrWhiteSpace(startupContext.StartupMessage))
            {
                AppendLog(startupContext.StartupMessage);
            }
        }

        private void AddInputRoots(IEnumerable<string> paths)
        {
            List<string> normalizedPaths = DropToolLaunchSupport.NormalizePaths(paths);
            foreach (string path in normalizedPaths)
            {
                inputRoots.Add(path);
            }

            TryApplyDefaultOutputFolder(normalizedPaths);
        }

        private void RefreshView()
        {
            InputSummaryTextBlock.Text = inputRoots.Count > 0
                ? string.Join(Environment.NewLine, inputRoots.OrderBy(x => x))
                : "未選択";
            OutputSummaryTextBlock.Text = string.IsNullOrWhiteSpace(outputFolderPath)
                ? "未選択"
                : outputFolderPath;

            if (!isRunning && StatusTextBlock.Text.StartsWith("処理中", StringComparison.Ordinal))
            {
                StatusTextBlock.Text = "待機中";
            }

            StartButton.IsEnabled =
                !isRunning && inputRoots.Count > 0 && !string.IsNullOrWhiteSpace(outputFolderPath);
            StartButton.Content = isRunning ? "処理中..." : "開始";
        }

        private static IReadOnlyList<ParallelismOption> CreateParallelismOptions()
        {
            return Enumerable.Range(1, 24).Select(value => new ParallelismOption(value)).ToArray();
        }

        private static ParallelismOption ResolveDefaultParallelismOption()
        {
            int recommendedValue = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
            return ParallelismOptions.FirstOrDefault(option => option.Value == recommendedValue)
                ?? ParallelismOptions[0];
        }

        private void TryApplyDefaultOutputFolder(IEnumerable<string> normalizedPaths)
        {
            if (!string.IsNullOrWhiteSpace(outputFolderPath))
            {
                return;
            }

            string inputDirectoryPath = normalizedPaths
                ?.FirstOrDefault(Directory.Exists);
            if (string.IsNullOrWhiteSpace(inputDirectoryPath))
            {
                return;
            }

            outputFolderPath = ResolveDefaultOutputFolderPath(inputDirectoryPath);
            AppendLog($"出力フォルダを既定設定しました: {outputFolderPath}");
        }

        private static string ResolveDefaultOutputFolderPath(string inputDirectoryPath)
        {
            string thumbFolderPath = Path.Combine(inputDirectoryPath, "Thumb");
            if (Directory.Exists(thumbFolderPath))
            {
                return thumbFolderPath;
            }

            string legacyThumbFolderPath = Path.Combine(inputDirectoryPath, "Thum");
            if (Directory.Exists(legacyThumbFolderPath))
            {
                return legacyThumbFolderPath;
            }

            return thumbFolderPath;
        }

        private void SetStatusText(string statusText)
        {
            if (string.IsNullOrWhiteSpace(statusText))
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatusText(statusText));
                return;
            }

            StatusTextBlock.Text = statusText;
        }

        // ログはUIスレッドへ寄せて追記し、進行の見える化を優先する。
        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
                return;
            }

            StringBuilder builder = new(LogTextBox.Text);
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append('[');
            builder.Append(DateTime.Now.ToString("HH:mm:ss"));
            builder.Append("] ");
            builder.Append(message);

            LogTextBox.Text = builder.ToString();
            LogTextBox.ScrollToEnd();
        }
    }
}

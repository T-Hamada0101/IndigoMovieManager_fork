using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly object FileNotFoundLogLock = new();
        private const string MovieInfoProbeArgument = "--movieinfo-probe";

        public App()
        {
#if DEBUG
            // デバッグ中だけ、FileNotFound の詳細（対象ファイル名/発生箇所）を出す。
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
#endif
        }

#if DEBUG
        private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is not FileNotFoundException ex)
            {
                return;
            }
            if (IsIgnorableFileNotFound(ex))
            {
                return;
            }

            string stack = ex.StackTrace ?? "";
            // ローカライズ探索などのノイズを減らすため、手掛かりがあるものだけ拾う。
            bool hasFileName = !string.IsNullOrWhiteSpace(ex.FileName);
            bool isAppStack = stack.Contains("IndigoMovieManager", StringComparison.Ordinal);
            if (!hasFileName && !isAppStack)
            {
                return;
            }

            Debug.WriteLine(
                $"[FileNotFound] File='{ex.FileName ?? "(unknown)"}' Message='{ex.Message}'"
            );
            Debug.WriteLine(stack);
            WriteFileNotFoundLog(ex.FileName, ex.Message, stack);
        }

        private static bool IsIgnorableFileNotFound(FileNotFoundException ex)
        {
            string fileName = ex.FileName ?? "";
            string message = ex.Message ?? "";

            // XmlSerializer は事前生成DLLを探索してから動的生成へフォールバックする。
            // その探索失敗は通常動作なので、診断ログ対象から外す。
            if (fileName.Contains(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (message.Contains(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static void WriteFileNotFoundLog(string fileName, string message, string stack)
        {
            try
            {
                // VS出力が拾いづらい環境でも見られるよう、ローカルへ追記する。
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
                Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, "firstchance.log");
                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] File='{fileName ?? "(unknown)"}' Message='{message}'{Environment.NewLine}{stack}{Environment.NewLine}";

                lock (FileNotFoundLogLock)
                {
                    File.AppendAllText(logPath, line);
                }
            }
            catch
            {
                // ログ出力失敗で本体動作を止めない。
            }
        }
#endif

        protected override void OnStartup(StartupEventArgs e)
        {
#if DEBUG
            if (TryRunMovieInfoProbeMode(e))
            {
                return;
            }
#endif
            base.OnStartup(e);
        }

#if DEBUG
        private bool TryRunMovieInfoProbeMode(StartupEventArgs e)
        {
            string targetPath = ResolveMovieInfoProbeTargetPath(e.Args);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            // 専用モードではメイン画面を起動せず、比較結果だけ採って終了する。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!File.Exists(targetPath))
            {
                string missingMessage = $"probe target not found: '{targetPath}'";
                DebugRuntimeLog.Write("movieinfo-probe", missingMessage);
                MessageBox.Show(
                    missingMessage,
                    "MovieInfo Probe",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                Shutdown(2);
                return true;
            }

            MovieInfoMetadataProbeSet probe = MovieInfo.ProbeMetadataSources(targetPath);
            string message = BuildMovieInfoProbeMessage(probe);
            DebugRuntimeLog.Write("movieinfo-probe", "startup probe completed.");
            MessageBox.Show(
                message,
                "MovieInfo Probe",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            Shutdown(0);
            return true;
        }

        private static string ResolveMovieInfoProbeTargetPath(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return "";
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], MovieInfoProbeArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return "";
                }

                return args[i + 1] ?? "";
            }

            return "";
        }

        private static string BuildMovieInfoProbeMessage(MovieInfoMetadataProbeSet probe)
        {
            StringBuilder sb = new();
            sb.AppendLine("MovieInfo 3系統比較");
            sb.AppendLine(probe.MoviePath);
            sb.AppendLine();

            foreach (string line in probe.ToDebugLines())
            {
                sb.AppendLine(line);
            }

            sb.AppendLine();
            sb.AppendLine("debug-runtime.log にも出力済み");
            return sb.ToString().TrimEnd();
        }
#endif

        // StartupUri=MainWindow.xaml により、アプリ起動時は MainWindow が最初に開く。
        // グローバル初期化が必要になった場合はここへ追記する。
    }
}

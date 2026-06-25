using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Vs2013DarkTheme = AvalonDock.Themes.Vs2013DarkTheme;
using Vs2013LightTheme = AvalonDock.Themes.Vs2013LightTheme;

namespace IndigoMovieManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly object FileNotFoundLogLock = new();
        private static bool? LastAppliedSystemAutoDarkTheme;
        private const int DwmaUseImmersiveDarkMode = 20;
        private const int DwmaUseImmersiveDarkModeLegacy = 19;
        private const int DwmaCaptionColor = 35;
        private const int DwmaTextColor = 36;
        private const int DwmaColorDefault = unchecked((int)0xFFFFFFFF);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const string DispatcherSetWin32TimerStackMarker =
            "System.Windows.Threading.Dispatcher.SetWin32Timer";
        private const string DispatcherTimerStartStackMarker =
            "System.Windows.Threading.DispatcherTimer.Start";
        private const string MediaContextCommitStackMarker =
            "System.Windows.Media.MediaContext.CommitChannelAfterNextVSync";
        internal const string ThemeModeIndigo = "Indigo";
        internal const string ThemeModeSimpleLight = "SimpleLight";
        internal const string ThemeModeSimpleDark = "SimpleDark";
        internal const string ThemeModeSystemAuto = "SystemAuto";
        internal const string DiagnosticNoPersistEnvironmentVariable = "INDIGO_DIAGNOSTIC_NO_PERSIST";
        private const string LegacyThemeModeOriginal = "Original";
        private const string LegacyThemeModeOsSync = "OsSync";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags
        );

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
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);

                string logPath = IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
                    Path.Combine(logDir, "firstchance.log")
                );
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
            // 補助 overlay window が一瞬残っても、MainWindow close を終了条件として固定する。
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // WPF 内部の SetWin32Timer 失敗だけは、ログと機能縮退へ逃がして本体クラッシュを防ぐ。
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Queue / FailureDb / 補助ログの保存先は host 側で固定し、Queue project へ app 固有規約を持ち込まない。
            ThumbnailQueueHostPathPolicy.Configure(
                queueDbDirectoryPath: AppLocalDataPaths.QueueDbPath,
                failureDbDirectoryPath: AppLocalDataPaths.FailureDbPath,
                logDirectoryPath: AppLocalDataPaths.LogsPath
            );

            // rescue trace は engine 側へ app 固有パスを持ち込まず、起動時に設定する。
            IndigoMovieManager.Thumbnail.ThumbnailRescueTraceLog.ConfigureLogDirectory(
                AppLocalDataPaths.LogsPath
            );

            // OSテーマ変更時に、OS連動モードだけ即時反映する。
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // 起動時に保存済みテーマモードを適用する。
            ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);

            base.OnStartup(e);

            // App.xaml はテーマ辞書を起動時に動的適用するため、MainWindow はここで明示的に開く。
            // XAML 生成前に ApplyTheme を済ませることで、初期 StaticResource 解決を安定させる。
            ShowMainWindow();
        }

        internal static bool IsDiagnosticNoPersistEnabled()
        {
            return IsDiagnosticNoPersistEnabledForTesting(
                Environment.GetEnvironmentVariable(DiagnosticNoPersistEnvironmentVariable)
            );
        }

        internal static bool IsDiagnosticNoPersistEnabledForTesting(string value)
        {
            string normalizedValue = value?.Trim() ?? "";
            return string.Equals(normalizedValue, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e
        )
        {
            if (!ShouldSuppressKnownDispatcherTimerWin32Exception(e?.Exception))
            {
                return;
            }

            LogKnownDispatcherTimerWin32Exception(e.Exception);
            if (Current?.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HandleDispatcherTimerInfrastructureFault(
                    "dispatcher-unhandled",
                    e.Exception as System.ComponentModel.Win32Exception
                );
            }

            e.Handled = true;
        }

        private static void ShowMainWindow()
        {
            if (Current?.MainWindow is MainWindow existingMainWindow)
            {
                existingMainWindow.Show();
                existingMainWindow.Activate();
                return;
            }

            // MainWindow を作ってからアプリの終了条件に紐付ける。
            var mainWindow = new MainWindow();
            Current.MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();
        }

        // WPF 内部の render timer 起点だけを狙い撃ちし、他の Win32Exception は握り潰さない。
        internal static bool ShouldSuppressKnownDispatcherTimerWin32Exception(
            Exception exception,
            string stackTraceOverride = null
        )
        {
            if (exception is not System.ComponentModel.Win32Exception)
            {
                return false;
            }

            string stackTrace = stackTraceOverride ?? exception.StackTrace ?? "";
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return string.Equals(
                    exception.TargetSite?.Name,
                    "SetWin32Timer",
                    StringComparison.Ordinal
                );
            }

            bool isSetWin32TimerPath = stackTrace.Contains(
                DispatcherSetWin32TimerStackMarker,
                StringComparison.Ordinal
            );
            bool isDispatcherTimerStartPath = stackTrace.Contains(
                DispatcherTimerStartStackMarker,
                StringComparison.Ordinal
            );
            bool isMediaContextCommitPath = stackTrace.Contains(
                MediaContextCommitStackMarker,
                StringComparison.Ordinal
            );

            // SetWin32Timer という名前だけでは広すぎるため、DispatcherTimer 経路まで見えている時だけ握る。
            return (isSetWin32TimerPath && isDispatcherTimerStartPath)
                || (isDispatcherTimerStartPath && isMediaContextCommitPath);
        }

        private static void LogKnownDispatcherTimerWin32Exception(Exception exception)
        {
            int nativeErrorCode = exception is System.ComponentModel.Win32Exception win32
                ? win32.NativeErrorCode
                : 0;
            int userObjects = TryGetGuiResourceCount(0);
            int gdiObjects = TryGetGuiResourceCount(1);
            string stackHead = ExtractStackHead(exception?.StackTrace);
            DebugRuntimeLog.Write(
                "ui-timer",
                $"suppressed WPF SetWin32Timer failure: native_error={nativeErrorCode} user_objects={userObjects} gdi_objects={gdiObjects} err='{exception?.GetType().Name}: {exception?.Message}' stack='{stackHead}'"
            );
        }

        private static string ExtractStackHead(string stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return "";
            }

            string[] lines = stackTrace.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
            );
            return string.Join(" | ", lines.Take(3));
        }

        private static int TryGetGuiResourceCount(int resourceKind)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                return GetGuiResources(process.Handle, resourceKind);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// テーマ用 ResourceDictionary を差し替える。
        /// 旧モードを正規化し、SystemAuto は OS 状態から実際の Profile を決める。
        /// </summary>
        public static void ApplyTheme(string themeMode)
        {
            var app = Current;
            if (app == null) return;
            if (!app.Dispatcher.CheckAccess())
            {
                if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                app.Dispatcher.Invoke(() => ApplyTheme(themeMode));
                return;
            }

            var resolvedTheme = ResolveThemeMode(themeMode);
            if (
                !string.Equals(
                    IndigoMovieManager.Properties.Settings.Default.ThemeMode,
                    resolvedTheme.Mode,
                    StringComparison.Ordinal
                )
            )
            {
                SaveThemeModeSettingBestEffort(resolvedTheme.Mode, "apply-theme-normalize");
            }

            var dict = CreateThemeResourceDictionary(resolvedTheme.ProfileMode);

            // 新しい辞書を先に積み、切替途中に DynamicResource の参照先が空になる瞬間を避ける。
            app.Resources.MergedDictionaries.Add(dict);

            // 既存のテーマ辞書があれば、新しい Profile 以外を後から除去する。
            var existingThemeDictionaries = app.Resources.MergedDictionaries
                .Where(d => !ReferenceEquals(d, dict) && IsThemeDictionarySource(d.Source))
                .ToList();
            foreach (var existing in existingThemeDictionaries)
            {
                app.Resources.MergedDictionaries.Remove(existing);
            }

            bool useDarkTheme = string.Equals(
                resolvedTheme.ProfileMode,
                ThemeModeSimpleDark,
                StringComparison.Ordinal
            );
            LastAppliedSystemAutoDarkTheme = resolvedTheme.IsSystemAuto ? useDarkTheme : null;

            // 上下タブは AvalonDock のテーマだけ明示的に合わせる。
            app.Resources["AvalonDockTheme"] =
                useDarkTheme ? new Vs2013DarkTheme() : new Vs2013LightTheme();

            // 設定画面の文字は、実際にダーク Profile を読む時だけ白へ寄せる。
            bool useLightSettingsForeground = useDarkTheme;
            app.Resources["SettingsForegroundBrush"] = new SolidColorBrush(
                useLightSettingsForeground ? Colors.White : Colors.Black
            );

            // 左ドロワーは、Indigo では旧UI相当の色、軽量 Profile では本文色へ追従させる。
            app.Resources["LeftDrawerForegroundBrush"] =
                resolvedTheme.IsIndigo
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF303F9F"))
                    : app.TryFindResource("MaterialDesignBody") as Brush
                        ?? new SolidColorBrush(Colors.Black);

            // メインヘッダーは背景が暗くなるため、ラベル文字と入力文字を分けて追従させる。
            app.Resources["MainHeaderForegroundBrush"] = new SolidColorBrush(
                resolvedTheme.IsIndigo || useDarkTheme ? Colors.White : Colors.Black
            );
            app.Resources["MainHeaderInputForegroundBrush"] = new SolidColorBrush(
                resolvedTheme.IsIndigo ? Colors.Black : (useDarkTheme ? Colors.White : Colors.Black)
            );
            Brush upperTabPanelForegroundBrush =
                resolvedTheme.IsIndigo
                    ? app.TryFindResource(SystemColors.ControlTextBrushKey) as Brush
                        ?? SystemColors.ControlTextBrush
                    : app.TryFindResource("MaterialDesignBody") as Brush
                        ?? new SolidColorBrush(useDarkTheme ? Colors.White : Colors.Black);
            app.Resources["UpperTabPanelForegroundBrush"] = upperTabPanelForegroundBrush;
            app.Resources["GridTabTitleForegroundBrush"] =
                app.TryFindResource("MaterialDesignBodyLight") as Brush
                    ?? new SolidColorBrush(Colors.DimGray);

            // 開いている全ウィンドウのタイトルバーも、テーマ変更に追従させる。
            foreach (Window window in app.Windows)
            {
                ApplyWindowTitleBarTheme(window, resolvedTheme.IsIndigo, useDarkTheme);
            }
        }

        private static (
            string Mode,
            string ProfileMode,
            bool IsSystemAuto,
            bool IsIndigo
        ) ResolveThemeMode(string themeMode)
        {
            string normalizedMode = NormalizeThemeMode(themeMode);
            if (string.Equals(normalizedMode, ThemeModeSystemAuto, StringComparison.Ordinal))
            {
                string profileMode = IsWindowsAppsDarkThemeEnabled()
                    ? ThemeModeSimpleDark
                    : ThemeModeSimpleLight;
                return (normalizedMode, profileMode, true, false);
            }

            return (
                normalizedMode,
                normalizedMode,
                false,
                string.Equals(normalizedMode, ThemeModeIndigo, StringComparison.Ordinal)
            );
        }

        internal static string NormalizeThemeMode(string themeMode)
        {
            string mode = (themeMode ?? "").Trim();
            if (
                string.Equals(mode, LegacyThemeModeOriginal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, ThemeModeIndigo, StringComparison.OrdinalIgnoreCase)
            )
            {
                return ThemeModeIndigo;
            }

            if (
                string.Equals(mode, LegacyThemeModeOsSync, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, ThemeModeSystemAuto, StringComparison.OrdinalIgnoreCase)
            )
            {
                return ThemeModeSystemAuto;
            }

            if (string.Equals(mode, ThemeModeSimpleLight, StringComparison.OrdinalIgnoreCase))
            {
                return ThemeModeSimpleLight;
            }

            if (string.Equals(mode, ThemeModeSimpleDark, StringComparison.OrdinalIgnoreCase))
            {
                return ThemeModeSimpleDark;
            }

            return ThemeModeIndigo;
        }

        internal static void SaveThemeModeSettingBestEffort(string normalizedThemeMode, string reason)
        {
            // テーマ移行の保存失敗で起動を止めない。画面上は新しい値を使い、永続化だけ後で復旧できるようログへ閉じる。
            if (IsDiagnosticNoPersistEnabled())
            {
                DebugRuntimeLog.Write(
                    "theme",
                    $"settings save skipped: reason={reason} diagnostic_no_persist=1"
                );
                return;
            }

            IndigoMovieManager.Properties.Settings.Default.ThemeMode = NormalizeThemeMode(
                normalizedThemeMode
            );
            try
            {
                IndigoMovieManager.Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "theme",
                    $"settings save failed: reason={reason} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private static ResourceDictionary CreateThemeResourceDictionary(string profileMode)
        {
            string profileUri = BuildApplicationResourceUri($"Themes/Profiles/{profileMode}.xaml");
            try
            {
                // XAML は App*Style 前提なので、旧色辞書へは戻さず Profile 体系で完結させる。
                return new ResourceDictionary { Source = new Uri(profileUri) };
            }
            catch (Exception) when (!string.Equals(profileMode, ThemeModeIndigo, StringComparison.Ordinal))
            {
                // 不意の Profile 読み込み失敗時も、必ず新構成の Indigo へ戻して起動を守る。
                return new ResourceDictionary
                {
                    Source = new Uri(BuildApplicationResourceUri("Themes/Profiles/Indigo.xaml")),
                };
            }
        }

        private static bool IsThemeDictionarySource(Uri source)
        {
            if (source == null)
            {
                return false;
            }

            string sourceText = source.ToString();
            return sourceText.Contains("/Themes/Profiles/", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildApplicationResourceUri(string relativePath)
        {
            string assemblyName = typeof(App).Assembly.GetName().Name ?? "IndigoMovieManager";
            string normalizedPath = (relativePath ?? "").TrimStart('/');
            return $"pack://application:,,,/{assemblyName};component/{normalizedPath}";
        }

        // 各ウィンドウの標準タイトルバーへ、現在のテーマ設定を反映する。
        public static void ApplyWindowTitleBarTheme(Window window)
        {
            if (window == null)
            {
                return;
            }

            string themeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";
            var resolvedTheme = ResolveThemeMode(themeMode);
            bool useDarkTheme = string.Equals(
                resolvedTheme.ProfileMode,
                ThemeModeSimpleDark,
                StringComparison.Ordinal
            );
            ApplyWindowTitleBarTheme(window, resolvedTheme.IsIndigo, useDarkTheme);
        }

        // 軽量ダークは標準ダークバー、Indigo は固定 indigo、それ以外は OS 既定へ戻す。
        private static void ApplyWindowTitleBarTheme(
            Window window,
            bool isIndigoTheme,
            bool useDarkTheme
        )
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int immersiveDark = !isIndigoTheme && useDarkTheme ? 1 : 0;
            if (!TrySetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, immersiveDark))
            {
                _ = TrySetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeLegacy, immersiveDark);
            }

            int captionColor = isIndigoTheme
                ? ToColorRef((Color)ColorConverter.ConvertFromString("#FF303F9F"))
                : DwmaColorDefault;
            int textColor = isIndigoTheme ? ToColorRef(Colors.White) : DwmaColorDefault;
            _ = TrySetWindowAttribute(hwnd, DwmaCaptionColor, captionColor);
            _ = TrySetWindowAttribute(hwnd, DwmaTextColor, textColor);

            // 非クライアント領域の再描画を促して、色変更を即時反映させる。
            _ = SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged
            );
        }

        private static void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e
        )
        {
            string themeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";
            if (
                !string.Equals(
                    NormalizeThemeMode(themeMode),
                    ThemeModeSystemAuto,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            if (
                e.Category != UserPreferenceCategory.General
                && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.VisualStyle
            )
            {
                return;
            }

            bool nextIsDark = IsWindowsAppsDarkThemeEnabled();
            if (
                LastAppliedSystemAutoDarkTheme.HasValue
                && LastAppliedSystemAutoDarkTheme.Value == nextIsDark
            )
            {
                return;
            }

            Current?.Dispatcher.BeginInvoke(() => ApplyTheme(ThemeModeSystemAuto));
        }

        private static bool IsWindowsAppsDarkThemeEnabled()
        {
            try
            {
                object value =
                    Registry.CurrentUser
                        .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                        ?.GetValue("AppsUseLightTheme");

                return value is int intValue && intValue == 0;
            }
            catch
            {
                // OSテーマ判定に失敗した時は、無理に白文字へ倒さない。
                return false;
            }
        }

        // Win32 COLORREF(0x00BBGGRR) に変換して、DWMへそのまま渡す。
        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        // 未対応OSでは E_INVALIDARG になり得るため、失敗は握り潰してフォールバックする。
        private static bool TrySetWindowAttribute(IntPtr hwnd, int attribute, int value)
        {
            int localValue = value;
            return DwmSetWindowAttribute(hwnd, attribute, ref localValue, sizeof(int)) >= 0;
        }

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
    }
}

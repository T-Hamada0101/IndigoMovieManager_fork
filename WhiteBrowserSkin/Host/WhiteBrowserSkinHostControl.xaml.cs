using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using IndigoMovieManager.Skin.Runtime;
using Microsoft.Web.WebView2.Core;

namespace IndigoMovieManager.Skin.Host
{
    /// <summary>
    /// WhiteBrowser 互換スキン専用の WebView2 ホスト。
    /// MainWindow 側はこの control を出し入れするだけで良い形を目指す。
    /// </summary>
    public partial class WhiteBrowserSkinHostControl : UserControl, IDisposable
    {
        private readonly WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        private readonly WhiteBrowserSkinRenderCoordinator renderCoordinator = new();
        private NavigationReuseKey? lastSuccessfulNavigationKey;
        private bool disposed;

        public WhiteBrowserSkinHostControl()
        {
            InitializeComponent();
            runtimeBridge.WebMessageReceived += RuntimeBridge_WebMessageReceived;
        }

        public WhiteBrowserSkinRuntimeBridge RuntimeBridge => runtimeBridge;

        public event EventHandler<WhiteBrowserSkinWebMessageReceivedEventArgs> WebMessageReceived;

        public async Task NavigateAsync(
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string skinHtmlPath,
            string thumbRootPath
        )
        {
            WhiteBrowserSkinHostOperationResult result = await TryNavigateAsync(
                requestedSkinName,
                userDataFolder,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }
        }

        public async Task<WhiteBrowserSkinHostOperationResult> TryNavigateAsync(
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string skinHtmlPath,
            string thumbRootPath,
            string dbKey = "",
            bool allowSameDocumentNavigateSkip = false
        )
        {
            Stopwatch hostNavigateStopwatch = Stopwatch.StartNew();
            double initialDocumentBuildMilliseconds = 0;
            double navigateToStringMilliseconds = 0;
            if (disposed)
            {
                return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                    requestedSkinName ?? "",
                    "External skin host is already disposed."
                )
                    .WithTimings(
                        hostNavigateElapsedMilliseconds: hostNavigateStopwatch
                            .Elapsed
                            .TotalMilliseconds
                    );
            }

            WhiteBrowserSkinHostOperationResult attachResult = await runtimeBridge.TryEnsureAttachedAsync(
                SkinWebView,
                requestedSkinName,
                userDataFolder,
                skinRootPath,
                thumbRootPath
            );
            if (!attachResult.Succeeded)
            {
                lastSuccessfulNavigationKey = null;
                return attachResult.WithTimings(
                    hostNavigateElapsedMilliseconds: hostNavigateStopwatch.Elapsed.TotalMilliseconds
                );
            }

            Stopwatch initialDocumentStopwatch = Stopwatch.StartNew();
            WhiteBrowserSkinRenderDocument document = await renderCoordinator.BuildInitialDocumentAsync(
                skinRootPath,
                skinHtmlPath
            );
            initialDocumentBuildMilliseconds = initialDocumentStopwatch.Elapsed.TotalMilliseconds;
            await ResumeOnHostDispatcherAsync();
            if (disposed)
            {
                lastSuccessfulNavigationKey = null;
                return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                    requestedSkinName ?? "",
                    "External skin host is already disposed."
                )
                    .WithTimings(
                        hostNavigateElapsedMilliseconds: hostNavigateStopwatch
                            .Elapsed
                            .TotalMilliseconds,
                        initialDocumentBuildElapsedMilliseconds: initialDocumentBuildMilliseconds
                    );
            }

            NavigationReuseKey navigationReuseKey = BuildNavigationReuseKey(
                requestedSkinName,
                userDataFolder,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath,
                dbKey,
                document
            );
            if (
                allowSameDocumentNavigateSkip
                && lastSuccessfulNavigationKey?.Equals(navigationReuseKey) == true
                && SkinWebView.CoreWebView2 != null
            )
            {
                return WhiteBrowserSkinHostOperationResult
                    .CreateNavigateSkipped(requestedSkinName, "same-document")
                    .WithTimings(
                        hostNavigateElapsedMilliseconds: hostNavigateStopwatch
                            .Elapsed
                            .TotalMilliseconds,
                        initialDocumentBuildElapsedMilliseconds: initialDocumentBuildMilliseconds
                    );
            }

            // ここから先は旧ページへ leave を送るため、失敗時に leave 済みページを再利用しない。
            lastSuccessfulNavigationKey = null;
            // 新しい document へ進む時は、旧 document だけが許可した外部サムネを持ち越さない。
            runtimeBridge.ClearRegisteredExternalThumbnailPaths();
            // 実際に新しい document を流す時だけ、旧ページの終了 callback を先に返す。
            await runtimeBridge.HandleSkinLeaveAsync();
            Stopwatch navigateToStringStopwatch = Stopwatch.StartNew();
            await NavigateToStringAsync(document.Html);
            navigateToStringMilliseconds = navigateToStringStopwatch.Elapsed.TotalMilliseconds;
            lastSuccessfulNavigationKey = navigationReuseKey;
            return WhiteBrowserSkinHostOperationResult.CreateSuccess(requestedSkinName)
                .WithTimings(
                    hostNavigateElapsedMilliseconds: hostNavigateStopwatch.Elapsed.TotalMilliseconds,
                    initialDocumentBuildElapsedMilliseconds: initialDocumentBuildMilliseconds,
                    navigateToStringElapsedMilliseconds: navigateToStringMilliseconds
                );
        }

        public void Clear()
        {
            _ = ClearIgnoringErrorsAsync();
        }

        public async Task ClearAsync()
        {
            lastSuccessfulNavigationKey = null;
            if (disposed)
            {
                return;
            }

            runtimeBridge.ClearRegisteredExternalThumbnailPaths();
            // 終了経路では未初期化の host も来るので、その時は空 HTML への遷移を無理に撃たない。
            if (SkinWebView.CoreWebView2 == null)
            {
                return;
            }

            await runtimeBridge.HandleSkinLeaveAsync();
            await NavigateToStringAsync("<html><body></body></html>");
        }

        public Task HandleSkinLeaveAsync()
        {
            return runtimeBridge.HandleSkinLeaveAsync();
        }

        public void RegisterExternalThumbnailPath(string thumbPath)
        {
            runtimeBridge.RegisterExternalThumbnailPath(thumbPath);
        }

        public Task ResolveRequestAsync(string messageId, object payload)
        {
            return runtimeBridge.ResolveRequestAsync(messageId, payload);
        }

        public Task RejectRequestAsync(string messageId, string errorMessage)
        {
            return runtimeBridge.RejectRequestAsync(messageId, errorMessage);
        }

        public Task DispatchCallbackAsync(string callbackName, object payload)
        {
            return runtimeBridge.DispatchCallbackAsync(callbackName, payload);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lastSuccessfulNavigationKey = null;
            runtimeBridge.WebMessageReceived -= RuntimeBridge_WebMessageReceived;
            runtimeBridge.Dispose();

            try
            {
                // WPF の visual tree から外した後も hwnd が残ることがあるため、
                // 終了時は WebView2 実体まで明示破棄して close race を減らす。
                SkinWebView?.Dispose();
            }
            catch
            {
                // 終了経路では host 破棄を優先する。
            }
        }

        private void RuntimeBridge_WebMessageReceived(
            object sender,
            WhiteBrowserSkinWebMessageReceivedEventArgs e
        )
        {
            WebMessageReceived?.Invoke(this, e);
        }

        private async Task ClearIgnoringErrorsAsync()
        {
            try
            {
                await ClearAsync();
            }
            catch
            {
                // 終了経路では host 破棄を優先し、blank 遷移失敗は握りつぶす。
            }
        }

        private async Task NavigateToStringAsync(string html)
        {
            if (disposed)
            {
                throw new InvalidOperationException("External skin host is already disposed.");
            }

            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (_, args) =>
            {
                SkinWebView.NavigationCompleted -= handler;
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                    return;
                }

                navigationCompleted.TrySetException(
                    new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                );
            };

            SkinWebView.NavigationCompleted += handler;
            try
            {
                SkinWebView.NavigateToString(html ?? "<html><body></body></html>");
            }
            catch
            {
                SkinWebView.NavigationCompleted -= handler;
                throw;
            }

            Task completedTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(completedTask, navigationCompleted.Task))
            {
                SkinWebView.NavigationCompleted -= handler;
                throw new TimeoutException(
                    "WhiteBrowser skin host の NavigateToString が 10 秒以内に完了しませんでした。"
                );
            }

            await navigationCompleted.Task;
        }

        private Task ResumeOnHostDispatcherAsync()
        {
            if (Dispatcher.CheckAccess())
            {
                return Task.CompletedTask;
            }

            // 背景で document を作った後も、WebView2 実体操作は必ず host の UI Dispatcher へ戻す。
            return Dispatcher.InvokeAsync(() => { }).Task;
        }

        private static NavigationReuseKey BuildNavigationReuseKey(
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string skinHtmlPath,
            string thumbRootPath,
            string dbKey,
            WhiteBrowserSkinRenderDocument document
        )
        {
            // 同じ HTML と host 入力の時だけ、ページ破棄を伴う再 navigate を省く。
            return new NavigationReuseKey(
                requestedSkinName ?? "",
                NormalizePathForReuseKey(userDataFolder),
                NormalizePathForReuseKey(skinRootPath),
                NormalizePathForReuseKey(skinHtmlPath),
                NormalizePathForReuseKey(thumbRootPath),
                NormalizePathForReuseKey(dbKey),
                document?.Html ?? "",
                document?.SkinBaseUri ?? "",
                document?.ThumbnailBaseUri ?? ""
            );
        }

        private static string NormalizePathForReuseKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path ?? "";
            }
        }

        private readonly record struct NavigationReuseKey(
            string RequestedSkinName,
            string UserDataFolder,
            string SkinRootPath,
            string SkinHtmlPath,
            string ThumbRootPath,
            string DbKey,
            string Html,
            string SkinBaseUri,
            string ThumbnailBaseUri
        );
    }
}

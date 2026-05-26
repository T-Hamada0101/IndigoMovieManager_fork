namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// Host 初期化やナビゲーション結果を、例外ではなく呼び出し側が分岐しやすい形で返す。
    /// raw skin 名も結果へ残し、Runtime 未導入時でも上位で潰さず扱えるようにする。
    /// </summary>
    public sealed class WhiteBrowserSkinHostOperationResult
    {
        private WhiteBrowserSkinHostOperationResult(
            bool succeeded,
            bool runtimeAvailable,
            string requestedSkinName,
            string errorMessage,
            string errorType,
            double prepareElapsedMilliseconds = 0,
            double filePrepareElapsedMilliseconds = 0,
            double hostNavigateElapsedMilliseconds = 0,
            double initialDocumentBuildElapsedMilliseconds = 0,
            double navigateToStringElapsedMilliseconds = 0
        )
        {
            Succeeded = succeeded;
            RuntimeAvailable = runtimeAvailable;
            RequestedSkinName = requestedSkinName ?? "";
            ErrorMessage = errorMessage ?? "";
            ErrorType = errorType ?? "";
            PrepareElapsedMilliseconds = SanitizeElapsedMilliseconds(prepareElapsedMilliseconds);
            FilePrepareElapsedMilliseconds = SanitizeElapsedMilliseconds(filePrepareElapsedMilliseconds);
            HostNavigateElapsedMilliseconds = SanitizeElapsedMilliseconds(hostNavigateElapsedMilliseconds);
            InitialDocumentBuildElapsedMilliseconds = SanitizeElapsedMilliseconds(
                initialDocumentBuildElapsedMilliseconds
            );
            NavigateToStringElapsedMilliseconds = SanitizeElapsedMilliseconds(
                navigateToStringElapsedMilliseconds
            );
        }

        public bool Succeeded { get; }
        public bool RuntimeAvailable { get; }
        public string RequestedSkinName { get; }
        public string ErrorMessage { get; }
        public string ErrorType { get; }
        public double PrepareElapsedMilliseconds { get; }
        public double FilePrepareElapsedMilliseconds { get; }
        public double HostNavigateElapsedMilliseconds { get; }
        public double InitialDocumentBuildElapsedMilliseconds { get; }
        public double NavigateToStringElapsedMilliseconds { get; }

        public WhiteBrowserSkinHostOperationResult WithTimings(
            double? prepareElapsedMilliseconds = null,
            double? filePrepareElapsedMilliseconds = null,
            double? hostNavigateElapsedMilliseconds = null,
            double? initialDocumentBuildElapsedMilliseconds = null,
            double? navigateToStringElapsedMilliseconds = null
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                Succeeded,
                RuntimeAvailable,
                RequestedSkinName,
                ErrorMessage,
                ErrorType,
                prepareElapsedMilliseconds ?? PrepareElapsedMilliseconds,
                filePrepareElapsedMilliseconds ?? FilePrepareElapsedMilliseconds,
                hostNavigateElapsedMilliseconds ?? HostNavigateElapsedMilliseconds,
                initialDocumentBuildElapsedMilliseconds ?? InitialDocumentBuildElapsedMilliseconds,
                navigateToStringElapsedMilliseconds ?? NavigateToStringElapsedMilliseconds
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateSuccess(string requestedSkinName)
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: true,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: "",
                errorType: ""
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateRuntimeUnavailable(
            string requestedSkinName,
            string errorMessage
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: false,
                requestedSkinName: requestedSkinName,
                errorMessage: errorMessage,
                errorType: "WebView2RuntimeNotFound"
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateMissingHtml(
            string requestedSkinName,
            string skinHtmlPath
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: $"Skin HTML was not found: {skinHtmlPath ?? ""}",
                errorType: "SkinHtmlMissing"
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateSkipped(
            string requestedSkinName,
            string reason
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: reason,
                errorType: "RefreshSkippedStale"
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateFailed(
            string requestedSkinName,
            string errorMessage,
            string errorType = "HostPrepareFailed"
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: errorMessage,
                errorType: errorType
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateFailed(
            string requestedSkinName,
            Exception exception
        )
        {
            return CreateFailed(
                requestedSkinName,
                exception?.Message ?? "",
                exception?.GetType().Name ?? ""
            );
        }

        private static double SanitizeElapsedMilliseconds(double elapsedMilliseconds)
        {
            if (double.IsNaN(elapsedMilliseconds) || double.IsInfinity(elapsedMilliseconds))
            {
                return 0;
            }

            return Math.Max(0, elapsedMilliseconds);
        }
    }
}

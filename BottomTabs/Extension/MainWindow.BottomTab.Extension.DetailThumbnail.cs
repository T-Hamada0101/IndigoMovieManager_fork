using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ExtensionDetailThumbnailTabIndex = 99;
        private const string ExtensionDetailPlaceholderFileName = "errorGrid.jpg";
        private int _extensionDetailThumbnailRequestVersion;
        private readonly record struct ExtensionDetailThumbnailSnapshotRequest(
            MovieRecords Record,
            long MovieId,
            string MoviePath,
            string MovieBody,
            string Hash,
            bool IsExists,
            string DbFullPath,
            string DbName,
            string ThumbFolder,
            int RequestVersion,
            bool EnqueueIfMissing,
            bool AllowAutoRescue,
            ImageRequest ImageRequest,
            ImageProbeRequest ImageProbeRequest
        );

        private readonly record struct ExtensionDetailThumbnailSnapshotResult(
            string ExistingThumbnailPath,
            string ExpectedThumbnailPath,
            bool HasErrorMarker,
            bool HasOpenRescueRequest,
            bool QueuedMissingCreate,
            bool QueuedAutoRescue,
            ImageProbeResult ImageProbeResult,
            ImageLoadResult ImageLoadResult
        );

        private static readonly string[] DetailLayoutFolderNames = [
            ThumbnailLayoutProfileResolver.DetailStandard.FolderName,
            ThumbnailLayoutProfileResolver.DetailWhiteBrowser.FolderName,
            ThumbnailLayoutProfileResolver.Small.FolderName,
            ThumbnailLayoutProfileResolver.Big.FolderName,
            ThumbnailLayoutProfileResolver.List.FolderName,
            ThumbnailLayoutProfileResolver.Big10.FolderName,
        ];

        private void InitializeDetailThumbnailModeRuntime()
        {
            // 詳細サムネの表示モードは子プロセスへも引き継ぎたいので、まずプロセスへ反映する。
            ThumbnailDetailModeRuntime.ApplyToProcess(ReadConfiguredDetailThumbnailMode());
        }

        internal void ChangeExtensionDetailThumbnailMode(string mode)
        {
            string normalizedMode = ThumbnailDetailModeRuntime.Normalize(mode);
            if (
                !string.Equals(
                    ReadConfiguredDetailThumbnailMode(),
                    normalizedMode,
                    StringComparison.Ordinal
                )
            )
            {
                Properties.Settings.Default.DetailThumbnailMode = normalizedMode;
                QueueApplicationSettingsSave("extension-detail-thumbnail-mode");
            }

            ThumbnailDetailModeRuntime.ApplyToProcess(normalizedMode);
            ExtensionTabViewHost?.ApplyConfiguredDetailThumbnailMode();

            // combo 操作はユーザーの明示的な切替なので、visibility gate で弾かない。
            // AvalonDock の IsActive が combo ポップアップへのフォーカス移動で
            // false になりうるため、gate を挟むと ThumbDetail 更新がスキップされる。
            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                return;
            }

            EnsureActiveExtensionDetailThumbnail(record);
            RefreshActiveExtensionDetailTab(record);
        }

        private string ReadConfiguredDetailThumbnailMode()
        {
            return ThumbnailDetailModeRuntime.Normalize(
                Properties.Settings.Default.DetailThumbnailMode
            );
        }

        private void PrepareExtensionDetailThumbnail(
            MovieRecords record,
            bool enqueueIfMissing,
            bool allowAutoRescue = false
        )
        {
            if (record == null)
            {
                return;
            }

            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(record);
            if (
                !string.IsNullOrWhiteSpace(expectedThumbnailPath)
                && !string.Equals(
                    record.ThumbDetail,
                    expectedThumbnailPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                // まだ未生成の時は、これから作る予定のパスを先に持たせておく。
                record.ThumbDetail = expectedThumbnailPath;
            }

            QueueExtensionDetailThumbnailSnapshotRefresh(
                record,
                enqueueIfMissing,
                allowAutoRescue
            );
        }

        private void EnsureActiveExtensionDetailThumbnail(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            // 詳細タブで今見せる画像が無ければ、その場で現在選択モードの作成要求まで進める。
            PrepareExtensionDetailThumbnail(
                record,
                enqueueIfMissing: true,
                allowAutoRescue: false
            );
            QueueExtensionDetailThumbnailSnapshotRefresh(
                record,
                enqueueIfMissing: true,
                allowAutoRescue: true
            );
        }

        private void EnsureMissingDetailThumbnailCreation(MovieRecords record)
        {
            if (record == null || !record.IsExists)
            {
                return;
            }

            if (IsThumbnailErrorPlaceholderPath(record.ThumbDetail))
            {
                return;
            }

            QueueExtensionDetailThumbnailSnapshotRefresh(
                record,
                enqueueIfMissing: true,
                allowAutoRescue: false
            );
        }

        internal void ReevaluateActiveExtensionDetailThumbnail()
        {
            if (!IsExtensionTabVisibleOrSelected())
            {
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                return;
            }

            EnsureActiveExtensionDetailThumbnail(record);
            RefreshActiveExtensionDetailTab(record);
        }

        private void QueueExtensionDetailThumbnailSnapshotRefresh(
            MovieRecords record,
            bool enqueueIfMissing,
            bool allowAutoRescue
        )
        {
            ExtensionDetailThumbnailSnapshotRequest request =
                CaptureExtensionDetailThumbnailSnapshotRequest(
                    record,
                    enqueueIfMissing,
                    allowAutoRescue
                );
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return;
            }

            _ = RunExtensionDetailThumbnailSnapshotRefreshAsync(request);
        }

        private ExtensionDetailThumbnailSnapshotRequest CaptureExtensionDetailThumbnailSnapshotRequest(
            MovieRecords record,
            bool enqueueIfMissing,
            bool allowAutoRescue
        )
        {
            if (record == null)
            {
                return default;
            }

            int requestVersion = Interlocked.Increment(
                ref _extensionDetailThumbnailRequestVersion
            );
            string moviePath = record.Movie_Path ?? "";
            ImageRequest imageRequest = CreateExtensionDetailImageRequest(
                record.ThumbDetail ?? "",
                moviePath,
                IsExtensionTabVisibleOrSelected(),
                requestVersion
            );
            return new ExtensionDetailThumbnailSnapshotRequest(
                record,
                record.Movie_Id,
                moviePath,
                record.Movie_Body ?? "",
                record.Hash ?? "",
                record.IsExists,
                MainVM?.DbInfo?.DBFullPath ?? "",
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? "",
                requestVersion,
                enqueueIfMissing,
                allowAutoRescue,
                imageRequest,
                ImageProbeRequest.ForExtensionDetailStatus(imageRequest)
            );
        }

        private async Task RunExtensionDetailThumbnailSnapshotRefreshAsync(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            try
            {
                ExtensionDetailThumbnailSnapshotResult result = await Task
                    .Run(() => LoadExtensionDetailThumbnailSnapshotCore(request))
                    .ConfigureAwait(false);

                if (IsExtensionDetailThumbnailShutdownStarted())
                {
                    return;
                }

                await Dispatcher
                    .InvokeAsync(
                        () => ApplyExtensionDetailThumbnailSnapshotResult(request, result),
                        DispatcherPriority.Background
                    )
                    .Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ImageLoadResult imageLoadResult = ImageLoadResult.Failed(
                    request.ImageRequest,
                    request.RequestVersion,
                    "background-check-exception",
                    usesPlaceholder: false
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"detail thumbnail background check failed: path='{request.MoviePath}' {ImageLoadLogFields.Build(imageLoadResult)} err_type='{ex.GetType().Name}' err='{ex.Message}'"
                );
            }
        }

        private ExtensionDetailThumbnailSnapshotResult LoadExtensionDetailThumbnailSnapshotCore(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (!IsExtensionDetailThumbnailRequestCurrentForBackground(request))
            {
                ImageLoadResult canceledResult = ImageLoadResult.Canceled(
                    request.ImageRequest,
                    request.RequestVersion,
                    "stale-background",
                    isStale: true
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"detail thumbnail image probe skipped: path='{request.MoviePath}' {ImageLoadLogFields.Build(canceledResult)}"
                );
                return default;
            }

            string existingThumbnailPath = ResolveExistingExtensionDetailThumbnailPath(request);
            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(request);
            bool hasErrorMarker = HasExtensionDetailErrorMarker(request);
            bool hasOpenRescueRequest = HasOpenExtensionDetailRescueRequest(request);
            bool expectedExists = !string.IsNullOrWhiteSpace(expectedThumbnailPath)
                && Path.Exists(expectedThumbnailPath);
            ImageProbeResult imageProbeResult = BuildExtensionDetailImageProbeResult(
                request,
                existingThumbnailPath,
                hasErrorMarker,
                hasOpenRescueRequest,
                expectedExists
            );
            ImageLoadResult imageLoadResult = BuildExtensionDetailImageLoadResult(
                request,
                existingThumbnailPath,
                expectedExists,
                hasErrorMarker,
                hasOpenRescueRequest,
                imageProbeResult
            );
            DebugRuntimeLog.Write(
                "ui-tempo",
                $"detail thumbnail image probe: path='{request.MoviePath}' {ImageProbeLogFields.Build(request.ImageProbeRequest, imageProbeResult)} {ImageLoadLogFields.Build(imageLoadResult)} expected_exists={FormatLogBool(expectedExists)} rescue_open={FormatLogBool(hasOpenRescueRequest)}"
            );

            bool queuedMissingCreate = false;
            if (
                request.EnqueueIfMissing
                && request.IsExists
                && string.IsNullOrWhiteSpace(existingThumbnailPath)
                && !expectedExists
                && !hasErrorMarker
                && !hasOpenRescueRequest
                && IsExtensionDetailThumbnailRequestCurrentForBackground(request)
            )
            {
                // 既存確認後にまだ無ければ、UIではなく背景側から明示要求を積む。
                queuedMissingCreate = TryEnqueueMissingExtensionDetailThumbnailManualCreate(
                    request
                );
            }

            bool queuedAutoRescue = false;
            if (
                request.AllowAutoRescue
                && hasErrorMarker
                && !hasOpenRescueRequest
                && IsExtensionDetailThumbnailRequestCurrentForBackground(request)
            )
            {
                // ERROR marker 起点の救済判定も背景側へ寄せ、UI tick ではFailureDbを読まない。
                queuedAutoRescue = TryEnqueueExtensionDetailThumbnailRescue(request);
            }

            return new ExtensionDetailThumbnailSnapshotResult(
                existingThumbnailPath,
                expectedThumbnailPath,
                hasErrorMarker,
                hasOpenRescueRequest,
                queuedMissingCreate,
                queuedAutoRescue,
                imageProbeResult,
                imageLoadResult
            );
        }

        // missing / ERROR marker / stamp の判定結果を、UI apply ではなく背景 probe の結果として畳む。
        private static ImageProbeResult BuildExtensionDetailImageProbeResult(
            ExtensionDetailThumbnailSnapshotRequest request,
            string existingThumbnailPath,
            bool hasErrorMarker,
            bool hasOpenRescueRequest,
            bool expectedExists
        )
        {
            bool hasExistingThumbnail = !string.IsNullOrWhiteSpace(existingThumbnailPath);
            bool isMissing =
                !hasExistingThumbnail
                && !expectedExists
                && !hasErrorMarker
                && !hasOpenRescueRequest;
            ImageProbeOutcome outcome = hasExistingThumbnail
                ? ImageProbeOutcome.Found
                : hasErrorMarker
                    ? ImageProbeOutcome.ErrorMarker
                    : isMissing
                        ? ImageProbeOutcome.Missing
                        : ImageProbeOutcome.Unknown;
            long stampUtcTicks =
                request.ImageProbeRequest.RequiresStampProbe && hasExistingThumbnail
                    ? TryGetImageStampUtcTicks(existingThumbnailPath)
                    : 0;
            return new ImageProbeResult(
                outcome,
                isMissing,
                hasErrorMarker,
                stampUtcTicks
            );
        }

        private ImageLoadResult BuildExtensionDetailImageLoadResult(
            ExtensionDetailThumbnailSnapshotRequest request,
            string existingThumbnailPath,
            bool expectedExists,
            bool hasErrorMarker,
            bool hasOpenRescueRequest,
            ImageProbeResult imageProbeResult
        )
        {
            if (!string.IsNullOrWhiteSpace(existingThumbnailPath) || expectedExists)
            {
                return ImageLoadResult.Ready(
                    request.ImageRequest,
                    usesPlaceholder: false,
                    resultRevision: request.RequestVersion
                );
            }

            if (hasErrorMarker || hasOpenRescueRequest)
            {
                return ImageLoadResult.Failed(
                    request.ImageRequest with
                    {
                        ThumbnailPath = GetExtensionDetailPlaceholderPath(),
                    },
                    request.RequestVersion,
                    hasErrorMarker ? "error-marker" : "rescue-open",
                    usesPlaceholder: true
                );
            }

            if (imageProbeResult.IsMissing)
            {
                return ImageLoadResult.Missing(request.ImageRequest, request.RequestVersion);
            }

            return new ImageLoadResult(
                request.ImageRequest,
                ImageLoadOutcome.Unknown,
                HasResolvedImage: false,
                UsesPlaceholder: false,
                IsStale: false,
                FailureReason: "",
                ResultRevision: request.RequestVersion
            );
        }

        private void ApplyExtensionDetailThumbnailSnapshotResult(
            ExtensionDetailThumbnailSnapshotRequest request,
            ExtensionDetailThumbnailSnapshotResult result
        )
        {
            if (!IsExtensionDetailThumbnailSnapshotRequestCurrent(request))
            {
                ImageLoadResult canceledResult = ImageLoadResult.Canceled(
                    request.ImageRequest,
                    request.RequestVersion,
                    "stale-apply",
                    isStale: true
                );
                DebugRuntimeLog.Write(
                    "ui-tempo",
                    $"detail thumbnail image apply skipped: path='{request.MoviePath}' {ImageLoadLogFields.Build(canceledResult)}"
                );
                return;
            }

            string nextThumbDetail = result.ExistingThumbnailPath;
            if (string.IsNullOrWhiteSpace(nextThumbDetail))
            {
                nextThumbDetail = result.HasErrorMarker || result.HasOpenRescueRequest
                    ? GetExtensionDetailPlaceholderPath()
                    : result.ExpectedThumbnailPath;
            }

            if (
                !string.IsNullOrWhiteSpace(nextThumbDetail)
                && !string.Equals(
                    request.Record.ThumbDetail,
                    nextThumbDetail,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ImageRequest imageRequest = request.ImageRequest with
                {
                    ThumbnailPath = nextThumbDetail,
                };
                int currentImageRequestRevision = Volatile.Read(
                    ref _extensionDetailThumbnailRequestVersion
                );
                if (
                    !ShouldApplyExtensionDetailImageRequest(
                        imageRequest,
                        currentImageRequestRevision
                    )
                )
                {
                    ImageLoadResult canceledResult = ImageLoadResult.Canceled(
                        imageRequest,
                        currentImageRequestRevision,
                        "stale-image-request",
                        isStale: true
                    );
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"detail thumbnail image request discarded: path='{request.MoviePath}' {ImageLoadLogFields.Build(canceledResult)}"
                    );
                    return;
                }

                // 背景確認の採用は最後にUIへ戻し、表示モデルだけを短く更新する。
                request.Record.ThumbDetail = nextThumbDetail;
            }

            if (result.QueuedMissingCreate || result.QueuedAutoRescue)
            {
                RequestThumbnailErrorSnapshotRefresh();
                RequestThumbnailProgressSnapshotRefresh();
            }

            if (IsExtensionTabVisibleOrSelected())
            {
                RefreshActiveExtensionDetailTab(request.Record);
            }
        }

        private bool IsExtensionDetailThumbnailSnapshotRequestCurrent(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            return !IsExtensionDetailThumbnailShutdownStarted()
                && request.RequestVersion == Volatile.Read(
                    ref _extensionDetailThumbnailRequestVersion
                )
                && AreSameMainDbPath(request.DbFullPath, MainVM?.DbInfo?.DBFullPath ?? "")
                && IsSameExtensionDetailThumbnailRecord(GetSelectedItemByTabIndex(), request);
        }

        private bool IsExtensionDetailThumbnailRequestCurrentForBackground(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            return !IsExtensionDetailThumbnailShutdownStarted()
                && request.RequestVersion == Volatile.Read(
                    ref _extensionDetailThumbnailRequestVersion
                )
                && AreSameMainDbPath(request.DbFullPath, MainVM?.DbInfo?.DBFullPath ?? "");
        }

        internal static ImageRequest CreateExtensionDetailImageRequest(
            string thumbnailPath,
            string moviePath,
            bool isVisiblePriority,
            int requestRevision
        )
        {
            return ImageRequest.ForExtensionDetail(
                thumbnailPath,
                QueueDbPathResolver.CreateMoviePathKey(moviePath) ?? "",
                isVisiblePriority,
                requestRevision
            );
        }

        internal static bool ShouldApplyExtensionDetailImageRequest(
            ImageRequest request,
            int currentRevision
        )
        {
            return request.ThumbnailRole == ImageRequestThumbnailRole.ExtensionDetail
                && request.RequestRevision == currentRevision;
        }

        private bool IsExtensionDetailThumbnailShutdownStarted()
        {
            return Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished;
        }

        private static bool IsSameExtensionDetailThumbnailRecord(
            MovieRecords record,
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (record == null)
            {
                return false;
            }

            if (request.MovieId > 0 && record.Movie_Id == request.MovieId)
            {
                return true;
            }

            return string.Equals(
                record.Movie_Path,
                request.MoviePath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private string ResolveExistingExtensionDetailThumbnailPath(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return "";
            }

            foreach (string outPath in EnumerateExtensionDetailCandidateOutPaths(request))
            {
                string thumbnailPath = ResolveExistingExtensionDetailThumbnailPathByOutPath(
                    outPath,
                    request
                );
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    return thumbnailPath;
                }
            }

            return "";
        }

        private IEnumerable<string> EnumerateExtensionDetailCandidateOutPaths(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            List<string> candidates = [];
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            string currentOutPath = ResolveThumbnailOutPath(
                ExtensionDetailThumbnailTabIndex,
                request.DbName,
                request.ThumbFolder
            );
            AddCandidatePath(candidates, unique, currentOutPath);

            string configuredThumbRoot = ResolveRuntimeThumbnailRoot(
                request.DbFullPath,
                request.DbName,
                request.ThumbFolder
            );
            if (!string.IsNullOrWhiteSpace(configuredThumbRoot))
            {
                AddCandidatePath(candidates, unique, configuredThumbRoot);
            }

            string layoutRootCandidate = ResolveCurrentCompatibleLayoutRoot(configuredThumbRoot);
            if (!string.IsNullOrWhiteSpace(layoutRootCandidate))
            {
                foreach (string profileFolderName in DetailLayoutFolderNames)
                {
                    AddCandidatePath(
                        candidates,
                        unique,
                        Path.Combine(layoutRootCandidate, profileFolderName)
                    );
                }
            }

            return candidates;
        }

        private static string ResolveCurrentCompatibleLayoutRoot(string thumbRoot)
        {
            if (string.IsNullOrWhiteSpace(thumbRoot))
            {
                return "";
            }

            string normalizedThumbRoot = thumbRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            if (string.IsNullOrWhiteSpace(normalizedThumbRoot))
            {
                return "";
            }

            string folderName = Path.GetFileName(normalizedThumbRoot);
            bool isLayoutFolder = !string.IsNullOrWhiteSpace(folderName)
                && KnownThumbnailLayoutFolderNames.Contains(folderName);
            if (!isLayoutFolder)
            {
                return "";
            }

            return Path.GetDirectoryName(normalizedThumbRoot) ?? "";
        }

        private static void AddCandidatePath(
            List<string> candidates,
            HashSet<string> unique,
            string value
        )
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!unique.Add(value))
            {
                return;
            }

            if (Directory.Exists(value))
            {
                candidates.Add(value);
                return;
            }
        }

        private string ResolveExistingExtensionDetailThumbnailPathByOutPath(
            string outPath,
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath) || string.IsNullOrWhiteSpace(outPath))
            {
                return "";
            }

            string thumbnailPath = ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                request.MoviePath,
                request.Hash
            );
            if (Path.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            if (!string.IsNullOrWhiteSpace(request.MovieBody))
            {
                string legacyThumbnailPath = ThumbnailPathResolver.BuildThumbnailPath(
                    outPath,
                    request.MovieBody,
                    request.Hash
                );
                if (Path.Exists(legacyThumbnailPath))
                {
                    return legacyThumbnailPath;
                }
            }

            if (
                ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                    outPath,
                    request.MoviePath,
                    out string existingByMoviePath
                )
            )
            {
                return existingByMoviePath;
            }

            if (
                !string.IsNullOrWhiteSpace(request.MovieBody)
                && ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                    outPath,
                    request.MovieBody,
                    out string existingByMovieBody
                )
            )
            {
                return existingByMovieBody;
            }

            if (
                TryFindExistingSuccessThumbnailPathByBodyScan(
                    outPath,
                    request.MoviePath,
                    out string scannedByMoviePath
                )
            )
            {
                return scannedByMoviePath;
            }

            if (
                !string.IsNullOrWhiteSpace(request.MovieBody)
                && TryFindExistingSuccessThumbnailPathByBodyScan(
                    outPath,
                    request.MovieBody,
                    out string scannedByMovieBody
                )
            )
            {
                return scannedByMovieBody;
            }

            if (
                ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                    request.MoviePath,
                    out string sourceImagePath
                )
            )
            {
                return sourceImagePath;
            }

            return "";
        }

        private static bool TryFindExistingSuccessThumbnailPathByBodyScan(
            string outPath,
            string movieNameOrPath,
            out string matchedPath
        )
        {
            matchedPath = "";
            if (string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(movieNameOrPath))
            {
                return false;
            }

            if (!Directory.Exists(outPath))
            {
                return false;
            }

            string targetBody = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetBody))
            {
                return false;
            }

            try
            {
                DateTime newestWriteTimeUtc = DateTime.MinValue;
                long newestLength = -1;
                string newestPath = "";

                foreach (string thumbnailPath in Directory.EnumerateFiles(outPath, "*.jpg"))
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(thumbnailPath) ?? "";
                    if (string.IsNullOrWhiteSpace(fileNameWithoutExt))
                    {
                        continue;
                    }

                    int separatorIndex = fileNameWithoutExt.LastIndexOf(".#", StringComparison.Ordinal);
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string body = fileNameWithoutExt[..separatorIndex];
                    if (!string.Equals(body, targetBody, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ThumbnailPathResolver.IsErrorMarker(thumbnailPath))
                    {
                        continue;
                    }

                    FileInfo fileInfo = new(thumbnailPath);
                    if (!fileInfo.Exists || fileInfo.Length <= 0)
                    {
                        continue;
                    }

                    if (
                        newestPath.Length == 0
                        || fileInfo.LastWriteTimeUtc > newestWriteTimeUtc
                        || (fileInfo.LastWriteTimeUtc == newestWriteTimeUtc && fileInfo.Length > newestLength)
                    )
                    {
                        newestWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                        newestLength = fileInfo.Length;
                        newestPath = thumbnailPath;
                    }
                }

                if (string.IsNullOrWhiteSpace(newestPath))
                {
                    return false;
                }

                matchedPath = newestPath;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long TryGetImageStampUtcTicks(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return 0;
            }

            try
            {
                FileInfo fileInfo = new(imagePath);
                return fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0;
            }
            catch
            {
                return 0;
            }
        }

        private bool HasExtensionDetailErrorMarker(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return false;
            }

            string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                ResolveThumbnailOutPath(
                    ExtensionDetailThumbnailTabIndex,
                    request.DbName,
                    request.ThumbFolder
                ),
                request.MoviePath
            );
            return Path.Exists(errorMarkerPath);
        }

        private bool HasOpenExtensionDetailRescueRequest(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = CreateThumbnailErrorFailureDbService(
                request.DbFullPath
            );
            if (failureDbService == null)
            {
                return false;
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                request.MoviePath
            );
            return failureDbService.HasOpenRescueRequest(
                moviePathKey,
                ExtensionDetailThumbnailTabIndex
            );
        }

        private string BuildExpectedExtensionDetailThumbnailPath(MovieRecords record)
        {
            if (record == null)
            {
                return "";
            }

            return BuildCurrentThumbnailPath(
                ExtensionDetailThumbnailTabIndex,
                record.Movie_Path,
                record.Hash
            );
        }

        private string BuildExpectedExtensionDetailThumbnailPath(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return "";
            }

            return ThumbnailPathResolver.BuildThumbnailPath(
                ResolveThumbnailOutPath(
                    ExtensionDetailThumbnailTabIndex,
                    request.DbName,
                    request.ThumbFolder
                ),
                request.MoviePath,
                request.Hash
            );
        }

        private static string GetExtensionDetailPlaceholderPath()
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "Images",
                ExtensionDetailPlaceholderFileName
            );
        }

        private bool TryEnqueueMissingExtensionDetailThumbnailManualCreate(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (!request.IsExists || string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return false;
            }

            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(request);
            NoLockImageConverter.InvalidateFilePath(expectedThumbnailPath);

            // 詳細サムネの不足作成は、背景確認後に preferred として通常キューへ積む。
            return TryEnqueueThumbnailJob(
                new QueueObj
                {
                    MovieId = request.MovieId,
                    MovieFullPath = request.MoviePath,
                    Hash = request.Hash,
                    Tabindex = ExtensionDetailThumbnailTabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                },
                bypassDebounce: true
            );
        }

        private bool TryEnqueueExtensionDetailThumbnailRescue(
            ExtensionDetailThumbnailSnapshotRequest request
        )
        {
            if (string.IsNullOrWhiteSpace(request.MoviePath))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = CreateThumbnailErrorFailureDbService(
                request.DbFullPath
            );
            QueueObj queueObj = new()
            {
                MovieId = request.MovieId,
                MovieFullPath = request.MoviePath,
                Hash = request.Hash,
                Tabindex = ExtensionDetailThumbnailTabIndex,
                Priority = ThumbnailQueuePriority.Preferred,
            };

            return TryEnqueueThumbnailErrorRescueSnapshotJob(
                queueObj,
                request.DbFullPath,
                failureDbService,
                request.DbName,
                request.ThumbFolder,
                reason: "detail-error-placeholder",
                requiresIdle: false,
                priorityUntilUtc: null
            );
        }
    }
}

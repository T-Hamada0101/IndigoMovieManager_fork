using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// 起動時全件変換で共有するサムネイル出力パス群のスナップショット。
        /// 変換ループの途中でパスが変わらないように、開始前に1回だけキャプチャする。
        /// </summary>
        private readonly record struct MovieRecordBulkBuildContext(
            string[] ThumbnailOutPaths,
            string DetailThumbnailOutPath,
            string ImagesDirectoryPath
        );

        /// <summary>
        /// 全レイアウトタブのサムネイル既存ファイル名をメモリ上にキャッシュし、
        /// 起動時全件変換で File.Exists の N×5 回呼び出しを HashSet.Contains に置き換える高速化用。
        /// </summary>
        private sealed class MovieRecordBulkBuildCache
        {
            public required HashSet<string>[] ThumbnailFileNamesByTab { get; init; }
            public required HashSet<string> DetailThumbnailFileNames { get; init; }
        }

        private readonly record struct StartupBulkCacheWarmResult(
            long ElapsedMs,
            int CandidateCount,
            int HitCount,
            long[] ComponentElapsedMs
        );

        private sealed class MovieRecordBulkBuildMetrics
        {
            public int SourceImageProbeCount { get; set; }
            public int SourceImageCacheHitCount { get; set; }
            public long SourceImageProbeTimestampTicks { get; set; }
        }

        private readonly record struct MovieRecordSourceApplyResult(
            MovieRecords[] Items,
            long BulkCacheElapsedMs,
            long RowConvertElapsedMs,
            long SourceImageProbeElapsedMs,
            int SourceImageProbeCount,
            int SourceImageCacheHitCount,
            long ReplaceMovieRecsElapsedMs,
            long QueueMovieExistsElapsedMs,
            long BackgroundElapsedMs
        );

        /// <summary>
        /// DBから拾った無骨なレコード1件を、キラキラな表示用（MovieRecords）へ変換する。
        /// 単発追加でもファイル存在確認は背景へ逃がし、UIは表示反映だけに集中する。
        /// </summary>
        private async Task DataRowToViewData(DataRow row, string expectedDbFullPath = "")
        {
            if (row == null)
            {
                return;
            }

            MovieRecordBulkBuildContext bulkContext = Dispatcher.CheckAccess()
                ? CaptureMovieRecordBulkBuildContext()
                : await Dispatcher
                    .InvokeAsync(CaptureMovieRecordBulkBuildContext, DispatcherPriority.Background)
                    .Task.ConfigureAwait(false);

            MovieRecords item = await Task.Run(() =>
                {
                    MovieRecordBulkBuildCache bulkCache = BuildMovieRecordBulkBuildCache(
                        bulkContext
                    );
                    return CreateMovieRecordFromDataRow(
                        row,
                        bulkContext,
                        bulkCache,
                        resolveMovieExists: false
                    );
                })
                .ConfigureAwait(false);
            if (item == null)
            {
                return;
            }

            await Dispatcher
                .InvokeAsync(
                    () =>
                    {
                        if (
                            !string.IsNullOrWhiteSpace(expectedDbFullPath)
                            && !AreSameMainDbPath(
                                expectedDbFullPath,
                                MainVM?.DbInfo?.DBFullPath ?? ""
                            )
                        )
                        {
                            return;
                        }

                        MainVM.MovieRecs.Add(item);
                        QueueMovieExistsRefresh([item], _filterAndSortRequestRevision);
                    },
                    DispatcherPriority.Background
                )
                .Task.ConfigureAwait(false);
        }

        /// <summary>
        /// DataRow 1行を表示用の MovieRecords に変換する。
        /// 単発追加・起動時全件変換とも、必要に応じて BulkBuildCache 経由の高速経路を使う。
        /// </summary>
        private MovieRecords CreateMovieRecordFromDataRow(
            DataRow row,
            MovieRecordBulkBuildContext? bulkContext = null,
            MovieRecordBulkBuildCache bulkCache = null,
            MovieRecordBulkBuildMetrics bulkMetrics = null,
            bool resolveMovieExists = true
        )
        {
            if (row == null)
            {
                return null;
            }

            string[] thumbErrorPath =
            [
                @"errorSmall.jpg",
                @"errorBig.jpg",
                @"errorGrid.jpg",
                @"errorList.jpg",
                @"errorBig.jpg",
            ];
            string[] thumbPath = new string[thumbErrorPath.Length];
            string hash = row["hash"]?.ToString() ?? "";
            string movieFullPath = row["movie_path"]?.ToString() ?? "";
            string movieName = row["movie_name"]?.ToString() ?? "";
            string imagesDirectoryPath =
                bulkContext?.ImagesDirectoryPath
                ?? Path.Combine(AppContext.BaseDirectory, "Images");
            bool deferSourceImageProbe = bulkMetrics != null;
            // bulk変換では同名source画像を探索せず、可視範囲確定後の背景probeへ譲る。
            LazyThumbnailSourceImagePathResolver sourceImageResolver = null;

            for (int i = 0; i < thumbErrorPath.Length; i++)
            {
                string fallbackPath = Path.Combine(imagesDirectoryPath, thumbErrorPath[i]);
                if (bulkContext.HasValue && bulkCache != null)
                {
                    thumbPath[i] = ResolveThumbnailDisplayPath(
                        bulkContext.Value.ThumbnailOutPaths[i],
                        bulkCache.ThumbnailFileNamesByTab[i],
                        movieFullPath,
                        movieName,
                        hash,
                        fallbackPath,
                        sourceImageResolver,
                        allowSourceImageProbe: !deferSourceImageProbe
                    );
                    continue;
                }

                // 生成側と同じ規則でまず探索し、旧命名が残っている環境はフォールバックで拾う。
                string tempPath = BuildCurrentThumbnailPath(i, movieFullPath, hash);
                if (!Path.Exists(tempPath) && !string.IsNullOrWhiteSpace(movieName))
                {
                    tempPath = BuildCurrentThumbnailPath(i, movieName, hash);
                }

                thumbPath[i] = Path.Exists(tempPath) ? tempPath : fallbackPath;
            }

            string thumbPathDetail;
            if (bulkContext.HasValue && bulkCache != null)
            {
                thumbPathDetail = ResolveThumbnailDisplayPath(
                    bulkContext.Value.DetailThumbnailOutPath,
                    bulkCache.DetailThumbnailFileNames,
                    movieFullPath,
                    movieName,
                    hash,
                    Path.Combine(imagesDirectoryPath, thumbErrorPath[2]),
                    sourceImageResolver,
                    allowSourceImageProbe: !deferSourceImageProbe
                );
            }
            else
            {
                // エクステンション詳細用も、本体と同じく旧命名をフォールバックで拾う。
                string tempPathExtensionDetail = BuildCurrentThumbnailPath(99, movieFullPath, hash);
                if (!Path.Exists(tempPathExtensionDetail) && !string.IsNullOrWhiteSpace(movieName))
                {
                    tempPathExtensionDetail = BuildCurrentThumbnailPath(99, movieName, hash);
                }

                thumbPathDetail = Path.Exists(tempPathExtensionDetail)
                    ? tempPathExtensionDetail
                    : Path.Combine(imagesDirectoryPath, thumbErrorPath[2]);
            }

            if (bulkMetrics != null && sourceImageResolver != null)
            {
                bulkMetrics.SourceImageProbeCount += sourceImageResolver.ProbeCount;
                bulkMetrics.SourceImageCacheHitCount += sourceImageResolver.CacheHitCount;
                bulkMetrics.SourceImageProbeTimestampTicks +=
                    sourceImageResolver.ProbeElapsedTimestampTicks;
            }

            string tags = row["tag"]?.ToString() ?? "";
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tags))
            {
                string[] splitTags = tags.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string tagItem in splitTags)
                {
                    tagArray.Add(tagItem);
                }
            }

            string tag = MyRegex().Replace(tags, "");
            string ext = Path.GetExtension(movieFullPath);
            string movieBody = Path.GetFileNameWithoutExtension(movieFullPath);

            return new MovieRecords
            {
                Movie_Id = (long)row["movie_id"],
                Movie_Name = $"{movieName}{ext}",
                Movie_Body = movieBody,
                Movie_Path = movieFullPath,
                Movie_Length = new TimeSpan(0, 0, (int)(long)row["movie_length"]).ToString(
                    @"hh\:mm\:ss"
                ),
                Movie_Size = (long)row["movie_size"],
                Last_Date = ReadDbDateTimeTextOrEmpty(row["last_date"]),
                File_Date = ReadDbDateTimeTextOrEmpty(row["file_date"]),
                Regist_Date = ReadDbDateTimeTextOrEmpty(row["regist_date"]),
                Score = (long)row["score"],
                View_Count = (long)row["view_count"],
                Hash = hash,
                Container = row["container"]?.ToString() ?? "",
                Video = row["video"]?.ToString() ?? "",
                Audio = row["audio"]?.ToString() ?? "",
                Extra = row["extra"]?.ToString() ?? "",
                Title = row["title"]?.ToString() ?? "",
                Album = row["album"]?.ToString() ?? "",
                Artist = row["artist"]?.ToString() ?? "",
                Grouping = row["grouping"]?.ToString() ?? "",
                Writer = row["writer"]?.ToString() ?? "",
                Genre = row["genre"]?.ToString() ?? "",
                Track = row["track"]?.ToString() ?? "",
                Camera = row["camera"]?.ToString() ?? "",
                Create_Time = row["create_time"]?.ToString() ?? "",
                Kana = row["kana"]?.ToString() ?? "",
                Roma = row["roma"]?.ToString() ?? "",
                Tags = tag,
                Tag = tagArray,
                Comment1 = row["comment1"]?.ToString() ?? "",
                Comment2 = row["comment2"]?.ToString() ?? "",
                Comment3 = row["comment3"]?.ToString() ?? "",
                ThumbPathSmall = thumbPath[0],
                ThumbPathBig = thumbPath[1],
                ThumbPathGrid = thumbPath[2],
                ThumbPathList = thumbPath[3],
                ThumbPathBig10 = thumbPath[4],
                ThumbDetail = thumbPathDetail,
                Drive = Path.GetPathRoot(movieFullPath),
                Dir = Path.GetDirectoryName(movieFullPath),
                // 起動時全件変換では存在確認を後段へ逃がし、まず一覧を出す。
                IsExists = resolveMovieExists ? Path.Exists(movieFullPath) : true,
                Ext = ext,
            };
        }

        /// <summary>
        /// 全件変換開始前にサムネイル出力パス群を1回だけ採取し、変換中のパス揺れを防ぐ。
        /// </summary>
        private MovieRecordBulkBuildContext CaptureMovieRecordBulkBuildContext()
        {
            string[] thumbnailOutPaths = new string[5];
            for (int index = 0; index < thumbnailOutPaths.Length; index++)
            {
                thumbnailOutPaths[index] = ResolveCurrentThumbnailOutPath(index);
            }

            return new MovieRecordBulkBuildContext(
                thumbnailOutPaths,
                ResolveCurrentThumbnailOutPath(99),
                Path.Combine(AppContext.BaseDirectory, "Images")
            );
        }

        /// <summary>
        /// 各レイアウトフォルダから既存 jpg ファイル名を一括収集し、HashSet 化して返す。
        /// 全件変換時の高速サムネイルパス解決に使う。
        /// </summary>
        private static MovieRecordBulkBuildCache BuildMovieRecordBulkBuildCache(
            MovieRecordBulkBuildContext context
        )
        {
            HashSet<string>[] thumbnailFileNamesByTab = new HashSet<string>[
                context.ThumbnailOutPaths.Length
            ];
            for (int index = 0; index < context.ThumbnailOutPaths.Length; index++)
            {
                thumbnailFileNamesByTab[index] = BuildThumbnailFileNameLookup(
                    context.ThumbnailOutPaths[index]
                );
            }

            return new MovieRecordBulkBuildCache
            {
                ThumbnailFileNamesByTab = thumbnailFileNamesByTab,
                DetailThumbnailFileNames = BuildThumbnailFileNameLookup(
                    context.DetailThumbnailOutPath
                ),
            };
        }

        /// <summary>
        /// 起動ページでは全ディレクトリを列挙せず、表示対象の現行名・旧名だけを増分確認する。
        /// 同じキャッシュへ追記するため、continuationでも確認済み結果をそのまま再利用できる。
        /// </summary>
        private static StartupBulkCacheWarmResult WarmMovieRecordBulkBuildCacheForStartupPage(
            MovieRecordBulkBuildCache cache,
            MovieRecordBulkBuildContext context,
            MainDbMovieReadItemResult[] items
        )
        {
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            long[] componentElapsedMs = new long[context.ThumbnailOutPaths.Length + 1];
            int candidateCount = 0;
            int hitCount = 0;

            for (int index = 0; index < context.ThumbnailOutPaths.Length; index++)
            {
                Stopwatch componentStopwatch = Stopwatch.StartNew();
                WarmThumbnailFileNamesForStartupItems(
                    context.ThumbnailOutPaths[index],
                    cache.ThumbnailFileNamesByTab[index],
                    items,
                    ref candidateCount,
                    ref hitCount
                );
                componentElapsedMs[index] = componentStopwatch.ElapsedMilliseconds;
            }

            Stopwatch detailStopwatch = Stopwatch.StartNew();
            WarmThumbnailFileNamesForStartupItems(
                context.DetailThumbnailOutPath,
                cache.DetailThumbnailFileNames,
                items,
                ref candidateCount,
                ref hitCount
            );
            componentElapsedMs[^1] = detailStopwatch.ElapsedMilliseconds;
            totalStopwatch.Stop();

            return new StartupBulkCacheWarmResult(
                totalStopwatch.ElapsedMilliseconds,
                candidateCount,
                hitCount,
                componentElapsedMs
            );
        }

        private static void WarmThumbnailFileNamesForStartupItems(
            string thumbnailOutPath,
            HashSet<string> lookup,
            MainDbMovieReadItemResult[] items,
            ref int candidateCount,
            ref int hitCount
        )
        {
            if (string.IsNullOrWhiteSpace(thumbnailOutPath) || items == null)
            {
                return;
            }

            foreach (MainDbMovieReadItemResult item in items)
            {
                AddStartupThumbnailCandidate(
                    thumbnailOutPath,
                    lookup,
                    ThumbnailPathResolver.BuildThumbnailFileName(item.MoviePath, item.Hash),
                    ref candidateCount,
                    ref hitCount
                );
                if (!string.IsNullOrWhiteSpace(item.MovieName))
                {
                    AddStartupThumbnailCandidate(
                        thumbnailOutPath,
                        lookup,
                        ThumbnailPathResolver.BuildThumbnailFileName(item.MovieName, item.Hash),
                        ref candidateCount,
                        ref hitCount
                    );
                }
            }
        }

        private static void AddStartupThumbnailCandidate(
            string thumbnailOutPath,
            HashSet<string> lookup,
            string fileName,
            ref int candidateCount,
            ref int hitCount
        )
        {
            if (lookup.Contains(fileName))
            {
                return;
            }

            candidateCount++;
            if (File.Exists(Path.Combine(thumbnailOutPath, fileName)))
            {
                lookup.Add(fileName);
                hitCount++;
            }
        }

        private static MovieRecordBulkBuildCache CreateEmptyMovieRecordBulkBuildCache(
            MovieRecordBulkBuildContext context
        )
        {
            HashSet<string>[] thumbnailFileNamesByTab = new HashSet<string>[
                context.ThumbnailOutPaths.Length
            ];
            for (int index = 0; index < thumbnailFileNamesByTab.Length; index++)
            {
                thumbnailFileNamesByTab[index] = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );
            }

            return new MovieRecordBulkBuildCache
            {
                ThumbnailFileNamesByTab = thumbnailFileNamesByTab,
                DetailThumbnailFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
        }

        internal static HashSet<string> BuildThumbnailFileNameLookup(string thumbnailOutPath)
        {
            return ThumbnailPathResolver.BuildThumbnailFileNameLookup(thumbnailOutPath);
        }

        /// <summary>
        /// HashSet キャッシュを使って最速でサムネイル表示パスを解決する。
        /// 現在の命名規則 → 旧命名規則 → 同名画像 fallback の順で探索する。
        /// </summary>
        internal static string ResolveThumbnailDisplayPath(
            string thumbnailOutPath,
            HashSet<string> existingFileNames,
            string movieFullPath,
            string movieName,
            string hash,
            string fallbackPath,
            LazyThumbnailSourceImagePathResolver sourceImageResolver = null,
            bool allowSourceImageProbe = true
        )
        {
            if (!string.IsNullOrWhiteSpace(thumbnailOutPath) && existingFileNames != null)
            {
                string currentFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                    movieFullPath,
                    hash
                );
                if (existingFileNames.Contains(currentFileName))
                {
                    return Path.Combine(thumbnailOutPath, currentFileName);
                }

                if (!string.IsNullOrWhiteSpace(movieName))
                {
                    string legacyFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                        movieName,
                        hash
                    );
                    if (existingFileNames.Contains(legacyFileName))
                    {
                        return Path.Combine(thumbnailOutPath, legacyFileName);
                    }
                }
            }

            if (!allowSourceImageProbe)
            {
                return fallbackPath;
            }

            string sourceImagePath;
            if (sourceImageResolver != null)
            {
                sourceImagePath = sourceImageResolver.Resolve();
            }
            else
            {
                // 既存の単発呼び出しは、従来どおりこの場で探索して返却パスを維持する。
                ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                    movieFullPath,
                    out sourceImagePath
                );
            }
            if (!string.IsNullOrWhiteSpace(sourceImagePath))
            {
                return sourceImagePath;
            }
            return fallbackPath;
        }

        /// <summary>
        /// 取得済みの生データ（movieData）から、表示用コレクションを背景で組み立てて一気に差し替える。
        /// </summary>
        private async Task<MovieRecordSourceApplyResult> SetRecordsToSource(
            DataTable sourceData,
            int requestRevision,
            CancellationToken cancellationToken = default,
            bool deferUiApplyForExternalCancellation = false
        )
        {
            DataTable targetData = sourceData ?? movieData;
            if (targetData == null)
            {
                return new MovieRecordSourceApplyResult([], 0, 0, 0, 0, 0, 0, 0, 0);
            }

            int rowCount = targetData.Rows.Count;
            MovieRecordBulkBuildContext bulkContext = CaptureMovieRecordBulkBuildContext();
            MovieRecordSourceApplyResult result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch backgroundStopwatch = Stopwatch.StartNew();
                Stopwatch bulkCacheStopwatch = Stopwatch.StartNew();
                MovieRecordBulkBuildCache bulkCache = BuildMovieRecordBulkBuildCache(bulkContext);
                bulkCacheStopwatch.Stop();
                MovieRecordBulkBuildMetrics bulkMetrics = new();
                MovieRecords[] loadedItems = new MovieRecords[rowCount];
                Stopwatch rowConvertStopwatch = Stopwatch.StartNew();
                for (int index = 0; index < rowCount; index++)
                {
                    if ((index & 63) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    loadedItems[index] = CreateMovieRecordFromDataRow(
                        targetData.Rows[index],
                        bulkContext,
                        bulkCache,
                        bulkMetrics,
                        resolveMovieExists: false
                    );
                }
                rowConvertStopwatch.Stop();
                backgroundStopwatch.Stop();

                return new MovieRecordSourceApplyResult(
                    loadedItems,
                    bulkCacheStopwatch.ElapsedMilliseconds,
                    rowConvertStopwatch.ElapsedMilliseconds,
                    (long)(
                        bulkMetrics.SourceImageProbeTimestampTicks * 1000.0 / Stopwatch.Frequency
                    ),
                    bulkMetrics.SourceImageProbeCount,
                    bulkMetrics.SourceImageCacheHitCount,
                    0,
                    0,
                    backgroundStopwatch.ElapsedMilliseconds
                );
            }, cancellationToken);

            if (
                deferUiApplyForExternalCancellation
                && Dispatcher != null
                && !Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished
            )
            {
                // partial全件整合だけは未処理Inputへ先を譲り、スクロール開始をtokenへ反映させる。
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Background,
                    cancellationToken
                );
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (requestRevision != _filterAndSortRequestRevision)
            {
                return result;
            }

            Stopwatch replaceStopwatch = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();
            MainVM.ReplaceMovieRecs(result.Items);
            replaceStopwatch.Stop();
            Stopwatch queueStopwatch = Stopwatch.StartNew();
            QueueMovieExistsRefresh(result.Items, requestRevision);
            queueStopwatch.Stop();
            return result with
            {
                ReplaceMovieRecsElapsedMs = replaceStopwatch.ElapsedMilliseconds,
                QueueMovieExistsElapsedMs = queueStopwatch.ElapsedMilliseconds,
            };
        }

        /// <summary>
        /// 起動時全件変換で省略したファイル存在チェックをバックグラウンドで後追い実行し、
        /// 見つからないファイルの IsExists を false に更新する。128件ずつバッチでUIスレッドへ反映。
        /// </summary>
        private void QueueMovieExistsRefresh(IReadOnlyList<MovieRecords> items, int requestRevision)
        {
            if (items == null || items.Count < 1)
            {
                return;
            }

            int refreshRevision = Interlocked.Increment(ref _movieExistsRefreshRevision);
            _ = Task.Run(async () =>
            {
                try
                {
                    List<(MovieRecords Record, bool Exists)> pending = [];
                    for (int index = 0; index < items.Count; index++)
                    {
                        if (
                            refreshRevision != Volatile.Read(ref _movieExistsRefreshRevision)
                            || requestRevision != Volatile.Read(ref _filterAndSortRequestRevision)
                        )
                        {
                            return;
                        }

                        MovieRecords item = items[index];
                        bool exists = Path.Exists(item?.Movie_Path ?? "");
                        if (item != null && item.IsExists != exists)
                        {
                            pending.Add((item, exists));
                        }

                        if (pending.Count >= 128)
                        {
                            await ApplyMovieExistsRefreshBatchAsync(
                                pending.ToArray(),
                                refreshRevision,
                                requestRevision
                            );
                            pending.Clear();
                        }
                    }

                    if (pending.Count > 0)
                    {
                        await ApplyMovieExistsRefreshBatchAsync(
                            pending.ToArray(),
                            refreshRevision,
                            requestRevision
                        );
                    }
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "ui-tempo",
                        $"movie exists refresh failed: revision={requestRevision} err='{ex.GetType().Name}: {ex.Message}'"
                    );
                }
            });
        }

        private Task ApplyMovieExistsRefreshBatchAsync(
            (MovieRecords Record, bool Exists)[] batch,
            int refreshRevision,
            int requestRevision
        )
        {
            return Dispatcher
                .InvokeAsync(
                    () =>
                    {
                        if (
                            refreshRevision != _movieExistsRefreshRevision
                            || requestRevision != _filterAndSortRequestRevision
                        )
                        {
                            return;
                        }

                        for (int index = 0; index < batch.Length; index++)
                        {
                            batch[index].Record.IsExists = batch[index].Exists;
                        }
                    },
                    DispatcherPriority.Background
                )
                .Task;
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.ViewModels
{
    public enum FilteredMovieRecsUpdateMode
    {
        Reset = 0,
        Diff = 1,
        Move = 2,
    }

    public readonly record struct FilteredMovieRecsUpdateResult(
        bool HasChanges,
        int RetainedPrefixCount,
        int RetainedSuffixCount,
        int RemovedCount,
        int InsertedCount,
        int MovedCount,
        int UpdatedCount = 0
    );

    /// <summary>
    /// メイン画面(MainWindow)のUIとガッツリ連携する、縁の下の力持ちViewModel！💪
    /// DBデータの保持から、TreeViewメニューの構築、一覧画面の爆速検索・ソートロジックまで、裏方の全責任を背負い込む最高にタフなクラスだ！✨
    /// </summary>
    public class MainWindowViewModel
    {
        private const int MaxStableKeyAnchoredSpliceCount = 16;

        // アプリ全体の設定情報（DBパスやスキンなど）を持つプロパティ
        public DBInfo DbInfo { get; set; }

        // 画面左側のTreeViewに表示する最近使ったファイルのルートコレクション
        public ObservableCollection<TreeSource> RecentTreeRoot { get; set; }

        // メインの一覧画面に表示する動画レコードの管理用コレクション
        public ResettableObservableCollection<MovieRecords> MovieRecs { get; set; }

        // 検索や絞り込みをかけた後の、実際に画面へ表示するコレクション
        public ResettableObservableCollection<MovieRecords> FilteredMovieRecs { get; set; }

        // ERROR マーカー付き動画の専用一覧。
        public ObservableCollection<ThumbnailErrorRecordViewModel> ThumbnailErrorRecs { get; set; }

        // サムネ失敗タブ上部の進行状況サマリ。
        public ThumbnailErrorProgressViewState ThumbnailErrorProgress { get; }

        // MainDB書き込み前の仮表示（登録待ち）を保持するコレクション。
        public ObservableCollection<PendingMoviePlaceholder> PendingMovieRecs { get; set; }

        // 下部タブ「サムネイル進捗」の表示状態。
        public ThumbnailProgressViewState ThumbnailProgress { get; }

        // ブックマーク一覧、履歴一覧用のコレクション
        public ObservableCollection<MovieRecords> BookmarkRecs { get; set; }
        public ObservableCollection<History> HistoryRecs { get; set; }

        // 画面上部のソートドロップダウンに表示する選択肢のリスト
        public ObservableCollection<SortItem> SortLists { get; set; }

        /// <summary>
        /// 立ち上げの儀！空っぽの器（コレクション）たちを用意し、魅惑のメニューツリーやソート項目を一気に組み上げるぜ！🛠️
        /// </summary>
        public MainWindowViewModel()
        {
            DbInfo = new DBInfo();
            RecentTreeRoot = [];

            MovieRecs = [];
            FilteredMovieRecs = [];
            ThumbnailErrorRecs = [];
            ThumbnailErrorProgress = new ThumbnailErrorProgressViewState();
            PendingMovieRecs = [];
            ThumbnailProgress = new ThumbnailProgressViewState();
            BookmarkRecs = [];
            HistoryRecs = [];

            // UIスレッド外の無法地帯（別タスク）からコレクションをいじっても落ちないように、神の盾（ロック）を展開するぜ！🛡️
            BindingOperations.EnableCollectionSynchronization(MovieRecs, new object());
            BindingOperations.EnableCollectionSynchronization(FilteredMovieRecs, new object());
            BindingOperations.EnableCollectionSynchronization(ThumbnailErrorRecs, new object());
            BindingOperations.EnableCollectionSynchronization(PendingMovieRecs, new object());

            // ユーザーが選択可能なソート順の定義一覧
            SortLists =
            [
                new SortItem("0", "アクセス(新しい順)"),
                new SortItem("1", "アクセス(古い順)"),
                new SortItem("2", "ファイル(新しい順)"),
                new SortItem("3", "ファイル(古い順)"),
                //new SortItem("4", "スター数(多い順)"),    //tag内のスターを数えるのがかったるいので実装しない
                //new SortItem("5", "スター数(少ない順)"),  //tag内のスターを数えるのがかったるいので実装しない
                new SortItem("6", "スコア(高い順)"),
                new SortItem("7", "スコア(低い順)"),
                new SortItem("8", "再生数(多い順)"),
                new SortItem("9", "再生数(少ない順)"),
                new SortItem("10", "名前かな(昇順)"),
                new SortItem("11", "名前かな(降順)"),
                new SortItem("12", "ファイル名(昇順)"),
                new SortItem("13", "ファイル名(降順)"),
                new SortItem("14", "ファイルパス(昇順)"),
                new SortItem("15", "ファイルパス(降順)"),
                new SortItem("16", "サイズ(大きい順)"),
                new SortItem("17", "サイズ(小さい順)"),
                new SortItem("18", "登録(新しい順)"),
                new SortItem("19", "登録(古い順)"),
                new SortItem("20", "再生時間(長い順)"),
                new SortItem("21", "再生時間(短い順)"),
                new SortItem("22", "コメント1(昇順)"),
                new SortItem("23", "コメント1(降順)"),
                new SortItem("24", "コメント2(昇順)"),
                new SortItem("25", "コメント2(降順)"),
                new SortItem("26", "コメント3(昇順)"),
                new SortItem("27", "コメント3(降順)"),
                new SortItem("28", "エラー(多い順)"),
                //new SortList("28", "ランダム")            //ランダムソートもかったるいので実装しない。要るか？
            ];
        }

        /// <summary>
        /// 検索結果で表示用コレクションの中身を丸ごと総入れ替えする荒業！🧹
        /// XAML側のバインディング（FilteredMovieRecs）を一切壊さず、中身だけを最新にすり替えるスマートなヘルパーだぜ！✨
        /// </summary>
        public FilteredMovieRecsUpdateResult ReplaceFilteredMovieRecs(
            IEnumerable<MovieRecords> source,
            FilteredMovieRecsUpdateMode updateMode = FilteredMovieRecsUpdateMode.Diff
        )
        {
            List<MovieRecords> nextItems = source?.Where(movie => movie != null).ToList() ?? [];
            int currentCount = FilteredMovieRecs.Count;
            int nextCount = nextItems.Count;

            if (IsSameSequence(nextItems))
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: false,
                    RetainedPrefixCount: nextCount,
                    RetainedSuffixCount: 0,
                    RemovedCount: 0,
                    InsertedCount: 0,
                    MovedCount: 0
                );
            }

            if (updateMode == FilteredMovieRecsUpdateMode.Reset)
            {
                return ResetFilteredMovieRecs(nextItems);
            }

            int retainedPrefixCount = 0;
            while (
                retainedPrefixCount < currentCount
                && retainedPrefixCount < nextCount
                && ReferenceEquals(
                    FilteredMovieRecs[retainedPrefixCount],
                    nextItems[retainedPrefixCount]
                )
            )
            {
                retainedPrefixCount++;
            }

            int retainedSuffixCount = 0;
            while (
                retainedSuffixCount < currentCount - retainedPrefixCount
                && retainedSuffixCount < nextCount - retainedPrefixCount
                && ReferenceEquals(
                    FilteredMovieRecs[currentCount - 1 - retainedSuffixCount],
                    nextItems[nextCount - 1 - retainedSuffixCount]
                )
            )
            {
                retainedSuffixCount++;
            }

            if (
                updateMode == FilteredMovieRecsUpdateMode.Move
                && TryReorderFilteredMovieRecsWithMove(
                    nextItems,
                    out int movedCount,
                    out int moveUpdatedCount
                )
            )
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: movedCount > 0 || moveUpdatedCount > 0,
                    RetainedPrefixCount: retainedPrefixCount,
                    RetainedSuffixCount: retainedSuffixCount,
                    RemovedCount: 0,
                    InsertedCount: 0,
                    MovedCount: movedCount,
                    UpdatedCount: moveUpdatedCount
                );
            }

            int removeStartIndex = retainedPrefixCount;
            int removedCount = currentCount - retainedPrefixCount - retainedSuffixCount;
            int insertedCount = nextCount - retainedPrefixCount - retainedSuffixCount;
            int updatedCount;

            if (
                TryReplaceStableKeyUpdatesInPlace(
                    nextItems,
                    removeStartIndex,
                    removedCount,
                    insertedCount,
                    out updatedCount
                )
            )
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: updatedCount > 0,
                    RetainedPrefixCount: retainedPrefixCount,
                    RetainedSuffixCount: retainedSuffixCount,
                    RemovedCount: 0,
                    InsertedCount: 0,
                    MovedCount: 0,
                    UpdatedCount: updatedCount
                );
            }

            if (
                updateMode == FilteredMovieRecsUpdateMode.Diff
                && TryApplyStableKeyAnchoredSmallInsertOrRemove(
                    nextItems,
                    removeStartIndex,
                    removedCount,
                    insertedCount,
                    out updatedCount
                )
            )
            {
                return new FilteredMovieRecsUpdateResult(
                    HasChanges: true,
                    RetainedPrefixCount: retainedPrefixCount,
                    RetainedSuffixCount: retainedSuffixCount,
                    RemovedCount: Math.Max(removedCount - insertedCount, 0),
                    InsertedCount: Math.Max(insertedCount - removedCount, 0),
                    MovedCount: 0,
                    UpdatedCount: updatedCount
                );
            }

            for (int index = 0; index < removedCount; index++)
            {
                FilteredMovieRecs.RemoveAt(removeStartIndex);
            }

            for (int index = 0; index < insertedCount; index++)
            {
                FilteredMovieRecs.Insert(
                    removeStartIndex + index,
                    nextItems[removeStartIndex + index]
                );
            }

            return new FilteredMovieRecsUpdateResult(
                HasChanges: removedCount > 0 || insertedCount > 0,
                RetainedPrefixCount: retainedPrefixCount,
                RetainedSuffixCount: retainedSuffixCount,
                RemovedCount: removedCount,
                InsertedCount: insertedCount,
                MovedCount: 0,
                UpdatedCount: updatedCount
            );
        }

        /// <summary>
        /// 元データ一覧をまとめて差し替え、起動時の全件通知地獄を避ける。
        /// </summary>
        public void ReplaceMovieRecs(IEnumerable<MovieRecords> source)
        {
            MovieRecs.ReplaceAll(source?.Where(movie => movie != null) ?? []);
        }

        private bool IsSameSequence(IReadOnlyList<MovieRecords> nextItems)
        {
            if (nextItems == null || FilteredMovieRecs.Count != nextItems.Count)
            {
                return false;
            }

            for (int index = 0; index < nextItems.Count; index++)
            {
                if (!ReferenceEquals(FilteredMovieRecs[index], nextItems[index]))
                {
                    return false;
                }
            }

            return true;
        }

        // VirtualizingWrapPanel で崩れないよう、全件入れ直しへ戻す安全経路。
        private FilteredMovieRecsUpdateResult ResetFilteredMovieRecs(
            IReadOnlyList<MovieRecords> nextItems
        )
        {
            int removedCount = FilteredMovieRecs.Count;
            int insertedCount = nextItems?.Count ?? 0;
            FilteredMovieRecs.ReplaceAll(nextItems);

            return new FilteredMovieRecsUpdateResult(
                HasChanges: removedCount > 0 || insertedCount > 0,
                RetainedPrefixCount: 0,
                RetainedSuffixCount: 0,
                RemovedCount: removedCount,
                InsertedCount: insertedCount,
                MovedCount: 0
            );
        }

        // sort-only で要素集合が同じ時は、remove/insert ではなく Move だけで並び替える。
        private bool TryReorderFilteredMovieRecsWithMove(
            IReadOnlyList<MovieRecords> nextItems,
            out int movedCount,
            out int updatedCount
        )
        {
            movedCount = 0;
            updatedCount = 0;
            int count = FilteredMovieRecs.Count;
            if (count != nextItems.Count)
            {
                return false;
            }

            if (
                !HasUniqueMovieViewStableKeys(FilteredMovieRecs)
                || !HasUniqueMovieViewStableKeys(nextItems)
            )
            {
                return false;
            }

            Dictionary<string, int> indexByStableKey = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < count; index++)
            {
                MovieRecords currentItem = FilteredMovieRecs[index];
                if (!MovieViewStableKeyPolicy.TryResolve(currentItem, out string stableKey))
                {
                    return false;
                }

                indexByStableKey[stableKey] = index;
            }

            HashSet<string> nextStableKeys = new(StringComparer.OrdinalIgnoreCase);
            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                MovieRecords nextItem = nextItems[targetIndex];
                if (!MovieViewStableKeyPolicy.TryResolve(nextItem, out string stableKey))
                {
                    return false;
                }

                if (!nextStableKeys.Add(stableKey) || !indexByStableKey.ContainsKey(stableKey))
                {
                    return false;
                }
            }

            for (int targetIndex = 0; targetIndex < count; targetIndex++)
            {
                MovieRecords nextItem = nextItems[targetIndex];
                if (!MovieViewStableKeyPolicy.TryResolve(nextItem, out string stableKey))
                {
                    return false;
                }

                int currentIndex = indexByStableKey[stableKey];
                if (currentIndex == targetIndex)
                {
                    continue;
                }

                FilteredMovieRecs.Move(currentIndex, targetIndex);
                movedCount++;

                int rangeStart = Math.Min(targetIndex, currentIndex);
                int rangeEnd = Math.Max(targetIndex, currentIndex);
                for (int index = rangeStart; index <= rangeEnd; index++)
                {
                    if (!MovieViewStableKeyPolicy.TryResolve(
                            FilteredMovieRecs[index],
                            out stableKey
                        ))
                    {
                        return false;
                    }

                    indexByStableKey[stableKey] = index;
                }
            }

            // Move 後の位置で同じ key の新インスタンスだけを置き換える。
            for (int index = 0; index < count; index++)
            {
                if (!ReferenceEquals(FilteredMovieRecs[index], nextItems[index]))
                {
                    FilteredMovieRecs[index] = nextItems[index];
                    updatedCount++;
                }
            }

            return true;
        }

        // 差分区間の stable key を見て、ReadModel diff 上の update 件数を数える。
        private int CountStableKeyUpdates(
            IReadOnlyList<MovieRecords> nextItems,
            int startIndex,
            int removedCount,
            int insertedCount
        )
        {
            if (removedCount != insertedCount || removedCount < 1)
            {
                return 0;
            }

            int updatedCount = 0;
            for (int offset = 0; offset < removedCount; offset++)
            {
                MovieRecords currentItem = FilteredMovieRecs[startIndex + offset];
                MovieRecords nextItem = nextItems[startIndex + offset];
                if (
                    currentItem != null
                    && nextItem != null
                    && !ReferenceEquals(currentItem, nextItem)
                    && MovieViewStableKeyPolicy.AreSame(currentItem, nextItem)
                )
                {
                    updatedCount++;
                }
            }

            return updatedCount;
        }

        private bool TryReplaceStableKeyUpdatesInPlace(
            IReadOnlyList<MovieRecords> nextItems,
            int startIndex,
            int removedCount,
            int insertedCount,
            out int updatedCount
        )
        {
            if (removedCount != insertedCount || removedCount < 1)
            {
                updatedCount = 0;
                return false;
            }

            updatedCount = CountStableKeyUpdates(
                nextItems,
                startIndex,
                removedCount,
                insertedCount
            );
            if (updatedCount != removedCount)
            {
                return false;
            }

            if (
                !HasUniqueMovieViewStableKeys(FilteredMovieRecs)
                || !HasUniqueMovieViewStableKeys(nextItems)
            )
            {
                updatedCount = 0;
                return false;
            }

            // 同一 stable key だけの更新は、要素数を揺らさず中身だけ置き換える。
            for (int offset = 0; offset < updatedCount; offset++)
            {
                FilteredMovieRecs[startIndex + offset] = nextItems[startIndex + offset];
            }

            return true;
        }

        private bool TryApplyStableKeyAnchoredSmallInsertOrRemove(
            IReadOnlyList<MovieRecords> nextItems,
            int startIndex,
            int removedCount,
            int insertedCount,
            out int updatedCount
        )
        {
            updatedCount = 0;

            int spliceCount = Math.Abs(insertedCount - removedCount);
            if (
                removedCount < 1
                || insertedCount < 1
                || spliceCount < 1
                || spliceCount > MaxStableKeyAnchoredSpliceCount
            )
            {
                return false;
            }

            int matchedPrefixCount = 0;
            int comparableCount = Math.Min(removedCount, insertedCount);
            while (
                matchedPrefixCount < comparableCount
                && MovieViewStableKeyPolicy.AreSame(
                    FilteredMovieRecs[startIndex + matchedPrefixCount],
                    nextItems[startIndex + matchedPrefixCount]
                )
            )
            {
                matchedPrefixCount++;
            }

            int matchedSuffixCount = 0;
            while (
                matchedSuffixCount < removedCount - matchedPrefixCount
                && matchedSuffixCount < insertedCount - matchedPrefixCount
                && MovieViewStableKeyPolicy.AreSame(
                    FilteredMovieRecs[startIndex + removedCount - 1 - matchedSuffixCount],
                    nextItems[startIndex + insertedCount - 1 - matchedSuffixCount]
                )
            )
            {
                matchedSuffixCount++;
            }

            int matchedCount = matchedPrefixCount + matchedSuffixCount;
            if (insertedCount > removedCount)
            {
                if (matchedCount != removedCount)
                {
                    return false;
                }
            }
            else if (matchedCount != insertedCount)
            {
                return false;
            }

            if (
                !HasUniqueMovieViewStableKeys(FilteredMovieRecs)
                || !HasUniqueMovieViewStableKeys(nextItems)
            )
            {
                return false;
            }

            // 同一キー更新を先に置換してから、余った行だけを単一範囲で足し引きする。
            for (int offset = 0; offset < matchedPrefixCount; offset++)
            {
                if (
                    !ReferenceEquals(
                        FilteredMovieRecs[startIndex + offset],
                        nextItems[startIndex + offset]
                    )
                )
                {
                    FilteredMovieRecs[startIndex + offset] = nextItems[startIndex + offset];
                    updatedCount++;
                }
            }

            for (int offset = matchedSuffixCount - 1; offset >= 0; offset--)
            {
                int currentIndex = startIndex + removedCount - 1 - offset;
                int nextIndex = startIndex + insertedCount - 1 - offset;
                if (!ReferenceEquals(FilteredMovieRecs[currentIndex], nextItems[nextIndex]))
                {
                    FilteredMovieRecs[currentIndex] = nextItems[nextIndex];
                    updatedCount++;
                }
            }

            if (insertedCount > removedCount)
            {
                int insertStartIndex = startIndex + matchedPrefixCount;
                int nextInsertStartIndex = startIndex + matchedPrefixCount;
                for (int offset = 0; offset < spliceCount; offset++)
                {
                    FilteredMovieRecs.Insert(
                        insertStartIndex + offset,
                        nextItems[nextInsertStartIndex + offset]
                    );
                }
            }
            else
            {
                int removeStartIndex = startIndex + matchedPrefixCount;
                for (int offset = 0; offset < spliceCount; offset++)
                {
                    FilteredMovieRecs.RemoveAt(removeStartIndex);
                }
            }

            return true;
        }

        private static bool HasUniqueMovieViewStableKeys(IReadOnlyList<MovieRecords> items)
        {
            HashSet<string> stableKeys = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> moviePaths = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < items.Count; index++)
            {
                MovieRecords item = items[index];
                if (!MovieViewStableKeyPolicy.TryResolve(item, out string stableKey))
                {
                    return false;
                }

                if (!stableKeys.Add(stableKey))
                {
                    return false;
                }

                // ID stable key の行でも、同じ実パスが複数ある時は従来通り安全側へ戻す。
                string moviePath = item.Movie_Path ?? "";
                if (!string.IsNullOrWhiteSpace(moviePath) && !moviePaths.Add(moviePath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// ERROR 一覧もバインディングを壊さず中身だけ差し替える。
        /// </summary>
        public void ReplaceThumbnailErrorRecs(IEnumerable<ThumbnailErrorRecordViewModel> source)
        {
            ThumbnailErrorRecs.Clear();
            foreach (var movie in source)
            {
                ThumbnailErrorRecs.Add(movie);
            }
        }

        /// <summary>
        /// 検索キーワードという刃を振るって、膨大な獲物（コレクション）の中から条件に合う動画だけを容赦なく切り出す凄腕フィルター！⚔️
        /// </summary>
        public IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword
        )
        {
            return SearchService.FilterMovies(source, searchKeyword);
        }

        public IEnumerable<MovieRecords> FilterMovies(
            IEnumerable<MovieRecords> source,
            string searchKeyword,
            CancellationToken cancellationToken,
            bool allowExpensiveAsciiPhoneticFallback = true
        )
        {
            return SearchService.FilterMovies(
                source,
                searchKeyword,
                cancellationToken,
                allowExpensiveAsciiPhoneticFallback
            );
        }

        /// <summary>
        /// SortListsで定義された「ソートの掟（ID）」に従って、絞り込み結果を美しく整列させる神の采配だ！⚡
        /// </summary>
        public IEnumerable<MovieRecords> SortMovies(IEnumerable<MovieRecords> source, string sortId)
        {
            var query = source ?? Enumerable.Empty<MovieRecords>();
            return sortId switch
            {
                "0" => query.OrderByDescending(x => x.Last_Date),
                "1" => query.OrderBy(x => x.Last_Date),
                "2" => query.OrderByDescending(x => x.File_Date),
                "3" => query.OrderBy(x => x.File_Date),
                "6" => query.OrderByDescending(x => x.Score),
                "7" => query.OrderBy(x => x.Score),
                "8" => query.OrderByDescending(x => x.View_Count),
                "9" => query.OrderBy(x => x.View_Count),
                "10" => query.OrderBy(x => x.Kana),
                "11" => query.OrderByDescending(x => x.Kana),
                "12" => query.OrderBy(x => x.Movie_Name),
                "13" => query.OrderByDescending(x => x.Movie_Name),
                "14" => query.OrderBy(x => x.Movie_Path),
                "15" => query.OrderByDescending(x => x.Movie_Path),
                "16" => query.OrderByDescending(x => x.Movie_Size),
                "17" => query.OrderBy(x => x.Movie_Size),
                "18" => query.OrderByDescending(x => x.Regist_Date),
                "19" => query.OrderBy(x => x.Regist_Date),
                "20" => query.OrderByDescending(x => x.Movie_Length),
                "21" => query.OrderBy(x => x.Movie_Length),
                "22" => query.OrderBy(x => x.Comment1),
                "23" => query.OrderByDescending(x => x.Comment1),
                "24" => query.OrderBy(x => x.Comment2),
                "25" => query.OrderByDescending(x => x.Comment2),
                "26" => query.OrderBy(x => x.Comment3),
                "27" => query.OrderByDescending(x => x.Comment3),
                "28" => query
                    .OrderByDescending(ResolveThumbnailErrorSortCount)
                    .ThenBy(x => x.Movie_Name ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Movie_Path ?? "", StringComparer.CurrentCultureIgnoreCase),
                _ => query, // 万一未知のIDが来た場合はソートなしのまま返す
            };
        }

        // エラー順は、見えている placeholder と `. #ERROR.jpg` の両方をまとめて扱う。
        private static int ResolveThumbnailErrorSortCount(MovieRecords movie)
        {
            if (movie == null)
            {
                return 0;
            }

            return Math.Max(
                ThumbnailErrorPlaceholderHelper.CountPlaceholders(movie),
                movie.ThumbnailErrorMarkerCount
            );
        }

        // 表示用コンボボックスにバインドするための、ソート項目のキーバリュークラス
        public class SortItem(string id, string name)
        {
            public string Id { get; set; } = id;
            public string Name { get; set; } = name;
        }
    }
}

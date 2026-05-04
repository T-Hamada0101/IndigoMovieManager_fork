using System;
using System.Collections.Generic;
using System.Linq;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// 外部 skin API が DTO を組み立てるために必要な UI 状態を、1 回の読み取り結果として固定する。
    /// </summary>
    public sealed class WhiteBrowserSkinApiUiSnapshot
    {
        public static WhiteBrowserSkinApiUiSnapshot Empty { get; } = new();

        public WhiteBrowserSkinApiUiSnapshot(
            IReadOnlyList<MovieRecords> visibleMovies = null,
            int currentTabIndex = 2,
            string dbFullPath = "",
            string dbName = "",
            string skinName = "",
            string sortId = "",
            string sortName = "",
            string searchKeyword = "",
            int registeredMovieCount = 0,
            IReadOnlyList<string> filterTokens = null,
            string thumbFolder = "",
            MovieRecords selectedMovie = null,
            IReadOnlyList<MovieRecords> selectedMovies = null
        )
        {
            VisibleMovies = CopyMovies(visibleMovies);
            CurrentTabIndex = currentTabIndex;
            DbFullPath = dbFullPath ?? "";
            DbName = dbName ?? "";
            SkinName = skinName ?? "";
            SortId = sortId ?? "";
            SortName = sortName ?? "";
            SearchKeyword = searchKeyword ?? "";
            RegisteredMovieCount = Math.Max(0, registeredMovieCount);
            FilterTokens = CopyFilterTokens(filterTokens);
            ThumbFolder = thumbFolder ?? "";
            SelectedMovie = selectedMovie;
            SelectedMovies = CopyMovies(selectedMovies);
        }

        public IReadOnlyList<MovieRecords> VisibleMovies { get; }
        public int CurrentTabIndex { get; }
        public string DbFullPath { get; }
        public string DbName { get; }
        public string SkinName { get; }
        public string SortId { get; }
        public string SortName { get; }
        public string SearchKeyword { get; }
        public int RegisteredMovieCount { get; }
        public IReadOnlyList<string> FilterTokens { get; }
        public string ThumbFolder { get; }
        public MovieRecords SelectedMovie { get; }
        public IReadOnlyList<MovieRecords> SelectedMovies { get; }

        private static MovieRecords[] CopyMovies(IReadOnlyList<MovieRecords> movies)
        {
            return movies?.Where(movie => movie != null).ToArray() ?? Array.Empty<MovieRecords>();
        }

        private static string[] CopyFilterTokens(IReadOnlyList<string> filterTokens)
        {
            return filterTokens
                    ?.Where(token => !string.IsNullOrWhiteSpace(token))
                    .Select(token => token.Trim())
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
                ?? Array.Empty<string>();
        }
    }
}

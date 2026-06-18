using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using IndigoMovieManager.DB;

namespace IndigoMovieManager;

internal sealed class MovieViewReadModelRequest
{
    public int RequestRevision { get; init; }
    public string SortId { get; init; } = "";
    public string SearchKeyword { get; init; } = "";
    public string RouteLabel { get; init; } = "";
    public IReadOnlyList<MovieRecords> SourceMovies { get; init; } = [];
    public IReadOnlyList<MovieRecords> CurrentFilteredMovies { get; init; } = [];
    public IReadOnlyList<MainWindow.WatchChangedMovie> ChangedMovies { get; init; } = [];
    public bool UseChangedPathRefresh { get; init; }
    public bool AllowExpensiveAsciiPhoneticFallback { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public Func<
        IEnumerable<MovieRecords>,
        string,
        CancellationToken,
        bool,
        IEnumerable<MovieRecords>
    > FilterMovies { get; init; }
    public Func<IEnumerable<MovieRecords>, string, IEnumerable<MovieRecords>> SortMovies { get; init; }
    public Action<string> Log { get; init; }
}

internal sealed class MovieViewReadModelResult
{
    public static MovieViewReadModelResult FromSorted(
        IReadOnlyList<MovieRecords> sortedMovies,
        int searchCount,
        string fallbackReason = "none"
    )
    {
        return new MovieViewReadModelResult
        {
            SortedMovies = sortedMovies?.Where(movie => movie != null).ToArray() ?? [],
            SearchCount = searchCount,
            ChangedPathFallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
                ? "none"
                : fallbackReason,
        };
    }

    public IReadOnlyList<MovieRecords> SortedMovies { get; init; } = [];
    public int SearchCount { get; init; }
    public bool UsedChangedPathRefresh { get; init; }
    public bool CanReuseCurrentOrder { get; init; }
    public string ChangedPathFallbackReason { get; init; } = "none";
    public long FilterMoviesElapsedMs { get; init; }
    public long SortMoviesElapsedMs { get; init; }
}

internal static class MovieViewReadModelBuilder
{
    // UI から切り離した一覧 ReadModel 計算。MovieRecords は更新せず、呼び出し側が UI スレッドで適用する。
    public static MovieViewReadModelResult Build(MovieViewReadModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FilterMovies);
        ArgumentNullException.ThrowIfNull(request.SortMovies);

        CancellationToken cancellationToken = request.CancellationToken;
        MovieRecords[] sourceMovies = request.SourceMovies?.Where(movie => movie != null).ToArray() ?? [];
        MovieRecords[] currentFilteredMovies =
            request.CurrentFilteredMovies?.Where(movie => movie != null).ToArray() ?? [];
        string searchKeyword = request.SearchKeyword ?? "";
        string sortId = request.SortId ?? "";
        MovieRecords[] filtered = [];
        bool usedChangedPathRefresh = false;
        bool canReuseCurrentOrder = false;
        string changedPathFallbackReason = "none";
        long filterMoviesElapsedMs = 0;
        long sortMoviesElapsedMs = 0;

        cancellationToken.ThrowIfCancellationRequested();

        if (request.UseChangedPathRefresh)
        {
            usedChangedPathRefresh = TryBuildChangedMovieRefreshSourceWithReason(
                sourceMovies,
                currentFilteredMovies,
                searchKeyword,
                sortId,
                request.ChangedMovies,
                (movies, keyword) =>
                    request.FilterMovies(
                        movies,
                        keyword,
                        cancellationToken,
                        request.AllowExpensiveAsciiPhoneticFallback
                    ),
                out filtered,
                out canReuseCurrentOrder,
                out changedPathFallbackReason
            );
        }

        if (!usedChangedPathRefresh)
        {
            request.Log?.Invoke(
                $"filter stage begin: revision={request.RequestRevision} stage=filter-movies source={sourceMovies.Length} keyword='{searchKeyword}'"
            );
            Stopwatch filterMoviesStopwatch = Stopwatch.StartNew();
            filtered = request
                .FilterMovies(
                    sourceMovies,
                    searchKeyword,
                    cancellationToken,
                    request.AllowExpensiveAsciiPhoneticFallback
                )
                .Where(movie => movie != null)
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            filterMoviesStopwatch.Stop();
            filterMoviesElapsedMs = filterMoviesStopwatch.ElapsedMilliseconds;
            request.Log?.Invoke(
                $"filter stage end: revision={request.RequestRevision} stage=filter-movies filtered={filtered.Length} elapsed_ms={filterMoviesElapsedMs}"
            );
        }

        int searchCount = filtered.Length;
        MovieRecords[] sorted;
        if (canReuseCurrentOrder)
        {
            sorted = filtered;
        }
        else
        {
            request.Log?.Invoke(
                $"filter stage begin: revision={request.RequestRevision} stage=sort-movies filtered={searchCount} sort={sortId}"
            );
            Stopwatch sortMoviesStopwatch = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();
            sorted = request.SortMovies(filtered, sortId).Where(movie => movie != null).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            sortMoviesStopwatch.Stop();
            sortMoviesElapsedMs = sortMoviesStopwatch.ElapsedMilliseconds;
            request.Log?.Invoke(
                $"filter stage end: revision={request.RequestRevision} stage=sort-movies sorted={sorted.Length} elapsed_ms={sortMoviesElapsedMs}"
            );
        }

        return new MovieViewReadModelResult
        {
            SortedMovies = sorted,
            SearchCount = searchCount,
            UsedChangedPathRefresh = usedChangedPathRefresh,
            CanReuseCurrentOrder = canReuseCurrentOrder,
            ChangedPathFallbackReason = changedPathFallbackReason,
            FilterMoviesElapsedMs = filterMoviesElapsedMs,
            SortMoviesElapsedMs = sortMoviesElapsedMs,
        };
    }

    public static bool ShouldRunFilterSortOnBackground(int sourceCount)
    {
        return sourceCount >= 64;
    }

    public static bool ShouldUseFastAsciiSearchProjection(int sourceCount)
    {
        return sourceCount >= 64;
    }

    public static bool TryBuildChangedMovieRefreshSource(
        IEnumerable<MovieRecords> sourceMovies,
        IEnumerable<MovieRecords> currentFilteredMovies,
        string searchKeyword,
        string sortId,
        IEnumerable<MainWindow.WatchChangedMovie> changedMovies,
        Func<IEnumerable<MovieRecords>, string, IEnumerable<MovieRecords>> filterMovies,
        out MovieRecords[] nextFilteredMovies,
        out bool canReuseCurrentOrder
    )
    {
        return TryBuildChangedMovieRefreshSourceWithReason(
            sourceMovies,
            currentFilteredMovies,
            searchKeyword,
            sortId,
            changedMovies,
            filterMovies,
            out nextFilteredMovies,
            out canReuseCurrentOrder,
            out _
        );
    }

    public static bool TryBuildChangedMovieRefreshSourceWithReason(
        IEnumerable<MovieRecords> sourceMovies,
        IEnumerable<MovieRecords> currentFilteredMovies,
        string searchKeyword,
        string sortId,
        IEnumerable<MainWindow.WatchChangedMovie> changedMovies,
        Func<IEnumerable<MovieRecords>, string, IEnumerable<MovieRecords>> filterMovies,
        out MovieRecords[] nextFilteredMovies,
        out bool canReuseCurrentOrder,
        out string fallbackReason
    )
    {
        nextFilteredMovies = [];
        canReuseCurrentOrder = false;
        fallbackReason = "none";
        List<MainWindow.WatchChangedMovie> normalizedChangedMovies =
            MainWindow.MergeChangedMovies([], changedMovies);
        if (normalizedChangedMovies.Count < 1)
        {
            fallbackReason = "no-changed-movies";
            return false;
        }

        if (filterMovies == null)
        {
            fallbackReason = "filter-unavailable";
            return false;
        }

        // {dup} は集合全体の意味が変わるため、局所更新せず安全に全体再評価へ戻す。
        bool isDuplicateSearch = Infrastructure.SearchService.IsDuplicateSearchKeyword(searchKeyword);
        if (
            isDuplicateSearch
            && normalizedChangedMovies.Any(changedMovie =>
                (changedMovie.DirtyFields & MainWindow.WatchMovieDirtyFields.Hash)
                != MainWindow.WatchMovieDirtyFields.None
            )
        )
        {
            fallbackReason = "dup-hash-dirty";
            return false;
        }

        HashSet<string> changedPathLookup = BuildChangedMoviePathLookup(normalizedChangedMovies);
        Dictionary<string, MovieRecords> sourceByPath = BuildChangedSourceMovieLookup(
            sourceMovies,
            changedPathLookup
        );
        HashSet<string> currentFilteredPathLookup = new(
            currentFilteredMovies?
                .Where(movie =>
                    movie != null
                    && !string.IsNullOrWhiteSpace(movie.Movie_Path)
                    && changedPathLookup.Contains(movie.Movie_Path)
                )
                .Select(movie => movie.Movie_Path) ?? [],
            StringComparer.OrdinalIgnoreCase
        );
        List<MovieRecords> nextMovies = currentFilteredMovies?.Where(movie => movie != null).ToList() ?? [];

        bool canBypassFilterForEmptySearch = string.IsNullOrWhiteSpace(searchKeyword);
        bool shouldReapplySort = false;
        foreach (MainWindow.WatchChangedMovie changedMovie in normalizedChangedMovies)
        {
            string moviePath = changedMovie.MoviePath;
            if (!sourceByPath.TryGetValue(moviePath, out MovieRecords sourceMovie))
            {
                RemoveMovieByPath(nextMovies, moviePath);
                continue;
            }

            bool wasMatchedBefore = currentFilteredPathLookup.Contains(moviePath);
            bool canReuseCurrentSearchState =
                !canBypassFilterForEmptySearch
                && changedMovie.ChangeKind == MainWindow.WatchMovieChangeKind.None
                && !DoesSearchDependOnDirtyFields(searchKeyword, changedMovie.DirtyFields);

            bool isMatch =
                canBypassFilterForEmptySearch
                || (
                    canReuseCurrentSearchState
                        ? wasMatchedBefore
                        : filterMovies([sourceMovie], searchKeyword).Any()
                );
            if (isMatch)
            {
                if (!wasMatchedBefore)
                {
                    nextMovies.Add(sourceMovie);
                    shouldReapplySort = true;
                }
                else if (DoesCurrentSortDependOnDirtyFields(sortId, changedMovie.DirtyFields))
                {
                    shouldReapplySort = true;
                    ReplaceMovieByPath(nextMovies, sourceMovie);
                }
                else
                {
                    ReplaceMovieByPath(nextMovies, sourceMovie);
                }
            }
            else if (wasMatchedBefore)
            {
                RemoveMovieByPath(nextMovies, moviePath);
            }
        }

        nextFilteredMovies = nextMovies.ToArray();
        canReuseCurrentOrder = !shouldReapplySort;
        return true;
    }

    public static Dictionary<string, MovieRecords> BuildChangedSourceMovieLookup(
        IEnumerable<MovieRecords> sourceMovies,
        IEnumerable<MainWindow.WatchChangedMovie> changedMovies
    )
    {
        return BuildChangedSourceMovieLookup(sourceMovies, BuildChangedMoviePathLookup(changedMovies));
    }

    public static bool DoesSearchDependOnDirtyFields(
        string searchKeyword,
        MainWindow.WatchMovieDirtyFields dirtyFields
    )
    {
        if (
            dirtyFields == MainWindow.WatchMovieDirtyFields.None
            || string.IsNullOrWhiteSpace(searchKeyword)
        )
        {
            return false;
        }

        if (Infrastructure.SearchService.IsDuplicateSearchKeyword(searchKeyword))
        {
            return (dirtyFields & MainWindow.WatchMovieDirtyFields.Hash)
                != MainWindow.WatchMovieDirtyFields.None;
        }

        if (Infrastructure.SearchService.IsTagOnlySearchKeyword(searchKeyword))
        {
            return false;
        }

        MainWindow.WatchMovieDirtyFields searchRelevantFields =
            MainWindow.WatchMovieDirtyFields.MovieName
            | MainWindow.WatchMovieDirtyFields.MoviePath
            | MainWindow.WatchMovieDirtyFields.Kana
            | MainWindow.WatchMovieDirtyFields.Comment1
            | MainWindow.WatchMovieDirtyFields.Comment2
            | MainWindow.WatchMovieDirtyFields.Comment3;
        return (dirtyFields & searchRelevantFields) != MainWindow.WatchMovieDirtyFields.None;
    }

    public static bool DoesCurrentSortDependOnDirtyFields(
        string sortId,
        MainWindow.WatchMovieDirtyFields dirtyFields
    )
    {
        if (dirtyFields == MainWindow.WatchMovieDirtyFields.None)
        {
            return false;
        }

        MainWindow.WatchMovieDirtyFields relevantFields = sortId switch
        {
            "0" or "1" => MainWindow.WatchMovieDirtyFields.LastDate,
            "2" or "3" => MainWindow.WatchMovieDirtyFields.FileDate,
            "6" or "7" => MainWindow.WatchMovieDirtyFields.Score,
            "8" or "9" => MainWindow.WatchMovieDirtyFields.ViewCount,
            "10" or "11" => MainWindow.WatchMovieDirtyFields.Kana,
            "12" or "13" => MainWindow.WatchMovieDirtyFields.MovieName,
            "14" or "15" => MainWindow.WatchMovieDirtyFields.MoviePath,
            "16" or "17" => MainWindow.WatchMovieDirtyFields.MovieSize,
            "18" or "19" => MainWindow.WatchMovieDirtyFields.RegistDate,
            "20" or "21" => MainWindow.WatchMovieDirtyFields.MovieLength,
            "22" or "23" => MainWindow.WatchMovieDirtyFields.Comment1,
            "24" or "25" => MainWindow.WatchMovieDirtyFields.Comment2,
            "26" or "27" => MainWindow.WatchMovieDirtyFields.Comment3,
            "28" => MainWindow.WatchMovieDirtyFields.ThumbnailError
                | MainWindow.WatchMovieDirtyFields.MovieName
                | MainWindow.WatchMovieDirtyFields.MoviePath,
            _ => MainWindow.WatchMovieDirtyFields.None,
        };

        return (dirtyFields & relevantFields) != MainWindow.WatchMovieDirtyFields.None;
    }

    private static HashSet<string> BuildChangedMoviePathLookup(
        IEnumerable<MainWindow.WatchChangedMovie> changedMovies
    )
    {
        return new HashSet<string>(
            changedMovies?
                .Where(changedMovie => !string.IsNullOrWhiteSpace(changedMovie.MoviePath))
                .Select(changedMovie => changedMovie.MoviePath) ?? [],
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static Dictionary<string, MovieRecords> BuildChangedSourceMovieLookup(
        IEnumerable<MovieRecords> sourceMovies,
        HashSet<string> changedPathLookup
    )
    {
        Dictionary<string, MovieRecords> sourceByPath = new(StringComparer.OrdinalIgnoreCase);
        if (changedPathLookup.Count < 1 || sourceMovies == null)
        {
            return sourceByPath;
        }

        foreach (MovieRecords movie in sourceMovies)
        {
            if (movie == null || string.IsNullOrWhiteSpace(movie.Movie_Path))
            {
                continue;
            }

            if (!changedPathLookup.Contains(movie.Movie_Path))
            {
                continue;
            }

            // 同一 path が複数ある場合は、従来どおり後勝ちで表示正本へ寄せる。
            sourceByPath[movie.Movie_Path] = movie;
        }

        return sourceByPath;
    }

    private static void ReplaceMovieByPath(List<MovieRecords> movies, MovieRecords sourceMovie)
    {
        if (movies == null || sourceMovie == null || string.IsNullOrWhiteSpace(sourceMovie.Movie_Path))
        {
            return;
        }

        int index = movies.FindIndex(movie =>
            movie != null
            && string.Equals(
                movie.Movie_Path,
                sourceMovie.Movie_Path,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (index >= 0)
        {
            movies[index] = sourceMovie;
        }
    }

    private static void RemoveMovieByPath(List<MovieRecords> movies, string moviePath)
    {
        if (movies == null || string.IsNullOrWhiteSpace(moviePath))
        {
            return;
        }

        movies.RemoveAll(movie =>
            movie != null
            && string.Equals(movie.Movie_Path, moviePath, StringComparison.OrdinalIgnoreCase)
        );
    }

}

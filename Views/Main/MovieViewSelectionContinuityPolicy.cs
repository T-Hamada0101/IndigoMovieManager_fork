using System;
using System.Collections.Generic;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager
{
    internal static class MovieViewSelectionContinuityPolicy
    {
        internal static bool TryCaptureStableKey(
            MovieRecords selectedMovie,
            out string stableKey
        )
        {
            return MovieViewStableKeyPolicy.TryResolve(selectedMovie, out stableKey);
        }

        internal static IReadOnlyList<string> CaptureStableKeys(
            MovieRecords primarySelectedMovie,
            IEnumerable<MovieRecords> selectedMovies
        )
        {
            var stableKeys = new List<string>();
            var capturedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 主選択を先頭に固定し、選択一覧との重複を除きながら選択順を保つ。
            CaptureStableKey(primarySelectedMovie, stableKeys, capturedKeys);
            if (selectedMovies == null)
            {
                return stableKeys;
            }

            foreach (MovieRecords selectedMovie in selectedMovies)
            {
                CaptureStableKey(selectedMovie, stableKeys, capturedKeys);
            }

            return stableKeys;
        }

        internal static MovieRecords ResolveAfterCollectionApply(
            string stableKey,
            IEnumerable<MovieRecords> currentMovies,
            FilteredMovieRecsUpdateMode updateMode,
            bool hasCollectionChanges
        )
        {
            if (
                updateMode != FilteredMovieRecsUpdateMode.Reset
                || !hasCollectionChanges
                || string.IsNullOrWhiteSpace(stableKey)
                || currentMovies == null
            )
            {
                return null;
            }

            // Reset 後の現行実体だけを返し、古い選択インスタンスを UI へ戻さない。
            foreach (MovieRecords currentMovie in currentMovies)
            {
                if (
                    MovieViewStableKeyPolicy.TryResolve(currentMovie, out string currentStableKey)
                    && MovieViewStableKeyPolicy.AreSame(stableKey, currentStableKey)
                )
                {
                    return currentMovie;
                }
            }

            return null;
        }

        internal static IReadOnlyList<MovieRecords> ResolveManyAfterCollectionApply(
            IEnumerable<string> stableKeys,
            IEnumerable<MovieRecords> currentMovies,
            FilteredMovieRecsUpdateMode updateMode,
            bool hasCollectionChanges
        )
        {
            if (
                updateMode != FilteredMovieRecsUpdateMode.Reset
                || !hasCollectionChanges
                || stableKeys == null
                || currentMovies == null
            )
            {
                return Array.Empty<MovieRecords>();
            }

            var requestedStableKeys = new List<string>();
            var requestedKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string stableKey in stableKeys)
            {
                if (
                    !string.IsNullOrWhiteSpace(stableKey)
                    && requestedKeySet.Add(stableKey)
                )
                {
                    requestedStableKeys.Add(stableKey);
                }
            }

            if (requestedStableKeys.Count == 0)
            {
                return Array.Empty<MovieRecords>();
            }

            var currentByStableKey = new Dictionary<string, MovieRecords>(
                StringComparer.OrdinalIgnoreCase
            );
            var ambiguousKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 現行一覧を一度だけ走査し、重複した key は安全に復元できないため候補から外す。
            foreach (MovieRecords currentMovie in currentMovies)
            {
                if (
                    !MovieViewStableKeyPolicy.TryResolve(currentMovie, out string currentStableKey)
                    || !requestedKeySet.Contains(currentStableKey)
                    || ambiguousKeys.Contains(currentStableKey)
                )
                {
                    continue;
                }

                if (!currentByStableKey.TryAdd(currentStableKey, currentMovie))
                {
                    currentByStableKey.Remove(currentStableKey);
                    ambiguousKeys.Add(currentStableKey);
                }
            }

            var resolvedMovies = new List<MovieRecords>();
            foreach (string stableKey in requestedStableKeys)
            {
                if (currentByStableKey.TryGetValue(stableKey, out MovieRecords currentMovie))
                {
                    resolvedMovies.Add(currentMovie);
                }
            }

            return resolvedMovies;
        }

        private static void CaptureStableKey(
            MovieRecords movie,
            ICollection<string> stableKeys,
            ISet<string> capturedKeys
        )
        {
            if (
                MovieViewStableKeyPolicy.TryResolve(movie, out string stableKey)
                && capturedKeys.Add(stableKey)
            )
            {
                stableKeys.Add(stableKey);
            }
        }
    }
}

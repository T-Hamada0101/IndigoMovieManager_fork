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
    }
}

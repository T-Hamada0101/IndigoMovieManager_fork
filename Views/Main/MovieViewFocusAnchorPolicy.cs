using System.Collections.Generic;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager;

internal readonly record struct MovieViewFocusAnchor(string StableKey);

internal static class MovieViewFocusAnchorPolicy
{
    internal static bool TryCapture(
        MovieRecords focusedMovie,
        out MovieViewFocusAnchor anchor
    )
    {
        anchor = default;
        if (!MovieViewStableKeyPolicy.TryResolve(focusedMovie, out string stableKey))
        {
            return false;
        }

        anchor = new MovieViewFocusAnchor(stableKey);
        return true;
    }

    internal static MovieRecords ResolveAfterCollectionApply(
        MovieViewFocusAnchor anchor,
        IEnumerable<MovieRecords> currentMovies,
        FilteredMovieRecsUpdateMode updateMode,
        bool hasChanges
    )
    {
        if (
            updateMode != FilteredMovieRecsUpdateMode.Reset
            || !hasChanges
            || string.IsNullOrWhiteSpace(anchor.StableKey)
            || currentMovies == null
        )
        {
            return null;
        }

        MovieRecords resolvedMovie = null;

        // Reset 後の現行一覧で一意に特定できる時だけ、focus 復元へ渡す。
        foreach (MovieRecords currentMovie in currentMovies)
        {
            if (
                !MovieViewStableKeyPolicy.TryResolve(currentMovie, out string currentStableKey)
                || !MovieViewStableKeyPolicy.AreSame(anchor.StableKey, currentStableKey)
            )
            {
                continue;
            }

            if (resolvedMovie != null)
            {
                return null;
            }

            resolvedMovie = currentMovie;
        }

        return resolvedMovie;
    }
}

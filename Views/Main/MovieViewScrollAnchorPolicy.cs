using System.Collections.Generic;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager;

internal readonly record struct MovieViewScrollAnchor(string StableKey, double TopOffset);

internal static class MovieViewScrollAnchorPolicy
{
    internal static bool TryCapture(
        MovieRecords firstVisibleMovie,
        double topOffset,
        out MovieViewScrollAnchor anchor
    )
    {
        anchor = default;
        if (
            !double.IsFinite(topOffset)
            || !MovieViewStableKeyPolicy.TryResolve(firstVisibleMovie, out string stableKey)
        )
        {
            return false;
        }

        // 負の位置は viewport 上端で部分表示されている状態として、そのまま保持する。
        anchor = new MovieViewScrollAnchor(stableKey, topOffset);
        return true;
    }

    internal static MovieRecords ResolveAfterCollectionApply(
        MovieViewScrollAnchor anchor,
        IEnumerable<MovieRecords> currentMovies,
        FilteredMovieRecsUpdateMode updateMode,
        bool hasChanges
    )
    {
        if (
            updateMode != FilteredMovieRecsUpdateMode.Reset
            || !hasChanges
            || string.IsNullOrWhiteSpace(anchor.StableKey)
            || !double.IsFinite(anchor.TopOffset)
            || currentMovies == null
        )
        {
            return null;
        }

        MovieRecords resolvedMovie = null;

        // Reset 後の現行実体を一意に特定できる時だけ、スクロール復元へ渡す。
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

    internal static double CalculateRestoredVerticalOffset(
        double currentVerticalOffset,
        double currentContainerTop,
        double anchorTop
    )
    {
        double maintainedOffset = double.IsFinite(currentVerticalOffset)
            ? Math.Max(0, currentVerticalOffset)
            : 0;

        if (!double.IsFinite(currentContainerTop) || !double.IsFinite(anchorTop))
        {
            return maintainedOffset;
        }

        double restoredOffset = currentVerticalOffset + currentContainerTop - anchorTop;
        return double.IsFinite(restoredOffset) ? Math.Max(0, restoredOffset) : maintainedOffset;
    }
}

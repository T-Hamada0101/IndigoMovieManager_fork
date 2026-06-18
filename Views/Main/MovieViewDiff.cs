using System;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager;

internal enum MovieViewDiffOperation
{
    NoChange = 0,
    Reset = 1,
    Diff = 2,
    Move = 3,
}

internal enum MovieViewSelectionImpact
{
    Preserve = 0,
    Refresh = 1,
}

internal enum MovieViewScrollImpact
{
    Preserve = 0,
    Recalculate = 1,
    Reset = 2,
}

// ReadModel の UI 反映結果を、次の Diff-first 境界へ渡せる小さな語彙へ畳む。
internal readonly record struct MovieViewDiff(
    string StableKey,
    int SourceRevision,
    int ViewRevision,
    MovieViewDiffOperation Operation,
    MovieViewSelectionImpact SelectionImpact,
    MovieViewScrollImpact ScrollImpact,
    string FallbackReason
)
{
    internal string OperationLogValue => Operation switch
    {
        MovieViewDiffOperation.NoChange => "no-change",
        MovieViewDiffOperation.Reset => "reset",
        MovieViewDiffOperation.Diff => "diff",
        MovieViewDiffOperation.Move => "move",
        _ => "unknown",
    };

    internal string SelectionImpactLogValue => SelectionImpact switch
    {
        MovieViewSelectionImpact.Preserve => "preserve",
        MovieViewSelectionImpact.Refresh => "refresh",
        _ => "unknown",
    };

    internal string ScrollImpactLogValue => ScrollImpact switch
    {
        MovieViewScrollImpact.Preserve => "preserve",
        MovieViewScrollImpact.Recalculate => "recalculate",
        MovieViewScrollImpact.Reset => "reset",
        _ => "unknown",
    };
}

internal static class MovieViewDiffFactory
{
    internal const string StableKeyMoviePath = "movie-path";
    internal const string FallbackReasonNone = "none";

    internal static MovieViewDiff FromCollectionUpdate(
        int sourceRevision,
        int viewRevision,
        FilteredMovieRecsUpdateMode updateMode,
        FilteredMovieRecsUpdateResult updateResult,
        bool selectionRefreshApplied,
        string fallbackReason
    )
    {
        MovieViewDiffOperation operation = ResolveOperation(updateMode, updateResult);
        return new MovieViewDiff(
            StableKeyMoviePath,
            sourceRevision,
            viewRevision,
            operation,
            selectionRefreshApplied
                ? MovieViewSelectionImpact.Refresh
                : MovieViewSelectionImpact.Preserve,
            ResolveScrollImpact(operation),
            NormalizeFallbackReason(fallbackReason)
        );
    }

    private static MovieViewDiffOperation ResolveOperation(
        FilteredMovieRecsUpdateMode updateMode,
        FilteredMovieRecsUpdateResult updateResult
    )
    {
        if (!updateResult.HasChanges)
        {
            return MovieViewDiffOperation.NoChange;
        }

        if (updateMode == FilteredMovieRecsUpdateMode.Reset)
        {
            return MovieViewDiffOperation.Reset;
        }

        if (
            updateMode == FilteredMovieRecsUpdateMode.Move
            && updateResult.MovedCount > 0
            && updateResult.RemovedCount == 0
            && updateResult.InsertedCount == 0
        )
        {
            return MovieViewDiffOperation.Move;
        }

        return MovieViewDiffOperation.Diff;
    }

    private static MovieViewScrollImpact ResolveScrollImpact(MovieViewDiffOperation operation)
    {
        return operation switch
        {
            MovieViewDiffOperation.NoChange => MovieViewScrollImpact.Preserve,
            MovieViewDiffOperation.Reset => MovieViewScrollImpact.Reset,
            _ => MovieViewScrollImpact.Recalculate,
        };
    }

    private static string NormalizeFallbackReason(string fallbackReason)
    {
        return string.IsNullOrWhiteSpace(fallbackReason)
            ? FallbackReasonNone
            : fallbackReason.Trim();
    }
}

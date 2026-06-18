using System;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager;

internal enum MovieViewDiffOperation
{
    NoChange = 0,
    Add = 1,
    Delete = 2,
    Update = 3,
    Move = 4,
    FullFallback = 5,
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
    string FallbackReason,
    int AddedCount,
    int DeletedCount,
    int UpdatedCount,
    int MovedCount
)
{
    internal string OperationLogValue => Operation switch
    {
        MovieViewDiffOperation.NoChange => "no-change",
        MovieViewDiffOperation.Add => "add",
        MovieViewDiffOperation.Delete => "delete",
        MovieViewDiffOperation.Update => "update",
        MovieViewDiffOperation.Move => "move",
        MovieViewDiffOperation.FullFallback => "full-fallback",
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
    internal const string FallbackReasonQuery = "query";
    internal const string FallbackReasonSort = "sort";
    internal const string FallbackReasonDbSwitch = "db-switch";
    internal const string FallbackReasonUnsafe = "unsafe";
    internal const string FallbackReasonMassive = "massive";

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
            NormalizeFallbackReason(fallbackReason),
            updateResult.InsertedCount,
            updateResult.RemovedCount,
            updateResult.UpdatedCount,
            updateResult.MovedCount
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
            return MovieViewDiffOperation.FullFallback;
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

        if (updateResult.UpdatedCount > 0)
        {
            return MovieViewDiffOperation.Update;
        }

        if (updateResult.InsertedCount > 0 && updateResult.RemovedCount == 0)
        {
            return MovieViewDiffOperation.Add;
        }

        if (updateResult.RemovedCount > 0 && updateResult.InsertedCount == 0)
        {
            return MovieViewDiffOperation.Delete;
        }

        return MovieViewDiffOperation.Update;
    }

    private static MovieViewScrollImpact ResolveScrollImpact(MovieViewDiffOperation operation)
    {
        return operation switch
        {
            MovieViewDiffOperation.NoChange => MovieViewScrollImpact.Preserve,
            MovieViewDiffOperation.FullFallback => MovieViewScrollImpact.Reset,
            _ => MovieViewScrollImpact.Recalculate,
        };
    }

    private static string NormalizeFallbackReason(string fallbackReason)
    {
        if (string.IsNullOrWhiteSpace(fallbackReason))
        {
            return FallbackReasonNone;
        }

        string reason = fallbackReason.Trim();
        if (string.Equals(reason, FallbackReasonNone, StringComparison.OrdinalIgnoreCase))
        {
            return FallbackReasonNone;
        }

        if (reason.Contains("sort", StringComparison.OrdinalIgnoreCase))
        {
            return FallbackReasonSort;
        }

        if (reason.Contains("db", StringComparison.OrdinalIgnoreCase))
        {
            return FallbackReasonDbSwitch;
        }

        if (reason.Contains("unsafe", StringComparison.OrdinalIgnoreCase))
        {
            return FallbackReasonUnsafe;
        }

        if (
            reason.Contains("massive", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("bulk", StringComparison.OrdinalIgnoreCase)
        )
        {
            return FallbackReasonMassive;
        }

        return FallbackReasonQuery;
    }
}

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

internal enum MovieViewDiffApplyKind
{
    DiffApply = 0,
    FullFallback = 1,
}

internal readonly record struct MovieViewDiffApplyPlan(
    MovieViewDiffApplyKind ApplyKind,
    string FullFallbackReason
)
{
    internal bool IsDiffApplyCandidate => ApplyKind == MovieViewDiffApplyKind.DiffApply;

    internal string ApplyKindLogValue => ApplyKind switch
    {
        MovieViewDiffApplyKind.DiffApply => "diff-apply",
        MovieViewDiffApplyKind.FullFallback => "full-fallback",
        _ => "unknown",
    };
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
    internal MovieViewDiffApplyPlan ApplyPlan => MovieViewDiffApplyPolicy.Resolve(this);

    internal bool IsDiffApplyCandidate => ApplyPlan.IsDiffApplyCandidate;

    internal string ApplyKindLogValue => ApplyPlan.ApplyKindLogValue;

    internal string FullFallbackReason => ApplyPlan.FullFallbackReason;

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
    internal const string FallbackReasonNone = MovieViewDiffApplyPolicy.FallbackReasonNone;
    internal const string FallbackReasonQuery = MovieViewDiffApplyPolicy.FallbackReasonQuery;
    internal const string FallbackReasonSort = MovieViewDiffApplyPolicy.FallbackReasonSort;
    internal const string FallbackReasonDbSwitch = MovieViewDiffApplyPolicy.FallbackReasonDbSwitch;
    internal const string FallbackReasonUnsafe = MovieViewDiffApplyPolicy.FallbackReasonUnsafe;
    internal const string FallbackReasonMassive = MovieViewDiffApplyPolicy.FallbackReasonMassive;

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
        return MovieViewDiffApplyPolicy.NormalizeFallbackReason(fallbackReason);
    }
}

internal static class MovieViewDiffApplyPolicy
{
    internal const string FallbackReasonNone = "none";
    internal const string FallbackReasonQuery = "query";
    internal const string FallbackReasonSort = "sort";
    internal const string FallbackReasonDbSwitch = "db-switch";
    internal const string FallbackReasonUnsafe = "unsafe";
    internal const string FallbackReasonMassive = "massive";

    internal static MovieViewDiffApplyPlan Resolve(MovieViewDiff diff)
    {
        string fullFallbackReason = ResolveFullFallbackReason(diff.FallbackReason);
        bool shouldUseFullFallback =
            diff.Operation == MovieViewDiffOperation.FullFallback
            || !string.Equals(
                fullFallbackReason,
                FallbackReasonNone,
                StringComparison.Ordinal
            );

        return new MovieViewDiffApplyPlan(
            shouldUseFullFallback
                ? MovieViewDiffApplyKind.FullFallback
                : MovieViewDiffApplyKind.DiffApply,
            fullFallbackReason
        );
    }

    internal static string NormalizeFallbackReason(string fallbackReason)
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

        if (
            reason.Contains("sort", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("order", StringComparison.OrdinalIgnoreCase)
        )
        {
            return FallbackReasonSort;
        }

        if (reason.Contains("db", StringComparison.OrdinalIgnoreCase))
        {
            return FallbackReasonDbSwitch;
        }

        if (
            reason.Contains("unsafe", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("dup", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("hash", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("filter-unavailable", StringComparison.OrdinalIgnoreCase)
        )
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

        if (
            reason.Contains("query", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("search", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("is-get-new", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("startup", StringComparison.OrdinalIgnoreCase)
        )
        {
            return FallbackReasonQuery;
        }

        // changed-path などの小変更由来の札は full fallback 理由に昇格させない。
        return FallbackReasonNone;
    }

    internal static bool IsFullFallbackReason(string fallbackReason)
    {
        string normalizedReason = NormalizeFallbackReason(fallbackReason);
        return !string.Equals(
            ResolveFullFallbackReason(normalizedReason),
            FallbackReasonNone,
            StringComparison.Ordinal
        );
    }

    private static string ResolveFullFallbackReason(string fallbackReason)
    {
        string normalizedReason = NormalizeFallbackReason(fallbackReason);
        return normalizedReason switch
        {
            FallbackReasonQuery => FallbackReasonQuery,
            FallbackReasonSort => FallbackReasonSort,
            FallbackReasonDbSwitch => FallbackReasonDbSwitch,
            FallbackReasonUnsafe => FallbackReasonUnsafe,
            FallbackReasonMassive => FallbackReasonMassive,
            _ => FallbackReasonNone,
        };
    }
}

namespace IndigoMovieManager
{
    internal readonly record struct SortComboSelectionPlan(
        bool ShouldHandle,
        string SortId,
        bool ShouldUseStartupFullReload,
        bool ShouldRefreshThumbnailErrorRecords
    );

    internal static class SortComboSelectionPolicy
    {
        internal static SortComboSelectionPlan BuildPlan(
            string dbFullPath,
            bool isSelectionChangeSuppressed,
            int movieCount,
            string selectedSortId,
            bool isStartupFeedPartialActive
        )
        {
            // UI 側は選択値の取り出しだけを行い、sort 経路の判定はここへ寄せる。
            if (string.IsNullOrEmpty(dbFullPath))
            {
                return default;
            }

            if (isSelectionChangeSuppressed)
            {
                return default;
            }

            if (movieCount <= 0)
            {
                return default;
            }

            if (selectedSortId == null)
            {
                return default;
            }

            return new SortComboSelectionPlan(
                ShouldHandle: true,
                SortId: selectedSortId,
                ShouldUseStartupFullReload: isStartupFeedPartialActive,
                ShouldRefreshThumbnailErrorRecords: selectedSortId == "28"
            );
        }
    }
}

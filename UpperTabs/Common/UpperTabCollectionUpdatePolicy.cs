using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの一覧更新方式を決める最小ポリシー。
    /// VirtualizingWrapPanel は差分通知で不安定なため、当面は縦並びの List(DataGrid) だけ Diff/Move を許可する。
    /// </summary>
    public static class UpperTabCollectionUpdatePolicy
    {
        private const int ListTabIndex = 3;

        public static FilteredMovieRecsUpdateMode ResolveUpdateMode(
            int? tabIndex,
            bool isSortOnly
        )
        {
            // 縦並びの List だけ差分通知を許可し、WrapPanel 系は安全に作り直す。
            if (tabIndex == ListTabIndex)
            {
                return isSortOnly
                    ? FilteredMovieRecsUpdateMode.Move
                    : FilteredMovieRecsUpdateMode.Diff;
            }

            return FilteredMovieRecsUpdateMode.Reset;
        }

        // 縦並びの List は Diff/Move 通知を素直に扱えるので、
        // ここでは一覧全体 Refresh を省いて差分反映を主経路にする。
        public static bool ShouldRefreshAfterCollectionApply(
            int? tabIndex,
            FilteredMovieRecsUpdateMode updateMode
        )
        {
            bool isStableLinearTab = tabIndex == ListTabIndex;
            return !(isStableLinearTab && updateMode != FilteredMovieRecsUpdateMode.Reset);
        }
    }
}

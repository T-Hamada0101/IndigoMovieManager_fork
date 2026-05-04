using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの一覧更新方式を決める最小ポリシー。
    /// VirtualizingWrapPanel は差分通知で不安定なため、当面は縦並びで安定するタブだけ Diff/Move を許可する。
    /// </summary>
    public static class UpperTabCollectionUpdatePolicy
    {
        private const int ListTabIndex = 3;
        private const int PlayerTabIndex = 7;

        public static FilteredMovieRecsUpdateMode ResolveUpdateMode(
            int? tabIndex,
            bool isSortOnly
        )
        {
            // 縦並びで差分通知を素直に扱えるタブだけを軽量更新へ流す。
            if (IsStableLinearTab(tabIndex))
            {
                return isSortOnly
                    ? FilteredMovieRecsUpdateMode.Move
                    : FilteredMovieRecsUpdateMode.Diff;
            }

            return FilteredMovieRecsUpdateMode.Reset;
        }

        // 縦並びで安定するタブは Diff/Move 通知を素直に扱えるので、
        // ここでは一覧全体 Refresh を省いて差分反映を主経路にする。
        public static bool ShouldRefreshAfterCollectionApply(
            int? tabIndex,
            FilteredMovieRecsUpdateMode updateMode
        )
        {
            bool isStableLinearTab = IsStableLinearTab(tabIndex);
            return !(isStableLinearTab && updateMode != FilteredMovieRecsUpdateMode.Reset);
        }

        private static bool IsStableLinearTab(int? tabIndex)
        {
            // Player 右レールも Grid/List と同じ線形表示なので、Reset 固定から外す。
            return tabIndex == ListTabIndex || tabIndex == PlayerTabIndex;
        }
    }
}

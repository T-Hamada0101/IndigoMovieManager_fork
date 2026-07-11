namespace IndigoMovieManager;

internal static class UiOperationFeedbackPolicy
{
    internal const int DelayMs = 250;

    // 操作理由だけを表示文言へ変換し、UI固有の状態は持ち込まない。
    internal static string ResolveStatusText(string reason)
    {
        if (string.Equals(reason, "search", StringComparison.OrdinalIgnoreCase))
        {
            return "検索中";
        }

        if (string.Equals(reason, "sort", StringComparison.OrdinalIgnoreCase))
        {
            return "並び替え中";
        }

        if (string.Equals(reason, "player", StringComparison.OrdinalIgnoreCase))
        {
            return "Player準備中";
        }

        return "処理中";
    }

    // 終了時にcurrentRevisionを進めることで、待機中の古いtickを表示前に失効させる。
    internal static bool ShouldShow(
        long delayedRequestRevision,
        long currentRevision,
        bool isUserPriorityActive
    )
    {
        return isUserPriorityActive && delayedRequestRevision == currentRevision;
    }
}

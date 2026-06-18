namespace IndigoMovieManager
{
    internal static class RegisteredMovieCountRefreshPolicy
    {
        internal static bool ShouldApplyRefreshResult(
            int requestRevision,
            int currentRevision,
            bool isCurrentDb
        )
        {
            // 後着要求とDB切替を同じ入口で弾き、古い件数をヘッダーへ戻さない。
            return requestRevision == currentRevision && isCurrentDb;
        }
    }
}

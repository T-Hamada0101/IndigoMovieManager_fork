namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// StandardFileSystem バックエンドを IFileIndexProvider 契約へ接続する。
    /// 既存の安全なファイル列挙ベースを明示的な選択肢として分離する。
    /// </summary>
    internal sealed class StandardFileSystemProvider : LiteFileIndexProviderBase
    {
        public override string ProviderKey => FileIndexProviderFactory.ProviderStandardFileSystem;
        public override string ProviderDisplayName => "StandardFileSystem";

        protected override Lite.FileIndexBackendMode BackendMode =>
            Lite.FileIndexBackendMode.StandardFileSystem;

        // テストからキャッシュ状態を検証するための補助API。
        internal static int GetCacheEntryCountForTesting()
        {
            return GetCacheEntryCountForTestingCore();
        }

        // テストから上限値を参照し、将来の定数変更に追従できるようにする。
        internal static int GetCacheCapacityForTesting()
        {
            return GetCacheCapacityForTestingCore();
        }

        // テスト終了時にキャッシュを明示クリアして、ケース間の干渉を防ぐ。
        internal static void ClearCacheForTesting()
        {
            ClearCacheForTestingCore();
        }
    }
}

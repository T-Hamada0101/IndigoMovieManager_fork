namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// 設定値からIFileIndexProviderを決定し、Facadeを生成する。
    /// </summary>
    internal static class FileIndexProviderFactory
    {
        public const string ProviderEverything = "everything";
        public const string ProviderUsnMft = "usnmft";

        public static IIndexProviderFacade CreateFacade()
        {
            string providerKey = ResolveProviderKey();
            IFileIndexProvider provider = CreateProvider(providerKey);
            return new IndexProviderFacade(provider);
        }

        // 文字列揺れを吸収し、everything 以外は usnmft に丸める。
        private static string ResolveProviderKey()
        {
            string raw = (Properties.Settings.Default.FileIndexProvider ?? "").Trim();
            return NormalizeProviderKey(raw);
        }

        // UI保存時と生成時で同じ正規化ルールを共有する。
        internal static string NormalizeProviderKey(string raw)
        {
            if (string.Equals(raw?.Trim(), ProviderEverything, StringComparison.OrdinalIgnoreCase))
            {
                return ProviderEverything;
            }

            return ProviderUsnMft;
        }

        private static IFileIndexProvider CreateProvider(string providerKey)
        {
            return providerKey switch
            {
                ProviderUsnMft => new UsnMftProvider(),
                _ => new EverythingProvider(),
            };
        }
    }
}

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// 設定値からIFileIndexProviderを決定し、Facadeを生成する。
    /// </summary>
    internal static class FileIndexProviderFactory
    {
        public const string ProviderEverything = "everything";
        public const string ProviderUsnMft = "usnmft";
        public const string ProviderStandardFileSystem = "standardfilesystem";

        public static IIndexProviderFacade CreateFacade()
        {
            string providerKey = ResolveProviderKey();
            IFileIndexProvider provider = CreateProvider(providerKey);
            return new IndexProviderFacade(provider);
        }

        // 文字列揺れを吸収し、未知値は everything へ戻す。
        private static string ResolveProviderKey()
        {
            string raw = (Properties.Settings.Default.FileIndexProvider ?? "").Trim();
            return NormalizeProviderKey(raw);
        }

        // UI保存時と生成時で同じ正規化ルールを共有する。
        internal static string NormalizeProviderKey(string raw)
        {
            string normalized = (raw ?? "").Trim();
            if (string.Equals(normalized, ProviderEverything, StringComparison.OrdinalIgnoreCase))
            {
                return ProviderEverything;
            }

            if (string.Equals(normalized, ProviderUsnMft, StringComparison.OrdinalIgnoreCase))
            {
                return ProviderUsnMft;
            }

            if (
                string.Equals(
                    normalized,
                    ProviderStandardFileSystem,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ProviderStandardFileSystem;
            }

            return ProviderEverything;
        }

        private static IFileIndexProvider CreateProvider(string providerKey)
        {
            return providerKey switch
            {
                ProviderEverything => new EverythingProvider(),
                ProviderUsnMft => new UsnMftProvider(),
                ProviderStandardFileSystem => new StandardFileSystemProvider(),
                _ => new EverythingProvider(),
            };
        }
    }
}

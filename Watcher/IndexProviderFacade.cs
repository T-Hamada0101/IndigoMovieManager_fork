namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// ホスト側から使うEverything連携の統一窓口。
    /// mode判定とfallback方針をここへ集約する。
    /// </summary>
    internal sealed class IndexProviderFacade : IIndexProviderFacade
    {
        private readonly IFileIndexProvider _provider;

        public IndexProviderFacade(IFileIndexProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public string ProviderKey => _provider.ProviderKey;

        public string ProviderDisplayName => _provider.ProviderDisplayName;

        public bool IsIntegrationConfigured(IntegrationMode mode)
        {
            return mode != IntegrationMode.Off;
        }

        public AvailabilityResult CheckAvailability(IntegrationMode mode)
        {
            if (!IsIntegrationConfigured(mode))
            {
                return new AvailabilityResult(false, EverythingReasonCodes.SettingDisabled);
            }

            AvailabilityResult providerResult = _provider.CheckAvailability();
            return new AvailabilityResult(
                providerResult.CanUse,
                FileIndexReasonTable.NormalizeByMode(mode, providerResult.Reason)
            );
        }

        public ScanByProviderResult CollectMoviePathsWithFallback(
            FileIndexQueryOptions options,
            IntegrationMode mode
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            AvailabilityResult availability = CheckAvailability(mode);
            if (!availability.CanUse)
            {
                return new ScanByProviderResult(
                    FileIndexStrategies.Filesystem,
                    _provider.ProviderKey,
                    _provider.ProviderDisplayName,
                    availability.Reason,
                    [],
                    null
                );
            }

            FileIndexMovieResult providerResult = _provider.CollectMoviePaths(options);
            if (providerResult.Success)
            {
                return new ScanByProviderResult(
                    FileIndexStrategies.Everything,
                    _provider.ProviderKey,
                    _provider.ProviderDisplayName,
                    providerResult.Reason,
                    providerResult.MoviePaths,
                    providerResult.MaxObservedChangedUtc
                );
            }

            return new ScanByProviderResult(
                FileIndexStrategies.Filesystem,
                _provider.ProviderKey,
                _provider.ProviderDisplayName,
                FileIndexReasonTable.NormalizeByMode(mode, providerResult.Reason),
                [],
                null
            );
        }

        public FileIndexThumbnailBodyResult CollectThumbnailBodiesWithFallback(
            string thumbFolder,
            IntegrationMode mode
        )
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                throw new ArgumentException("thumbFolder is required.", nameof(thumbFolder));
            }

            AvailabilityResult availability = CheckAvailability(mode);
            if (!availability.CanUse)
            {
                return new FileIndexThumbnailBodyResult(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    availability.Reason
                );
            }

            FileIndexThumbnailBodyResult providerResult = _provider.CollectThumbnailBodies(
                thumbFolder
            );
            return new FileIndexThumbnailBodyResult(
                providerResult.Success,
                providerResult.Bodies,
                FileIndexReasonTable.NormalizeByMode(mode, providerResult.Reason)
            );
        }
    }
}

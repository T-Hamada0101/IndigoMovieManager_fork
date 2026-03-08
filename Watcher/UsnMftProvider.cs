using System.IO;
using System.Runtime.Versioning;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// 管理者サービス経由で UsnMft を使う provider。
    /// 管理者判定はローカルではなく service 側へ寄せる。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class UsnMftProvider : IFileIndexProvider
    {
        private readonly IAdminFileIndexClient client;

        public UsnMftProvider()
            : this(new AdminFileIndexClient()) { }

        internal UsnMftProvider(IAdminFileIndexClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public string ProviderKey => FileIndexProviderFactory.ProviderUsnMft;
        public string ProviderDisplayName => "usnmft";

        public AvailabilityResult CheckAvailability()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new AvailabilityResult(false, EverythingReasonCodes.EverythingNotAvailable);
            }

            return client.CheckAvailability();
        }

        public FileIndexMovieResult CollectMoviePaths(FileIndexQueryOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            AvailabilityResult availability = CheckAvailability();
            if (!availability.CanUse)
            {
                return new FileIndexMovieResult(false, [], null, availability.Reason);
            }

            try
            {
                return client.CollectMoviePaths(options);
            }
            catch (Exception ex)
            {
                return new FileIndexMovieResult(
                    false,
                    [],
                    null,
                    EverythingReasonCodes.BuildEverythingQueryError(ex)
                );
            }
        }

        public FileIndexThumbnailBodyResult CollectThumbnailBodies(string thumbFolder)
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                throw new ArgumentException("thumbFolder is required.", nameof(thumbFolder));
            }

            HashSet<string> existingThumbBodies = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(thumbFolder))
                {
                    return new FileIndexThumbnailBodyResult(
                        true,
                        existingThumbBodies,
                        EverythingReasonCodes.Ok
                    );
                }

                foreach (
                    string filePath in Directory.EnumerateFiles(
                        thumbFolder,
                        "*.jpg",
                        SearchOption.TopDirectoryOnly
                    )
                )
                {
                    string fileName = Path.GetFileName(filePath) ?? "";
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    string body = ExtractThumbnailBody(fileName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        existingThumbBodies.Add(body);
                    }
                }

                return new FileIndexThumbnailBodyResult(
                    true,
                    existingThumbBodies,
                    EverythingReasonCodes.Ok
                );
            }
            catch (Exception ex)
            {
                return new FileIndexThumbnailBodyResult(
                    false,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    EverythingReasonCodes.BuildEverythingThumbQueryError(ex)
                );
            }
        }

        // service 側へキャッシュを移したため、Watcher から観測できるローカルキャッシュは持たない。
        internal static int GetCacheEntryCountForTesting()
        {
            return 0;
        }

        // service 側へキャッシュを移したため、Watcher から観測できるローカル上限は持たない。
        internal static int GetCacheCapacityForTesting()
        {
            return 0;
        }

        internal static void ClearCacheForTesting()
        {
        }

        private static string ExtractThumbnailBody(string fileName)
        {
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return "";
            }

            int hashMarkerIndex = nameWithoutExt.LastIndexOf(
                ".#",
                StringComparison.OrdinalIgnoreCase
            );
            if (hashMarkerIndex >= 0)
            {
                return nameWithoutExt[..hashMarkerIndex];
            }

            return nameWithoutExt;
        }
    }
}

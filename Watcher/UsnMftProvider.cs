using System.Runtime.Versioning;
using System.Security.Principal;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// UsnMft バックエンドを IFileIndexProvider 契約へ接続する。
    /// StandardFileSystem とは分離し、USN/MFT 専用経路として扱う。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class UsnMftProvider : LiteFileIndexProviderBase
    {
        public override string ProviderKey => FileIndexProviderFactory.ProviderUsnMft;
        public override string ProviderDisplayName => "usnmft";

        protected override Lite.FileIndexBackendMode BackendMode =>
            Lite.FileIndexBackendMode.AdminUsnMft;

        protected override AvailabilityResult CheckWindowsAvailability()
        {
            // StandardFileSystem と混同しないよう、管理者権限がない環境では未使用扱いにする。
            if (!IsAdministrator())
            {
                return new AvailabilityResult(
                    false,
                    $"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminRequired"
                );
            }

            return new AvailabilityResult(true, EverythingReasonCodes.Ok);
        }

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

        private static bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null)
            {
                return false;
            }

            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

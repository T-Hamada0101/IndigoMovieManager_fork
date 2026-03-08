using System.IO;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// macOS が作る AppleDouble サイドカー(._*)だけを見分ける補助判定。
    /// 名前だけで除外せず、ヘッダー magic まで確認してから弾く。
    /// </summary>
    internal static class MacMetadataSidecarDetector
    {
        private static readonly byte[] AppleDoubleMagic =
        [
            0x00,
            0x05,
            0x16,
            0x07,
        ];

        internal static bool IsAppleDoubleSidecar(string fullPath)
        {
            if (!LooksLikeAppleDoubleSidecarName(fullPath))
            {
                return false;
            }

            try
            {
                using FileStream stream = new(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                if (stream.Length < AppleDoubleMagic.Length)
                {
                    return false;
                }

                Span<byte> header = stackalloc byte[4];
                int read = stream.Read(header);
                if (read < AppleDoubleMagic.Length)
                {
                    return false;
                }

                for (int i = 0; i < AppleDoubleMagic.Length; i++)
                {
                    if (header[i] != AppleDoubleMagic[i])
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                // 判定不能時は本体動画の取りこぼし回避を優先し、除外しない。
                return false;
            }
        }

        private static bool LooksLikeAppleDoubleSidecarName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(fullPath);
            return !string.IsNullOrWhiteSpace(fileName)
                && fileName.StartsWith("._", StringComparison.Ordinal);
        }
    }
}

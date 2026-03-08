using System.Collections.Concurrent;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 動画ごとの軽量メタキャッシュをまとめる。
    /// hash、duration、DRM/SWF 事前判定を service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailMovieMetaCache
    {
        private const int MovieMetaCacheMaxCount = 10000;
        private const int AsfDrmScanMaxBytes = 64 * 1024;
        private const int SwfSignatureLength = 3;
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> Cache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly byte[] AsfContentEncryptionObjectGuid =
        [
            0xFB,
            0xB3,
            0x11,
            0x22,
            0x23,
            0xBD,
            0xD2,
            0x11,
            0xB4,
            0xB7,
            0x00,
            0xA0,
            0xC9,
            0x55,
            0xFC,
            0x6E,
        ];
        private static readonly byte[] SwfSignatureFws = [0x46, 0x57, 0x53];
        private static readonly byte[] SwfSignatureCws = [0x43, 0x57, 0x53];
        private static readonly byte[] SwfSignatureZws = [0x5A, 0x57, 0x53];

        public static CachedMovieMetaLookup GetOrCreate(string movieFullPath, string hashHint)
        {
            string cacheKey = BuildCacheKey(movieFullPath);
            CachedMovieMeta meta = Cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    string hash = ResolveMovieHash(movieFullPath, hashHint);
                    bool isDrmSuspected = false;
                    string drmDetail = "";
                    bool isSwfCandidate = false;
                    string swfDetail = "";
                    if (IsAsfFamilyFile(movieFullPath))
                    {
                        isDrmSuspected = TryDetectAsfDrmProtected(movieFullPath, out drmDetail);
                    }
                    else if (IsSwfFile(movieFullPath))
                    {
                        // SWFは専用経路へ流すため、ここでは候補判定だけ保持する。
                        isSwfCandidate = TryDetectSwfKnownSignature(movieFullPath, out swfDetail);
                    }

                    return new CachedMovieMeta(
                        hash,
                        null,
                        isDrmSuspected,
                        drmDetail,
                        isSwfCandidate,
                        swfDetail,
                        false,
                        ""
                    );
                }
            );

            return new CachedMovieMetaLookup(cacheKey, meta);
        }

        public static void UpdateDuration(
            string cacheKey,
            CachedMovieMeta currentMeta,
            double? durationSec
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return;
            }

            Cache[cacheKey] = new CachedMovieMeta(
                currentMeta?.Hash ?? "",
                durationSec,
                currentMeta?.IsDrmSuspected ?? false,
                currentMeta?.DrmDetail ?? "",
                currentMeta?.IsSwfCandidate ?? false,
                currentMeta?.SwfDetail ?? "",
                currentMeta?.IsUnsupportedPrecheck ?? false,
                currentMeta?.UnsupportedDetail ?? ""
            );

            if (Cache.Count > MovieMetaCacheMaxCount)
            {
                Cache.Clear();
            }
        }

        private static string ResolveMovieHash(string movieFullPath, string hashHint)
        {
            if (!string.IsNullOrWhiteSpace(hashHint))
            {
                return hashHint;
            }

            return GetHashCRC32(movieFullPath);
        }

        private static string BuildCacheKey(string movieFullPath)
        {
            try
            {
                FileInfo fi = new(movieFullPath);
                if (!fi.Exists)
                {
                    return movieFullPath;
                }

                return $"{movieFullPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return movieFullPath;
            }
        }

        private static bool IsAsfFamilyFile(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath);
            return ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSwfFile(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath);
            return ext.Equals(".swf", StringComparison.OrdinalIgnoreCase);
        }

        // Content Encryption Object GUID がヘッダー内にあるかを調べる。
        private static bool TryDetectAsfDrmProtected(string movieFullPath, out string detail)
        {
            detail = "";
            if (!Path.Exists(movieFullPath))
            {
                detail = "file_not_found";
                return false;
            }

            try
            {
                using FileStream fs = new(
                    movieFullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                int readLength = (int)Math.Min(AsfDrmScanMaxBytes, fs.Length);
                if (readLength < AsfContentEncryptionObjectGuid.Length)
                {
                    detail = "header_too_short";
                    return false;
                }

                byte[] buffer = new byte[readLength];
                int totalRead = 0;
                while (totalRead < readLength)
                {
                    int read = fs.Read(buffer, totalRead, readLength - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                int hitIndex = IndexOfBytes(buffer, totalRead, AsfContentEncryptionObjectGuid);
                if (hitIndex >= 0)
                {
                    detail = $"drm_guid_found_offset={hitIndex}";
                    return true;
                }

                detail = "drm_guid_not_found";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"scan_error:{ex.GetType().Name}";
                return false;
            }
        }

        // 先頭3バイトのSWFシグネチャ(FWS/CWS/ZWS)を調べる。
        private static bool TryDetectSwfKnownSignature(string movieFullPath, out string detail)
        {
            detail = "";
            if (!Path.Exists(movieFullPath))
            {
                detail = "file_not_found";
                return false;
            }

            try
            {
                using FileStream fs = new(
                    movieFullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                if (fs.Length < SwfSignatureLength)
                {
                    detail = "header_too_short";
                    return false;
                }

                byte[] header = new byte[SwfSignatureLength];
                int totalRead = 0;
                while (totalRead < SwfSignatureLength)
                {
                    int read = fs.Read(header, totalRead, SwfSignatureLength - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                if (totalRead < SwfSignatureLength)
                {
                    detail = "header_too_short";
                    return false;
                }

                if (
                    header[0] == SwfSignatureFws[0]
                    && header[1] == SwfSignatureFws[1]
                    && header[2] == SwfSignatureFws[2]
                )
                {
                    detail = "swf_signature=FWS";
                    return true;
                }

                if (
                    header[0] == SwfSignatureCws[0]
                    && header[1] == SwfSignatureCws[1]
                    && header[2] == SwfSignatureCws[2]
                )
                {
                    detail = "swf_signature=CWS";
                    return true;
                }

                if (
                    header[0] == SwfSignatureZws[0]
                    && header[1] == SwfSignatureZws[1]
                    && header[2] == SwfSignatureZws[2]
                )
                {
                    detail = "swf_signature=ZWS";
                    return true;
                }

                detail = "swf_signature_not_found";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"scan_error:{ex.GetType().Name}";
                return false;
            }
        }

        private static int IndexOfBytes(byte[] source, int sourceLength, byte[] pattern)
        {
            if (
                source == null
                || pattern == null
                || sourceLength < pattern.Length
                || pattern.Length < 1
            )
            {
                return -1;
            }

            int last = sourceLength - pattern.Length;
            for (int i = 0; i <= last; i++)
            {
                bool matched = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal sealed class CachedMovieMetaLookup
    {
        public CachedMovieMetaLookup(string cacheKey, CachedMovieMeta meta)
        {
            CacheKey = cacheKey ?? "";
            Meta = meta ?? throw new ArgumentNullException(nameof(meta));
        }

        public string CacheKey { get; }

        public CachedMovieMeta Meta { get; }
    }

    internal sealed class CachedMovieMeta
    {
        public CachedMovieMeta(
            string hash,
            double? durationSec,
            bool isDrmSuspected,
            string drmDetail,
            bool isSwfCandidate,
            string swfDetail,
            bool isUnsupportedPrecheck,
            string unsupportedDetail
        )
        {
            Hash = hash ?? "";
            DurationSec = durationSec;
            IsDrmSuspected = isDrmSuspected;
            DrmDetail = drmDetail ?? "";
            IsSwfCandidate = isSwfCandidate;
            SwfDetail = swfDetail ?? "";
            IsUnsupportedPrecheck = isUnsupportedPrecheck;
            UnsupportedDetail = unsupportedDetail ?? "";
        }

        public string Hash { get; }
        public double? DurationSec { get; }
        public bool IsDrmSuspected { get; }
        public string DrmDetail { get; }
        public bool IsSwfCandidate { get; }
        public string SwfDetail { get; }
        public bool IsUnsupportedPrecheck { get; }
        public string UnsupportedDetail { get; }
    }
}

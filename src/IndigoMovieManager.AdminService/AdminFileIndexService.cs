using System.Collections.Concurrent;
using IndigoMovieManager.FileIndex.UsnMft;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager
{
    // 管理者サービス側で AdminUsnMft のインデックスを保持し、Watcher からの問い合わせへ答える。
    internal static class AdminFileIndexService
    {
        private const int SearchLimit = 1_000_000;
        private static readonly TimeSpan RebuildCooldown = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CacheEntryIdleTtl = TimeSpan.FromMinutes(20);
        private static readonly object CacheMaintenanceLock = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static AdminFileIndexMovieResultDto CollectMoviePaths(AdminFileIndexQueryDto query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }
            if (string.IsNullOrWhiteSpace(query.RootPath))
            {
                throw new ArgumentException("RootPath is required.", nameof(query));
            }

            string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(query.RootPath);
            string normalizedRootWithoutSlash = NormalizeDirectoryPathWithoutTrailingSlash(
                query.RootPath
            );
            HashSet<string> targetExtensions = ParseTargetExtensions(query.CheckExt);
            DateTime? changedSinceUtc = query.ChangedSinceUtc;

            CacheEntry cacheEntry = GetOrCreateCacheEntry();
            IReadOnlyList<SearchResultItem> indexedItems = QueryIndexedItems(
                cacheEntry,
                out bool rebuilt,
                out DateTime indexedAtUtc
            );

            List<string> moviePaths = [];
            DateTime? maxObservedChangedUtc = null;
            foreach (SearchResultItem item in indexedItems)
            {
                if (item.IsDirectory)
                {
                    continue;
                }

                if (!IsUnderRoot(item.FullPath, normalizedRootWithSlash))
                {
                    continue;
                }

                if (
                    !query.IncludeSubdirectories
                    && !IsDirectChild(item.FullPath, normalizedRootWithoutSlash)
                )
                {
                    continue;
                }

                if (!IsTargetExtension(item.FullPath, targetExtensions))
                {
                    continue;
                }

                DateTime itemChangedUtc = NormalizeToUtc(item.LastWriteTimeUtc);
                if (changedSinceUtc.HasValue && itemChangedUtc < changedSinceUtc.Value)
                {
                    continue;
                }

                moviePaths.Add(item.FullPath);
                if (!maxObservedChangedUtc.HasValue || itemChangedUtc > maxObservedChangedUtc.Value)
                {
                    maxObservedChangedUtc = itemChangedUtc;
                }
            }

            string reason = changedSinceUtc.HasValue
                ? $"ok:provider=usnmft index={(rebuilt ? "rebuilt" : "cached")} indexed_at={indexedAtUtc:O} count={moviePaths.Count} since={changedSinceUtc.Value:O}"
                : $"ok:provider=usnmft index={(rebuilt ? "rebuilt" : "cached")} indexed_at={indexedAtUtc:O} count={moviePaths.Count}";

            return new AdminFileIndexMovieResultDto
            {
                Success = true,
                MoviePaths = moviePaths,
                MaxObservedChangedUtc = maxObservedChangedUtc,
                Reason = reason,
            };
        }

        private static CacheEntry GetOrCreateCacheEntry()
        {
            DateTime nowUtc = DateTime.UtcNow;
            CleanupExpiredEntries(nowUtc);
            CacheEntry entry = Cache.GetOrAdd(
                "admin-usnmft",
                _ =>
                    new CacheEntry(
                        new FileIndexService(
                            new FileIndexServiceOptions
                            {
                                BackendMode = FileIndexBackendMode.AdminUsnMft,
                            }
                        )
                    )
            );
            lock (entry.SyncRoot)
            {
                entry.LastAccessUtc = nowUtc;
            }
            return entry;
        }

        private static IReadOnlyList<SearchResultItem> QueryIndexedItems(
            CacheEntry cacheEntry,
            out bool rebuilt,
            out DateTime indexedAtUtc
        )
        {
            lock (cacheEntry.SyncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                cacheEntry.LastAccessUtc = nowUtc;
                bool shouldRebuild =
                    !cacheEntry.HasIndexed
                    || nowUtc - cacheEntry.LastIndexedUtc >= RebuildCooldown;
                if (shouldRebuild)
                {
                    cacheEntry.Service.RebuildIndexAsync(null, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    cacheEntry.LastIndexedUtc = DateTime.UtcNow;
                    cacheEntry.HasIndexed = true;
                    rebuilt = true;
                }
                else
                {
                    rebuilt = false;
                }

                indexedAtUtc = cacheEntry.LastIndexedUtc;
                return cacheEntry.Service.Search("", SearchLimit);
            }
        }

        private static void CleanupExpiredEntries(DateTime nowUtc)
        {
            lock (CacheMaintenanceLock)
            {
                foreach (KeyValuePair<string, CacheEntry> pair in Cache.ToArray())
                {
                    DateTime lastAccessUtc;
                    lock (pair.Value.SyncRoot)
                    {
                        lastAccessUtc = pair.Value.LastAccessUtc;
                    }

                    if (nowUtc - lastAccessUtc < CacheEntryIdleTtl)
                    {
                        continue;
                    }

                    if (Cache.TryRemove(pair.Key, out CacheEntry removed))
                    {
                        lock (removed.SyncRoot)
                        {
                            removed.Service.Dispose();
                        }
                    }
                }
            }
        }

        private static HashSet<string> ParseTargetExtensions(string checkExt)
        {
            HashSet<string> targetExtensions = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(checkExt))
            {
                return targetExtensions;
            }

            string[] parts = checkExt.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string ext = raw.Trim().Replace("*", "");
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }

                if (!ext.StartsWith('.'))
                {
                    ext = "." + ext;
                }

                targetExtensions.Add(ext);
            }

            return targetExtensions;
        }

        private static bool IsTargetExtension(string fullPath, HashSet<string> targetExtensions)
        {
            if (targetExtensions.Count < 1)
            {
                return true;
            }

            string ext = Path.GetExtension(fullPath);
            return targetExtensions.Contains(ext);
        }

        private static bool IsUnderRoot(string candidatePath, string rootWithSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                return normalizedCandidate.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDirectChild(string candidatePath, string rootWithoutSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                string parent = Path.GetDirectoryName(normalizedCandidate) ?? "";
                string normalizedParent = NormalizeDirectoryPathWithoutTrailingSlash(parent);
                return string.Equals(
                    normalizedParent,
                    rootWithoutSlash,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeDirectoryPathWithTrailingSlash(string path)
        {
            string normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized + Path.DirectorySeparatorChar;
        }

        private static string NormalizeDirectoryPathWithoutTrailingSlash(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            };
        }

        private sealed class CacheEntry
        {
            public CacheEntry(IFileIndexService service)
            {
                Service = service;
                LastAccessUtc = DateTime.UtcNow;
            }

            public object SyncRoot { get; } = new();
            public IFileIndexService Service { get; }
            public DateTime LastAccessUtc { get; set; }
            public DateTime LastIndexedUtc { get; set; }
            public bool HasIndexed { get; set; }
        }
    }
}

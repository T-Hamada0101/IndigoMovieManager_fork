using System.Collections.Generic;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの画像更新を、選択中タブだけへ絞るための最小ヘルパ。
    /// </summary>
    public static class UpperTabActivationGate
    {
        private static readonly object PreferredMoviePathKeysGate = new();
        private static readonly HashSet<string> PreferredMoviePathKeys =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool HasPreferredMoviePathKeysSnapshot;

        public static bool ShouldApplyImageUpdate(object isSelectedValue)
        {
            return ShouldApplyImageUpdate(isSelectedValue, null);
        }

        public static bool ShouldApplyImageUpdate(object isSelectedValue, object moviePathValue)
        {
            return ShouldApplyImageRequest(
                CreateUpperTabImageRequest(null, isSelectedValue, moviePathValue, requestRevision: 0)
            );
        }

        internal static ImageRequest CreateUpperTabImageRequest(
            object thumbnailPathValue,
            object isSelectedValue,
            object moviePathValue,
            int requestRevision
        )
        {
            string moviePathKey = ResolveMoviePathKey(moviePathValue);
            bool isVisiblePriority = ResolveUpperTabVisiblePriority(
                isSelectedValue,
                moviePathKey,
                moviePathValue
            );
            return ImageRequest.ForUpperTab(
                thumbnailPathValue as string,
                moviePathKey,
                isVisiblePriority,
                requestRevision
            );
        }

        internal static ImageRequest CreatePlayerRightRailImageRequest(
            object thumbnailPathValue,
            object isVisibleValue,
            object moviePathValue,
            int requestRevision
        )
        {
            string moviePathKey = ResolveMoviePathKey(moviePathValue);
            bool isVisiblePriority = ResolveUpperTabVisiblePriority(
                isVisibleValue,
                moviePathKey,
                moviePathValue
            );
            return ImageRequest.ForPlayerRightRail(
                thumbnailPathValue as string,
                moviePathKey,
                isVisiblePriority,
                requestRevision
            );
        }

        internal static bool ShouldApplyImageRequest(ImageRequest request)
        {
            return request.ShouldDecode;
        }

        internal static bool ShouldApplyPlayerRightRailImageRequest(
            ImageRequest request,
            int currentRevision
        )
        {
            return request.ThumbnailRole == ImageRequestThumbnailRole.PlayerRightRail
                && request.RequestRevision == currentRevision;
        }

        internal static int ResolveImageRequestRevision(object revisionValue)
        {
            return revisionValue switch
            {
                int intValue => intValue,
                long longValue when longValue >= int.MinValue && longValue <= int.MaxValue =>
                    (int)longValue,
                _ => 0,
            };
        }

        private static bool ResolveUpperTabVisiblePriority(
            object isSelectedValue,
            string moviePathKey,
            object moviePathValue
        )
        {
            // TabItem の祖先解決が遅い瞬間は UnsetValue になることがある。
            // 表示不能を避けるため、明確に false の時だけ止める。
            if (isSelectedValue is bool isSelected && !isSelected)
            {
                return false;
            }

            // 可視近傍キーが入っている時だけ、off-screen の画像再評価を止める。
            if (moviePathValue is not string || string.IsNullOrWhiteSpace(moviePathKey))
            {
                return true;
            }

            lock (PreferredMoviePathKeysGate)
            {
                // Clear 直後は viewport 未計測として従来互換で通し、空 snapshot が確定した時だけ止める。
                return !HasPreferredMoviePathKeysSnapshot
                    || PreferredMoviePathKeys.Contains(moviePathKey);
            }
        }

        private static string ResolveMoviePathKey(object moviePathValue)
        {
            if (moviePathValue is not string moviePath || string.IsNullOrWhiteSpace(moviePath))
            {
                return "";
            }

            return QueueDbPathResolver.CreateMoviePathKey(moviePath) ?? "";
        }

        public static bool UpdatePreferredMoviePathKeys(IReadOnlyList<string> moviePathKeys)
        {
            lock (PreferredMoviePathKeysGate)
            {
                if (moviePathKeys == null)
                {
                    bool changed = HasPreferredMoviePathKeysSnapshot
                        || PreferredMoviePathKeys.Count > 0;
                    PreferredMoviePathKeys.Clear();
                    HasPreferredMoviePathKeysSnapshot = false;
                    return changed;
                }

                if (
                    HasPreferredMoviePathKeysSnapshot
                    && ArePreferredMoviePathKeysEqual(moviePathKeys)
                )
                {
                    return false;
                }

                PreferredMoviePathKeys.Clear();
                HasPreferredMoviePathKeysSnapshot = true;
                foreach (string moviePathKey in moviePathKeys)
                {
                    if (string.IsNullOrWhiteSpace(moviePathKey))
                    {
                        continue;
                    }

                    _ = PreferredMoviePathKeys.Add(moviePathKey);
                }

                return true;
            }
        }

        public static void ClearPreferredMoviePathKeys()
        {
            lock (PreferredMoviePathKeysGate)
            {
                PreferredMoviePathKeys.Clear();
                HasPreferredMoviePathKeysSnapshot = false;
            }
        }

        private static bool ArePreferredMoviePathKeysEqual(IReadOnlyList<string> moviePathKeys)
        {
            int nextCount = 0;
            for (int index = 0; index < moviePathKeys.Count; index++)
            {
                string moviePathKey = moviePathKeys[index];
                if (string.IsNullOrWhiteSpace(moviePathKey))
                {
                    continue;
                }

                nextCount++;
                if (!PreferredMoviePathKeys.Contains(moviePathKey))
                {
                    return false;
                }
            }

            return PreferredMoviePathKeys.Count == nextCount;
        }
    }
}

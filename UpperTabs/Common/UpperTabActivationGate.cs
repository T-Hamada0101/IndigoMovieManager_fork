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
            // TabItem の祖先解決が遅い瞬間は UnsetValue になることがある。
            // 表示不能を避けるため、明確に false の時だけ止める。
            if (isSelectedValue is bool isSelected && !isSelected)
            {
                return false;
            }

            // 可視近傍キーが入っている時だけ、off-screen の画像再評価を止める。
            if (moviePathValue is not string moviePath || string.IsNullOrWhiteSpace(moviePath))
            {
                return true;
            }

            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath);
            if (string.IsNullOrWhiteSpace(moviePathKey))
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

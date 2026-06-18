using System.Collections.Concurrent;

namespace IndigoMovieManager.Skin.Runtime
{
    internal enum WhiteBrowserSkinProfileValueCacheState
    {
        Pending,
        Persisted,
        Faulted,
    }

    internal readonly record struct WhiteBrowserSkinProfileValuePersistState(
        string Value,
        bool IsDirty,
        bool IsFailed,
        bool IsRetryable,
        bool NotifyUi
    );

    /// <summary>
    /// profile 値のセッション内 cache。
    /// API からは pending も見せるが、初期タブ復元は persisted だけを見る。
    /// </summary>
    internal static class WhiteBrowserSkinProfileValueCache
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> Entries =
            new(StringComparer.Ordinal);

        internal static void ClearForTesting()
        {
            Entries.Clear();
        }

        internal static void RecordPending(string dbFullPath, string skinName, string key, string value)
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            Entries[identityKey] = new CacheEntry(
                WhiteBrowserSkinProfileValueCacheState.Pending,
                value ?? "",
                isDirty: true,
                isFailed: false,
                isRetryable: false
            );
        }

        internal static void RecordPersisted(string dbFullPath, string skinName, string key, string value)
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            Entries[identityKey] = new CacheEntry(
                WhiteBrowserSkinProfileValueCacheState.Persisted,
                value ?? "",
                isDirty: false,
                isFailed: false,
                isRetryable: false
            );
        }

        internal static void RecordFault(
            string dbFullPath,
            string skinName,
            string key,
            string value = null
        )
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            PersistenceFailureNotificationState failureState =
                PersistenceFailureNotificationPolicy.BuildFailureState(
                    PersistenceFailureKind.SkinProfile
                );

            Entries.AddOrUpdate(
                identityKey,
                _ =>
                    new CacheEntry(
                        WhiteBrowserSkinProfileValueCacheState.Faulted,
                        value ?? "",
                        isDirty: failureState.Dirty,
                        isFailed: failureState.Failed,
                        isRetryable: failureState.Retryable,
                        notifyUi: failureState.NotifyUi
                    ),
                (_, previous) =>
                    new CacheEntry(
                        WhiteBrowserSkinProfileValueCacheState.Faulted,
                        value ?? previous?.Value ?? "",
                        isDirty: failureState.Dirty,
                        isFailed: failureState.Failed,
                        isRetryable: failureState.Retryable,
                        notifyUi: failureState.NotifyUi
                    )
            );
        }

        internal static bool TryGetApiVisibleValue(
            string dbFullPath,
            string skinName,
            string key,
            out string value
        )
        {
            value = "";
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (
                string.IsNullOrWhiteSpace(identityKey)
                || !Entries.TryGetValue(identityKey, out CacheEntry entry)
            )
            {
                return false;
            }

            if (entry.State == WhiteBrowserSkinProfileValueCacheState.Faulted)
            {
                return false;
            }

            value = entry.Value ?? "";
            return true;
        }

        internal static bool TryGetPersistedValue(
            string dbFullPath,
            string skinName,
            string key,
            out string value
        )
        {
            value = "";
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (
                string.IsNullOrWhiteSpace(identityKey)
                || !Entries.TryGetValue(identityKey, out CacheEntry entry)
                || entry.State != WhiteBrowserSkinProfileValueCacheState.Persisted
            )
            {
                return false;
            }

            value = entry.Value ?? "";
            return true;
        }

        internal static bool TryGetPersistState(
            string dbFullPath,
            string skinName,
            string key,
            out WhiteBrowserSkinProfileValuePersistState state
        )
        {
            state = default;
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (
                string.IsNullOrWhiteSpace(identityKey)
                || !Entries.TryGetValue(identityKey, out CacheEntry entry)
            )
            {
                return false;
            }

            // UI 側へ重い問い合わせを戻さず、cache 内の保存状態だけを軽く読めるようにする。
            state = new WhiteBrowserSkinProfileValuePersistState(
                entry.Value ?? "",
                entry.IsDirty,
                entry.IsFailed,
                entry.IsRetryable,
                entry.NotifyUi
            );
            return true;
        }

        private static string BuildIdentityKey(string dbFullPath, string skinName, string key)
        {
            string dbIdentity = WhiteBrowserSkinDbIdentity.Build(dbFullPath);
            string normalizedSkinName = skinName?.Trim() ?? "";
            string normalizedKey = key?.Trim() ?? "";
            if (
                string.IsNullOrWhiteSpace(dbIdentity)
                || string.IsNullOrWhiteSpace(normalizedSkinName)
                || string.IsNullOrWhiteSpace(normalizedKey)
            )
            {
                return "";
            }

            return $"{dbIdentity}:{normalizedSkinName}:{normalizedKey}";
        }

        private sealed class CacheEntry
        {
            internal CacheEntry(
                WhiteBrowserSkinProfileValueCacheState state,
                string value,
                bool isDirty,
                bool isFailed,
                bool isRetryable,
                bool notifyUi = false
            )
            {
                State = state;
                Value = value ?? "";
                IsDirty = isDirty;
                IsFailed = isFailed;
                IsRetryable = isRetryable;
                NotifyUi = notifyUi;
            }

            internal WhiteBrowserSkinProfileValueCacheState State { get; }
            internal string Value { get; }
            internal bool IsDirty { get; }
            internal bool IsFailed { get; }
            internal bool IsRetryable { get; }
            internal bool NotifyUi { get; }
        }
    }
}

using System.Collections.Concurrent;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 出力ファイル単位の排他制御をまとめる。
    /// service 本体からロック辞書と寿命管理を切り離す。
    /// </summary>
    internal static class ThumbnailOutputLockManager
    {
        private static readonly ConcurrentDictionary<string, OutputFileLockEntry> OutputFileLocks = new(
            StringComparer.OrdinalIgnoreCase
        );

        public static async Task<OutputFileLockEntry> AcquireAsync(
            string saveThumbFileName,
            CancellationToken cts
        )
        {
            if (string.IsNullOrWhiteSpace(saveThumbFileName))
            {
                throw new ArgumentException(
                    "saveThumbFileName is required.",
                    nameof(saveThumbFileName)
                );
            }

            while (true)
            {
                OutputFileLockEntry entry = OutputFileLocks.GetOrAdd(
                    saveThumbFileName,
                    _ => new OutputFileLockEntry()
                );
                if (!entry.TryAcquireUserRef())
                {
                    continue;
                }

                try
                {
                    await entry.Semaphore.WaitAsync(cts);
                    return entry;
                }
                catch
                {
                    Release(saveThumbFileName, entry, releaseSemaphore: false);
                    throw;
                }
            }
        }

        public static void Release(
            string saveThumbFileName,
            OutputFileLockEntry entry,
            bool releaseSemaphore = true
        )
        {
            if (entry == null)
            {
                return;
            }

            bool released = false;
            try
            {
                if (releaseSemaphore)
                {
                    entry.Semaphore.Release();
                    released = true;
                }
            }
            catch (SemaphoreFullException)
            {
                released = true;
            }

            int remainingUsers = entry.ReleaseUserRef();
            if (remainingUsers > 1)
            {
                return;
            }

            if (!entry.TryBeginClose())
            {
                return;
            }

            if (
                OutputFileLocks.TryGetValue(saveThumbFileName, out OutputFileLockEntry current)
                && ReferenceEquals(current, entry)
            )
            {
                OutputFileLocks.TryRemove(saveThumbFileName, out _);
            }

            if (released || !releaseSemaphore)
            {
                entry.Semaphore.Dispose();
            }
        }

        public static int GetEntryCountForTest()
        {
            return OutputFileLocks.Count;
        }
    }

    internal sealed class OutputFileLockEntry
    {
        private int refCount = 1;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        // 辞書から閉鎖中のエントリを掴んだ場合は false を返し、再取得へ回す。
        public bool TryAcquireUserRef()
        {
            while (true)
            {
                int current = Volatile.Read(ref refCount);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref refCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public int ReleaseUserRef()
        {
            return Interlocked.Decrement(ref refCount);
        }

        // 利用者ゼロを確認した瞬間だけ閉鎖状態へ遷移させる。
        public bool TryBeginClose()
        {
            return Interlocked.CompareExchange(ref refCount, -1, 1) == 1;
        }
    }
}

using System.IO;

namespace IndigoMovieManager
{
    /// <summary>
    /// 監視フォルダ編集画面へドロップされたパス群を、
    /// 追加候補とスキップ理由へ整理するポリシークラス。
    /// </summary>
    internal static class WatchFolderDropRegistrationPolicy
    {
        // ドロップされたパスの中に、登録可能なフォルダが1件でも含まれるかを返す。
        internal static bool CanAccept(IEnumerable<string> droppedPaths)
        {
            foreach (string droppedPath in droppedPaths ?? Array.Empty<string>())
            {
                if (HasPotentialDirectoryDropPath(droppedPath))
                {
                    return true;
                }
            }

            return false;
        }

        // Drop確定後の背景処理専用。ここだけ実在確認を行い、UIイベントをI/O待ちへ巻き込まない。
        internal static WatchFolderDropResult BuildAfterDropExistenceCheck(
            IEnumerable<string> droppedPaths,
            IEnumerable<string> existingDirectories
        )
        {
            var knownDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string existingDirectory in existingDirectories ?? Array.Empty<string>())
            {
                string normalizedExistingDirectory = NormalizeDirectoryPath(existingDirectory);
                if (!string.IsNullOrEmpty(normalizedExistingDirectory))
                {
                    knownDirectories.Add(normalizedExistingDirectory);
                }
            }

            var directoriesToAdd = new List<string>();
            int duplicateCount = 0;
            int invalidCount = 0;

            foreach (string droppedPath in droppedPaths ?? Array.Empty<string>())
            {
                string normalizedDroppedDirectory = NormalizeDirectoryPath(droppedPath);
                if (string.IsNullOrEmpty(normalizedDroppedDirectory) || !Directory.Exists(normalizedDroppedDirectory))
                {
                    invalidCount++;
                    continue;
                }

                if (!knownDirectories.Add(normalizedDroppedDirectory))
                {
                    duplicateCount++;
                    continue;
                }

                directoriesToAdd.Add(normalizedDroppedDirectory);
            }

            return new WatchFolderDropResult(directoriesToAdd, duplicateCount, invalidCount);
        }

        // DragOverでは文字列としてフォルダ候補かだけを見る。
        // 実在確認はDrop確定後の背景処理へ送る。
        private static bool HasPotentialDirectoryDropPath(string droppedPath)
        {
            string normalizedDroppedDirectory = NormalizeDirectoryPath(droppedPath);
            return !string.IsNullOrEmpty(normalizedDroppedDirectory)
                && string.IsNullOrWhiteSpace(Path.GetExtension(normalizedDroppedDirectory));
        }

        // 比較用にパス表記を正規化し、壊れた入力は空扱いへ落とす。
        internal static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(directoryPath.Trim());
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// フォルダドロップの判定結果を保持する。
    /// </summary>
    internal sealed class WatchFolderDropResult
    {
        internal WatchFolderDropResult(
            IReadOnlyList<string> directoriesToAdd,
            int duplicateCount,
            int invalidCount
        )
        {
            DirectoriesToAdd = directoriesToAdd;
            DuplicateCount = duplicateCount;
            InvalidCount = invalidCount;
        }

        internal IReadOnlyList<string> DirectoriesToAdd { get; }

        internal int DuplicateCount { get; }

        internal int InvalidCount { get; }
    }
}

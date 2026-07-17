using System.IO;

namespace IndigoMovieManager.BottomTabs.FileOrganizer
{
    internal sealed record FileOrganizerWatchFolder(
        string FolderPath,
        bool IsWatchEnabled,
        bool IncludeSubdirectories
    );

    internal static class FileOrganizerWatchCoveragePolicy
    {
        // 登録先そのもの、またはsub有効な親フォルダに含まれていれば監視対象と判断する。
        internal static bool IsCovered(
            string destinationFolder,
            IEnumerable<FileOrganizerWatchFolder> watchFolders
        )
        {
            string destination = Normalize(destinationFolder);
            if (string.IsNullOrEmpty(destination))
            {
                return false;
            }

            foreach (FileOrganizerWatchFolder watchFolder in watchFolders ?? [])
            {
                if (watchFolder == null || !watchFolder.IsWatchEnabled)
                {
                    continue;
                }

                string registered = Normalize(watchFolder.FolderPath);
                if (string.IsNullOrEmpty(registered))
                {
                    continue;
                }

                if (string.Equals(destination, registered, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (watchFolder.IncludeSubdirectories && IsChildOf(destination, registered))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsChildOf(string destination, string registered)
        {
            string registeredWithSeparator = registered.EndsWith(Path.DirectorySeparatorChar)
                ? registered
                : registered + Path.DirectorySeparatorChar;
            return destination.StartsWith(
                registeredWithSeparator,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string Normalize(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return "";
            }

            try
            {
                string fullPath = Path.GetFullPath(folderPath.Trim());
                string root = Path.GetPathRoot(fullPath) ?? "";
                return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                    ? fullPath
                    : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return "";
            }
        }
    }
}

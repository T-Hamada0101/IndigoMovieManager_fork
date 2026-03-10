using System.IO;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // MainDB単位で失敗履歴DBの保存先を一意に決める。
    public static class ThumbnailFailureDebugDbPathResolver
    {
        private const string FailureDbRootFolderName = "IndigoMovieManager_fork";
        private const string FailureDbFolderName = "FailureDb";

        public static string ResolveFailureDbPath(string mainDbFullPath)
        {
            string safeMainDbPath = mainDbFullPath ?? "";
            string dbName = Path.GetFileNameWithoutExtension(safeMainDbPath);
            if (string.IsNullOrWhiteSpace(dbName))
            {
                dbName = "main";
            }

            string normalizedDbName = SanitizeFileName(dbName);
            string hash8 = QueueDb.QueueDbPathResolver.GetMainDbPathHash8(safeMainDbPath);
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FailureDbRootFolderName,
                FailureDbFolderName
            );
            Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, $"{normalizedDbName}.{hash8}.failure-debug.imm");
        }

        public static string CreateMoviePathKey(string moviePath)
        {
            return QueueDb.QueueDbPathResolver.CreateMoviePathKey(moviePath);
        }

        private static string SanitizeFileName(string fileName)
        {
            string result = fileName ?? "";
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidChar, '_');
            }

            return result;
        }
    }
}

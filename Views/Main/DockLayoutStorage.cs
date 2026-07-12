using System;
using System.Collections.Generic;
using System.IO;

namespace IndigoMovieManager
{
    internal static class DockLayoutStorage
    {
        internal const string LayoutFileName = "layout.xml";
        internal const string DefaultLayoutFileName = "layout.default.xml";

        internal static string LayoutFilePath =>
            Path.Combine(AppLocalDataPaths.LayoutsPath, LayoutFileName);

        internal static string DefaultLayoutFilePath =>
            Path.Combine(AppLocalDataPaths.LayoutsPath, DefaultLayoutFileName);

        internal static IReadOnlyList<string> MigrateLegacyFiles(
            string legacyDirectoryPath,
            string targetDirectoryPath
        )
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectoryPath);

            Directory.CreateDirectory(targetDirectoryPath);
            List<string> migratedFiles = [];

            foreach (string fileName in new[] { LayoutFileName, DefaultLayoutFileName })
            {
                string sourcePath = Path.Combine(legacyDirectoryPath, fileName);
                string targetPath = Path.Combine(targetDirectoryPath, fileName);
                if (!File.Exists(sourcePath) || File.Exists(targetPath))
                {
                    continue;
                }

                // 旧インストール先からユーザーデータ領域へ移し、次回アンインストールで残置させない。
                File.Move(sourcePath, targetPath);
                migratedFiles.Add(fileName);
            }

            return migratedFiles;
        }
    }
}

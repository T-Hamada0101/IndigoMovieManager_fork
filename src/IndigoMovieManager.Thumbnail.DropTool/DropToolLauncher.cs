using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace IndigoMovieManager.Thumbnail.DropTool
{
    internal static class DropToolLauncher
    {
        public static int Run(string[] args)
        {
            string manifestPath = "";

            try
            {
                string workerExecutablePath = DropToolLaunchSupport.ResolveWorkerExecutablePath(
                    AppContext.BaseDirectory
                );
                if (string.IsNullOrWhiteSpace(workerExecutablePath))
                {
                    MessageBox.Show(
                        "Worker.exe が見つかりませんでした。",
                        "起動エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return 1;
                }

                List<string> droppedPaths = DropToolLaunchSupport.NormalizePaths(args);
                if ((args?.Length ?? 0) > 0 && droppedPaths.Count < 1)
                {
                    MessageBox.Show(
                        "Drop.exe にはファイルまたはフォルダをドロップしてください。",
                        "入力なし",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return 1;
                }

                if (droppedPaths.Count > 0)
                {
                    // 受け取った入力だけ一時manifestへ落とし、Worker UIへ初期状態として引き継ぐ。
                    manifestPath = DropToolLaunchSupport.CreateManifestFile(droppedPaths);
                }

                DropToolLaunchSupport.StartWorkerProcess(workerExecutablePath, manifestPath);
                return 0;
            }
            catch (Exception ex)
            {
                TryDeleteManifestQuietly(manifestPath);
                MessageBox.Show(
                    $"Worker.exe の起動に失敗しました: {ex.Message}",
                    "起動エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return 1;
            }
        }

        private static void TryDeleteManifestQuietly(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return;
            }

            try
            {
                File.Delete(manifestPath);
            }
            catch
            {
                // handoff失敗時の一時ファイル掃除はベストエフォートにする。
            }
        }
    }
}

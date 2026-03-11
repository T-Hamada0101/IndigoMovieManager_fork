using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace IndigoMovieManager.Thumbnail.DropTool
{
    public sealed class DropToolStartupContext
    {
        public IReadOnlyList<string> InitialInputPaths { get; init; } = [];

        public string StartupMessage { get; init; } = "";
    }

    internal sealed class DropToolLaunchManifest
    {
        public List<string> InputPaths { get; set; } = [];
    }

    internal static class DropToolLaunchSupport
    {
        private const string DropManifestFolderName = "drop-manifests";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        // Explorer ドロップの引数を、そのまま Worker へ渡せる実在パスへ整える。
        public static List<string> NormalizePaths(IEnumerable<string> paths)
        {
            HashSet<string> uniquePaths = new(StringComparer.OrdinalIgnoreCase);
            if (paths == null)
            {
                return [];
            }

            foreach (string rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(rawPath);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    continue;
                }

                uniquePaths.Add(fullPath);
            }

            return uniquePaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // 受け渡し情報は一時manifestへ落として、長いコマンドラインを避ける。
        public static string CreateManifestFile(IEnumerable<string> inputPaths)
        {
            List<string> normalizedPaths = NormalizePaths(inputPaths);
            string manifestRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork",
                DropManifestFolderName
            );
            Directory.CreateDirectory(manifestRoot);

            string manifestPath = Path.Combine(
                manifestRoot,
                $"drop-manifest-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"
            );
            DropToolLaunchManifest manifest = new()
            {
                InputPaths = normalizedPaths,
            };

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions)
            );
            return manifestPath;
        }

        // Worker 起動時にmanifestを読み、役目を終えた一時ファイルは消しておく。
        public static DropToolStartupContext LoadStartupContext(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return new DropToolStartupContext();
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                DropToolLaunchManifest manifest =
                    JsonSerializer.Deserialize<DropToolLaunchManifest>(json, JsonOptions) ?? new();

                return new DropToolStartupContext
                {
                    InitialInputPaths = NormalizePaths(manifest.InputPaths),
                    StartupMessage = "Drop.exe から入力を引き継ぎました。",
                };
            }
            catch (Exception ex)
            {
                return new DropToolStartupContext
                {
                    StartupMessage =
                        $"Drop.exe からの入力引き継ぎに失敗しました: {ex.Message}",
                };
            }
            finally
            {
                TryDeleteFileQuietly(manifestPath);
            }
        }

        // Drop.exe と Worker.exe は同梱配置も、個別bin起動も拾える候補順で解決する。
        public static string ResolveWorkerExecutablePath(string appBaseDirectory)
        {
            List<string> candidatePaths =
            [
                Path.Combine(
                    appBaseDirectory ?? "",
                    "..",
                    "thumbnail-worker",
                    "IndigoMovieManager.Thumbnail.Worker.exe"
                ),
                Path.Combine(appBaseDirectory ?? "", "IndigoMovieManager.Thumbnail.Worker.exe"),
                Path.Combine(
                    appBaseDirectory ?? "",
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "IndigoMovieManager.Thumbnail.Worker",
                    "bin",
                    "x64",
                    "Debug",
                    "net8.0-windows",
                    "IndigoMovieManager.Thumbnail.Worker.exe"
                ),
                Path.Combine(
                    appBaseDirectory ?? "",
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "IndigoMovieManager.Thumbnail.Worker",
                    "bin",
                    "x64",
                    "Release",
                    "net8.0-windows",
                    "IndigoMovieManager.Thumbnail.Worker.exe"
                ),
            ];

            foreach (string candidatePath in candidatePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(candidatePath);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // 候補解決失敗は次候補へ進む。
                }
            }

            return "";
        }

        public static void StartWorkerProcess(string workerExecutablePath, string manifestPath)
        {
            ProcessStartInfo startInfo = new(workerExecutablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(workerExecutablePath) ?? "",
            };

            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                startInfo.Arguments =
                    $"--drop-manifest {QuoteArgument(manifestPath)}";
            }

            Process.Start(startInfo);
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時manifestの掃除失敗は動作を優先する。
            }
        }
    }
}

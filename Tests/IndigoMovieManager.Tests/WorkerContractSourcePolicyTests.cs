using System.IO;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WorkerContractSourcePolicyTests
{
    private static readonly string[] WorkerContractSourceDirectories =
    [
        "src/IndigoMovieManager.Thumbnail.Queue",
    ];

    private static readonly string[] WorkerContractSourceFiles =
    [
        "Thumbnail/ThumbnailRescueWorkerJobJsonClient.cs",
        "Watcher/WatchMetadataProbeWorkerContractAdapter.cs",
    ];

    private static readonly string[] ForbiddenUiFragments =
    [
        "using System.Windows",
        "System.Windows.",
        "Windows.Threading",
        "Dispatcher",
        "DispatcherTimer",
        "ViewModel",
        "ViewModels",
        "ObservableCollection",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Xaml",
        "Microsoft.Xaml",
        "Microsoft.Web.WebView2",
        "WebView2",
        "MainWindow",
    ];

    [Test]
    public void Worker契約SourceはWpfDispatcherViewModelを参照しない()
    {
        string repoRoot = FindRepoRoot();
        string[] relativePaths = EnumerateWorkerContractSourceFiles(repoRoot).ToArray();

        Assert.That(
            relativePaths,
            Does.Contain("src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs")
        );
        Assert.That(
            relativePaths,
            Does.Contain("src/IndigoMovieManager.Thumbnail.Queue/ThumbnailProgressRuntime.cs")
        );
        Assert.That(
            relativePaths,
            Does.Contain("src/IndigoMovieManager.Thumbnail.Queue/ThumbnailQueueProcessor.cs")
        );
        Assert.That(
            relativePaths,
            Does.Contain(
                "src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/ThumbnailQueueWorkerContractAdapter.cs"
            )
        );
        Assert.That(
            relativePaths,
            Does.Contain("Thumbnail/ThumbnailRescueWorkerJobJsonClient.cs")
        );
        Assert.That(
            relativePaths,
            Does.Contain("Watcher/WatchMetadataProbeWorkerContractAdapter.cs")
        );

        foreach (string relativePath in relativePaths)
        {
            string source = File.ReadAllText(ToAbsolutePath(repoRoot, relativePath));

            // Worker契約はUIを知らないことが肝なので、WPF/ViewModel語が混ざった瞬間に止める。
            foreach (string forbidden in ForbiddenUiFragments)
            {
                Assert.That(
                    source,
                    Does.Not.Contain(forbidden),
                    $"{relativePath} に UI 層参照 '{forbidden}' を入れないでください。"
                );
            }
        }
    }

    [Test]
    public void ThumbnailQueueProjectはWpfDesktop参照を有効化しない()
    {
        string repoRoot = FindRepoRoot();
        string projectSource = File.ReadAllText(
            ToAbsolutePath(
                repoRoot,
                "src/IndigoMovieManager.Thumbnail.Queue/IndigoMovieManager.Thumbnail.Queue.csproj"
            )
        );

        Assert.That(projectSource, Does.Not.Contain("<UseWPF>true</UseWPF>"));
        Assert.That(projectSource, Does.Not.Contain("Microsoft.WindowsDesktop.App.WPF"));
        Assert.That(projectSource, Does.Not.Contain("PresentationCore"));
        Assert.That(projectSource, Does.Not.Contain("PresentationFramework"));
        Assert.That(projectSource, Does.Not.Contain("WindowsBase"));
        Assert.That(projectSource, Does.Not.Contain("Microsoft.Xaml"));
    }

    [Test]
    public void Worker契約Dtoはロードマップ語彙を保持する()
    {
        string repoRoot = FindRepoRoot();
        string dtoSource = File.ReadAllText(
            ToAbsolutePath(
                repoRoot,
                "src/IndigoMovieManager.Thumbnail.Queue/Ipc/ThumbnailIpcDtos.cs"
            )
        );

        Assert.That(dtoSource, Does.Contain("WorkerJobRequestDto"));
        Assert.That(dtoSource, Does.Contain("WorkerJobResultDto"));
        Assert.That(dtoSource, Does.Contain("WorkerJobProgressDto"));
        Assert.That(dtoSource, Does.Contain("WorkerJobArtifactDto"));
        Assert.That(dtoSource, Does.Contain("InputFiles"));
        Assert.That(dtoSource, Does.Contain("OutputArtifactPath"));
        Assert.That(dtoSource, Does.Contain("DiagnosticContext"));
        Assert.That(dtoSource, Does.Contain("Retryability"));
    }

    [Test]
    public void ThumbnailQueueRequestはWorker契約Dtoへ写せる()
    {
        string repoRoot = FindRepoRoot();
        string adapterSource = File.ReadAllText(
            ToAbsolutePath(
                repoRoot,
                "src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/ThumbnailQueueWorkerContractAdapter.cs"
            )
        );

        Assert.That(adapterSource, Does.Contain("ToWorkerJobRequestDto("));
        Assert.That(adapterSource, Does.Contain("WorkerJobRequestDto"));
        Assert.That(adapterSource, Does.Contain("thumbnail-create"));
        Assert.That(adapterSource, Does.Contain("DiagnosticContext"));
        Assert.That(adapterSource, Does.Not.Contain("Dispatcher"));
        Assert.That(adapterSource, Does.Not.Contain("MainWindow"));
    }

    [Test]
    public void ThumbnailQueue実行結果はWorker契約Dtoへ写せる()
    {
        string repoRoot = FindRepoRoot();
        string adapterSource = File.ReadAllText(
            ToAbsolutePath(
                repoRoot,
                "src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/ThumbnailQueueWorkerContractAdapter.cs"
            )
        );

        Assert.That(adapterSource, Does.Contain("ToWorkerJobResultDto("));
        Assert.That(adapterSource, Does.Contain("WorkerJobResultDto"));
        Assert.That(adapterSource, Does.Contain("WorkerJobArtifactDto"));
        Assert.That(adapterSource, Does.Contain("failureKind"));
        Assert.That(adapterSource, Does.Contain("elapsedMs"));
        Assert.That(adapterSource, Does.Contain("retryable"));
        Assert.That(adapterSource, Does.Contain("metrics"));
        Assert.That(adapterSource, Does.Not.Contain("Path.Exists"));
        Assert.That(adapterSource, Does.Not.Contain("File."));
        Assert.That(adapterSource, Does.Not.Contain("Directory."));
        Assert.That(adapterSource, Does.Not.Contain("Dispatcher"));
        Assert.That(adapterSource, Does.Not.Contain("MainWindow"));
    }

    [Test]
    public void ThumbnailQueue進捗はWorker契約Dtoへ写せる()
    {
        string repoRoot = FindRepoRoot();
        string adapterSource = File.ReadAllText(
            ToAbsolutePath(
                repoRoot,
                "src/IndigoMovieManager.Thumbnail.Queue/QueuePipeline/ThumbnailQueueWorkerContractAdapter.cs"
            )
        );

        Assert.That(adapterSource, Does.Contain("WorkerJobProgressDto"));
        Assert.That(adapterSource, Does.Contain("ThumbnailProgressRuntimeSnapshot"));
        Assert.That(adapterSource, Does.Contain("ProgressStageRunning"));
        Assert.That(adapterSource, Does.Contain("CompletedCount"));
        Assert.That(adapterSource, Does.Contain("TotalCount"));
        Assert.That(adapterSource, Does.Contain("CurrentInputFile"));
        Assert.That(adapterSource, Does.Not.Contain("Path.Exists"));
        Assert.That(adapterSource, Does.Not.Contain("File."));
        Assert.That(adapterSource, Does.Not.Contain("Directory."));
        Assert.That(adapterSource, Does.Not.Contain("Dispatcher"));
        Assert.That(adapterSource, Does.Not.Contain("MainWindow"));
    }

    [Test]
    public void MetadataProbeはWorker契約Dtoへ写せる()
    {
        string repoRoot = FindRepoRoot();
        string adapterSource = File.ReadAllText(
            ToAbsolutePath(repoRoot, "Watcher/WatchMetadataProbeWorkerContractAdapter.cs")
        );

        Assert.That(adapterSource, Does.Contain("ToWorkerJobRequestDto("));
        Assert.That(adapterSource, Does.Contain("ToWorkerJobResultDto("));
        Assert.That(adapterSource, Does.Contain("WorkerJobRequestDto"));
        Assert.That(adapterSource, Does.Contain("WorkerJobResultDto"));
        Assert.That(adapterSource, Does.Contain("WorkerJobArtifactDto"));
        Assert.That(adapterSource, Does.Contain("metadata-probe"));
        Assert.That(adapterSource, Does.Contain("watch-metadata-probe"));
        Assert.That(adapterSource, Does.Contain("DiagnosticContext"));
        Assert.That(adapterSource, Does.Not.Contain("Path.Exists"));
        Assert.That(adapterSource, Does.Not.Contain("File."));
        Assert.That(adapterSource, Does.Not.Contain("Directory."));
        Assert.That(adapterSource, Does.Not.Contain("Dispatcher"));
        Assert.That(adapterSource, Does.Not.Contain("MainWindow"));
    }

    private static IEnumerable<string> EnumerateWorkerContractSourceFiles(string repoRoot)
    {
        foreach (string sourceDirectory in WorkerContractSourceDirectories)
        {
            string absoluteDirectory = ToAbsolutePath(repoRoot, sourceDirectory);
            Assert.That(
                Directory.Exists(absoluteDirectory),
                Is.True,
                $"{sourceDirectory} が見つかりません。"
            );

            foreach (
                string fullPath in Directory.EnumerateFiles(
                    absoluteDirectory,
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            {
                if (IsBuildOutputPath(fullPath))
                {
                    continue;
                }

                yield return ToRelativeRepositoryPath(repoRoot, fullPath);
            }
        }

        foreach (string sourceFile in WorkerContractSourceFiles)
        {
            string absolutePath = ToAbsolutePath(repoRoot, sourceFile);
            Assert.That(File.Exists(absolutePath), Is.True, $"{sourceFile} が見つかりません。");
            yield return sourceFile;
        }
    }

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        string startDirectory =
            Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory();
        DirectoryInfo? current = new(startDirectory);
        while (current != null)
        {
            if (
                File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))
            )
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("repo root を解決できませんでした。");
        return "";
    }

    private static bool IsBuildOutputPath(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToAbsolutePath(string repoRoot, string relativePath)
    {
        return Path.Combine(
            [repoRoot, .. relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)]
        );
    }

    private static string ToRelativeRepositoryPath(string repoRoot, string fullPath)
    {
        return Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
    }
}

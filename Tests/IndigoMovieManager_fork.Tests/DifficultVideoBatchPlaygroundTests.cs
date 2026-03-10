using System.Diagnostics;
using System.Text;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DifficultVideoBatchPlaygroundTests
{
    private static readonly HashSet<string> TargetExtensions = new(
        [".mp4", ".mkv", ".flv", ".avi", ".wmv", ".asf", ".swf"],
        StringComparer.OrdinalIgnoreCase
    );

    // 実動画フォルダ依存のため、既定ルートは持たず環境変数からだけ受け取る。
    private const string DefaultRootPath = "";
    private const string RootPathEnvName = "IMM_TEST_DIFFICULT_VIDEO_ROOT";

    [Test]
    [Explicit("実動画フォルダ依存。IMM_TEST_DIFFICULT_VIDEO_ROOT で対象ルートを差し替え可能。")]
    public async Task 実動画フォルダ配下を_現行本線で一括試行できる()
    {
        string rootPath = ResolveRootPath();
        if (!Directory.Exists(rootPath))
        {
            Assert.Ignore($"対象フォルダが見つかりません: {rootPath}");
        }

        string tempRoot = CreateTempRoot();
        try
        {
            ThumbnailCreationService service = new();
            List<string> moviePaths = EnumerateTargetMoviePaths(rootPath);
            if (moviePaths.Count < 1)
            {
                Assert.Ignore($"対象動画がありません: {rootPath}");
            }

            List<BatchAttemptResult> results = [];
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            Directory.CreateDirectory(thumbRoot);

            TestContext.Out.WriteLine($"root={rootPath}");
            TestContext.Out.WriteLine($"count={moviePaths.Count}");
            TestContext.Out.WriteLine($"work={tempRoot}");

            int movieId = 1;
            foreach (string moviePath in moviePaths)
            {
                BatchAttemptResult result = await RunSingleAttemptAsync(
                    service,
                    moviePath,
                    thumbRoot,
                    movieId++
                );
                results.Add(result);
                TestContext.Out.WriteLine(
                    $"batch attempt: success={result.IsSuccess} elapsed_ms={result.ElapsedMs} ext={result.Extension} movie='{result.MoviePath}' error='{result.ErrorMessage}'"
                );
            }

            string summaryPath = Path.Combine(tempRoot, "difficult-video-batch-summary.csv");
            WriteSummaryCsv(summaryPath, results);
            string publishedSummaryPath = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_tests",
                "difficult-video-batch-summary-latest.csv"
            );
            string artifactSummaryPath = Path.Combine(
                AppContext.BaseDirectory,
                "difficult-video-batch-summary-latest.csv"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(publishedSummaryPath) ?? tempRoot);
            File.Copy(summaryPath, publishedSummaryPath, overwrite: true);
            File.Copy(summaryPath, artifactSummaryPath, overwrite: true);
            TestContext.Out.WriteLine($"summary={summaryPath}");
            TestContext.Out.WriteLine($"published_summary={publishedSummaryPath}");
            TestContext.Out.WriteLine($"artifact_summary={artifactSummaryPath}");
            TestContext.Out.WriteLine(
                $"success={results.Count(x => x.IsSuccess)} failed={results.Count(x => !x.IsSuccess)}"
            );

            Assert.That(results.Count, Is.EqualTo(moviePaths.Count));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string ResolveRootPath()
    {
        string configuredPath = Environment.GetEnvironmentVariable(RootPathEnvName)?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(configuredPath) ? DefaultRootPath : configuredPath;
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            "difficult_video_batch_playground",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static List<string> EnumerateTargetMoviePaths(string rootPath)
    {
        return Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => TargetExtensions.Contains(Path.GetExtension(path) ?? ""))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<BatchAttemptResult> RunSingleAttemptAsync(
        ThumbnailCreationService service,
        string moviePath,
        string thumbRoot,
        int movieId
    )
    {
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            ThumbnailCreateResult result = await service.CreateThumbAsync(
                new QueueObj
                {
                    MovieId = movieId,
                    Tabindex = 2,
                    MovieFullPath = moviePath,
                    AttemptCount = 0,
                },
                dbName: "batch",
                thumbFolder: thumbRoot,
                isResizeThumb: true,
                isManual: false
            );
            sw.Stop();

            return new BatchAttemptResult(
                moviePath,
                Path.GetExtension(moviePath) ?? "",
                result?.IsSuccess == true,
                sw.ElapsedMilliseconds,
                result?.ErrorMessage ?? "",
                result?.SaveThumbFileName ?? ""
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BatchAttemptResult(
                moviePath,
                Path.GetExtension(moviePath) ?? "",
                false,
                sw.ElapsedMilliseconds,
                $"{ex.GetType().Name}: {ex.Message}",
                ""
            );
        }
    }

    private static void WriteSummaryCsv(string outputPath, IEnumerable<BatchAttemptResult> results)
    {
        StringBuilder builder = new();
        builder.AppendLine("movie_path,extension,is_success,elapsed_ms,error_message,output_path");
        foreach (BatchAttemptResult result in results)
        {
            builder.AppendLine(
                string.Join(
                    ",",
                    EscapeCsv(result.MoviePath),
                    EscapeCsv(result.Extension),
                    result.IsSuccess ? "true" : "false",
                    result.ElapsedMs.ToString(),
                    EscapeCsv(result.ErrorMessage),
                    EscapeCsv(result.OutputPath)
                )
            );
        }

        Encoding encoding = new UTF8Encoding(false);
        File.WriteAllText(outputPath, builder.ToString(), encoding);
    }

    private static string EscapeCsv(string value)
    {
        string text = value ?? "";
        if (text.Contains('"'))
        {
            text = text.Replace("\"", "\"\"");
        }

        if (text.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            return $"\"{text}\"";
        }

        return text;
    }

    private sealed record BatchAttemptResult(
        string MoviePath,
        string Extension,
        bool IsSuccess,
        long ElapsedMs,
        string ErrorMessage,
        string OutputPath
    );
}

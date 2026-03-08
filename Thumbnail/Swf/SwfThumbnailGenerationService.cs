using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF用の代表フレーム候補を順に試し、採用フレームを決める。
    /// </summary>
    internal class SwfThumbnailGenerationService
    {
        private const string FfmpegExePathEnvName = "IMM_FFMPEG_EXE_PATH";
        private readonly SwfEmbeddedImageExtractor embeddedImageExtractor = new();

        public virtual async Task<SwfThumbnailCandidate> TryCaptureRepresentativeFrameAsync(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options,
            CancellationToken cts = default
        )
        {
            options ??= SwfThumbnailCaptureOptions.CreateDefault(320, 240);

            if (string.IsNullOrWhiteSpace(swfInputPath) || !Path.Exists(swfInputPath))
            {
                return SwfThumbnailCandidate.CreateRejected(
                    0d,
                    outputPath,
                    "swf input file is missing",
                    "",
                    false,
                    false
                );
            }

            string outputDir = Path.GetDirectoryName(outputPath) ?? "";
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            SwfEmbeddedImageExtractionResult extractionResult = embeddedImageExtractor
                .TryExtractRepresentativeImage(swfInputPath, outputPath, options);
            if (extractionResult.IsSuccess && Path.Exists(extractionResult.OutputPath))
            {
                SwfThumbnailCandidate extractedCandidate = SwfThumbnailCandidate.CreateAccepted(
                    0d,
                    extractionResult.OutputPath,
                    "extract"
                );
                Debug.WriteLine(
                    $"swf embedded image accepted: detail='{extractionResult.Detail}', out='{extractionResult.OutputPath}', size={extractionResult.Width}x{extractionResult.Height}"
                );
                return extractedCandidate;
            }

            if (!string.IsNullOrWhiteSpace(extractionResult.Detail))
            {
                Debug.WriteLine($"swf embedded image skipped: reason='{extractionResult.Detail}'");
            }

            return await TryCaptureWithFfmpegCandidatesAsync(
                swfInputPath,
                outputPath,
                options,
                cts
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// 埋め込み画像で救えなかった時だけ、既存のffmpeg候補試行へ落とす。
        /// テストではこの入口を差し替えて、縮退経路の発火有無を検証する。
        /// </summary>
        protected virtual async Task<SwfThumbnailCandidate> TryCaptureWithFfmpegCandidatesAsync(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options,
            CancellationToken cts
        )
        {
            string ffmpegExePath = ResolveFfmpegExecutablePath();
            SwfThumbnailCandidate lastCandidate = null;

            // 候補秒数を先頭から順に試し、白画面寄りでなければ即採用する。
            foreach (double captureSec in options.CandidateSeconds)
            {
                cts.ThrowIfCancellationRequested();

                string tempOutputPath = BuildTempOutputPath(outputPath, captureSec);
                TryDeleteFile(tempOutputPath);

                ProcessStartInfo psi = SwfThumbnailFfmpegCommandBuilder.BuildProcessStartInfo(
                    ffmpegExePath,
                    swfInputPath,
                    tempOutputPath,
                    captureSec,
                    options
                );

                (bool ok, string err) = await RunProcessAsync(
                    psi,
                    options.ProcessTimeout,
                    cts
                ).ConfigureAwait(false);

                if (!ok || !Path.Exists(tempOutputPath))
                {
                    lastCandidate = SwfThumbnailCandidate.CreateRejected(
                        captureSec,
                        tempOutputPath,
                        "ffmpeg capture failed",
                        err,
                        false,
                        false,
                        "ffmpeg"
                    );
                    Debug.WriteLine($"swf candidate rejected: {lastCandidate.ToLogText()}");
                    continue;
                }

                bool analyzed = SwfThumbnailFrameAnalyzer.TryAnalyzeFrame(
                    tempOutputPath,
                    options,
                    out bool isMostlyFlatBrightFrame
                );
                if (!analyzed)
                {
                    lastCandidate = SwfThumbnailCandidate.CreateRejected(
                        captureSec,
                        tempOutputPath,
                        "captured image is unreadable",
                        "",
                        false,
                        true,
                        "ffmpeg"
                    );
                    Debug.WriteLine($"swf candidate unreadable reject: {lastCandidate.ToLogText()}");
                    TryDeleteFile(tempOutputPath);
                    continue;
                }

                if (isMostlyFlatBrightFrame)
                {
                    lastCandidate = SwfThumbnailCandidate.CreateRejected(
                        captureSec,
                        tempOutputPath,
                        "frame looks like loading or blank screen",
                        "",
                        true,
                        true,
                        "ffmpeg"
                    );
                    Debug.WriteLine($"swf candidate bright reject: {lastCandidate.ToLogText()}");
                    TryDeleteFile(tempOutputPath);
                    continue;
                }

                try
                {
                    TryDeleteFile(outputPath);
                    File.Move(tempOutputPath, outputPath);
                    lastCandidate = SwfThumbnailCandidate.CreateAccepted(
                        captureSec,
                        outputPath,
                        "ffmpeg"
                    );
                    Debug.WriteLine($"swf candidate accepted: {lastCandidate.ToLogText()}");
                    return lastCandidate;
                }
                catch (Exception ex)
                {
                    lastCandidate = SwfThumbnailCandidate.CreateRejected(
                        captureSec,
                        tempOutputPath,
                        $"failed to move output: {ex.Message}",
                        "",
                        false,
                        true,
                        "ffmpeg"
                    );
                    Debug.WriteLine($"swf candidate move reject: {lastCandidate.ToLogText()}");
                    TryDeleteFile(tempOutputPath);
                }
            }

            return lastCandidate
                ?? SwfThumbnailCandidate.CreateRejected(
                    0d,
                    outputPath,
                    "no swf capture candidate configured",
                    "",
                    false,
                    false,
                    "ffmpeg"
                );
        }

        private static async Task<(bool ok, string err)> RunProcessAsync(
            ProcessStartInfo psi,
            TimeSpan timeout,
            CancellationToken cts
        )
        {
            try
            {
                using Process process = new() { StartInfo = psi };
                if (!process.Start())
                {
                    return (false, "process start returned false");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                Task waitForExitTask = process.WaitForExitAsync();
                Task cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cts);
                Task completedTask;
                bool hasTimeout = timeout != Timeout.InfiniteTimeSpan;
                Task timeoutTask = Task.CompletedTask;

                if (!hasTimeout)
                {
                    completedTask = await Task.WhenAny(waitForExitTask, cancelTask).ConfigureAwait(false);
                }
                else
                {
                    timeoutTask = Task.Delay(timeout);
                    completedTask = await Task
                        .WhenAny(waitForExitTask, timeoutTask, cancelTask)
                        .ConfigureAwait(false);
                }

                if (completedTask == cancelTask)
                {
                    TryKillProcess(process);
                    throw new OperationCanceledException(cts);
                }

                if (hasTimeout && completedTask == timeoutTask)
                {
                    TryKillProcess(process);
                    await waitForExitTask.ConfigureAwait(false);
                    _ = await stdoutTask.ConfigureAwait(false);
                    string timeoutError = await SafeReadProcessErrorAsync(stderrTask).ConfigureAwait(false);
                    return (
                        false,
                        string.IsNullOrWhiteSpace(timeoutError)
                            ? $"ffmpeg timeout after {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s"
                            : timeoutError
                    );
                }

                await waitForExitTask.ConfigureAwait(false);
                _ = await stdoutTask.ConfigureAwait(false);
                string stderr = await SafeReadProcessErrorAsync(stderrTask).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return (false, $"exit={process.ExitCode}, err={stderr}");
                }

                return (true, stderr);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static async Task<string> SafeReadProcessErrorAsync(Task<string> stderrTask)
        {
            try
            {
                return await stderrTask.ConfigureAwait(false);
            }
            catch
            {
                return "";
            }
        }

        private static string BuildTempOutputPath(string outputPath, double captureSec)
        {
            string dir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
            string fileName = Path.GetFileNameWithoutExtension(outputPath);
            string ext = Path.GetExtension(outputPath);
            string secText = captureSec.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');
            return Path.Combine(dir, $"{fileName}.swf.{secText}.{Guid.NewGuid():N}{ext}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Path.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時ファイル掃除失敗では処理を止めない。
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 停止失敗でも次の縮退を優先する。
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(FfmpegExePathEnvName);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string normalizedConfiguredPath = configuredPath.Trim().Trim('"');
                if (File.Exists(normalizedConfiguredPath))
                {
                    return normalizedConfiguredPath;
                }

                if (Directory.Exists(normalizedConfiguredPath))
                {
                    string candidate = Path.Combine(normalizedConfiguredPath, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;
            string[] bundledCandidates =
            [
                Path.Combine(baseDir, "ffmpeg.exe"),
                Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "ffmpeg.exe"),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", "ffmpeg.exe"),
            ];

            foreach (string candidate in bundledCandidates)
            {
                if (Path.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "ffmpeg";
        }
    }
}

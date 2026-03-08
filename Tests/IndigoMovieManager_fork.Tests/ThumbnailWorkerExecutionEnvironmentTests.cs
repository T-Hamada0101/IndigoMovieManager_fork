using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailWorkerExecutionEnvironmentTests
{
    [Test]
    public void Apply_GpuOff_AlwaysForcesOff()
    {
        string? previousGpu = Environment.GetEnvironmentVariable(
            ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName
        );

        try
        {
            Environment.SetEnvironmentVariable(
                ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName,
                "cuda"
            );

            ThumbnailWorkerExecutionEnvironment.Apply(
                new ThumbnailWorkerResolvedSettings
                {
                    GpuDecodeEnabled = false,
                    SlowLaneMinGb = 50,
                    ProcessPriorityName = "BelowNormal",
                    FfmpegPriorityName = "Idle",
                }
            );

            Assert.That(
                Environment.GetEnvironmentVariable(
                    ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName
                ),
                Is.EqualTo("off")
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName,
                previousGpu
            );
        }
    }

    [Test]
    public void Apply_GpuOn_InheritsKnownMode()
    {
        string? previousGpu = Environment.GetEnvironmentVariable(
            ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName
        );

        try
        {
            Environment.SetEnvironmentVariable(
                ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName,
                "qsv"
            );

            ThumbnailWorkerExecutionEnvironment.Apply(
                new ThumbnailWorkerResolvedSettings
                {
                    GpuDecodeEnabled = true,
                    SlowLaneMinGb = 50,
                    ProcessPriorityName = "BelowNormal",
                    FfmpegPriorityName = "Idle",
                }
            );

            Assert.That(
                Environment.GetEnvironmentVariable(
                    ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName
                ),
                Is.EqualTo("qsv")
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ThumbnailWorkerExecutionEnvironment.GpuDecodeModeEnvName,
                previousGpu
            );
        }
    }
}

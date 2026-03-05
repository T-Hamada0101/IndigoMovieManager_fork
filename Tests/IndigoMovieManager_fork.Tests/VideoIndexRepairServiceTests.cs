using IndigoMovieManager.Thumbnail.Engines.IndexRepair;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public class VideoIndexRepairServiceTests
{
    [Test]
    public async Task RepairAsync_入力出力同一パスは失敗する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "same.flv");
            File.WriteAllBytes(moviePath, [0x46, 0x4C, 0x56, 0x01]);
            var service = new VideoIndexRepairService();

            VideoIndexRepairResult result = await service.RepairAsync(moviePath, moviePath);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.ErrorMessage,
                Does.Contain("input and output path must be different")
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task RepairAsync_非許可拡張子は失敗する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "input.flv");
            string outputPath = Path.Combine(tempRoot, "fixed.flv");
            File.WriteAllBytes(moviePath, [0x46, 0x4C, 0x56, 0x01]);
            var service = new VideoIndexRepairService();

            VideoIndexRepairResult result = await service.RepairAsync(moviePath, outputPath);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("output extension must be .mp4 or .mkv"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task ProbeAsync_ファイル未存在は未検知で返す()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string moviePath = Path.Combine(tempRoot, "missing.flv");
            var service = new VideoIndexRepairService();

            VideoIndexProbeResult result = await service.ProbeAsync(moviePath);

            Assert.That(result.IsIndexCorruptionDetected, Is.False);
            Assert.That(result.DetectionReason, Is.EqualTo("movie file not found"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }
}

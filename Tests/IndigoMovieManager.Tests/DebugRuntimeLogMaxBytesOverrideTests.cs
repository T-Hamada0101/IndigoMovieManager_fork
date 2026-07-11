using System.Text;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DebugRuntimeLogMaxBytesOverrideTests
{
    private const string LogPathEnvironmentVariable = "INDIGO_DEBUG_RUNTIME_LOG_PATH";
    private const string LogMaxBytesEnvironmentVariable = "INDIGO_DEBUG_RUNTIME_LOG_MAX_BYTES";
    private string? _originalLogPath;
    private string? _originalMaxBytes;
    private string _temporaryDirectory = "";

    [SetUp]
    public void SetUp()
    {
        _originalLogPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        _originalMaxBytes = Environment.GetEnvironmentVariable(LogMaxBytesEnvironmentVariable);
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager.Tests",
            Guid.NewGuid().ToString("N")
        );
        DebugRuntimeLog.ResetThrottleStateForTests();
        IndigoMovieManager.Properties.Settings.Default.DebugLogOtherEnabled = true;
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(LogPathEnvironmentVariable, _originalLogPath);
        Environment.SetEnvironmentVariable(LogMaxBytesEnvironmentVariable, _originalMaxBytes);

        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch
        {
            // テスト後始末の失敗で本来の検証結果を隠さない。
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("invalid")]
    [TestCase("0")]
    [TestCase("1048575")]
    [TestCase("-1")]
    [TestCase("268435457")]
    public void ResolveMaxLogFileBytesForTesting_未指定と範囲外は既定20MBを維持する(
        string? requestedMaxBytes
    )
    {
        Assert.That(
            DebugRuntimeLog.ResolveMaxLogFileBytesForTesting(requestedMaxBytes!),
            Is.EqualTo(20L * 1024 * 1024)
        );
    }

    [TestCase("1048576", 1048576L)]
    [TestCase("134217728", 134217728L)]
    [TestCase("268435456", 268435456L)]
    public void ResolveMaxLogFileBytesForTesting_安全範囲の整数値を採用する(
        string requestedMaxBytes,
        long expectedMaxBytes
    )
    {
        Assert.That(
            DebugRuntimeLog.ResolveMaxLogFileBytesForTesting(requestedMaxBytes),
            Is.EqualTo(expectedMaxBytes)
        );
    }

    [Test]
    public void Write_上書きした上限で既存ログを退避して新規行を追記する()
    {
        string logPath = Path.Combine(_temporaryDirectory, "runtime", "audit.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllBytes(logPath, new byte[1024 * 1024]);
        Environment.SetEnvironmentVariable(LogPathEnvironmentVariable, logPath);
        Environment.SetEnvironmentVariable(LogMaxBytesEnvironmentVariable, "1048576");

        DebugRuntimeLog.Write("max-bytes-override-test", "rotated write");

        string currentContent = File.ReadAllText(logPath, Encoding.UTF8);
        Assert.Multiple(() =>
        {
            Assert.That(currentContent, Does.Contain("[max-bytes-override-test] rotated write"));
            Assert.That(currentContent.Length, Is.LessThan(1024 * 1024));
            Assert.That(
                Directory.GetFiles(Path.GetDirectoryName(logPath)!),
                Has.Length.EqualTo(2)
            );
        });
    }
}

using System.Text;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class DebugRuntimeLogPathOverrideTests
{
    private const string LogPathEnvironmentVariable = "INDIGO_DEBUG_RUNTIME_LOG_PATH";
    private string? _originalLogPath;
    private string _temporaryDirectory = "";

    [SetUp]
    public void SetUp()
    {
        _originalLogPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
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

    [Test]
    public void ResolveLogPathForTesting_未指定と相対パスは既定先を維持する()
    {
        string defaultPath = Path.Combine(_temporaryDirectory, "default", "debug-runtime.log");

        Assert.Multiple(() =>
        {
            Assert.That(
                DebugRuntimeLog.ResolveLogPathForTesting(null!, defaultPath),
                Is.EqualTo(defaultPath)
            );
            Assert.That(
                DebugRuntimeLog.ResolveLogPathForTesting("relative\\audit.log", defaultPath),
                Is.EqualTo(defaultPath)
            );
        });
    }

    [Test]
    public void Write_環境変数で指定した専用ファイルへだけ追記する()
    {
        string overridePath = Path.Combine(_temporaryDirectory, "child", "audit.log");
        Environment.SetEnvironmentVariable(LogPathEnvironmentVariable, overridePath);

        DebugRuntimeLog.Write("path-override-test", "isolated child process log");

        Assert.That(File.Exists(overridePath), Is.True);
        string content = File.ReadAllText(overridePath, Encoding.UTF8);
        Assert.That(content, Does.Contain("[path-override-test] isolated child process log"));
        Assert.That(
            Directory.GetFiles(_temporaryDirectory, "*", SearchOption.AllDirectories),
            Has.Length.EqualTo(1)
        );
    }
}

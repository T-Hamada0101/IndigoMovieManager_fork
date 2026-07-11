using System.Diagnostics;
using System.Text.Json;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class Phase0ManualReviewScriptPolicyTests
{
    [Test]
    public void テンプレートはscorecard契約の8シナリオ36チェックをBOMなしLFで生成する()
    {
        string tempRoot = CreateTempDirectory();
        string outputPath = Path.Combine(tempRoot, "review", "phase0.json");

        try
        {
            ProcessResult result = RunScript(outputPath);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.BuildDiagnosticMessage());
            Assert.That(result.StandardOutput, Does.Contain(outputPath));
            Assert.That(File.Exists(outputPath), Is.True, outputPath);

            byte[] bytes = File.ReadAllBytes(outputPath);
            Assert.Multiple(() =>
            {
                Assert.That(bytes.Length, Is.GreaterThan(3));
                Assert.That(bytes[0], Is.Not.EqualTo((byte)0xef));
                Assert.That(bytes[1], Is.Not.EqualTo((byte)0xbb));
                Assert.That(bytes[2], Is.Not.EqualTo((byte)0xbf));
                Assert.That(bytes, Does.Not.Contain((byte)'\r'));
            });

            using JsonDocument document = JsonDocument.Parse(bytes);
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("phase0-manual-review-v1"));
            Assert.That(document.RootElement.GetProperty("created_utc").GetString(), Is.Not.Empty);

            JsonElement scenarios = document.RootElement.GetProperty("scenarios");
            DebugRuntimeLogPhase0ScenarioScorecard scorecard = CreateEmptyEvidenceScorecard();
            IReadOnlyList<DebugRuntimeLogPhase0ScenarioScore> expectedScenarios = scorecard.Scenarios;

            string[] actualScenarioKeys = scenarios
                .EnumerateArray()
                .Select(scenario => scenario.GetProperty("key").GetString() ?? "")
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(expectedScenarios, Has.Count.EqualTo(8));
                Assert.That(
                    expectedScenarios.Sum(scenario => scenario.ManualVisualReviewKeys.Count),
                    Is.EqualTo(36)
                );
                Assert.That(scenarios.GetArrayLength(), Is.EqualTo(expectedScenarios.Count));
                Assert.That(
                    actualScenarioKeys,
                    Is.EqualTo(expectedScenarios.Select(scenario => scenario.Key))
                );
            });

            for (int index = 0; index < expectedScenarios.Count; index++)
            {
                JsonElement scenario = scenarios[index];
                string key = scenario.GetProperty("key").GetString() ?? "";
                DebugRuntimeLogPhase0ScenarioScore expectedScenario = expectedScenarios[index];
                Assert.That(key, Is.EqualTo(expectedScenario.Key));

                JsonElement.ArrayEnumerator checks = scenario.GetProperty("checks").EnumerateArray();
                string[] actualCheckKeys = checks.Select(check => check.GetProperty("key").GetString() ?? "").ToArray();
                Assert.That(actualCheckKeys, Is.EqualTo(expectedScenario.ManualVisualReviewKeys));

                foreach (JsonElement check in scenario.GetProperty("checks").EnumerateArray())
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(check.GetProperty("status").GetString(), Is.EqualTo("pending"));
                        Assert.That(check.GetProperty("notes").GetString(), Is.EqualTo(""));
                    });
                }
            }
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void 既存の目視確認テンプレートは上書きしない()
    {
        string tempRoot = CreateTempDirectory();
        string outputPath = Path.Combine(tempRoot, "phase0.json");

        try
        {
            ProcessResult first = RunScript(outputPath);
            byte[] originalBytes = File.ReadAllBytes(outputPath);
            ProcessResult second = RunScript(outputPath);

            Assert.Multiple(() =>
            {
                Assert.That(first.ExitCode, Is.EqualTo(0), first.BuildDiagnosticMessage());
                Assert.That(second.ExitCode, Is.Not.EqualTo(0), second.BuildDiagnosticMessage());
                Assert.That(second.StandardError + second.StandardOutput, Does.Contain("上書きしません"));
                Assert.That(File.ReadAllBytes(outputPath), Is.EqualTo(originalBytes));
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ProcessResult RunScript(string outputPath)
    {
        string scriptPath = Path.Combine(FindRepoRoot(), "scripts", "New-Phase0ManualReview.ps1");
        ProcessStartInfo startInfo = new("pwsh")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(outputPath);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwshを開始できませんでした。");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static DebugRuntimeLogPhase0ScenarioScorecard CreateEmptyEvidenceScorecard()
    {
        string[] emptyEvidence = [];
        return DebugRuntimeLogPhase0ScenarioScorecardPolicy.Evaluate(
            DebugRuntimeLogEvidencePolicy.Evaluate(emptyEvidence),
            DebugRuntimeLogPhase0EvidencePolicy.Evaluate(emptyEvidence)
        );
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"imm-phase0-manual-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("repo rootを解決できませんでした。");
        return "";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string BuildDiagnosticMessage() => $"stdout: {StandardOutput}{Environment.NewLine}stderr: {StandardError}";
    }
}

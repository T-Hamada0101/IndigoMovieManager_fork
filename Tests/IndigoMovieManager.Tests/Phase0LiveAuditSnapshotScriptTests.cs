using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class Phase0LiveAuditSnapshotScriptTests
{
    [Test]
    public void Session以降の同basename_rotationだけを退避順で現行logへ連結する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string currentPath = Path.Combine(tempRoot, "debug-runtime.log");
            string firstArchivePath = Path.Combine(tempRoot, "debug-runtime_20260712.log");
            string secondArchivePath = Path.Combine(tempRoot, "debug-runtime_20260712_02.log");
            string oldArchivePath = Path.Combine(tempRoot, "debug-runtime_20260711.log");
            string otherArchivePath = Path.Combine(tempRoot, "other_20260712.log");
            File.WriteAllText(firstArchivePath, "first", new UTF8Encoding(false));
            File.WriteAllText(secondArchivePath, "second\n", new UTF8Encoding(false));
            File.WriteAllText(oldArchivePath, "old\n", new UTF8Encoding(false));
            File.WriteAllText(otherArchivePath, "other\n", new UTF8Encoding(false));
            File.WriteAllText(currentPath, "current\n", new UTF8Encoding(false));
            File.SetLastWriteTime(firstArchivePath, new DateTime(2026, 7, 12, 10, 1, 0));
            File.SetLastWriteTime(secondArchivePath, new DateTime(2026, 7, 12, 10, 2, 0));
            File.SetLastWriteTime(oldArchivePath, new DateTime(2026, 7, 11, 23, 59, 0));
            File.SetLastWriteTime(otherArchivePath, new DateTime(2026, 7, 12, 10, 3, 0));

            SnapshotResult result = RunSnapshot(
                currentPath,
                "2026-07-12 10:00:00.000",
                includeRotationArchives: true,
                holdCurrentOpen: true
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.SourcePaths,
                    Is.EqualTo([firstArchivePath, secondArchivePath, currentPath])
                );
                Assert.That(result.Content, Is.EqualTo("first\nsecond\ncurrent\n"));
                Assert.That(Directory.GetFiles(tempRoot, "indigo-phase0-audit-*.log"), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void Session未指定と任意LogPathは現行logだけをsnapshotにする()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string currentPath = Path.Combine(tempRoot, "custom.log");
            File.WriteAllText(
                Path.Combine(tempRoot, "custom_20260712.log"),
                "archive\n",
                new UTF8Encoding(false)
            );
            File.WriteAllText(currentPath, "current\n", new UTF8Encoding(false));

            SnapshotResult result = RunSnapshot(
                currentPath,
                "2026-07-12 10:00:00.000",
                includeRotationArchives: false,
                holdCurrentOpen: false
            );

            Assert.Multiple(() =>
            {
                Assert.That(result.SourcePaths, Is.EqualTo([currentPath]));
                Assert.That(result.Content, Is.EqualTo("current\n"));
                Assert.That(Directory.GetFiles(tempRoot, "indigo-phase0-audit-*.log"), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static SnapshotResult RunSnapshot(
        string currentPath,
        string sessionStartedLocal,
        bool includeRotationArchives,
        bool holdCurrentOpen
    )
    {
        string tempRoot = Path.GetDirectoryName(currentPath)!;
        string harnessPath = Path.Combine(tempRoot, "snapshot-harness.ps1");
        string snapshotPath = Path.Combine(
            tempRoot,
            $"indigo-phase0-audit-{Guid.NewGuid():N}.log"
        );
        string scriptPath = Path.Combine(FindRepoRoot(), "Scripts", "Invoke-Phase0LiveAudit.ps1");
        File.WriteAllText(harnessPath, BuildHarness(), new UTF8Encoding(false));

        FileStream? heldStream = null;
        try
        {
            if (holdCurrentOpen)
            {
                heldStream = File.Open(
                    currentPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete
                );
            }

            ProcessStartInfo startInfo = new("pwsh")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[]
            {
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                harnessPath,
                "-ScriptPath",
                scriptPath,
                "-CurrentPath",
                currentPath,
                "-SessionStartedLocal",
                sessionStartedLocal,
                "-IncludeRotationArchives",
                includeRotationArchives ? "1" : "0",
                "-SnapshotPath",
                snapshotPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.EqualTo(0), output + Environment.NewLine + error);
            return JsonSerializer.Deserialize<SnapshotResult>(output.Trim())!;
        }
        finally
        {
            heldStream?.Dispose();
            File.Delete(snapshotPath);
            File.Delete(harnessPath);
        }
    }

    private static string BuildHarness() =>
        """
        param(
            [string]$ScriptPath,
            [string]$CurrentPath,
            [string]$SessionStartedLocal,
            [int]$IncludeRotationArchives,
            [string]$SnapshotPath
        )
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$errors)
        foreach ($name in @('Get-Phase0AuditLogSnapshotSources', 'Copy-Phase0AuditLogSnapshot'))
        {
            $function = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true) | Select-Object -First 1
            Invoke-Expression $function.Extent.Text
        }
        $sources = @(Get-Phase0AuditLogSnapshotSources -CurrentLogPath $CurrentPath -SessionStartedLocal $SessionStartedLocal -IncludeRotationArchives:($IncludeRotationArchives -eq 1))
        Copy-Phase0AuditLogSnapshot -SourcePaths $sources -DestinationPath $SnapshotPath
        [pscustomobject]@{
            SourcePaths = $sources
            Content = [System.IO.File]::ReadAllText($SnapshotPath)
        } | ConvertTo-Json -Compress
        """;

    private static string CreateTempRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepoRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = ""
    )
    {
        DirectoryInfo? current = new(
            Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory()
        );
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("repo rootを解決できませんでした。");
    }

    private sealed record SnapshotResult(string[] SourcePaths, string Content);
}

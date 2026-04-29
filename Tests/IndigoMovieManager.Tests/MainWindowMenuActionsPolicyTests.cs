using System;
using System.IO;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowMenuActionsPolicyTests
{
    [Test]
    public void MenuScore_Click_DB更新は背景へ逃がす()
    {
        string source = GetRepoText("Views", "Main", "MainWindow.MenuActions.cs");
        string scoreMethod = GetMethodBlock(source, "private void MenuScore_Click(");
        string persistMethod = GetMethodBlock(source, "private void QueueMovieScorePersist(");

        Assert.That(scoreMethod, Does.Contain("QueueMovieScorePersist("));
        Assert.That(scoreMethod, Does.Not.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(persistMethod, Does.Contain("Task.Run("));
        Assert.That(persistMethod, Does.Contain("_mainDbMovieMutationFacade.UpdateScore("));
        Assert.That(persistMethod, Does.Contain("score persist failed"));
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int bodyStart = source.IndexOf('{', start);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signature} の本文開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }
        }

        Assert.Fail($"{signature} の本文終了が見つかりません。");
        return "";
    }
}

using System.IO;
using System.Runtime.CompilerServices;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinRuntimeBridgeAttachPolicyTests
{
    [Test]
    public void 同一WebView再Attachで同一thumbRootなら外部サムネ登録を保持する()
    {
        string currentRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "thumb");
        string nextRoot = currentRoot + Path.DirectorySeparatorChar;

        bool shouldClear =
            WhiteBrowserSkinRuntimeBridge.ShouldClearRegisteredExternalThumbnailPathsForAttach(
                isSameCoreWebViewAttach: true,
                currentRoot,
                nextRoot
            );

        Assert.That(shouldClear, Is.False);
    }

    [Test]
    public void thumbRoot変更または別WebViewAttachでは外部サムネ登録を破棄する()
    {
        string currentRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "thumb");
        string nextRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "thumb-next");

        Assert.That(
            WhiteBrowserSkinRuntimeBridge.ShouldClearRegisteredExternalThumbnailPathsForAttach(
                isSameCoreWebViewAttach: true,
                currentRoot,
                nextRoot
            ),
            Is.True
        );
        Assert.That(
            WhiteBrowserSkinRuntimeBridge.ShouldClearRegisteredExternalThumbnailPathsForAttach(
                isSameCoreWebViewAttach: false,
                currentRoot,
                currentRoot
            ),
            Is.True
        );
    }

    [Test]
    public void AttachとDetachのソース契約は同一Attach保持とDetach破棄を固定する()
    {
        string source = GetSourceText(
                new[] { "WhiteBrowserSkin", "Runtime", "WhiteBrowserSkinRuntimeBridge.cs" }
            )
            .Replace("\r\n", "\n");
        string attachMethod = ExtractMethod(source, "public void Attach(");
        string detachMethod = ExtractMethod(source, "private void Detach()");

        Assert.That(
            attachMethod,
            Does.Contain("ShouldClearRegisteredExternalThumbnailPathsForAttach(")
        );
        Assert.That(
            attachMethod,
            Does.Contain("isSameCoreWebViewAttach: true")
        );
        Assert.That(
            attachMethod,
            Does.Contain("managedThumbnailRootPath = normalizedThumbRootPath;")
        );
        Assert.That(detachMethod, Does.Contain("registeredExternalThumbnailPaths.Clear();"));
    }

    [Test]
    public void HostのsameDocumentSkipは外部サムネ登録を消さず実Navigateだけ消す()
    {
        string source = GetSourceText(
                new[] { "WhiteBrowserSkin", "Host", "WhiteBrowserSkinHostControl.xaml.cs" }
            )
            .Replace("\r\n", "\n");
        string tryNavigateMethod = ExtractMethod(
            source,
            "public async Task<WhiteBrowserSkinHostOperationResult> TryNavigateAsync("
        );

        int skipIndex = tryNavigateMethod.IndexOf(
            "CreateNavigateSkipped(requestedSkinName, \"same-document\")",
            StringComparison.Ordinal
        );
        int clearIndex = tryNavigateMethod.IndexOf(
            "runtimeBridge.ClearRegisteredExternalThumbnailPaths();",
            StringComparison.Ordinal
        );
        int leaveIndex = tryNavigateMethod.IndexOf(
            "await runtimeBridge.HandleSkinLeaveAsync();",
            StringComparison.Ordinal
        );
        int navigateIndex = tryNavigateMethod.IndexOf(
            "await NavigateToStringAsync(document.Html);",
            StringComparison.Ordinal
        );

        Assert.That(skipIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(clearIndex, Is.GreaterThan(skipIndex));
        Assert.That(leaveIndex, Is.GreaterThan(clearIndex));
        Assert.That(navigateIndex, Is.GreaterThan(leaveIndex));
    }

    private static string GetSourceText(
        string[] relativeSegments,
        [CallerFilePath] string testSourcePath = ""
    )
    {
        string? repoRootFromSource = ResolveRepoRootFromCallerSource(testSourcePath);
        if (!string.IsNullOrEmpty(repoRootFromSource))
        {
            string sourceCandidate = Path.Combine(
                new[] { repoRootFromSource }.Concat(relativeSegments).ToArray()
            );
            if (File.Exists(sourceCandidate))
            {
                return File.ReadAllText(sourceCandidate);
            }
        }

        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(new[] { current.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail(
            $"source file を repo root から解決できませんでした: {string.Join(Path.DirectorySeparatorChar, relativeSegments)}"
        );
        return string.Empty;
    }

    private static string? ResolveRepoRootFromCallerSource(string testSourcePath)
    {
        if (string.IsNullOrWhiteSpace(testSourcePath))
        {
            return null;
        }

        string? sourceDirectory = Path.GetDirectoryName(testSourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return null;
        }

        DirectoryInfo? current = new(sourceDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "IndigoMovieManager.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{signature} が見つかりません。");

        int braceStart = source.IndexOf('{', start);
        Assert.That(
            braceStart,
            Is.GreaterThanOrEqualTo(0),
            $"{signature} の開始波括弧が見つかりません。"
        );

        int depth = 0;
        for (int index = braceStart; index < source.Length; index++)
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

        Assert.Fail($"{signature} の終端が見つかりません。");
        return string.Empty;
    }
}

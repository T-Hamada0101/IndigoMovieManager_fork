namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchInputDebounceSourcePolicyTests
{
    [Test]
    public void DebounceはBinding反映後の同値でも検索正本へ進む()
    {
        string source = GetSearchSource();
        string tickMethod = GetMethodBlock(
            source,
            "private async void SearchInputDebounceTimer_Tick("
        );

        // Editable ComboBox の Binding が先に SearchKeyword を更新しても、入力要求は捨てない。
        Assert.That(tickMethod, Does.Contain("await ExecuteSearchKeywordFromInputAsync(text);"));
        Assert.That(tickMethod, Does.Not.Contain("MainVM.DbInfo.SearchKeyword"));
        Assert.That(tickMethod, Does.Not.Contain("string.Equals("));
    }

    [Test]
    public void DebounceはIME未完成構文startupの既存guardを維持する()
    {
        string source = GetSearchSource();
        string textChangedMethod = GetMethodBlock(source, "private void SearchBox_TextChanged(");
        string canRunMethod = GetMethodBlock(source, "private bool CanRunIncrementalSearch(");
        string tickMethod = GetMethodBlock(
            source,
            "private async void SearchInputDebounceTimer_Tick("
        );

        Assert.That(textChangedMethod, Does.Contain("string.IsNullOrEmpty(MainVM.DbInfo.DBFullPath)"));
        Assert.That(textChangedMethod, Does.Contain("if (_imeFlag)"));
        Assert.That(tickMethod, Does.Contain("if (_imeFlag)"));
        Assert.That(tickMethod, Does.Contain("if (SearchBox == null)"));
        Assert.That(tickMethod, Does.Contain("if (!CanRunIncrementalSearch(text))"));
        Assert.That(canRunMethod, Does.Contain("IsStartupFeedPartialActive"));
        Assert.That(canRunMethod, Does.Contain("text.IndexOf('{')"));
        Assert.That(canRunMethod, Does.Contain("text.IndexOf('}')"));
        Assert.That(canRunMethod, Does.Contain("lastChar != '-'"));
        Assert.That(canRunMethod, Does.Contain("lastChar != '|'"));
        Assert.That(canRunMethod, Does.Contain("lastChar != '{'"));
    }

    [Test]
    public void Debounceログは識別子長さ待機時間だけを出し検索本文を出さない()
    {
        string source = GetSearchSource();
        string queueMethod = GetMethodBlock(source, "private void QueueIncrementalSearch(");
        string tickMethod = GetMethodBlock(
            source,
            "private async void SearchInputDebounceTimer_Tick("
        );
        string debounceFlow = queueMethod + Environment.NewLine + tickMethod;

        Assert.That(debounceFlow, Does.Contain("search_input_id"));
        Assert.That(debounceFlow, Does.Contain("text_length"));
        Assert.That(debounceFlow, Does.Contain("debounce_ms"));
        Assert.That(debounceFlow, Does.Not.Match(@"DebugRuntimeLog\.Write\([\s\S]*?\{text\}"));
        Assert.That(debounceFlow, Does.Not.Contain("$\"{text}"));
        Assert.That(debounceFlow, Does.Not.Contain("keyword='"));
        Assert.That(debounceFlow, Does.Not.Contain("text='"));
    }

    private static string GetSearchSource()
    {
        return File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    @"..\..\..\..\..\..\Views\Main\MainWindow.Search.cs"
                )
            )
        );
    }

    private static string GetMethodBlock(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
        int openingBrace = source.IndexOf('{', start);
        Assert.That(openingBrace, Is.GreaterThan(start), signature);

        int depth = 0;
        for (int index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new AssertionException($"メソッド終端が見つかりません: {signature}");
    }
}

using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class CancelableDbReadSourcePolicyTests
{
    [Test]
    public void GetDataのtokenはSQLiteCommand取消へ接続しDBエラー扱いにしない()
    {
        string source = GetRepoText("DB", "SQLite.cs");
        string method = ExtractMethod(
            source,
            "public static DataTable GetData(\n            string dbFullPath,\n            string sql,\n            CancellationToken cancellationToken"
        );

        int commandCreation = method.IndexOf("connection.CreateCommand()", StringComparison.Ordinal);
        int cancellationRegistration = method.IndexOf(
            "cancellationToken.Register(",
            StringComparison.Ordinal
        );
        int commandCancel = method.IndexOf("((SQLiteCommand)state).Cancel()", StringComparison.Ordinal);
        int fill = method.IndexOf("adapter.Fill(dt)", StringComparison.Ordinal);
        int cancellationTranslation = method.IndexOf(
            "catch (Exception) when (cancellationToken.IsCancellationRequested)",
            StringComparison.Ordinal
        );
        int dbErrorCatch = method.IndexOf("catch (Exception e)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            // DB読込開始前と完了直後にも取消を観測し、取消済み結果を返さない。
            Assert.That(
                CountOccurrences(method, "cancellationToken.ThrowIfCancellationRequested()"),
                Is.GreaterThanOrEqualTo(2)
            );

            // 実行中のSQLite処理へCancelを接続し、登録寿命をcommandより短く閉じる。
            Assert.That(commandCreation, Is.GreaterThanOrEqualTo(0));
            Assert.That(cancellationRegistration, Is.GreaterThan(commandCreation));
            Assert.That(commandCancel, Is.GreaterThan(cancellationRegistration));
            Assert.That(fill, Is.GreaterThan(commandCancel));
            Assert.That(method, Does.Contain("using CancellationTokenRegistration"));

            // SQLite由来例外でも取消要求中ならOperationCanceledExceptionへ統一し、通常DB障害と分離する。
            Assert.That(cancellationTranslation, Is.GreaterThan(fill));
            Assert.That(dbErrorCatch, Is.GreaterThan(cancellationTranslation));
            Assert.That(
                method.IndexOf(
                    "throw new OperationCanceledException(cancellationToken)",
                    cancellationTranslation,
                    StringComparison.Ordinal
                ),
                Is.LessThan(dbErrorCatch)
            );
        });
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ExtractMethod(string source, string signature)
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

    private static string GetRepoText(
        string firstPathPart,
        string fileName,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        foreach (string startPath in new[]
                 {
                     Path.GetDirectoryName(callerFilePath) ?? "",
                     TestContext.CurrentContext.TestDirectory,
                     TestContext.CurrentContext.WorkDirectory,
                     Directory.GetCurrentDirectory(),
                 })
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                continue;
            }

            DirectoryInfo? current = new(startPath);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, firstPathPart, fileName);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                current = current.Parent;
            }
        }

        Assert.Fail($"{Path.Combine(firstPathPart, fileName)} が見つかりません。");
        return "";
    }
}

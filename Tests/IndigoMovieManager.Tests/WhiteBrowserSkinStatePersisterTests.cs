using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using IndigoMovieManager.DB;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinStatePersisterTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void TryUpsertSystemTable_同一キーを原子的に上書きできる()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Assert.Multiple(() =>
            {
                // 同じキーを連続で保存しても 1 件に収まり、最後の値が残ることを確認する。
                Assert.That(SQLite.TryUpsertSystemTable(dbPath, "skin", "OldSkin"), Is.True);
                Assert.That(SQLite.TryUpsertSystemTable(dbPath, "skin", "NewSkin"), Is.True);
                Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("NewSkin"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void TrySelectProfileValue_空文字保存済みと未保存を区別できる()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);
            Assert.That(SQLite.TryUpsertProfileTable(dbPath, "Alpha2", "lastfind", ""), Is.True);

            bool emptyExists = SQLite.TrySelectProfileValue(
                dbPath,
                "Alpha2",
                "lastfind",
                out string emptyValue
            );
            bool missingExists = SQLite.TrySelectProfileValue(
                dbPath,
                "Alpha2",
                "missing",
                out string missingValue
            );

            Assert.Multiple(() =>
            {
                Assert.That(emptyExists, Is.True);
                Assert.That(emptyValue, Is.EqualTo(""));
                Assert.That(missingExists, Is.False);
                Assert.That(missingValue, Is.EqualTo(""));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_同一キー連続要求では最後の値だけ保存する()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Channel<WhiteBrowserSkinStatePersistRequest> channel =
                Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
            WhiteBrowserSkinStatePersister persister = new(channel.Reader, batchWindowMs: 10);

            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "OldSkin")
            );
            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "NewSkin")
            );
            channel.Writer.TryComplete();

            await persister.RunAsync();

            Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("NewSkin"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_SystemとProfileを同じDBへ保存できる()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Channel<WhiteBrowserSkinStatePersistRequest> channel =
                Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
            WhiteBrowserSkinStatePersister persister = new(channel.Reader, batchWindowMs: 10);

            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "SampleExternalSkin")
            );
            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateProfile(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    "DefaultGrid"
                )
            );
            channel.Writer.TryComplete();

            await persister.RunAsync();

            Assert.Multiple(() =>
            {
                Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("SampleExternalSkin"));
                Assert.That(
                    ReadProfileValue(dbPath, "SampleExternalSkin", "LastUpperTab"),
                    Is.EqualTo("DefaultGrid")
                );
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_Profile保存失敗時はFaultへ落としてCacheを見せない()
    {
        string dbPath = Path.Combine(
            Path.GetTempPath(),
            Path.GetRandomFileName(),
            "missing",
            "main.wb"
        );

        Channel<WhiteBrowserSkinStatePersistRequest> channel =
            Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
        List<string> logs = [];
        WhiteBrowserSkinStatePersister persister = new(
            channel.Reader,
            batchWindowMs: 10,
            log: message => logs.Add(message ?? "")
        );

        WhiteBrowserSkinProfileValueCache.RecordPending(
            dbPath,
            "SampleExternalSkin",
            "LastUpperTab",
            "DefaultGrid"
        );
        channel.Writer.TryWrite(
            WhiteBrowserSkinStatePersistRequest.CreateProfile(
                dbPath,
                "SampleExternalSkin",
                "LastUpperTab",
                "DefaultGrid"
            )
        );
        channel.Writer.TryComplete();

        await persister.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    out _
                ),
                Is.False
            );
            Assert.That(
                WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    out _
                ),
                Is.False
            );
            Assert.That(
                WhiteBrowserSkinProfileValueCache.TryGetPersistState(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    out WhiteBrowserSkinProfileValuePersistState state
                ),
                Is.True
            );
            Assert.That(state.Value, Is.EqualTo("DefaultGrid"));
            Assert.That(state.IsDirty, Is.True);
            Assert.That(state.IsFailed, Is.True);
            Assert.That(state.IsRetryable, Is.True);
            Assert.That(state.NotifyUi, Is.False);
            Assert.That(
                logs.Any(
                    static x =>
                        x.Contains("skin state persist failed:", StringComparison.Ordinal)
                        && x.Contains("write_kind=background-db-write", StringComparison.Ordinal)
                        && x.Contains("write_reason=persister-write", StringComparison.Ordinal)
                        && x.Contains(
                            "queue_key=skin-profile:SampleExternalSkin:LastUpperTab",
                            StringComparison.Ordinal
                        )
                        && x.Contains("write_succeeded=false", StringComparison.Ordinal)
                        && x.Contains("failure_kind=skin-profile", StringComparison.Ordinal)
                        && x.Contains("dirty=true", StringComparison.Ordinal)
                        && x.Contains("failed=true", StringComparison.Ordinal)
                        && x.Contains("retryable=true", StringComparison.Ordinal)
                        && x.Contains("notify_ui=false", StringComparison.Ordinal)
                ),
                Is.True
            );
        });
    }

    [Test]
    public void PersistRequest_Profile失敗ログはdirty_failed_retryableを持つ()
    {
        WhiteBrowserSkinStatePersistRequest request =
            WhiteBrowserSkinStatePersistRequest.CreateProfile(
                @"C:\temp\missing.wb",
                "SampleSkin",
                "LastUpperTab",
                "DefaultGrid"
            );

        Assert.That(
            request.BuildFailureStateLogFields(),
            Is.EqualTo("dirty=true failed=true retryable=true notify_ui=false")
        );
    }

    [Test]
    public void PersistRequest_Profile共通write語彙を作る()
    {
        WhiteBrowserSkinStatePersistRequest request =
            WhiteBrowserSkinStatePersistRequest.CreateProfile(
                "missing.wb",
                "SampleSkin",
                "LastUpperTab",
                "DefaultGrid"
            );

        PersistenceWriteRequest writeRequest = request.BuildWriteRequest("queue-rejected");
        string failureLog = request.BuildWriteFailureResultLogFields(
            "persister-write",
            TimeSpan.FromMilliseconds(12.34d)
        );
        string successLog = request.BuildWriteSuccessResultLogFields(
            "fallback-write",
            TimeSpan.FromMilliseconds(1.2d)
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                writeRequest.BuildLogFields(),
                Is.EqualTo(
                    "write_kind=background-db-write write_reason=queue-rejected queue_key=skin-profile:SampleSkin:LastUpperTab retryable_policy=true"
                )
            );
            Assert.That(failureLog, Does.Contain("write_kind=background-db-write"));
            Assert.That(failureLog, Does.Contain("write_reason=persister-write"));
            Assert.That(failureLog, Does.Contain("write_succeeded=false"));
            Assert.That(failureLog, Does.Contain("failure_kind=skin-profile"));
            Assert.That(failureLog, Does.Contain("dirty=true failed=true retryable=true notify_ui=false"));
            Assert.That(successLog, Does.Contain("write_succeeded=true"));
            Assert.That(successLog, Does.Contain("failure_kind=none"));
        });
    }

    [Test]
    public void PersistRequest_System失敗ログは非dirty_nonretryable通知条件を持つ()
    {
        WhiteBrowserSkinStatePersistRequest request =
            WhiteBrowserSkinStatePersistRequest.CreateSystem(
                "missing.wb",
                "skin",
                "DefaultGrid"
            );

        Assert.That(
            request.BuildFailureStateLogFields(),
            Is.EqualTo("dirty=false failed=true retryable=false notify_ui=true")
        );
    }

    [Test]
    public void PersistRequest_System共通write語彙は通知候補として読める()
    {
        WhiteBrowserSkinStatePersistRequest request =
            WhiteBrowserSkinStatePersistRequest.CreateSystem(
                "missing.wb",
                "skin",
                "DefaultGrid"
            );

        string failureLog = request.BuildWriteFailureResultLogFields(
            "queue-closed",
            TimeSpan.Zero
        );

        Assert.Multiple(() =>
        {
            Assert.That(failureLog, Does.Contain("write_kind=background-db-write"));
            Assert.That(failureLog, Does.Contain("write_reason=queue-closed"));
            Assert.That(failureLog, Does.Contain("queue_key=skin-system:skin"));
            Assert.That(failureLog, Does.Contain("write_succeeded=false"));
            Assert.That(failureLog, Does.Contain("failure_kind=skin-system"));
            Assert.That(failureLog, Does.Contain("dirty=false failed=true retryable=false notify_ui=true"));
        });
    }

    [Test]
    public async Task RunAsync_同一traceのbatchはskin_dbログへtraceを引き継ぐ()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);
        List<string> logs = [];

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Channel<WhiteBrowserSkinStatePersistRequest> channel =
                Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
            WhiteBrowserSkinStatePersister persister = new(
                channel.Reader,
                batchWindowMs: 10,
                log: message => logs.Add(message ?? "")
            );

            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(
                    dbPath,
                    "skin",
                    "TraceSkin",
                    "trace=rq1001"
                )
            );
            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateProfile(
                    dbPath,
                    "TraceSkin",
                    "LastUpperTab",
                    "DefaultGrid",
                    "trace=rq1001"
                )
            );
            channel.Writer.TryComplete();

            await persister.RunAsync();

            Assert.That(
                logs.Any(static x => x.Contains("trace=rq1001 skin state persist:")),
                Is.True
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ReadSystemValue(string dbPath, string key)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM system WHERE attr = @attr LIMIT 1";
        command.Parameters.AddWithValue("@attr", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }

    private static string ReadProfileValue(string dbPath, string skinName, string key)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM profile WHERE skin = @skin AND key = @key LIMIT 1";
        command.Parameters.AddWithValue("@skin", skinName ?? "");
        command.Parameters.AddWithValue("@key", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }
}

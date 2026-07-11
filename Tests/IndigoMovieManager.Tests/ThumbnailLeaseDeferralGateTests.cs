using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ThumbnailLeaseDeferralGateTests
{
    [Test]
    public async Task WaitUntilLeaseAllowedAsync_nullまたはfalseなら即時継続する()
    {
        ThumbnailLeaseDeferralGate nullGate = new(null, 100, null);
        ThumbnailLeaseDeferralGate falseGate = new(static () => false, 100, null);

        await nullGate.WaitUntilLeaseAllowedAsync(CancellationToken.None);
        await falseGate.WaitUntilLeaseAllowedAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(nullGate.ShouldDeferLease(), Is.False);
            Assert.That(falseGate.ShouldDeferLease(), Is.False);
        });
    }

    [Test]
    public void ShouldDeferLease_状態遷移時だけ延期と再開を記録する()
    {
        bool shouldDefer = true;
        List<string> logs = [];
        ThumbnailLeaseDeferralGate gate = new(() => shouldDefer, 100, logs.Add);

        Assert.That(gate.ShouldDeferLease(), Is.True);
        Assert.That(gate.ShouldDeferLease(), Is.True);
        shouldDefer = false;
        Assert.That(gate.ShouldDeferLease(), Is.False);
        Assert.That(gate.ShouldDeferLease(), Is.False);

        Assert.That(logs, Has.Count.EqualTo(2));
        Assert.That(logs[0], Is.EqualTo("consumer lease deferred: reason=user-priority"));
        Assert.That(
            logs[1],
            Is.EqualTo("consumer lease resumed: reason=user-priority-released")
        );
    }

    [Test]
    public async Task WaitUntilLeaseAllowedAsync_resolver例外は記録して既存処理を継続する()
    {
        List<string> logs = [];
        ThumbnailLeaseDeferralGate gate = new(
            static () => throw new InvalidOperationException("resolver failed"),
            100,
            logs.Add
        );

        await gate.WaitUntilLeaseAllowedAsync(CancellationToken.None);

        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0], Does.Contain("policy=continue"));
        Assert.That(logs[0], Does.Contain("type=InvalidOperationException"));
    }

    [Test]
    public void WaitUntilLeaseAllowedAsync_延期待機中もキャンセルへ即応する()
    {
        ThumbnailLeaseDeferralGate gate = new(static () => true, 3000, null);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.That(
            async () => await gate.WaitUntilLeaseAllowedAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()
        );
    }

    [Test]
    public async Task EnumerateLeasedItemsAsync_取得済みleaseは延期中でも処理へ渡す()
    {
        QueueDbLeaseItem alreadyLeased = new()
        {
            QueueId = 42,
            OwnerInstanceId = "existing-owner",
        };
        ThumbnailLeaseDeferralGate gate = new(static () => true, 100, null);

        await using IAsyncEnumerator<QueueDbLeaseItem> enumerator =
            ThumbnailLeaseCoordinator
                .EnumerateLeasedItemsAsync(
                    null,
                    "existing-owner",
                    [alreadyLeased],
                    1,
                    5,
                    null,
                    null,
                    null,
                    gate,
                    CancellationToken.None
                )
                .GetAsyncEnumerator();

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current.QueueId, Is.EqualTo(42));
    }

    [Test]
    public async Task EnumerateLeasedItemsAsync_延期中は新規leaseせず解除後に取得する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-lease-deferral-{Guid.NewGuid():N}.wb"
        );
        string runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            $"imm-lease-deferral-runtime-{Guid.NewGuid():N}"
        );
        ThumbnailQueueHostPathPolicy.Configure(
            queueDbDirectoryPath: Path.Combine(runtimeRoot, "QueueDb"),
            failureDbDirectoryPath: Path.Combine(runtimeRoot, "FailureDb"),
            logDirectoryPath: ""
        );
        QueueDbService queueDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        bool shouldDefer = true;
        ThumbnailLeaseDeferralGate gate = new(() => shouldDefer, 100, null);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));

        try
        {
            _ = queueDbService.Upsert(
                [
                    new QueueDbUpsertItem
                    {
                        MoviePath = Path.Combine(runtimeRoot, "movie.mp4"),
                        MoviePathKey = "movie-key",
                        TabIndex = 1,
                    },
                ],
                DateTime.UtcNow
            );

            await using IAsyncEnumerator<QueueDbLeaseItem> enumerator =
                ThumbnailLeaseCoordinator
                    .EnumerateLeasedItemsAsync(
                        queueDbService,
                        "lease-deferral-owner",
                        [],
                        1,
                        5,
                        null,
                        null,
                        null,
                        gate,
                        cancellation.Token
                    )
                    .GetAsyncEnumerator(cancellation.Token);

            Task<bool> moveNextTask = enumerator.MoveNextAsync().AsTask();
            await Task.Delay(180, cancellation.Token);
            Assert.That(moveNextTask.IsCompleted, Is.False, "延期中にlease取得へ進んでいる");

            shouldDefer = false;
            Assert.That(await moveNextTask, Is.True);
            Assert.That(enumerator.Current.OwnerInstanceId, Is.EqualTo("lease-deferral-owner"));
        }
        finally
        {
            ThumbnailQueueHostPathPolicy.Configure("", "", "");
            TryDelete(queueDbPath);
            TryDelete(queueDbPath + "-wal");
            TryDelete(queueDbPath + "-shm");
            try
            {
                Directory.Delete(runtimeRoot, recursive: true);
            }
            catch
            {
                // テスト後の掃除失敗は本契約の判定対象外。
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // SQLite解放直後の掃除失敗は握りつぶす。
        }
    }
}

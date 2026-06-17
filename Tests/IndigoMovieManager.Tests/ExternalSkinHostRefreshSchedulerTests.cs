using System.Threading;
using System.Windows.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ExternalSkinHostRefreshSchedulerTests
{
    [Test]
    public async Task Queue_実行中に追加されたrefreshを直列化し最後の要求へ畳める()
    {
        RefreshSerializationResult result = await RunOnStaDispatcherAsync(async () =>
        {
            List<(int Generation, string Reason, string Request)> invocations = [];
            int currentConcurrency = 0;
            int maxConcurrency = 0;
            TaskCompletionSource<bool> firstStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirst = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> secondCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            ExternalSkinHostRefreshScheduler scheduler = new(
                Dispatcher.CurrentDispatcher,
                async (generation, reason, requestTraceId) =>
                {
                    currentConcurrency++;
                    maxConcurrency = Math.Max(maxConcurrency, currentConcurrency);
                    invocations.Add((generation, reason, requestTraceId));

                    if (invocations.Count == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await releaseFirst.Task;
                    }

                    if (invocations.Count == 2)
                    {
                        secondCompleted.TrySetResult(true);
                    }

                    currentConcurrency--;
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}")
            );

            Assert.That(scheduler.Queue("window-loaded", "rq0001"), Is.True);
            await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(5), "最初の refresh が始まりませんでした。");

            Assert.That(scheduler.Queue("dbinfo-Skin", "rq0002"), Is.True);
            Assert.That(scheduler.Queue("dbinfo-DBFullPath", "rq0003"), Is.True);
            releaseFirst.TrySetResult(true);

            await WaitAsync(
                secondCompleted.Task,
                TimeSpan.FromSeconds(5),
                "畳み込まれた 2 回目の refresh が完了しませんでした。"
            );

            return new RefreshSerializationResult(
                maxConcurrency,
                invocations.ToArray(),
                scheduler.CurrentGeneration
            );
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.MaxConcurrency, Is.EqualTo(1));
            Assert.That(result.Invocations, Has.Length.EqualTo(2));
            Assert.That(result.Invocations[0], Is.EqualTo((1, "window-loaded", "rq0001")));
            Assert.That(result.Invocations[1], Is.EqualTo((3, "dbinfo-DBFullPath", "rq0003")));
            Assert.That(result.FinalGeneration, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Queue_skinとDB切替が競合しても最後は最新generationだけ適用候補になる()
    {
        RefreshApplyResult result = await RunOnStaDispatcherAsync(async () =>
        {
            List<(int Generation, string Reason, string Request)> refreshed = [];
            List<(int Generation, string Reason, string Request)> applied = [];
            TaskCompletionSource<bool> firstStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirst = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> latestApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            ExternalSkinHostRefreshScheduler? scheduler = null;
            scheduler = new ExternalSkinHostRefreshScheduler(
                Dispatcher.CurrentDispatcher,
                async (generation, reason, requestTraceId) =>
                {
                    refreshed.Add((generation, reason, requestTraceId));

                    if (generation == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await releaseFirst.Task;
                    }

                    // MainWindow 側と同じく、完了時点で最新 generation だけを適用対象にする。
                    if (generation == scheduler!.CurrentGeneration)
                    {
                        applied.Add((generation, reason, requestTraceId));
                        latestApplied.TrySetResult(true);
                    }
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}")
            );

            Assert.That(scheduler.Queue("dbinfo-Skin", "rq0101"), Is.True);
            await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(5), "最初の refresh が始まりませんでした。");

            Assert.That(scheduler.Queue("dbinfo-DBFullPath", "rq0102"), Is.True);
            releaseFirst.TrySetResult(true);

            await WaitAsync(
                latestApplied.Task,
                TimeSpan.FromSeconds(5),
                "最新 generation の適用判定が完了しませんでした。"
            );

            return new RefreshApplyResult(refreshed.ToArray(), applied.ToArray());
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Refreshed, Has.Length.EqualTo(2));
            Assert.That(result.Refreshed[0], Is.EqualTo((1, "dbinfo-Skin", "rq0101")));
            Assert.That(result.Refreshed[1], Is.EqualTo((2, "dbinfo-DBFullPath", "rq0102")));
            Assert.That(result.Applied, Is.EqualTo(new[] { (2, "dbinfo-DBFullPath", "rq0102") }));
        });
    }

    [Test]
    public async Task Queue_実行中pendingではCatalogRefresh理由とtraceを軽い要求で潰さない()
    {
        RefreshSerializationResult result = await RunOnStaDispatcherAsync(async () =>
        {
            List<(int Generation, string Reason, string Request)> invocations = [];
            TaskCompletionSource<bool> firstStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirst = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> secondCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            ExternalSkinHostRefreshScheduler scheduler = new(
                Dispatcher.CurrentDispatcher,
                async (generation, reason, requestTraceId) =>
                {
                    invocations.Add((generation, reason, requestTraceId));

                    if (invocations.Count == 1)
                    {
                        firstStarted.TrySetResult(true);
                        await releaseFirst.Task;
                    }

                    if (invocations.Count == 2)
                    {
                        secondCompleted.TrySetResult(true);
                    }
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}"),
                SelectPreferredReasonForTest
            );

            Assert.That(scheduler.Queue("window-loaded", "rq0201"), Is.True);
            await WaitAsync(firstStarted.Task, TimeSpan.FromSeconds(5), "最初の refresh が始まりませんでした。");

            Assert.That(scheduler.Queue("header-reload", "rq0202"), Is.True);
            Assert.That(scheduler.Queue("minimal-chrome-reload", "rq0203"), Is.True);
            releaseFirst.TrySetResult(true);

            await WaitAsync(
                secondCompleted.Task,
                TimeSpan.FromSeconds(5),
                "優先 reason の 2 回目 refresh が完了しませんでした。"
            );

            return new RefreshSerializationResult(
                1,
                invocations.ToArray(),
                scheduler.CurrentGeneration
            );
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Invocations, Has.Length.EqualTo(2));
            Assert.That(result.Invocations[0], Is.EqualTo((1, "window-loaded", "rq0201")));
            Assert.That(result.Invocations[1], Is.EqualTo((3, "header-reload", "rq0202")));
            Assert.That(result.FinalGeneration, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Queue_dispatcher終了中はfalseを返しrefreshを受理しない()
    {
        bool accepted = await RunOnStaDispatcherAsync(() =>
        {
            int refreshCount = 0;
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            ExternalSkinHostRefreshScheduler scheduler = new(
                dispatcher,
                (_, _, _) =>
                {
                    refreshCount++;
                    return Task.CompletedTask;
                },
                ex => throw new AssertionException($"refresh drain failed: {ex.Message}")
            );

            dispatcher.InvokeShutdown();

            bool queueAccepted = scheduler.Queue("header-reload", "rq-shutdown");
            Assert.That(refreshCount, Is.Zero);
            return Task.FromResult(queueAccepted);
        });

        Assert.That(accepted, Is.False);
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                );
                _ = ExecuteAsync();
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    Dispatcher.Run();
                }

                async Task ExecuteAsync()
                {
                    try
                    {
                        T result = await action();
                        completion.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        {
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                        }
                    }
                }
            }
        );
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string SelectPreferredReasonForTest(string currentReason, string candidateReason)
    {
        if (string.IsNullOrWhiteSpace(candidateReason))
        {
            return currentReason ?? "";
        }

        return GetReasonPriority(candidateReason) >= GetReasonPriority(currentReason)
            ? candidateReason
            : currentReason ?? "";
    }

    private static int GetReasonPriority(string reason)
    {
        return reason switch
        {
            "header-reload" or "fallback-notice-retry" => 400,
            "dbinfo-DBFullPath" => 300,
            "dbinfo-Skin" => 200,
            "dbinfo-ThumbFolder" => 100,
            "minimal-chrome-reload" => 50,
            _ => 0,
        };
    }

    private sealed record RefreshSerializationResult(
        int MaxConcurrency,
        (int Generation, string Reason, string Request)[] Invocations,
        int FinalGeneration
    );

    private sealed record RefreshApplyResult(
        (int Generation, string Reason, string Request)[] Refreshed,
        (int Generation, string Reason, string Request)[] Applied
    );
}

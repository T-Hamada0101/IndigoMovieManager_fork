using System.Reflection;
using System.Text;
using System.Text.Json;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailQueueProcessorFailureDbTests
{
    [Test]
    public void HandleFailedItem_再試行系でもFailureDbへappendする()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-pending-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"movie-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 2);

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new InvalidOperationException("no frames decoded"),
                failureDbService
            );

            QueueDbFailedItem? failed = queueDbService.GetFailedItems().SingleOrDefault();
            Assert.That(failed, Is.Null);

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Pending.ToString()));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.TransientDecodeFailure));
            Assert.That(records[0].PanelType, Is.EqualTo("grid"));
            Assert.That(records[0].AttemptCount, Is.EqualTo(1));
            AssertFailureExtraJson(
                records[0].ExtraJson,
                "retry-scheduled",
                expectedWasRunning: false,
                expectedAttemptAfter: 1
            );
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_最終失敗でFailureDbへFailed記録する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-failed-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"missing-{Guid.NewGuid():N}.mkv"
        );

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 3);
            leasedItem.AttemptCount = 4;

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new FileNotFoundException("movie file not found"),
                failureDbService
            );

            List<QueueDbFailedItem> failedItems = queueDbService.GetFailedItems();
            Assert.That(failedItems.Count, Is.EqualTo(1));
            Assert.That(failedItems[0].LastError, Is.EqualTo("movie file not found"));

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Failed.ToString()));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.FileMissing));
            Assert.That(records[0].PanelType, Is.EqualTo("list"));
            Assert.That(records[0].AttemptCount, Is.EqualTo(4));
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
        }
    }

    [Test]
    public void HandleFailedItem_TimeoutExceptionはHangSuspectedとして記録する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-hang-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"hang-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            _ = queueDbService.MarkLeaseAsRunning(
                leasedItem.QueueId,
                leasedItem.OwnerInstanceId,
                DateTime.UtcNow
            );
            leasedItem.StartedAtUtc = DateTime.UtcNow;

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new TimeoutException("thumbnail processing timeout"),
                failureDbService
            );

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.HangSuspected));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Pending.ToString()));
            Assert.That(records[0].StartedAtUtc, Does.Contain("T"));
            AssertFailureExtraJson(
                records[0].ExtraJson,
                "hang-recovery-scheduled",
                expectedWasRunning: true,
                expectedAttemptAfter: 2
            );
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_HangSuspected初回はRecoveryレーンへ戻す()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-hang-recovery-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"hang-recovery-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new TimeoutException("thumbnail processing timeout"),
                failureDbService
            );

            List<QueueDbLeaseItem> retried = queueDbService.GetPendingAndLease(
                $"LEASE-{Guid.NewGuid():N}",
                takeCount: 1,
                leaseDuration: TimeSpan.FromMinutes(5),
                utcNow: DateTime.UtcNow
            );
            Assert.That(retried.Count, Is.EqualTo(1));
            Assert.That(retried[0].IsRescueRequest, Is.True);
            Assert.That(retried[0].AttemptCount, Is.GreaterThanOrEqualTo(2));

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Reason, Is.EqualTo("hang-recovery-scheduled"));
            Assert.That(records[0].FailureKind, Is.EqualTo(ThumbnailFailureKind.HangSuspected));
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_HangSuspected再発はFailedへ落とす()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-hang-final-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"hang-final-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            _ = queueDbService.ForceRetryMovieToPending(
                moviePath,
                tabIndex: 1,
                utcNow: DateTime.UtcNow,
                promoteToRecovery: true
            );

            leasedItem = queueDbService
                .GetPendingAndLease(
                    $"LEASE-{Guid.NewGuid():N}",
                    takeCount: 1,
                    leaseDuration: TimeSpan.FromMinutes(5),
                    utcNow: DateTime.UtcNow
                )
                .Single();

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new TimeoutException("thumbnail processing timeout"),
                failureDbService
            );

            List<QueueDbFailedItem> failedItems = queueDbService.GetFailedItems();
            Assert.That(failedItems.Count, Is.EqualTo(1));
            Assert.That(failedItems[0].MoviePath, Is.EqualTo(moviePath));

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].Reason, Is.EqualTo("final-failed"));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Failed.ToString()));
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_DbScopeChangedでもFailureDbへPending記録する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-dbscope-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"dbscope-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 0);

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new ThumbnailMainDbScopeChangedException(
                    "C:\\old\\sample.wb",
                    "C:\\new\\sample.wb",
                    moviePath
                ),
                failureDbService
            );

            QueueDbFailedItem? failed = queueDbService.GetFailedItems().SingleOrDefault();
            Assert.That(failed, Is.Null);

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Pending.ToString()));
            Assert.That(records[0].Reason, Is.EqualTo("db-scope-changed"));
            Assert.That(records[0].AttemptCount, Is.EqualTo(0));
            AssertFailureExtraJson(
                records[0].ExtraJson,
                "db-scope-changed",
                expectedWasRunning: false,
                expectedAttemptAfter: 0
            );
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleCanceledItem_停止要求でもFailureDbへPending記録する()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-canceled-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"canceled-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 4);

            InvokeHandleCanceledItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new OperationCanceledException("stop requested"),
                failureDbService
            );

            QueueDbFailedItem? failed = queueDbService.GetFailedItems().SingleOrDefault();
            Assert.That(failed, Is.Null);

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(records[0].QueueStatus, Is.EqualTo(ThumbnailQueueStatus.Pending.ToString()));
            Assert.That(records[0].Reason, Is.EqualTo("canceled"));
            Assert.That(records[0].PanelType, Is.EqualTo("big10"));
            Assert.That(records[0].AttemptCount, Is.EqualTo(0));
            AssertFailureExtraJson(
                records[0].ExtraJson,
                "canceled",
                expectedWasRunning: false,
                expectedAttemptAfter: 0
            );
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_ResultメタデータもFailureDbへappendする()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-resultmeta-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"resultmeta-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 1);
            ThumbnailCreateResult failedResult = new()
            {
                SaveThumbFileName = @"C:\thumbs\sample.jpg",
                DurationSec = 12.3,
                IsSuccess = false,
                ErrorMessage = "placeholder failed after retry",
                FailureStage = "postprocess-placeholder",
                PolicyDecision = "placeholder-create-failed",
                PlaceholderAction = "failed",
                PlaceholderKind = "UnsupportedCodec",
                FinalizerAction = "deleted",
                FinalizerDetail = "",
            };

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new ThumbnailCreateFailedException("thumbnail create failed", failedResult),
                failureDbService
            );

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));
            using JsonDocument document = JsonDocument.Parse(records[0].ExtraJson);
            JsonElement root = document.RootElement;
            Assert.That(
                root.GetProperty("ResultFailureStage").GetString(),
                Is.EqualTo("postprocess-placeholder")
            );
            Assert.That(
                root.GetProperty("ResultPolicyDecision").GetString(),
                Is.EqualTo("placeholder-create-failed")
            );
            Assert.That(
                root.GetProperty("ResultPlaceholderAction").GetString(),
                Is.EqualTo("failed")
            );
            Assert.That(
                root.GetProperty("ResultPlaceholderKind").GetString(),
                Is.EqualTo("UnsupportedCodec")
            );
            Assert.That(
                root.GetProperty("ResultFinalizerAction").GetString(),
                Is.EqualTo("deleted")
            );
            Assert.That(
                root.GetProperty("ResultErrorMessage").GetString(),
                Is.EqualTo("placeholder failed after retry")
            );
            Assert.That(
                root.GetProperty("DecisionBasis").GetString(),
                Is.EqualTo("postprocess-placeholder:placeholder-create-failed")
            );
            Assert.That(root.GetProperty("RepairAttempted").GetBoolean(), Is.False);
            Assert.That(
                root.GetProperty("PreflightBranch").GetString(),
                Is.EqualTo("unsupported-codec")
            );
            Assert.That(
                root.GetProperty("RecoveryRoute").GetString(),
                Is.EqualTo("retry")
            );
            Assert.That(
                root.GetProperty("result_signature").GetString(),
                Is.EqualTo("unknown")
            );
            Assert.That(
                root.GetProperty("preflight_branch").GetString(),
                Is.EqualTo("unsupported-codec")
            );
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    [Test]
    public void HandleFailedItem_QueueExtraJsonはworkthree受領最小キーを満たす()
    {
        string mainDbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-queue-failuredb-minimal-{Guid.NewGuid():N}.wb"
        );
        QueueDbService queueDbService = new(mainDbPath);
        ThumbnailFailureDebugDbService failureDbService = new(mainDbPath);
        string queueDbPath = queueDbService.QueueDbFullPath;
        string failureDbPath = failureDbService.FailureDbFullPath;
        string moviePath = Path.Combine(
            Path.GetTempPath(),
            "imm_queue_failuredb",
            $"minimal-{Guid.NewGuid():N}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(moviePath) ?? "");
        File.WriteAllBytes(moviePath, [1, 2, 3, 4]);

        try
        {
            QueueDbLeaseItem leasedItem = CreateLeasedItem(queueDbService, moviePath, tabIndex: 2);

            InvokeHandleFailedItem(
                queueDbService,
                leasedItem,
                leasedItem.OwnerInstanceId,
                new InvalidOperationException("no frames decoded"),
                failureDbService
            );

            List<ThumbnailFailureRecord> records = failureDbService.GetFailureRecords();
            Assert.That(records.Count, Is.EqualTo(1));

            using JsonDocument actualDocument = JsonDocument.Parse(records[0].ExtraJson);
            using JsonDocument fixtureDocument = JsonDocument.Parse(
                File.ReadAllText(
                    Path.Combine(
                        TestContext.CurrentContext.TestDirectory,
                        "Fixtures",
                        "workthree_failuredb_minimal.json"
                    ),
                    Encoding.UTF8
                )
            );

            JsonElement actualRoot = actualDocument.RootElement;
            foreach (JsonProperty property in fixtureDocument.RootElement.EnumerateObject())
            {
                Assert.That(
                    actualRoot.TryGetProperty(property.Name, out _),
                    Is.True,
                    $"missing key: {property.Name}"
                );
            }
        }
        finally
        {
            TryDeleteSqliteFamily(queueDbPath);
            TryDeleteSqliteFamily(failureDbPath);
            TryDelete(moviePath);
        }
    }

    private static QueueDbLeaseItem CreateLeasedItem(
        QueueDbService queueDbService,
        string moviePath,
        int tabIndex
    )
    {
        _ = queueDbService.Upsert(
            [
                new QueueDbUpsertItem
                {
                    MoviePath = moviePath,
                    MoviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath),
                    TabIndex = tabIndex,
                },
            ],
            DateTime.UtcNow
        );

        List<QueueDbLeaseItem> leased = queueDbService.GetPendingAndLease(
            $"LEASE-{Guid.NewGuid():N}",
            takeCount: 1,
            leaseDuration: TimeSpan.FromMinutes(5),
            utcNow: DateTime.UtcNow
        );
        Assert.That(leased.Count, Is.EqualTo(1));
        return leased[0];
    }

    private static void InvokeHandleFailedItem(
        QueueDbService queueDbService,
        QueueDbLeaseItem leasedItem,
        string ownerInstanceId,
        Exception ex,
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        MethodInfo? method = typeof(ThumbnailQueueProcessor).GetMethod(
            "HandleFailedItem",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        _ = method.Invoke(
            null,
            [
                queueDbService,
                leasedItem,
                ownerInstanceId,
                ex,
                (Action<string>)(_ => { }),
                failureDbService,
                ThumbnailQueueWorkerRole.Normal,
            ]
        );
    }

    private static void InvokeHandleCanceledItem(
        QueueDbService queueDbService,
        QueueDbLeaseItem leasedItem,
        string ownerInstanceId,
        OperationCanceledException ex,
        ThumbnailFailureDebugDbService failureDbService
    )
    {
        MethodInfo? method = typeof(ThumbnailQueueProcessor).GetMethod(
            "HandleCanceledItem",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.That(method, Is.Not.Null);

        _ = method.Invoke(
            null,
            [
                queueDbService,
                leasedItem,
                ownerInstanceId,
                ex,
                (Action<string>)(_ => { }),
                failureDbService,
                ThumbnailQueueWorkerRole.Normal,
            ]
        );
    }

    private static void TryDeleteSqliteFamily(string dbPath)
    {
        TryDelete(dbPath);
        TryDelete(dbPath + "-wal");
        TryDelete(dbPath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // テスト後始末の失敗は無視する。
        }
    }

    private static void AssertFailureExtraJson(
        string extraJson,
        string expectedReason,
        bool expectedWasRunning,
        int expectedAttemptAfter
    )
    {
        using JsonDocument document = JsonDocument.Parse(extraJson);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("FailureKindSource").GetString(), Is.EqualTo("queue"));
        Assert.That(root.GetProperty("Reason").GetString(), Is.EqualTo(expectedReason));
        Assert.That(root.GetProperty("WasRunning").GetBoolean(), Is.EqualTo(expectedWasRunning));
        Assert.That(
            root.GetProperty("AttemptCountAfter").GetInt32(),
            Is.EqualTo(expectedAttemptAfter)
        );
        Assert.That(root.GetProperty("ResultSignature").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("RecoveryRoute").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("DecisionBasis").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("RepairAttempted").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
        Assert.That(root.GetProperty("PreflightBranch").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("failure_kind_source").GetString(), Is.EqualTo("queue"));
        Assert.That(root.GetProperty("material_duration_sec").ValueKind, Is.EqualTo(JsonValueKind.Number).Or.EqualTo(JsonValueKind.Null));
        Assert.That(root.GetProperty("thumb_sec").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(root.GetProperty("engine_attempted").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("engine_succeeded").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
        Assert.That(root.GetProperty("seek_strategy").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("seek_sec").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(root.GetProperty("repair_succeeded").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
        Assert.That(root.GetProperty("preflight_branch").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("result_signature").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("repro_confirmed").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
        Assert.That(root.GetProperty("recovery_route").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("decision_basis").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(root.GetProperty("ExceptionType").GetString(), Is.Not.Empty);
        Assert.That(root.GetProperty("MovieExists").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
    }
}

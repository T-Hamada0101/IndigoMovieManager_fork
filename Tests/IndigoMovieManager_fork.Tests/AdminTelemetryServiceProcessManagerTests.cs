using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AdminTelemetryServiceProcessManagerTests
{
    [Test]
    public void 起動失敗後は同一セッション中に再試行しない()
    {
        AdminTelemetryServiceProcessManager manager = new();

        manager.SuppressRetryUntilNextSupervisorSession();

        Assert.That(manager.CanAttemptStart(), Is.False);
    }

    [Test]
    public void 新しいセッション開始時に再試行抑止を解除する()
    {
        AdminTelemetryServiceProcessManager manager = new();
        manager.SuppressRetryUntilNextSupervisorSession();

        manager.ResetRetrySuppressionForNewSupervisorSession();

        Assert.That(manager.CanAttemptStart(), Is.True);
    }
}

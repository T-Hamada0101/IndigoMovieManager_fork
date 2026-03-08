using System.Reflection;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailCoordinatorSlotPlannerTests
{
    [Test]
    public void ResolveSlotCounts_NormalFirst_NormalDemandDominant_KeepsSlowAtMinimum()
    {
        CoordinatorSlotDecision decision = InvokeResolveSlotCounts(
            effectiveParallelism: 8,
            operationMode: ThumbnailCoordinatorOperationMode.NormalFirst,
            new QueueDbDemandSnapshot
            {
                QueuedNormalCount = 20,
                QueuedSlowCount = 1,
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.FastSlotCount, Is.EqualTo(7));
            Assert.That(decision.SlowSlotCount, Is.EqualTo(1));
            Assert.That(decision.MinimumSlowSlots, Is.EqualTo(1));
            Assert.That(decision.WeightedSlowDemand, Is.EqualTo(1));
            Assert.That(decision.DecisionCategory, Is.EqualTo(ThumbnailCoordinatorDecisionCategory.Minimum));
            Assert.That(decision.DecisionSummary, Does.Contain("最小 slow=1 を維持"));
        });
    }

    [Test]
    public void ResolveSlotCounts_NormalFirst_OnlySlowInitialDemand_LeavesFastDelegationHeadroom()
    {
        CoordinatorSlotDecision decision = InvokeResolveSlotCounts(
            effectiveParallelism: 8,
            operationMode: ThumbnailCoordinatorOperationMode.NormalFirst,
            new QueueDbDemandSnapshot
            {
                QueuedSlowCount = 20,
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.FastSlotCount, Is.EqualTo(3));
            Assert.That(decision.SlowSlotCount, Is.EqualTo(5));
            Assert.That(decision.MaximumSlowSlots, Is.EqualTo(5));
            Assert.That(decision.DecisionCategory, Is.EqualTo(ThumbnailCoordinatorDecisionCategory.DelegationCapped));
            Assert.That(decision.DecisionSummary, Does.Contain("fast 代行余地を残す"));
        });
    }

    [Test]
    public void ResolveSlotCounts_RecoveryFirst_RecoveryPressureBiasesSlowSlots()
    {
        CoordinatorSlotDecision decision = InvokeResolveSlotCounts(
            effectiveParallelism: 8,
            operationMode: ThumbnailCoordinatorOperationMode.RecoveryFirst,
            new QueueDbDemandSnapshot
            {
                QueuedNormalCount = 2,
                QueuedSlowCount = 1,
                QueuedRecoveryCount = 4,
            }
        );

        Assert.Multiple(() =>
        {
            Assert.That(decision.FastSlotCount, Is.EqualTo(1));
            Assert.That(decision.SlowSlotCount, Is.EqualTo(7));
            Assert.That(decision.WeightedSlowDemand, Is.EqualTo(17));
            Assert.That(decision.MinimumSlowSlots, Is.EqualTo(2));
            Assert.That(decision.DecisionCategory, Is.EqualTo(ThumbnailCoordinatorDecisionCategory.RecoveryBiased));
            Assert.That(decision.DecisionSummary, Does.Contain("回復需要を優先"));
        });
    }

    private static CoordinatorSlotDecision InvokeResolveSlotCounts(
        int effectiveParallelism,
        string operationMode,
        QueueDbDemandSnapshot demandSnapshot
    )
    {
        Type hostServiceType = typeof(ThumbnailCoordinatorRuntimeOptions).Assembly.GetType(
                "IndigoMovieManager.Thumbnail.ThumbnailCoordinatorHostService",
                throwOnError: true
            )!;

        MethodInfo? method = hostServiceType.GetMethod(
            "ResolveSlotCounts",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);

        object? result = method!.Invoke(null, [effectiveParallelism, operationMode, demandSnapshot]);
        Assert.That(result, Is.TypeOf<CoordinatorSlotDecision>());
        return (CoordinatorSlotDecision)result!;
    }
}

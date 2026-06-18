using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinProfileValueCacheTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void Pending値はApiから見えるがRestoreからは見えない()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );

        bool apiHit = WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string apiValue
        );
        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string restoreValue
        );
        bool stateHit = WhiteBrowserSkinProfileValueCache.TryGetPersistState(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out WhiteBrowserSkinProfileValuePersistState state
        );

        Assert.Multiple(() =>
        {
            Assert.That(apiHit, Is.True);
            Assert.That(apiValue, Is.EqualTo("DefaultList"));
            Assert.That(restoreHit, Is.False);
            Assert.That(restoreValue, Is.Empty);
            Assert.That(stateHit, Is.True);
            Assert.That(state.Value, Is.EqualTo("DefaultList"));
            Assert.That(state.IsDirty, Is.True);
            Assert.That(state.IsFailed, Is.False);
            Assert.That(state.IsRetryable, Is.False);
            Assert.That(state.NotifyUi, Is.False);
        });
    }

    [Test]
    public void Persist成功後はRestoreからも見える()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );
        WhiteBrowserSkinProfileValueCache.RecordPersisted(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            "DefaultList"
        );

        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out string restoreValue
        );
        bool stateHit = WhiteBrowserSkinProfileValueCache.TryGetPersistState(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "LastUpperTab",
            out WhiteBrowserSkinProfileValuePersistState state
        );

        Assert.Multiple(() =>
        {
            Assert.That(restoreHit, Is.True);
            Assert.That(restoreValue, Is.EqualTo("DefaultList"));
            Assert.That(stateHit, Is.True);
            Assert.That(state.Value, Is.EqualTo("DefaultList"));
            Assert.That(state.IsDirty, Is.False);
            Assert.That(state.IsFailed, Is.False);
            Assert.That(state.IsRetryable, Is.False);
            Assert.That(state.NotifyUi, Is.False);
        });
    }

    [Test]
    public void Fault後はCacheを使わない()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            "4"
        );
        WhiteBrowserSkinProfileValueCache.RecordFault(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            "4"
        );

        bool apiHit = WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            out _
        );
        bool restoreHit = WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            out _
        );
        bool stateHit = WhiteBrowserSkinProfileValueCache.TryGetPersistState(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "grid.columns",
            out WhiteBrowserSkinProfileValuePersistState state
        );

        Assert.Multiple(() =>
        {
            Assert.That(apiHit, Is.False);
            Assert.That(restoreHit, Is.False);
            Assert.That(stateHit, Is.True);
            Assert.That(state.Value, Is.EqualTo("4"));
            Assert.That(state.IsDirty, Is.True);
            Assert.That(state.IsFailed, Is.True);
            Assert.That(state.IsRetryable, Is.True);
            Assert.That(state.NotifyUi, Is.False);
        });
    }

    [Test]
    public void Fault値未指定時は直前Pending値を保持する()
    {
        WhiteBrowserSkinProfileValueCache.RecordPending(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "layout.mode",
            "compact"
        );
        WhiteBrowserSkinProfileValueCache.RecordFault(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "layout.mode"
        );

        bool stateHit = WhiteBrowserSkinProfileValueCache.TryGetPersistState(
            @"C:\temp\sample.wb",
            "SampleSkin",
            "layout.mode",
            out WhiteBrowserSkinProfileValuePersistState state
        );

        Assert.Multiple(() =>
        {
            Assert.That(stateHit, Is.True);
            Assert.That(state.Value, Is.EqualTo("compact"));
            Assert.That(state.IsDirty, Is.True);
            Assert.That(state.IsFailed, Is.True);
            Assert.That(state.IsRetryable, Is.True);
            Assert.That(state.NotifyUi, Is.False);
        });
    }
}

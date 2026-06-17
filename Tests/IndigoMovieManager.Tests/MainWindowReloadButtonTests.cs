using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using IndigoMovieManager;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowReloadButtonTests
{
    [Test]
    public void CreateHeaderReloadLogCorrelationId_短い相関IDを返す()
    {
        string reloadId = MainWindow.CreateHeaderReloadLogCorrelationId();

        Assert.That(reloadId, Does.Match("^hr[0-9a-f]{8}$"));
    }

    [Test]
    public async Task ExecuteHeaderReloadAsync_watch抑止下でfilter完了後にmanual_scanを遅延する()
    {
        MainWindow window = CreateWindow();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());

        List<string> steps = [];
        TaskCompletionSource filterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        window.ReloadBookmarkTabDataForTesting = () => steps.Add("bookmark");
        window.FilterAndSortAsyncForTesting = async (sortId, isGetNew) =>
        {
            steps.Add($"filter:{sortId}:{isGetNew}");
            Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));
            await filterGate.Task;
        };
        window.QueueCheckFolderAsyncForTesting = (mode, trigger) =>
        {
            steps.Add($"queue:{mode}:{trigger}");
            Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));
            return Task.CompletedTask;
        };

        Task reloadTask = window.ExecuteHeaderReloadAsync("1", "Header.ReloadButton");
        await Task.Yield();

        Assert.That(
            steps,
            Is.EqualTo(new[] { "bookmark", "filter:1:True" })
        );
        Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));

        filterGate.SetResult();
        await reloadTask;

        Assert.That(steps, Is.EqualTo(new[] { "bookmark", "filter:1:True" }));
        Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(0));
    }

    [Test]
    public void ExecuteHeaderReloadAsync_filter例外でもwatch抑止を解除して再throwする()
    {
        MainWindow window = CreateWindow();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());

        window.ReloadBookmarkTabDataForTesting = () => { };
        window.FilterAndSortAsyncForTesting = (_, _) =>
            throw new InvalidOperationException("filter failed");

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await window.ExecuteHeaderReloadAsync("1", "Header.ReloadButton")
        );

        Assert.That(ex?.Message, Is.EqualTo("filter failed"));
        Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(0));
    }

    [Test]
    public void TryGetDeferredManualReloadScanSkipReason_dispatcher未初期化ならscanを積まない()
    {
        bool skipped = MainWindow.TryGetDeferredManualReloadScanSkipReason(
            dispatcher: null!,
            mainVM: new MainWindowViewModel(),
            checkFolderRequestSync: new object(),
            out string reason
        );

        Assert.That(skipped, Is.True);
        Assert.That(reason, Is.EqualTo("dispatcher-null"));
    }

    [Test]
    public void TryGetDeferredManualReloadScanSkipReason_DB未選択ならscanを積まない()
    {
        MainWindowViewModel mainVM = new();

        bool skipped = MainWindow.TryGetDeferredManualReloadScanSkipReason(
            System.Windows.Threading.Dispatcher.CurrentDispatcher,
            mainVM,
            new object(),
            out string reason
        );

        Assert.That(skipped, Is.True);
        Assert.That(reason, Is.EqualTo("db-path-empty"));
    }

    [Test]
    public void TryGetDeferredManualReloadScanSkipReason_queue未初期化ならscanを積まない()
    {
        MainWindowViewModel mainVM = new();
        mainVM.DbInfo.DBFullPath = "test-main.wb";

        bool skipped = MainWindow.TryGetDeferredManualReloadScanSkipReason(
            System.Windows.Threading.Dispatcher.CurrentDispatcher,
            mainVM,
            checkFolderRequestSync: null!,
            out string reason
        );

        Assert.That(skipped, Is.True);
        Assert.That(reason, Is.EqualTo("queue-not-initialized"));
    }

    [Test]
    public void TryGetDeferredManualReloadScanSkipReason_scan可能ならfalseを返す()
    {
        MainWindowViewModel mainVM = new();
        mainVM.DbInfo.DBFullPath = "test-main.wb";

        bool skipped = MainWindow.TryGetDeferredManualReloadScanSkipReason(
            System.Windows.Threading.Dispatcher.CurrentDispatcher,
            mainVM,
            new object(),
            out string reason
        );

        Assert.That(skipped, Is.False);
        Assert.That(reason, Is.Empty);
    }

    [Test]
    public void IsDeferredManualReloadScanSuperseded_最新でないscanだけtrueを返す()
    {
        Assert.That(MainWindow.IsDeferredManualReloadScanSuperseded(1, 2), Is.True);
        Assert.That(MainWindow.IsDeferredManualReloadScanSuperseded(2, 2), Is.False);
    }

    private static MainWindow CreateWindow()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        field.SetValue(window, value);
    }

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return (T)field.GetValue(window)!;
    }
}

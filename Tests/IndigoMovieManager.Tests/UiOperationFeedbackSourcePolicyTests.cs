using System.Xml.Linq;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UiOperationFeedbackSourcePolicyTests
{
    [Test]
    public void UserPriorityRuntime_activatedとlastEndだけを表示境界へ接続する()
    {
        const string schedule = "ScheduleUiOperationFeedback(reason);";
        const string complete = "CompleteUiOperationFeedback();";
        string source = Read("Watcher", "MainWindow.UserPriorityRuntime.cs");

        Assert.That(source, Does.Contain("activated = _userPriorityWorkCount == 1;"));
        Assert.That(source.Split(schedule).Length - 1, Is.EqualTo(1));
        Assert.That(
            source.IndexOf(schedule),
            Is.GreaterThan(source.IndexOf("BuildUserPriorityBeginLogMessage"))
        );
        Assert.That(source.Split(complete).Length - 1, Is.EqualTo(1));
        Assert.That(
            source,
            Does.Contain($"if (!isStillActive)\n            {{\n                {complete}")
        );
        Assert.That(
            source.IndexOf(complete),
            Is.GreaterThan(source.IndexOf("QueueCheckFolderAsync("))
        );
    }

    [Test]
    public void UiPartial_250msのoneShotとrevisionGuardで後着表示を防ぐ()
    {
        const string increment = "Interlocked.Increment(ref _uiOperationFeedbackRevision)";
        string source = Read("Views", "Main", "MainWindow.UiOperationFeedback.cs");
        string policy = Read("Views", "Main", "UiOperationFeedbackPolicy.cs");
        int tick = source.IndexOf("private void UiOperationFeedbackTimer_Tick");
        int stop = source.IndexOf("StopDispatcherTimerSafely(", tick);

        Assert.That(policy, Does.Contain("internal const int DelayMs = 250;"));
        Assert.That(
            source,
            Does.Contain("TimeSpan.FromMilliseconds(UiOperationFeedbackPolicy.DelayMs)")
        );
        Assert.That(source.Split(increment).Length - 1, Is.EqualTo(2));
        Assert.That(
            source,
            Does.Contain("revision != Volatile.Read(ref _uiOperationFeedbackRevision)")
        );
        Assert.That(stop, Is.GreaterThan(tick));
        Assert.That(
            source.IndexOf("UiOperationFeedbackPolicy.ShouldShow(", tick),
            Is.GreaterThan(stop)
        );
        Assert.That(source, Does.Contain("IsUserPriorityWorkActive()"));
        Assert.That(source, Does.Contain("UiOperationFeedbackPolicy.ResolveStatusText("));
        Assert.That(
            source,
            Does.Contain("dispatcher.BeginInvoke(action, DispatcherPriority.Background)")
        );
        Assert.That(source, Does.Contain("dispatcher.HasShutdownStarted"));
        Assert.That(source, Does.Contain("UiOperationFeedbackPanel == null"));
        Assert.That(source, Does.Not.Contain("Mouse.OverrideCursor"));
        Assert.That(source, Does.Not.Contain(".IsEnabled"));
    }

    [Test]
    public void MainWindowXaml_pathと同じ列へ操作不能なcompact表示を重ねる()
    {
        XDocument xaml = XDocument.Load(
            TestRepoPath.GetRepoPath("Views", "Main", "MainWindow.xaml")
        );
        XElement panel = Named(xaml, "UiOperationFeedbackPanel");
        XElement path = Named(xaml, "lbDbFullPath");
        XElement progress = panel.Descendants().Single(x => x.Name.LocalName == "ProgressBar");

        Assert.That(Attr(panel, "Column"), Is.EqualTo("8"));
        Assert.That(Attr(path, "Column"), Is.EqualTo("8"));
        Assert.That(Attr(panel, "Visibility"), Is.EqualTo("Collapsed"));
        Assert.That(Attr(panel, "IsHitTestVisible"), Is.EqualTo("False"));
        Assert.That(Attr(progress, "IsIndeterminate"), Is.EqualTo("True"));
        Assert.That(
            Named(xaml, "UiOperationFeedbackStatusText").Name.LocalName,
            Is.EqualTo("TextBlock")
        );
        Assert.That(
            panel.DescendantsAndSelf().Attributes().Any(x => x.Name.LocalName == "IsEnabled"),
            Is.False
        );
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestRepoPath.GetRepoPath(parts));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants()
            .Single(x => x.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == name));

    private static string? Attr(XElement element, string name) =>
        element
            .Attributes()
            .SingleOrDefault(x => x.Name.LocalName == name || x.Name.LocalName.EndsWith($".{name}"))
            ?.Value;

    private static class TestRepoPath
    {
        private static readonly string Root = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..")
        );

        internal static string GetRepoPath(params string[] parts) => Path.Combine([Root, .. parts]);
    }
}

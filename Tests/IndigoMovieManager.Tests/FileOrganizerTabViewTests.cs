using System.Threading;
using System.Windows;
using IndigoMovieManager.BottomTabs.FileOrganizer;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class FileOrganizerTabViewTests
{
    [Test]
    public void 移動先一覧を初回描画しても読み取り専用バインドで例外にならない()
    {
        EnsureApplicationResources();
        ResourceDictionary originalResources = Application.Current.Resources;
        Application.Current.Resources = CreateIndigoProfileResources();

        try
        {
            FileOrganizerTabView view = new();
            view.SetItems(
                Enumerable
                    .Range(1, 9)
                    .Select(number =>
                        new FileOrganizerDestinationItem(number)
                        {
                            FolderPath = number == 1 ? @"C:\Movies" : "",
                        }
                    )
                    .ToArray()
            );
            view.SetSelectedMovie(
                new MovieRecords
                {
                    Movie_Name = "代表動画",
                    Movie_Path = @"C:\Movies\sample.mp4",
                    Dir = @"C:\Movies",
                    ThumbPathSmall = "",
                },
                targetCount: 25
            );

            Assert.DoesNotThrow(() =>
            {
                // DataTemplate は Measure 時に展開されるため、実際のタブ初回表示と同じ所まで進める。
                view.Measure(new Size(1200, 260));
                view.Arrange(new Rect(0, 0, 1200, 260));
                view.UpdateLayout();
            });
        }
        finally
        {
            Application.Current.Resources = originalResources;
        }
    }

    private static void EnsureApplicationResources()
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(App).Assembly;
        }

        if (Application.Current is not null)
        {
            return;
        }

        App app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
    }

    private static ResourceDictionary CreateIndigoProfileResources()
    {
        string assemblyName = typeof(App).Assembly.GetName().Name ?? "IndigoMovieManager";
        ResourceDictionary resources = new();
        resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assemblyName};component/Themes/Profiles/Indigo.xaml",
                    UriKind.Absolute
                ),
            }
        );
        return resources;
    }
}

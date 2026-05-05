using System.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailProgress;
using IndigoMovieManager;
using System.Windows;

namespace IndigoMovieManager.Tests;

[NonParallelizable]
public sealed class ThumbnailProgressTabViewTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void ThumbnailProgressTabView_単体生成で例外を投げない()
    {
        // アプリ共通テーマをテスト中だけ積み、UserControl 単体生成が崩れないことを確認する。
        EnsureApplicationResources();
        ResourceDictionary originalResources = Application.Current.Resources;
        Application.Current.Resources = CreateIndigoProfileResources();

        try
        {
            Assert.That(
                Application.Current.TryFindResource("BottomTabRootUserControlStyle"),
                Is.InstanceOf<Style>()
            );
            Assert.That(
                Application.Current.TryFindResource("MaterialDesignDiscreteSlider"),
                Is.InstanceOf<Style>()
            );

            ThumbnailProgressTabView view = new();

            Assert.That(view, Is.Not.Null);
            Assert.That(view.ResizeThumbCheckBox, Is.Not.Null);
            Assert.That(view.GpuDecodeEnabledCheckBox, Is.Not.Null);
            Assert.That(view.ParallelismSlider, Is.Not.Null);
            Assert.That(view.SlowLaneMinGbSlider, Is.Not.Null);
            Assert.That(view.PresetLowSpeedRadioButton, Is.Not.Null);
            Assert.That(view.PresetSsdRadioButton, Is.Not.Null);
            Assert.That(view.PresetNormalRadioButton, Is.Not.Null);
            Assert.That(view.PresetFastRadioButton, Is.Not.Null);
            Assert.That(view.PresetMaxRadioButton, Is.Not.Null);
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

        App app = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
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

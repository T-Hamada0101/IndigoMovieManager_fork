using System.Threading;
using IndigoMovieManager.BottomTabs.ThumbnailProgress;
using IndigoMovieManager;
using System.Windows;

namespace IndigoMovieManager.Tests;

[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class ThumbnailProgressTabViewTests
{
    private bool hadApplication;
    private ResourceDictionary? originalApplicationResources;
    private ResourceDictionarySnapshot? originalResourceSnapshot;

    [SetUp]
    public void SetUp()
    {
        hadApplication = Application.Current is not null;
        originalApplicationResources = Application.Current?.Resources;
        originalResourceSnapshot =
            originalApplicationResources is null
                ? null
                : ResourceDictionarySnapshot.Capture(originalApplicationResources);
    }

    [TearDown]
    public void TearDown()
    {
        RestoreApplicationResources();
    }

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

    private void RestoreApplicationResources()
    {
        if (Application.Current is null)
        {
            return;
        }

        if (hadApplication && originalApplicationResources is not null)
        {
            Application.Current.Resources = originalApplicationResources;
            originalResourceSnapshot?.Restore(originalApplicationResources);
            return;
        }

        // テスト中に初めて Application を作った場合も、次のテストへ辞書を残さない。
        Application.Current.Resources = new ResourceDictionary();
    }

    private sealed class ResourceDictionarySnapshot
    {
        private readonly KeyValuePair<object, object>[] values;
        private readonly ResourceDictionary[] mergedDictionaries;

        private ResourceDictionarySnapshot(
            KeyValuePair<object, object>[] values,
            ResourceDictionary[] mergedDictionaries
        )
        {
            this.values = values;
            this.mergedDictionaries = mergedDictionaries;
        }

        public static ResourceDictionarySnapshot Capture(ResourceDictionary dictionary)
        {
            // UserControl 生成中に Resources が汚れても、fixture 外へ持ち越さない。
            KeyValuePair<object, object>[] values = dictionary.Keys
                .Cast<object>()
                .Select(key => new KeyValuePair<object, object>(key, dictionary[key]))
                .ToArray();
            ResourceDictionary[] mergedDictionaries = dictionary.MergedDictionaries.ToArray();
            return new ResourceDictionarySnapshot(values, mergedDictionaries);
        }

        public void Restore(ResourceDictionary dictionary)
        {
            foreach (object key in dictionary.Keys.Cast<object>().ToArray())
            {
                dictionary.Remove(key);
            }

            dictionary.MergedDictionaries.Clear();

            foreach (KeyValuePair<object, object> value in values)
            {
                dictionary[value.Key] = value.Value;
            }

            foreach (ResourceDictionary mergedDictionary in mergedDictionaries)
            {
                dictionary.MergedDictionaries.Add(mergedDictionary);
            }
        }
    }
}

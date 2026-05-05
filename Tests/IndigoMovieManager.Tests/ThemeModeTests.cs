using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class ThemeModeTests
{
    private string originalThemeMode = "";
    private bool hadApplication;
    private ResourceDictionary? originalApplicationResources;
    private ResourceDictionarySnapshot? originalResourceSnapshot;

    private static readonly string[] ValidThemeModes =
    [
        "Indigo",
        "SimpleLight",
        "SimpleDark",
        "SystemAuto",
    ];

    private static readonly string[] RequiredSimpleBrushKeys =
    [
        "MaterialDesignPaper",
        "MaterialDesignCardBackground",
        "MaterialDesignDivider",
        "MaterialDesignBody",
        "MaterialDesignBodyLight",
        "MaterialDesignBodySecondary",
        "PrimaryHueMidBrush",
        "PrimaryHueMidForegroundBrush",
        "AppWindowBackgroundBrush",
        "AppSurfaceBrush",
        "AppSurfaceAltBrush",
        "AppBorderBrush",
        "AppTextBrush",
        "AppTextSubtleBrush",
        "AppAccentBrush",
        "AppAccentForegroundBrush",
        "MainHeaderBackgroundBrush",
        "MainHeaderForegroundBrush",
        "MainHeaderInputForegroundBrush",
        "NavigationDrawerBackgroundBrush",
        "NavigationDrawerForegroundBrush",
        "UpperTabBackgroundBrush",
        "ThumbImageBackgroundBrush",
    ];

    private static readonly string[] RequiredSimpleStyleKeys =
    [
        "AppHeaderButtonStyle",
        "AppIconButtonStyle",
        "AppIconRepeatButtonStyle",
        "AppSearchComboBoxStyle",
        "AppCompactComboBoxStyle",
        "AppTextBoxStyle",
        "AppTreeViewStyle",
        "AppDataGridColumnHeaderStyle",
        "AppDataGridCellStyle",
        "AppDataGridRowStyle",
        "AppBottomTabButtonStyle",
        "AppTabItemStyle",
        "AppComboBoxItemStyle",
        "AppHamburgerToggleButtonStyle",
        "AppDiscreteSliderStyle",
    ];

    [SetUp]
    public void SetUp()
    {
        originalThemeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";
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
        RestoreThemeModeSetting();
    }

    [TestCase("Original", "Indigo")]
    [TestCase("original", "Indigo")]
    [TestCase("OsSync", "SystemAuto")]
    [TestCase("ossync", "SystemAuto")]
    [TestCase("Indigo", "Indigo")]
    [TestCase("SimpleLight", "SimpleLight")]
    [TestCase("SimpleDark", "SimpleDark")]
    [TestCase("SystemAuto", "SystemAuto")]
    [TestCase("", "Indigo")]
    [TestCase("UnknownTheme", "Indigo")]
    public void NormalizeThemeMode_旧値と不明値を4テーマへ寄せる(string input, string expected)
    {
        string actual = App.NormalizeThemeMode(input);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void CommonSettingsWindow_テーマ保存値は4テーマだけを並べる()
    {
        string xamlPath = Path.Combine(
            FindRepositoryRoot(),
            "Views",
            "Settings",
            "CommonSettingsWindow.xaml"
        );
        XDocument document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement themeComboBox = document
            .Descendants(presentation + "ComboBox")
            .Single(item => (string?)item.Attribute(xaml + "Name") == "ThemeComboBox");

        string[] actualTags = themeComboBox
            .Elements(presentation + "ComboBoxItem")
            .Select(item => (string?)item.Attribute("Tag"))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Cast<string>()
            .ToArray();

        Assert.That(actualTags, Is.EqualTo(ValidThemeModes));
    }

    [Test]
    public void MaterialDesignなし_アプリ起動辞書とcsprojが本体パッケージを読まない()
    {
        string appXaml = GetRepoText("App.xaml");
        string projectFile = GetRepoText("IndigoMovieManager.csproj");
        string appSource = GetRepoText("App.xaml.cs");
        string settingsSource = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(appXaml, Does.Not.Contain("MaterialDesignThemes.Wpf;component"));
            Assert.That(appXaml, Does.Not.Contain("BundledTheme"));
            Assert.That(projectFile, Does.Not.Contain("PackageReference Include=\"MaterialDesignThemes"));
            Assert.That(projectFile, Does.Not.Contain("PackageReference Include=\"MaterialDesignColors"));
            Assert.That(appSource, Does.Not.Contain("OriginalColors.xaml"));
            Assert.That(appSource, Does.Not.Contain("OsSyncColors.xaml"));
            Assert.That(appSource, Does.Contain("SaveThemeModeSettingBestEffort"));
            Assert.That(appSource, Does.Contain("catch (Exception ex)"));
            Assert.That(settingsSource, Does.Contain("App.NormalizeThemeMode("));
            Assert.That(settingsSource, Does.Contain("App.SaveThemeModeSettingBestEffort("));
            Assert.That(settingsSource, Does.Not.Contain("private static string NormalizeThemeMode("));
        });
    }

    [TestCase("Indigo", "Themes/Profiles/Indigo.xaml")]
    [TestCase("Original", "Themes/Profiles/Indigo.xaml")]
    [TestCase("UnknownTheme", "Themes/Profiles/Indigo.xaml")]
    [TestCase("SimpleLight", "Themes/Profiles/SimpleLight.xaml")]
    [TestCase("SimpleDark", "Themes/Profiles/SimpleDark.xaml")]
    public void ApplyTheme_テーマモードに対応するプロファイル辞書を選ぶ(
        string input,
        string expectedProfilePath
    )
    {
        EnsureApplicationResources();

        App.ApplyTheme(input);

        string expectedSuffix = expectedProfilePath.Replace('\\', '/');
        bool hasExpectedProfile = Application.Current.Resources.MergedDictionaries.Any(dictionary =>
            dictionary.Source?.ToString().Replace('\\', '/').EndsWith(
                expectedSuffix,
                StringComparison.OrdinalIgnoreCase
            ) == true
        );

        Assert.That(
            hasExpectedProfile,
            Is.True,
            $"{input} は {expectedProfilePath} を読み込む必要があります。"
        );
    }

    [Test]
    public void ApplyTheme_4テーマを連続切替してProfile辞書を1つだけ残す()
    {
        EnsureApplicationResources();

        foreach (string themeMode in ValidThemeModes)
        {
            App.ApplyTheme(themeMode);

            string[] profileSources = GetCurrentProfileSources();
            Assert.That(
                profileSources,
                Has.Length.EqualTo(1),
                $"{themeMode} 切替後の Profile 辞書数です。"
            );
            Assert.That(
                profileSources[0],
                Does.Contain("/Themes/Profiles/"),
                $"{themeMode} は Profiles 配下の辞書を正本にします。"
            );
        }
    }

    [Test]
    public void ApplyTheme_SystemAutoはSimpleLightまたはSimpleDarkプロファイルを選ぶ()
    {
        EnsureApplicationResources();

        App.ApplyTheme("SystemAuto");

        string[] profileSources = Application.Current.Resources.MergedDictionaries
            .Select(dictionary => dictionary.Source?.ToString().Replace('\\', '/') ?? "")
            .Where(source => source.Contains("/Themes/Profiles/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(
            profileSources,
            Has.One.Matches<string>(source =>
                source.EndsWith("Themes/Profiles/SimpleLight.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Themes/Profiles/SimpleDark.xaml", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    [Test]
    public void ApplyTheme_SystemAutoは保存値をSystemAutoのまま維持する()
    {
        EnsureApplicationResources();
        string originalThemeMode = IndigoMovieManager.Properties.Settings.Default.ThemeMode ?? "";

        try
        {
            IndigoMovieManager.Properties.Settings.Default.ThemeMode = "Indigo";

            App.ApplyTheme("SystemAuto");

            Assert.That(
                IndigoMovieManager.Properties.Settings.Default.ThemeMode,
                Is.EqualTo("SystemAuto")
            );
        }
        finally
        {
            App.ApplyTheme(originalThemeMode);
            IndigoMovieManager.Properties.Settings.Default.ThemeMode = originalThemeMode;
            IndigoMovieManager.Properties.Settings.Default.Save();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ApplyTheme_既存MaterialDesign風辞書があってもProfileのBrushを優先する()
    {
        EnsureApplicationResources();
        ResourceDictionary fakeMaterialDesignDictionary = new()
        {
            ["MaterialDesignPaper"] = new SolidColorBrush(Colors.Magenta),
        };

        Application.Current.Resources.MergedDictionaries.Add(fakeMaterialDesignDictionary);
        try
        {
            App.ApplyTheme("SimpleDark");

            object actual = Application.Current.TryFindResource("MaterialDesignPaper");
            Assert.That(actual, Is.InstanceOf<SolidColorBrush>());
            Assert.That(
                ((SolidColorBrush)actual).Color,
                Is.EqualTo(Color.FromRgb(0x18, 0x1A, 0x20))
            );
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(fakeMaterialDesignDictionary);
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ApplyTheme_IndigoからSystemAutoへ切り替えても例外にならない()
    {
        EnsureApplicationResources();

        App.ApplyTheme("Indigo");

        Assert.DoesNotThrow(() => App.ApplyTheme("SystemAuto"));
    }

    [TestCase("SimpleLight")]
    [TestCase("SimpleDark")]
    [Apartment(ApartmentState.STA)]
    public void Simpleテーマプロファイル_主要BrushとStyleキーを解決できる(string themeName)
    {
        ResourceDictionary dictionary = LoadProfileDictionaryWithoutApplicationFallback(themeName);

        foreach (string key in RequiredSimpleBrushKeys)
        {
            object? value = FindResource(dictionary, key);
            Assert.That(value, Is.InstanceOf<Brush>(), $"{themeName} の Brush key '{key}' が未解決です。");
        }

        foreach (string key in RequiredSimpleStyleKeys)
        {
            object? value = FindResource(dictionary, key);
            Assert.That(value, Is.InstanceOf<Style>(), $"{themeName} の style key '{key}' が未解決です。");
        }
    }

    private static ResourceDictionary LoadProfileDictionaryWithoutApplicationFallback(string themeName)
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(App).Assembly;
        }

        ResourceDictionary? originalResources = Application.Current?.Resources;
        try
        {
            if (Application.Current is not null)
            {
                Application.Current.Resources = new ResourceDictionary();
            }

            // Simple 系はアプリ全体のMDIX辞書に頼らず、Profile内だけで主要キーを解決する。
            string assemblyName = typeof(App).Assembly.GetName().Name ?? "IndigoMovieManager";
            return new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assemblyName};component/Themes/Profiles/{themeName}.xaml",
                    UriKind.Absolute
                ),
            };
        }
        finally
        {
            if (Application.Current is not null && originalResources is not null)
            {
                Application.Current.Resources = originalResources;
            }
        }
    }

    private static object? FindResource(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary[key];
        }

        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            object? value = FindResource(mergedDictionary, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string[] GetCurrentProfileSources()
    {
        return Application.Current.Resources.MergedDictionaries
            .Select(dictionary => dictionary.Source?.ToString().Replace('\\', '/') ?? "")
            .Where(source => source.Contains("/Themes/Profiles/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(App).Assembly;
        }

        App app = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.InitializeComponent();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IndigoMovieManager.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("IndigoMovieManager.csproj を含むリポジトリルートを見つけられませんでした。");
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        string path = Path.Combine([FindRepositoryRoot(), .. relativePathParts]);
        return File.ReadAllText(path);
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

        // テスト中に初めて Application を作った場合も、次のテストへテーマ辞書を残さない。
        Application.Current.Resources = new ResourceDictionary();
    }

    private void RestoreThemeModeSetting()
    {
        if (
            string.Equals(
                IndigoMovieManager.Properties.Settings.Default.ThemeMode,
                originalThemeMode,
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        IndigoMovieManager.Properties.Settings.Default.ThemeMode = originalThemeMode;
        try
        {
            IndigoMovieManager.Properties.Settings.Default.Save();
        }
        catch
        {
            // 設定保存失敗は、テスト本体の成否と切り分ける。
        }
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
            // ApplyTheme は同じ Resources へキーも辞書も足すため、戻せる粒度で控える。
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

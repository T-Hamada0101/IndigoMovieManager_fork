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
        "AppScrollBarStyle",
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
    public void ThemeMode_新規既定値はOS自動にする()
    {
        string settingsSource = GetRepoText("Properties", "Settings.settings");
        string designerSource = GetRepoText("Properties", "Settings.Designer.cs");

        Assert.Multiple(() =>
        {
            Assert.That(
                settingsSource,
                Does.Contain("<Setting Name=\"ThemeMode\" Type=\"System.String\" Scope=\"User\">")
            );
            Assert.That(
                settingsSource,
                Does.Contain("<Value Profile=\"(Default)\">SystemAuto</Value>")
            );
            Assert.That(
                designerSource,
                Does.Contain("DefaultSettingValueAttribute(\"SystemAuto\")")
            );
        });
    }

    [Test]
    public void CommonSettingsWindow_カテゴリ左ペインと現在DB設定を持つ()
    {
        string settingsXaml = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml");
        string settingsSource = GetRepoText("Views", "Settings", "CommonSettingsWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(settingsXaml, Does.Contain("TabStripPlacement=\"Left\""));
            Assert.That(
                settingsXaml,
                Does.Contain("BasedOn=\"{StaticResource AppTabItemStyle}\"")
            );
            Assert.That(
                settingsXaml,
                Does.Contain("Background=\"{DynamicResource MaterialDesignPaper}\"")
            );
            Assert.That(settingsXaml, Does.Contain("Header=\"現在DB\""));
            Assert.That(settingsXaml, Does.Contain("Header=\"ツール\""));
            Assert.That(settingsXaml, Does.Contain("x:Name=\"CurrentDbSettingsPanel\""));
            Assert.That(settingsXaml, Does.Contain("x:Name=\"ThumbFolder\""));
            Assert.That(settingsSource, Does.Contain("InitializeCurrentDbSettings();"));
            Assert.That(settingsSource, Does.Contain("PersistCurrentDbSettingsValuesIfNeeded();"));
        });
    }

    [Test]
    public void Simpleテーマ_標準WPF色と軽量テンプレートを明示する()
    {
        string simpleLight = GetRepoText("Themes", "Colors", "SimpleLight.xaml");
        string simpleDark = GetRepoText("Themes", "Colors", "SimpleDark.xaml");
        string indigo = GetRepoText("Themes", "Colors", "Indigo.xaml");
        string lightweight = GetRepoText("Themes", "Controls", "Lightweight.xaml");

        string[] systemColorKeys =
        [
            "SystemColors.WindowBrushKey",
            "SystemColors.WindowTextBrushKey",
            "SystemColors.ControlBrushKey",
            "SystemColors.ControlTextBrushKey",
            "SystemColors.HighlightBrushKey",
            "SystemColors.HighlightTextBrushKey",
        ];

        foreach (string source in new[] { simpleLight, simpleDark, indigo })
        {
            foreach (string key in systemColorKeys)
            {
                Assert.That(source, Does.Contain(key), $"{key} がテーマ色辞書から落ちています。");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(lightweight, Does.Contain("ControlTemplate TargetType=\"{x:Type Button}\""));
            Assert.That(lightweight, Does.Contain("ControlTemplate TargetType=\"{x:Type ComboBox}\""));
            Assert.That(lightweight, Does.Contain("ControlTemplate TargetType=\"{x:Type ComboBoxItem}\""));
            Assert.That(lightweight, Does.Contain("ControlTemplate TargetType=\"{x:Type TabItem}\""));
            Assert.That(lightweight, Does.Contain("ControlTemplate TargetType=\"{x:Type ScrollBar}\""));
            Assert.That(lightweight, Does.Contain("x:Name=\"PART_EditableTextBox\""));
            Assert.That(lightweight, Does.Contain("x:Name=\"PART_Popup\""));
            Assert.That(lightweight, Does.Contain("x:Name=\"PART_Track\""));
            Assert.That(lightweight, Does.Contain("SystemColors.ControlBrushKey"));
            Assert.That(lightweight, Does.Contain("SystemColors.WindowBrushKey"));
            Assert.That(lightweight, Does.Contain("SystemColors.WindowTextBrushKey"));
            Assert.That(
                lightweight,
                Does.Contain("Style BasedOn=\"{StaticResource LightweightComboBoxItemStyle}\" TargetType=\"ComboBoxItem\"")
            );
            Assert.That(
                lightweight,
                Does.Contain("Style BasedOn=\"{StaticResource LightweightBaseButtonStyle}\" TargetType=\"Button\"")
            );
            Assert.That(
                lightweight,
                Does.Contain("Style BasedOn=\"{StaticResource LightweightComboBoxStyle}\" TargetType=\"ComboBox\"")
            );
            Assert.That(
                lightweight,
                Does.Contain("Style BasedOn=\"{StaticResource LightweightScrollBarStyle}\" TargetType=\"{x:Type ScrollBar}\"")
            );
            Assert.That(lightweight, Does.Contain("<Style x:Key=\"AppHeaderButtonStyle\""));
            Assert.That(lightweight, Does.Contain("<Setter Property=\"Height\" Value=\"24\" />"));
            Assert.That(lightweight, Does.Contain("<Style x:Key=\"AppSearchComboBoxStyle\""));
            Assert.That(lightweight, Does.Contain("<Setter Property=\"Height\" Value=\"26\" />"));
            Assert.That(lightweight, Does.Contain("<Style x:Key=\"AppCompactComboBoxStyle\""));
            Assert.That(lightweight, Does.Contain("TextElement.Foreground"));
            Assert.That(lightweight, Does.Not.Contain("BasedOn=\"{StaticResource {x:Type TabItem}}\""));
        });
    }

    [Test]
    public void MainWindow_上部タブ背景は個別指定せず標準タブ色へ任せる()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(mainWindowXaml, Does.Contain("BasedOn=\"{StaticResource AppTabItemStyle}\""));
            Assert.That(
                mainWindowXaml,
                Does.Contain("Background=\"{DynamicResource {x:Static SystemColors.WindowBrushKey}}\"")
            );
            Assert.That(mainWindowXaml, Does.Contain("<Style TargetType=\"ListView\">"));
            Assert.That(mainWindowXaml, Does.Contain("<Style TargetType=\"DataGrid\">"));
            Assert.That(
                mainWindowXaml,
                Does.Contain("ColumnHeaderStyle\" Value=\"{StaticResource HighDensityBottomTabDataGridColumnHeaderStyle}\"")
            );
            Assert.That(
                mainWindowXaml,
                Does.Contain("CellStyle\" Value=\"{StaticResource HighDensityBottomTabDataGridCellStyle}\"")
            );
            Assert.That(
                mainWindowXaml,
                Does.Contain("BasedOn=\"{StaticResource AppDataGridRowStyle}\" TargetType=\"DataGridRow\"")
            );
            Assert.That(
                mainWindowXaml,
                Does.Not.Contain("Background=\"{DynamicResource UpperTabBackground}\"")
            );
            Assert.That(mainWindowXaml, Does.Contain("<TabItem x:Name=\"TabSmall\" Header=\"Small\""));
            Assert.That(mainWindowXaml, Does.Contain("<TabItem x:Name=\"TabBig\" Header=\"Big\""));
            Assert.That(mainWindowXaml, Does.Contain("<TabItem x:Name=\"TabGrid\" Header=\"Grid\""));
            Assert.That(mainWindowXaml, Does.Contain("<TabItem x:Name=\"TabBig10\" Header=\"5x2\""));
        });
    }

    [Test]
    public void MainWindow_ヘッダーとメインタブは高密度にしてサムネ領域を確保する()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(mainWindowXaml, Does.Contain("Margin=\"0,32,0,0\""));
            Assert.That(mainWindowXaml, Does.Contain("<RowDefinition Height=\"32\" />"));
            Assert.That(mainWindowXaml, Does.Contain("Padding=\"6,3\""));
            Assert.That(mainWindowXaml, Does.Contain("x:Name=\"MainHeaderStandardChromePanel\""));
            Assert.That(mainWindowXaml, Does.Contain("Height=\"26\""));
            Assert.That(mainWindowXaml, Does.Contain("<Setter Property=\"Height\" Value=\"24\" />"));
            Assert.That(mainWindowXaml, Does.Contain("<Setter Property=\"FontSize\" Value=\"11\" />"));
            Assert.That(mainWindowXaml, Does.Contain("<Setter Property=\"Padding\" Value=\"10,2,10,2\" />"));
            Assert.That(mainWindowXaml, Does.Contain("Width=\"126\""));
            Assert.That(mainWindowXaml, Does.Contain("Width=\"132\""));
            Assert.That(mainWindowXaml, Does.Not.Contain("Margin=\"0,48,0,0\""));
            Assert.That(mainWindowXaml, Does.Not.Contain("<RowDefinition Height=\"48\" />"));
            Assert.That(mainWindowXaml, Does.Not.Contain("Padding=\"10,6\""));
            Assert.That(mainWindowXaml, Does.Not.Contain("Padding=\"16,6,16,6\""));
        });
    }

    [Test]
    public void MainWindow_標準ヘッダーの高密度配置をsource_policyで固定する()
    {
        string mainWindowXaml = GetRepoText("Views", "Main", "MainWindow.xaml");
        string standardHeader = GetSourceSection(
            mainWindowXaml,
            "x:Name=\"MainHeaderStandardChromePanel\"",
            "x:Name=\"ExternalSkinMinimalChromePanel\""
        );
        string headerColumns = GetSourceSection(
            standardHeader,
            "<Grid.ColumnDefinitions>",
            "</Grid.ColumnDefinitions>"
        );
        string[] columnDefinitions = headerColumns
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("<ColumnDefinition", StringComparison.Ordinal))
            .ToArray();
        string fallbackNotice = GetSourceSection(
            mainWindowXaml,
            "x:Name=\"ExternalSkinFallbackNoticeBorder\"",
            "</Border>"
        );
        string searchBox = GetNamedElementStartTag(mainWindowXaml, "SearchBox");
        string comboSort = GetNamedElementStartTag(mainWindowXaml, "ComboSort");
        string skinSelector = GetNamedElementStartTag(
            mainWindowXaml,
            "ExternalSkinMinimalSkinSelector"
        );
        string[] headerButtonNames =
        [
            "BtnNew",
            "BtnOpen",
            "BtnSettings",
            "BulkTagAssignButton",
            "ReloadButton",
            "ExternalSkinFallbackRetryButton",
            "ExternalSkinFallbackOpenRuntimeDownloadButton",
            "ExternalSkinFallbackOpenLogButton",
        ];

        Assert.Multiple(() =>
        {
            // ヘッダーの密度は表示面積に直結するため、MainWindow側の配置契約だけを固定する。
            Assert.That(standardHeader, Does.Contain("Height=\"26\""));

            Assert.That(columnDefinitions, Has.Length.EqualTo(10));
            Assert.That(
                columnDefinitions[0],
                Is.EqualTo("<ColumnDefinition Width=\"2*\" MinWidth=\"150\" MaxWidth=\"260\" />")
            );
            Assert.That(
                columnDefinitions[8],
                Is.EqualTo("<ColumnDefinition Width=\"*\" MinWidth=\"0\" />")
            );
            Assert.That(columnDefinitions[^1], Is.EqualTo("<ColumnDefinition Width=\"Auto\" />"));

            Assert.That(fallbackNotice, Does.Contain("Grid.Column=\"9\""));
            Assert.That(fallbackNotice, Does.Contain("MaxWidth=\"320\""));
            Assert.That(fallbackNotice, Does.Contain("TextTrimming=\"CharacterEllipsis\""));

            Assert.That(searchBox, Does.Contain("FontSize=\"11\""));
            Assert.That(searchBox, Does.Contain("Style=\"{StaticResource AppSearchComboBoxStyle}\""));
            Assert.That(comboSort, Does.Contain("FontSize=\"11\""));
            Assert.That(comboSort, Does.Contain("Style=\"{StaticResource AppCompactComboBoxStyle}\""));
            Assert.That(skinSelector, Does.Contain("FontSize=\"11\""));
            Assert.That(
                skinSelector,
                Does.Contain("Style=\"{StaticResource AppCompactComboBoxStyle}\"")
            );

            foreach (string buttonName in headerButtonNames)
            {
                string button = GetNamedElementStartTag(mainWindowXaml, buttonName);
                Assert.That(
                    button,
                    Does.Contain("Style=\"{StaticResource AppHeaderButtonStyle}\""),
                    $"{buttonName} はヘッダー共通ボタンstyleを使う必要があります。"
                );
            }
        });
    }

    [Test]
    public void RescueTab_対象タブComboBoxは共通ComboBox色へ寄せる()
    {
        string rescueTabXaml = GetRepoText("UpperTabs", "Rescue", "RescueTabView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(rescueTabXaml, Does.Contain("x:Name=\"TargetTabComboBox\""));
            Assert.That(
                rescueTabXaml,
                Does.Contain("Style=\"{StaticResource AppCompactComboBoxStyle}\"")
            );
            Assert.That(
                rescueTabXaml,
                Does.Contain("ItemContainerStyle=\"{StaticResource AppComboBoxItemStyle}\"")
            );
        });
    }

    [Test]
    public void ThumbnailProgressTab_未作成走査ボタンは共通ボタン色へ寄せる()
    {
        string thumbnailProgressXaml = GetRepoText(
            "BottomTabs",
            "ThumbnailProgress",
            "ThumbnailProgressTabView.xaml"
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                thumbnailProgressXaml,
                Does.Contain("x:Name=\"ThumbnailProgressMissingScanButton\"")
            );
            Assert.That(
                thumbnailProgressXaml,
                Does.Contain("Style=\"{StaticResource HighDensityBottomTabButtonStyle}\"")
            );
        });
    }

    [Test]
    public void DuplicateVideosTab_一覧UIパーツ背景はテーマ標準色へ寄せる()
    {
        string duplicateVideosXaml = GetRepoText(
            "UpperTabs",
            "DuplicateVideos",
            "DuplicateVideosTabView.xaml"
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("Style=\"{StaticResource BottomTabRootUserControlStyle}\"")
            );
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("BasedOn=\"{StaticResource AppBottomTabButtonStyle}\"")
            );
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("BasedOn=\"{StaticResource AppCompactComboBoxStyle}\"")
            );
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("Background=\"{DynamicResource {x:Static SystemColors.WindowBrushKey}}\"")
            );
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("Foreground=\"{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}\"")
            );
            Assert.That(duplicateVideosXaml, Does.Contain("CellStyle=\"{StaticResource AppDataGridCellStyle}\""));
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("ColumnHeaderStyle=\"{StaticResource AppDataGridColumnHeaderStyle}\"")
            );
            Assert.That(
                duplicateVideosXaml,
                Does.Contain("RowStyle=\"{StaticResource AppDataGridRowStyle}\"")
            );
        });
    }

    [Test]
    public void ExtDetail_ドロップダウンとスクロールバーはテーマ標準色へ寄せる()
    {
        string extDetailXaml = GetRepoText("UserControls", "ExtDetail.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(
                extDetailXaml,
                Does.Contain("BasedOn=\"{StaticResource AppCompactComboBoxStyle}\"")
            );
            Assert.That(
                extDetailXaml,
                Does.Contain("ItemContainerStyle\" Value=\"{StaticResource AppComboBoxItemStyle}\"")
            );
            Assert.That(
                extDetailXaml,
                Does.Contain("Background=\"{DynamicResource {x:Static SystemColors.WindowBrushKey}}\"")
            );
            Assert.That(
                extDetailXaml,
                Does.Contain("TextElement.Foreground=\"{DynamicResource {x:Static SystemColors.WindowTextBrushKey}}\"")
            );
            Assert.That(extDetailXaml, Does.Not.Contain("MaterialDesignOutlinedComboBox"));
            Assert.That(extDetailXaml, Does.Not.Contain("TextFieldAssist"));
            Assert.That(extDetailXaml, Does.Not.Contain("HintAssist"));
        });
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
                Is.EqualTo(Color.FromRgb(0x20, 0x20, 0x20))
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

    private static string GetSourceSection(string source, string startMarker, string endMarker)
    {
        int markerIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), $"{startMarker} が見つかりません。");

        int startIndex = source.LastIndexOf('<', markerIndex);
        Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), $"{startMarker} の開始タグを見つけられません。");

        int endIndex = source.IndexOf(endMarker, markerIndex, StringComparison.Ordinal);
        Assert.That(endIndex, Is.GreaterThanOrEqualTo(0), $"{endMarker} が見つかりません。");

        return source[startIndex..endIndex];
    }

    private static string GetNamedElementStartTag(string source, string xName)
    {
        int nameIndex = source.IndexOf($"x:Name=\"{xName}\"", StringComparison.Ordinal);
        Assert.That(nameIndex, Is.GreaterThanOrEqualTo(0), $"{xName} が見つかりません。");

        int startIndex = source.LastIndexOf('<', nameIndex);
        Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), $"{xName} の開始タグを見つけられません。");

        int endIndex = source.IndexOf('>', nameIndex);
        Assert.That(endIndex, Is.GreaterThanOrEqualTo(0), $"{xName} の終了位置を見つけられません。");

        return source[startIndex..(endIndex + 1)];
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

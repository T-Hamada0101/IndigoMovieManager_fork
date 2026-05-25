using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabImageSourceConverterTests
{
    [SetUp]
    public void SetUp()
    {
        UpperTabActivationGate.ClearPreferredMoviePathKeys();
    }

    [TearDown]
    public void TearDown()
    {
        UpperTabActivationGate.ClearPreferredMoviePathKeys();
    }

    [Test]
    public void 非アクティブタブでは画像更新しない()
    {
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(false), Is.False);
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(true), Is.True);
        Assert.That(UpperTabActivationGate.ShouldApplyImageUpdate(null), Is.True);
    }

    [Test]
    public void 可視近傍に無い動画は画像更新しない()
    {
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [QueueDbPathResolver.CreateMoviePathKey(Path.Combine("movies", "visible.mp4"))]
        );

        bool actual = UpperTabActivationGate.ShouldApplyImageUpdate(
            true,
            Path.Combine("movies", "hidden.mp4")
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void 同じ可視近傍キー更新は変更なしとして扱う()
    {
        string visibleMoviePathKey = QueueDbPathResolver.CreateMoviePathKey(
            Path.Combine("movies", "visible.mp4")
        );

        bool firstChanged = UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [visibleMoviePathKey]
        );
        bool secondChanged = UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [visibleMoviePathKey]
        );

        Assert.That(firstChanged, Is.True);
        Assert.That(secondChanged, Is.False);
        Assert.That(
            UpperTabActivationGate.ShouldApplyImageUpdate(
                true,
                Path.Combine("movies", "visible.mp4")
            ),
            Is.True
        );
    }

    [Test]
    public void Null更新は空確定ではなく未初期化へ戻す変更として扱う()
    {
        UpperTabActivationGate.UpdatePreferredMoviePathKeys([]);

        bool changed = UpperTabActivationGate.UpdatePreferredMoviePathKeys(null);

        Assert.That(changed, Is.True);
        Assert.That(
            UpperTabActivationGate.ShouldApplyImageUpdate(
                true,
                Path.Combine("movies", "hidden.mp4")
            ),
            Is.True
        );
    }

    [Test]
    public void 可視近傍の空集合が確定した場合はMoviePath付き画像更新を通さない()
    {
        // viewport 評価後に対象なしが確定した時は、Movie_Path 付きの再評価を止める。
        UpperTabActivationGate.UpdatePreferredMoviePathKeys([]);

        bool actual = UpperTabActivationGate.ShouldApplyImageUpdate(
            true,
            Path.Combine("movies", "hidden.mp4")
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void Clear直後の未初期化状態では互換のためMoviePath付き画像更新を通す()
    {
        // Clear は未初期化へ戻す入口なので、既存画面の初回表示互換を優先して通す。
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [QueueDbPathResolver.CreateMoviePathKey(Path.Combine("movies", "visible.mp4"))]
        );
        UpperTabActivationGate.ClearPreferredMoviePathKeys();

        bool actual = UpperTabActivationGate.ShouldApplyImageUpdate(
            true,
            Path.Combine("movies", "hidden.mp4")
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void Null更新は未初期化状態としてMoviePath付き画像更新を通す()
    {
        // null は snapshot なしとして扱い、空確定と混ぜない。
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(null);

        bool actual = UpperTabActivationGate.ShouldApplyImageUpdate(
            true,
            Path.Combine("movies", "hidden.mp4")
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void 非アクティブ時のconverterはUnsetValueを返す()
    {
        UpperTabImageSourceConverter converter = new();

        object actual = converter.Convert(
            [@"C:\thumb\a.jpg", true, false],
            typeof(System.Windows.Media.ImageSource),
            UpperTabDecodeProfile.SmallDecodePixelHeight,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.SameAs(DependencyProperty.UnsetValue));
    }

    [Test]
    public void 可視近傍キー外の動画はconverterでもUnsetValueを返す()
    {
        UpperTabImageSourceConverter converter = new();
        string visibleMoviePath = Path.Combine("movies", "visible.mp4");
        string hiddenMoviePath = Path.Combine("movies", "hidden.mp4");

        // gate 単体だけでなく converter 経路でも off-screen 抑止が効くことを固定する。
        UpperTabActivationGate.UpdatePreferredMoviePathKeys(
            [QueueDbPathResolver.CreateMoviePathKey(visibleMoviePath)]
        );

        object actual = converter.Convert(
            [@"C:\thumb\hidden.jpg", true, true, hiddenMoviePath],
            typeof(System.Windows.Media.ImageSource),
            UpperTabDecodeProfile.SmallDecodePixelHeight,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.SameAs(DependencyProperty.UnsetValue));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void 選択中で画像が存在すればImageSourceを返す()
    {
        UpperTabImageSourceConverter converter = new();
        string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        string moviePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        try
        {
            using Bitmap bitmap = new(8, 8);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Red);
            }

            bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
            UpperTabActivationGate.UpdatePreferredMoviePathKeys(
                [QueueDbPathResolver.CreateMoviePathKey(moviePath)]
            );

            object actual = converter.Convert(
                [tempPath, true, true, moviePath],
                typeof(System.Windows.Media.ImageSource),
                UpperTabDecodeProfile.GridDecodePixelHeight,
                CultureInfo.InvariantCulture
            );

            Assert.That(actual, Is.AssignableTo<System.Windows.Media.ImageSource>());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

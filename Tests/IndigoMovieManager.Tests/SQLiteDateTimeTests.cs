using System.Globalization;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SQLiteDateTimeTests
{
    [Test]
    public void ReadDbDateTimeTextOrEmpty_正規文字列は同じ参照を返す()
    {
        string source = new(['2', '0', '2', '4', '-', '0', '2', '-', '2', '9', ' ', '1', '2', ':', '3', '4', ':', '5', '6']);

        string actual = SQLite.ReadDbDateTimeTextOrEmpty(source);

        Assert.That(actual, Is.SameAs(source));
    }

    [TestCase("2024-02-29 23:59:59", true)]
    [TestCase("2023-02-29 23:59:59", false)]
    [TestCase("2024-13-01 00:00:00", false)]
    [TestCase("2024/02/29 23:59:59", false)]
    [TestCase("2024-02-29T23:59:59", false)]
    [TestCase("2024-02-29 23:59:5x", false)]
    public void IsCanonicalDbDateTimeText_形と暦の妥当性を厳密判定する(string value, bool expected)
    {
        Assert.That(SQLite.IsCanonicalDbDateTimeText(value), Is.EqualTo(expected));
    }

    [Test]
    public void ReadDbDateTimeTextOrEmpty_現在カルチャに依存せず正規文字列を同じ参照で返す()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            string source = new(['2', '0', '2', '4', '-', '0', '2', '-', '2', '9', ' ', '0', '1', ':', '0', '2', ':', '0', '3']);

            string actual = SQLite.ReadDbDateTimeTextOrEmpty(source);

            Assert.That(actual, Is.SameAs(source));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Test]
    public void ReadDbDateTimeTextOrEmpty_正規形以外は従来経路で処理する()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SQLite.ReadDbDateTimeTextOrEmpty(new DateTime(2024, 2, 29, 1, 2, 3)), Is.EqualTo("2024-02-29 01:02:03"));
            Assert.That(SQLite.ReadDbDateTimeTextOrEmpty("2024/02/29 1:02:03"), Is.EqualTo("2024-02-29 01:02:03"));
            Assert.That(SQLite.ReadDbDateTimeTextOrEmpty(""), Is.Empty);
            Assert.That(SQLite.ReadDbDateTimeTextOrEmpty(DBNull.Value), Is.Empty);
            Assert.That(SQLite.ReadDbDateTimeTextOrEmpty("2023-02-29 01:02:03"), Is.EqualTo("2023-02-29 01:02:03"));
        });
    }
}

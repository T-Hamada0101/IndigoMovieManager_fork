using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class DBInfoTests
{
    [Test]
    public void DBFullPath_設定値を読み戻せてPropertyChangedを通知する()
    {
        DBInfo info = new();
        string propertyName = "";
        info.PropertyChanged += (_, e) => propertyName = e.PropertyName ?? "";

        info.DBFullPath = @"E:\Movies\main.wb";

        Assert.Multiple(() =>
        {
            Assert.That(info.DBFullPath, Is.EqualTo(@"E:\Movies\main.wb"));
            Assert.That(propertyName, Is.EqualTo(nameof(DBInfo.DBFullPath)));
        });
    }
}

using IndigoMovieManager.UpperTabs.Common;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class UpperTabVisibleRangeTests
{
    [Test]
    public void overscan付きで前後範囲を計算する()
    {
        UpperTabVisibleRange actual = UpperTabVisibleRange.Create(
            firstVisibleIndex: 10,
            lastVisibleIndex: 20,
            totalCount: 100,
            overscanItemCount: 5
        );

        Assert.That(actual.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(actual.LastVisibleIndex, Is.EqualTo(20));
        Assert.That(actual.FirstNearVisibleIndex, Is.EqualTo(5));
        Assert.That(actual.LastNearVisibleIndex, Is.EqualTo(25));
    }

    [Test]
    public void 先頭末尾ではoverscanを範囲内へ丸める()
    {
        UpperTabVisibleRange actual = UpperTabVisibleRange.Create(
            firstVisibleIndex: 1,
            lastVisibleIndex: 3,
            totalCount: 4,
            overscanItemCount: 10
        );

        Assert.That(actual.FirstNearVisibleIndex, Is.EqualTo(0));
        Assert.That(actual.LastNearVisibleIndex, Is.EqualTo(3));
    }

    [Test]
    public void 件数ゼロや不正範囲ではEmptyを返す()
    {
        Assert.That(
            UpperTabVisibleRange.Create(-1, -1, totalCount: 0, overscanItemCount: 5),
            Is.EqualTo(UpperTabVisibleRange.Empty)
        );
        Assert.That(
            UpperTabVisibleRange.Create(5, 3, totalCount: 10, overscanItemCount: 2),
            Is.EqualTo(UpperTabVisibleRange.Empty)
        );
    }

    [Test]
    public void 標準縦リストはitem単位offsetからvisible範囲を求める()
    {
        UpperTabVisibleRange actual = UpperTabViewportTracker.CalculateVerticalItemVisibleRange(
            verticalOffset: 10.25,
            viewportHeight: 5.5,
            totalCount: 100,
            overscanItemCount: 2
        );

        Assert.That(actual.FirstVisibleIndex, Is.EqualTo(10));
        Assert.That(actual.LastVisibleIndex, Is.EqualTo(15));
        Assert.That(actual.FirstNearVisibleIndex, Is.EqualTo(8));
        Assert.That(actual.LastNearVisibleIndex, Is.EqualTo(17));
    }
}

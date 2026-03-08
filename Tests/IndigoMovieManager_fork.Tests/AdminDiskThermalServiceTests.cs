using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AdminDiskThermalServiceTests
{
    [Test]
    public void TryParseTemperatureCelsius_SMART194を読める()
    {
        byte[] vendorSpecific = CreateVendorSpecific(attributeId: 194, rawTemperature: 41);

        bool actual = AdminDiskThermalService.TryParseTemperatureCelsius(
            vendorSpecific,
            out int temperatureCelsius
        );

        Assert.That(actual, Is.True);
        Assert.That(temperatureCelsius, Is.EqualTo(41));
    }

    [Test]
    public void TryParseTemperatureCelsius_SMART190へフォールバックできる()
    {
        byte[] vendorSpecific = CreateVendorSpecific(attributeId: 190, rawTemperature: 52);

        bool actual = AdminDiskThermalService.TryParseTemperatureCelsius(
            vendorSpecific,
            out int temperatureCelsius
        );

        Assert.That(actual, Is.True);
        Assert.That(temperatureCelsius, Is.EqualTo(52));
    }

    [Test]
    public void TryParseTemperatureCelsius_妥当な温度が無ければ失敗する()
    {
        byte[] vendorSpecific = CreateVendorSpecific(attributeId: 194, rawTemperature: 0);

        bool actual = AdminDiskThermalService.TryParseTemperatureCelsius(
            vendorSpecific,
            out int temperatureCelsius
        );

        Assert.That(actual, Is.False);
        Assert.That(temperatureCelsius, Is.EqualTo(0));
    }

    [Test]
    public void DetermineThermalState_閾値でNormalWarningCriticalへ分かれる()
    {
        Assert.That(
            AdminDiskThermalService.DetermineThermalState(41, 55, 65),
            Is.EqualTo(DiskThermalState.Normal)
        );
        Assert.That(
            AdminDiskThermalService.DetermineThermalState(57, 55, 65),
            Is.EqualTo(DiskThermalState.Warning)
        );
        Assert.That(
            AdminDiskThermalService.DetermineThermalState(66, 55, 65),
            Is.EqualTo(DiskThermalState.Critical)
        );
    }

    [Test]
    public void MatchesSmartInstanceName_PnpDeviceId前方一致を受け入れる()
    {
        AdminDiskThermalService.PhysicalDiskIdentity disk = new(
            0,
            @"\\.\PHYSICALDRIVE0",
            @"IDE\DiskST1000DM010-2EP102________________CC43____\5&123456&0&0.0.0"
        );

        bool actual = AdminDiskThermalService.MatchesSmartInstanceName(
            @"IDE\DiskST1000DM010-2EP102________________CC43____\5&123456&0&0.0.0_0",
            disk
        );

        Assert.That(actual, Is.True);
    }

    [Test]
    public void MatchesSmartInstanceName_末尾ゼロを勝手に削らない()
    {
        AdminDiskThermalService.PhysicalDiskIdentity disk = new(
            0,
            @"\\.\PHYSICALDRIVE0",
            @"IDE\DiskABC10"
        );

        bool actual = AdminDiskThermalService.MatchesSmartInstanceName(
            @"IDE\DiskABC1_0",
            disk
        );

        Assert.That(actual, Is.False);
    }

    [Test]
    public void CreateSnapshot_温度と閾値からDTOを組み立てる()
    {
        DiskThermalSnapshotDto actual = AdminDiskThermalService.CreateSnapshot(@"C:\", 58, 55, 65);

        Assert.That(actual.DiskId, Is.EqualTo(@"C:\"));
        Assert.That(actual.TemperatureCelsius, Is.EqualTo(58));
        Assert.That(actual.WarningThresholdCelsius, Is.EqualTo(55));
        Assert.That(actual.CriticalThresholdCelsius, Is.EqualTo(65));
        Assert.That(actual.ThermalState, Is.EqualTo(DiskThermalState.Warning));
    }

    [Test]
    public void ParseDiskRequestIdentity_PhysicalDrive指定を保持する()
    {
        AdminDiskThermalService.DiskRequestIdentity actual =
            AdminDiskThermalService.ParseDiskRequestIdentity(@"\\.\PHYSICALDRIVE3");

        Assert.That(actual.NormalizedDiskId, Is.EqualTo(@"\\.\PHYSICALDRIVE3"));
        Assert.That(actual.LogicalRootPath, Is.EqualTo(""));
        Assert.That(actual.PhysicalDriveIndex, Is.EqualTo(3));
    }

    [Test]
    public void ParseDiskRequestIdentity_通常パスはドライブルートへ寄せる()
    {
        AdminDiskThermalService.DiskRequestIdentity actual =
            AdminDiskThermalService.ParseDiskRequestIdentity(@"C:\data\main.db");

        Assert.That(actual.NormalizedDiskId, Is.EqualTo(@"C:\"));
        Assert.That(actual.LogicalRootPath, Is.EqualTo(@"C:\"));
        Assert.That(actual.PhysicalDriveIndex, Is.EqualTo(-1));
    }

    private static byte[] CreateVendorSpecific(int attributeId, int rawTemperature)
    {
        byte[] data = new byte[512];
        data[0] = 1;
        data[1] = 0;

        int offset = 2;
        data[offset] = (byte)attributeId;
        data[offset + 1] = 0;
        data[offset + 2] = 0;
        data[offset + 3] = (byte)rawTemperature;
        data[offset + 4] = 0;
        data[offset + 5] = (byte)rawTemperature;
        return data;
    }
}

using System.Management;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager
{
    // HDD温度は機種差が大きいので、service 側で probe chain を閉じ込める。
    internal static class AdminDiskThermalService
    {
        private const string Cimv2Namespace = @"root\cimv2";
        private const string WmiNamespace = @"root\wmi";
        private static readonly string[] SmartDataClassNames =
        [
            "MSStorageDriver_ATAPISmartData",
            "MSStorageDriver_FailurePredictData",
        ];
        private static readonly int[] TemperatureAttributeIds = [194, 190, 231];

        public static DiskThermalSnapshotDto BuildSnapshot(
            string diskId,
            int warningTemperatureCelsius,
            int criticalTemperatureCelsius
        )
        {
            DiskRequestIdentity request = ParseDiskRequestIdentity(diskId);
            if (string.IsNullOrWhiteSpace(request.NormalizedDiskId))
            {
                return CreateUnavailableSnapshot(
                    request.NormalizedDiskId,
                    warningTemperatureCelsius,
                    criticalTemperatureCelsius
                );
            }

            try
            {
                if (!TryResolvePhysicalDiskIdentity(request, out PhysicalDiskIdentity disk))
                {
                    return CreateUnavailableSnapshot(
                        request.NormalizedDiskId,
                        warningTemperatureCelsius,
                        criticalTemperatureCelsius
                    );
                }

                foreach (string className in SmartDataClassNames)
                {
                    if (TryReadSmartTemperatureCelsius(disk, className, out int temperatureCelsius))
                    {
                        return CreateSnapshot(
                            request.NormalizedDiskId,
                            temperatureCelsius,
                            warningTemperatureCelsius,
                            criticalTemperatureCelsius
                        );
                    }
                }
            }
            catch
            {
                // 温度取得不能は正常系として扱い、ここでは unavailable へ畳む。
            }

            return CreateUnavailableSnapshot(
                request.NormalizedDiskId,
                warningTemperatureCelsius,
                criticalTemperatureCelsius
            );
        }

        public static bool IsSupportedEnvironment()
        {
            try
            {
                foreach (string className in SmartDataClassNames)
                {
                    using ManagementObjectSearcher searcher = new(
                        WmiNamespace,
                        $"SELECT InstanceName FROM {className}"
                    );
                    foreach (ManagementBaseObject _ in searcher.Get())
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        internal static DiskThermalSnapshotDto CreateSnapshot(
            string normalizedDiskId,
            int temperatureCelsius,
            int warningTemperatureCelsius,
            int criticalTemperatureCelsius
        )
        {
            return new DiskThermalSnapshotDto
            {
                DiskId = normalizedDiskId ?? "",
                TemperatureCelsius = temperatureCelsius,
                WarningThresholdCelsius = warningTemperatureCelsius,
                CriticalThresholdCelsius = criticalTemperatureCelsius,
                ThermalState = DetermineThermalState(
                    temperatureCelsius,
                    warningTemperatureCelsius,
                    criticalTemperatureCelsius
                ),
                CapturedAtUtc = DateTime.UtcNow,
            };
        }

        internal static DiskThermalState DetermineThermalState(
            int temperatureCelsius,
            int warningTemperatureCelsius,
            int criticalTemperatureCelsius
        )
        {
            if (temperatureCelsius <= 0)
            {
                return DiskThermalState.Unavailable;
            }

            if (criticalTemperatureCelsius > 0 && temperatureCelsius >= criticalTemperatureCelsius)
            {
                return DiskThermalState.Critical;
            }

            if (warningTemperatureCelsius > 0 && temperatureCelsius >= warningTemperatureCelsius)
            {
                return DiskThermalState.Warning;
            }

            return DiskThermalState.Normal;
        }

        internal static bool TryParseTemperatureCelsius(byte[] vendorSpecific, out int temperatureCelsius)
        {
            temperatureCelsius = 0;
            if (vendorSpecific == null || vendorSpecific.Length < 14)
            {
                return false;
            }

            for (int offset = 2; offset + 12 <= vendorSpecific.Length; offset += 12)
            {
                int attributeId = vendorSpecific[offset];
                if (attributeId == 0 || !TemperatureAttributeIds.Contains(attributeId))
                {
                    continue;
                }

                int rawTemperature = vendorSpecific[offset + 5];
                if (IsReasonableTemperature(rawTemperature))
                {
                    temperatureCelsius = rawTemperature;
                    return true;
                }

                int currentValue = vendorSpecific[offset + 3];
                if (IsReasonableTemperature(currentValue))
                {
                    temperatureCelsius = currentValue;
                    return true;
                }
            }

            return false;
        }

        internal static bool MatchesSmartInstanceName(string instanceName, PhysicalDiskIdentity disk)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return false;
            }

            string normalizedInstanceName = NormalizeMatchToken(instanceName);
            if (
                !string.IsNullOrWhiteSpace(disk.PnpDeviceId)
                && normalizedInstanceName.StartsWith(
                    NormalizeMatchToken(disk.PnpDeviceId),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(disk.DeviceId)
                && normalizedInstanceName.Contains(
                    NormalizeMatchToken(disk.DeviceId),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            return disk.Index >= 0
                && normalizedInstanceName.Contains(
                    $"PHYSICALDRIVE{disk.Index}",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool TryReadSmartTemperatureCelsius(
            PhysicalDiskIdentity disk,
            string className,
            out int temperatureCelsius
        )
        {
            temperatureCelsius = 0;
            using ManagementObjectSearcher searcher = new(
                WmiNamespace,
                $"SELECT InstanceName, VendorSpecific FROM {className}"
            );

            foreach (ManagementBaseObject item in searcher.Get())
            {
                string instanceName = item["InstanceName"]?.ToString() ?? "";
                if (!MatchesSmartInstanceName(instanceName, disk))
                {
                    continue;
                }

                if (item["VendorSpecific"] is byte[] vendorSpecific
                    && TryParseTemperatureCelsius(vendorSpecific, out temperatureCelsius))
                {
                    return true;
                }
            }

            return false;
        }

        internal static DiskRequestIdentity ParseDiskRequestIdentity(string diskId)
        {
            if (TryParsePhysicalDriveIndex(diskId, out int physicalDriveIndex))
            {
                return new DiskRequestIdentity(
                    $@"\\.\PHYSICALDRIVE{physicalDriveIndex}",
                    "",
                    physicalDriveIndex
                );
            }

            string logicalRootPath = NormalizeRootPath(diskId);
            if (string.IsNullOrWhiteSpace(logicalRootPath))
            {
                return default;
            }

            return new DiskRequestIdentity(logicalRootPath, logicalRootPath, -1);
        }

        private static bool TryResolvePhysicalDiskIdentity(
            DiskRequestIdentity request,
            out PhysicalDiskIdentity disk
        )
        {
            if (request.PhysicalDriveIndex >= 0)
            {
                return TryResolvePhysicalDiskIdentityByIndex(request.PhysicalDriveIndex, out disk);
            }

            return TryResolvePhysicalDiskIdentityFromLogicalRoot(request.LogicalRootPath, out disk);
        }

        private static bool TryResolvePhysicalDiskIdentityFromLogicalRoot(
            string logicalRootPath,
            out PhysicalDiskIdentity disk
        )
        {
            disk = default;
            string logicalDiskId = logicalRootPath.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(logicalDiskId))
            {
                return false;
            }

            string logicalDiskPath = BuildWmiObjectPath("Win32_LogicalDisk", logicalDiskId);
            using ManagementObjectSearcher partitionSearcher = new(
                Cimv2Namespace,
                $"ASSOCIATORS OF {{{logicalDiskPath}}} WHERE AssocClass = Win32_LogicalDiskToPartition"
            );

            foreach (ManagementBaseObject partition in partitionSearcher.Get())
            {
                string partitionDeviceId = partition["DeviceID"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(partitionDeviceId))
                {
                    continue;
                }

                string partitionPath = BuildWmiObjectPath("Win32_DiskPartition", partitionDeviceId);
                using ManagementObjectSearcher diskSearcher = new(
                    Cimv2Namespace,
                    $"ASSOCIATORS OF {{{partitionPath}}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"
                );

                foreach (ManagementBaseObject item in diskSearcher.Get())
                {
                    disk = new PhysicalDiskIdentity(
                        Index: TryReadInt32(item["Index"]),
                        DeviceId: item["DeviceID"]?.ToString() ?? "",
                        PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? ""
                    );
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePhysicalDiskIdentityByIndex(
            int physicalDriveIndex,
            out PhysicalDiskIdentity disk
        )
        {
            disk = default;
            if (physicalDriveIndex < 0)
            {
                return false;
            }

            using ManagementObjectSearcher searcher = new(
                Cimv2Namespace,
                $"SELECT Index, DeviceID, PNPDeviceID FROM Win32_DiskDrive WHERE Index = {physicalDriveIndex}"
            );

            foreach (ManagementBaseObject item in searcher.Get())
            {
                disk = new PhysicalDiskIdentity(
                    Index: TryReadInt32(item["Index"]),
                    DeviceId: item["DeviceID"]?.ToString() ?? "",
                    PnpDeviceId: item["PNPDeviceID"]?.ToString() ?? ""
                );
                return true;
            }

            return false;
        }

        private static string BuildWmiObjectPath(string className, string value)
        {
            string escapedValue = (value ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
            return $"{className}.DeviceID='{escapedValue}'";
        }

        private static int TryReadInt32(object value)
        {
            if (value == null)
            {
                return -1;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static DiskThermalSnapshotDto CreateUnavailableSnapshot(
            string normalizedDiskId,
            int warningTemperatureCelsius,
            int criticalTemperatureCelsius
        )
        {
            return new DiskThermalSnapshotDto
            {
                DiskId = normalizedDiskId ?? "",
                TemperatureCelsius = 0,
                WarningThresholdCelsius = warningTemperatureCelsius,
                CriticalThresholdCelsius = criticalTemperatureCelsius,
                ThermalState = DiskThermalState.Unavailable,
                CapturedAtUtc = DateTime.UtcNow,
            };
        }

        private static string NormalizeMatchToken(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private static bool IsReasonableTemperature(int temperatureCelsius)
        {
            return temperatureCelsius > 0 && temperatureCelsius <= 150;
        }

        private static string NormalizeRootPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            try
            {
                return Path.GetPathRoot(Path.GetFullPath(value.Trim())) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool TryParsePhysicalDriveIndex(string value, out int physicalDriveIndex)
        {
            physicalDriveIndex = -1;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            if (normalized.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[4..];
            }

            const string prefix = "PHYSICALDRIVE";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = normalized[prefix.Length..];
            return int.TryParse(suffix, out physicalDriveIndex) && physicalDriveIndex >= 0;
        }

        internal readonly record struct DiskRequestIdentity(
            string NormalizedDiskId,
            string LogicalRootPath,
            int PhysicalDriveIndex
        );

        internal readonly record struct PhysicalDiskIdentity(int Index, string DeviceId, string PnpDeviceId);
    }
}

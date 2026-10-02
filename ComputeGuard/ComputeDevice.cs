using System.Globalization;
using Vortice.DXGI;

namespace ComputeGuard;

internal readonly record struct ComputeDevice(
    long Luid,
    int VendorId,
    int DeviceId,
    int SubsystemId,
    int Revision,
    long DriverVersion,
    bool IsSoftware,
    long MemoryCapacity,
    string Description)
{
    private const int VersionFieldBits = 16;
    private const long VersionFieldMask = 0xFFFF;

    public string Identity => string.Create(
        CultureInfo.InvariantCulture,
        $"{VendorId:X4}:{DeviceId:X4}:{SubsystemId:X8}:{Revision:X2}");

    public string DriverVersionText => string.Create(
        CultureInfo.InvariantCulture,
        $"{(DriverVersion >> (VersionFieldBits * 3)) & VersionFieldMask}.{(DriverVersion >> (VersionFieldBits * 2)) & VersionFieldMask}.{(DriverVersion >> VersionFieldBits) & VersionFieldMask}.{DriverVersion & VersionFieldMask}");

    public static ComputeDevice FromAdapter(IDXGIAdapter adapter, bool isUma)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var description = adapter.Description;
        var isSoftware = false;
        using (var extended = adapter.QueryInterfaceOrNull<IDXGIAdapter1>())
        {
            if (extended is not null)
            {
                isSoftware = (extended.Description1.Flags & AdapterFlags.Software) != 0;
            }
        }

        var driverVersion = adapter.CheckInterfaceSupport<IDXGIDevice>(out var version) ? version : 0L;
        long capacity = isUma ? description.SharedSystemMemory : description.DedicatedVideoMemory;

        return new ComputeDevice(
            description.Luid,
            description.VendorId,
            description.DeviceId,
            description.SubsystemId,
            description.Revision,
            driverVersion,
            isSoftware,
            capacity,
            description.Description);
    }
}

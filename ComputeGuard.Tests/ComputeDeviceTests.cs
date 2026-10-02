using Vortice.DXGI;

namespace ComputeGuard.Tests;

public sealed class ComputeDeviceTests
{
    const int SoftwareRendererVendor = 0x1414;

    static ComputeDevice DeviceWithDriver(long driverVersion) =>
        new(1L, 0x8086, 0xA7A0, 0x00BB1E26, 4, driverVersion, false, 1L << 30, "Test Adapter");

    static List<IDXGIAdapter1> Adapters()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapters = new List<IDXGIAdapter1>();
        for (var index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            adapters.Add(adapter);
        return adapters;
    }

    static void Release(List<IDXGIAdapter1> adapters)
    {
        foreach (var adapter in adapters)
            adapter.Dispose();
    }

    [Fact]
    public void TheDriverVersionIsShownAsFourFields()
    {
        var device = DeviceWithDriver((32L << 48) | (101L << 16) | 7085L);

        var text = device.DriverVersionText;

        Assert.Equal("32.0.101.7085", text);
    }

    [Fact]
    public void EveryDriverVersionFieldHasSixteenBits()
    {
        var device = DeviceWithDriver(unchecked((long)0xFFFF_0001_0002_FFFFUL));

        var text = device.DriverVersionText;

        Assert.Equal("65535.1.2.65535", text);
    }

    [Fact]
    public void TheIdentityShowsTheVendorDeviceSubsystemAndRevision()
    {
        var device = DeviceWithDriver(0L);

        var identity = device.Identity;

        Assert.Equal("8086:A7A0:00BB1E26:04", identity);
    }

    [Fact]
    public void EveryAdapterMatchesItsDescription()
    {
        var adapters = Adapters();
        try
        {
            foreach (var adapter in adapters)
            {
                var description = adapter.Description;

                var device = ComputeDevice.FromAdapter(adapter, false);

                Assert.Equal((long)description.Luid, device.Luid);
                Assert.Equal(description.VendorId, device.VendorId);
                Assert.Equal(description.DeviceId, device.DeviceId);
                Assert.Equal(description.SubsystemId, device.SubsystemId);
                Assert.Equal(description.Revision, device.Revision);
                Assert.Equal(description.Description, device.Description);
            }
        }
        finally
        {
            Release(adapters);
        }
    }

    [Fact]
    public void EveryAdapterIsReadTheSameWayTwice()
    {
        var adapters = Adapters();
        try
        {
            foreach (var adapter in adapters)
            {
                var first = ComputeDevice.FromAdapter(adapter, false);

                var second = ComputeDevice.FromAdapter(adapter, false);

                Assert.Equal(first, second);
            }
        }
        finally
        {
            Release(adapters);
        }
    }

    [Fact]
    public void EveryAdapterReportsItsDriverVersion()
    {
        var adapters = Adapters();
        try
        {
            foreach (var adapter in adapters)
            {
                var device = ComputeDevice.FromAdapter(adapter, false);

                Assert.NotEqual(0L, device.DriverVersion);
            }
        }
        finally
        {
            Release(adapters);
        }
    }

    [Fact]
    public void TheCapacityIsTheSharedMemoryOnlyForUnifiedMemory()
    {
        var adapters = Adapters();
        try
        {
            foreach (var adapter in adapters)
            {
                var description = adapter.Description;

                var unified = ComputeDevice.FromAdapter(adapter, true);
                var discrete = ComputeDevice.FromAdapter(adapter, false);

                Assert.Equal((long)description.SharedSystemMemory, unified.MemoryCapacity);
                Assert.Equal((long)description.DedicatedVideoMemory, discrete.MemoryCapacity);
            }
        }
        finally
        {
            Release(adapters);
        }
    }

    [Fact]
    public void TheSoftwareRendererIsRecognised()
    {
        var adapters = Adapters();
        try
        {
            var software = adapters.Where(adapter => (adapter.Description1.Flags & AdapterFlags.Software) != 0).ToList();
            var hardware = adapters.Except(software).ToList();

            var softwareDevices = software.Select(adapter => ComputeDevice.FromAdapter(adapter, false)).ToList();
            var hardwareDevices = hardware.Select(adapter => ComputeDevice.FromAdapter(adapter, false)).ToList();

            Assert.NotEmpty(softwareDevices);
            Assert.All(softwareDevices, device => Assert.True(device.IsSoftware));
            Assert.Contains(softwareDevices, device => device.VendorId == SoftwareRendererVendor);
            Assert.All(hardwareDevices, device => Assert.False(device.IsSoftware));
        }
        finally
        {
            Release(adapters);
        }
    }

    [Fact]
    public void AnAdapterIsRequired()
    {
        Action read = () => ComputeDevice.FromAdapter(null!, false);

        Assert.Throws<ArgumentNullException>(read);
    }
}

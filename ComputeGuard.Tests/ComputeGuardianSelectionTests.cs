namespace ComputeGuard.Tests;

public sealed class ComputeGuardianSelectionTests
{
    static readonly ComputeWorkload Workload = new(1920, 1080, 1000L, 7UL);

    static readonly ComputeCheck PassingCheck = ComputeCheck.Count("determinism", 0L);

    static readonly ComputeCheck FailingCheck = ComputeCheck.Count("determinism", 3L);

    static readonly Func<ComputeDevice, IReadOnlyList<ComputeCheck>> PassingSelfTest = _ => [PassingCheck];

    static ComputeDevice Device(long luid = 1L, bool isSoftware = false, long capacity = 1_000_000L) =>
        new(luid, 0x8086, 0xA7A0, 0x00BB1E26, 4, (32L << 48) | (101L << 16) | 7085L, isSoftware, capacity, "Test Adapter");

    static (ComputeGuardian Guardian, List<ComputeAnomalyException> Reports) CreateGuardian()
    {
        var reports = new List<ComputeAnomalyException>();
        var guardian = new ComputeGuardian(new ComputeGuardianOptions
        {
            Report = anomaly =>
            {
                lock (reports)
                    reports.Add(anomaly);
            },
        });
        return (guardian, reports);
    }

    [Fact]
    public void WithoutADeviceTheCpuIsChosen()
    {
        var (guardian, reports) = CreateGuardian();
        var runs = 0;

        var decision = guardian.Select(null, Workload, _ =>
        {
            runs++;
            return [PassingCheck];
        });

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.NoDevice), decision);
        Assert.Equal(0, runs);
        Assert.Empty(reports);
    }

    [Fact]
    public void ADeviceThatPassesItsSelfTestUsesTheGpu()
    {
        var (guardian, reports) = CreateGuardian();

        var decision = guardian.Select(Device(), Workload, PassingSelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted), decision);
        Assert.Equal(ComputeHealth.Trusted, guardian.HealthOf(1L));
        Assert.Empty(reports);
    }

    [Fact]
    public void TheSelfTestRunsOnlyOnce()
    {
        var (guardian, _) = CreateGuardian();
        var runs = 0;
        IReadOnlyList<ComputeCheck> SelfTest(ComputeDevice device)
        {
            runs++;
            return [PassingCheck];
        }

        guardian.Select(Device(), Workload, SelfTest);
        guardian.Select(Device(), Workload, SelfTest);
        var decision = guardian.Select(Device(), Workload, SelfTest);

        Assert.Equal(ComputeRoute.Gpu, decision.Route);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void TheSelfTestReceivesTheSelectedDevice()
    {
        var (guardian, _) = CreateGuardian();
        var device = Device(42L);
        ComputeDevice? received = null;

        guardian.Select(device, Workload, candidate =>
        {
            received = candidate;
            return [PassingCheck];
        });

        Assert.Equal(device, received);
    }

    [Fact]
    public void ASoftwareAdapterGoesToTheCpuWithoutASelfTest()
    {
        var (guardian, reports) = CreateGuardian();
        var runs = 0;

        var decision = guardian.Select(Device(isSoftware: true), Workload, _ =>
        {
            runs++;
            return [PassingCheck];
        });

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.SoftwareAdapter), decision);
        Assert.Equal(0, runs);
        Assert.Equal(ComputeHealth.Untested, guardian.HealthOf(1L));
        Assert.Empty(reports);
    }

    [Fact]
    public void AWorkloadAboveTheMemoryShareGoesToTheCpu()
    {
        var (guardian, _) = CreateGuardian();
        var runs = 0;
        var workload = Workload with { GpuMemoryBytes = 1001L };

        var decision = guardian.Select(Device(capacity: 4000L), workload, _ =>
        {
            runs++;
            return [PassingCheck];
        });

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.MemoryLimit), decision);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void AWorkloadAtTheMemoryShareMayUseTheGpu()
    {
        var (guardian, _) = CreateGuardian();
        var workload = Workload with { GpuMemoryBytes = 1000L };

        var decision = guardian.Select(Device(capacity: 4000L), workload, PassingSelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted), decision);
    }

    [Fact]
    public void AFailingSelfTestPinsTheDeviceAndReportsTheCheck()
    {
        var (guardian, reports) = CreateGuardian();

        var decision = guardian.Select(Device(), Workload, _ => [PassingCheck, FailingCheck]);

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned), decision);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        var report = Assert.Single(reports);
        Assert.Equal(ComputeFailure.SelfTestFailed, report.Anomaly.Failure);
        Assert.Equal(FailingCheck, report.Anomaly.Check);
    }

    [Fact]
    public void APinnedDeviceIsNotTestedAgain()
    {
        var (guardian, reports) = CreateGuardian();
        var runs = 0;
        IReadOnlyList<ComputeCheck> SelfTest(ComputeDevice device)
        {
            runs++;
            return [FailingCheck];
        }
        guardian.Select(Device(), Workload, SelfTest);

        var decision = guardian.Select(Device(), Workload, SelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned), decision);
        Assert.Equal(1, runs);
        Assert.Single(reports);
    }

    [Fact]
    public void ASelfTestThatThrowsPinsTheDeviceAndKeepsTheException()
    {
        var (guardian, reports) = CreateGuardian();
        var cause = new InvalidOperationException("shader failed");

        var decision = guardian.Select(Device(), Workload, _ => throw cause);

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned), decision);
        var report = Assert.Single(reports);
        Assert.Equal(ComputeFailure.SelfTestFailed, report.Anomaly.Failure);
        Assert.Null(report.Anomaly.Check);
        Assert.Same(cause, report.InnerException);
    }

    [Fact]
    public void ConcurrentSelectionsRunTheSelfTestOnce()
    {
        var (guardian, _) = CreateGuardian();
        var runs = 0;
        var decisions = new ComputeDecision[32];

        Parallel.For(0, decisions.Length, index =>
        {
            decisions[index] = guardian.Select(Device(), Workload, _ =>
            {
                Interlocked.Increment(ref runs);
                Thread.Sleep(50);
                return [PassingCheck];
            });
        });

        Assert.Equal(1, runs);
        Assert.All(decisions, decision => Assert.Equal(new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted), decision));
    }

    [Fact]
    public void ATrustedSelectionAllocatesNothing()
    {
        var (guardian, _) = CreateGuardian();
        var device = Device();
        for (var warmUp = 0; warmUp < 1000; warmUp++)
            guardian.Select(device, Workload, PassingSelfTest);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var run = 0; run < 1000; run++)
            guardian.Select(device, Workload, PassingSelfTest);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
    }
}

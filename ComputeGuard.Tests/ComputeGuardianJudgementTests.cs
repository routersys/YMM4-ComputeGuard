namespace ComputeGuard.Tests;

public sealed class ComputeGuardianJudgementTests
{
    static readonly ComputeWorkload Workload = new(1920, 1080, 1000L);

    static readonly ComputeCheck PassingCheck = new("sum.R", 10.0, 10.0, new ComputeTolerance(8d, 10d, 0));

    static readonly ComputeCheck FirstFailingCheck = new("spot[0].R", 0.5, 0.25, new ComputeTolerance(8d, 1d, 0));

    static readonly ComputeCheck SecondFailingCheck = new("spot[1].G", 0.75, 0.25, new ComputeTolerance(8d, 1d, 0));

    static readonly Func<ComputeDevice, IReadOnlyList<ComputeCheck>> PassingSelfTest = _ => [ComputeCheck.Count("determinism", 0L)];

    static ComputeDevice Device(long luid = 1L) =>
        new(luid, 0x8086, 0xA7A0, 0x00BB1E26, 4, (32L << 48) | (101L << 16) | 7085L, false, 1_000_000L, "Test Adapter");

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

    static (ComputeGuardian Guardian, List<ComputeAnomalyException> Reports) CreateTrustedGuardian()
    {
        var created = CreateGuardian();
        created.Guardian.Select(Device(), Workload, PassingSelfTest);
        return created;
    }

    [Fact]
    public void PassingChecksKeepTheGpu()
    {
        var (guardian, reports) = CreateTrustedGuardian();

        var accepted = guardian.Judge(Device(), Workload, [PassingCheck, PassingCheck]);

        Assert.True(accepted);
        Assert.Equal(ComputeHealth.Trusted, guardian.HealthOf(1L));
        Assert.Empty(reports);
    }

    [Fact]
    public void AFailingCheckPinsTheDeviceAndReportsTheFirstFailure()
    {
        var (guardian, reports) = CreateTrustedGuardian();

        var accepted = guardian.Judge(Device(), Workload, [PassingCheck, FirstFailingCheck, SecondFailingCheck]);

        Assert.False(accepted);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        var report = Assert.Single(reports);
        Assert.Equal(ComputeFailure.CheckFailed, report.Anomaly.Failure);
        Assert.Equal(FirstFailingCheck, report.Anomaly.Check);
        Assert.Equal(Device(), report.Anomaly.Device);
        Assert.Equal(Workload, report.Anomaly.Workload);
    }

    [Fact]
    public void AfterAFailureTheCpuIsChosen()
    {
        var (guardian, _) = CreateTrustedGuardian();
        guardian.Judge(Device(), Workload, [FirstFailingCheck]);

        var decision = guardian.Select(Device(), Workload, PassingSelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned), decision);
    }

    [Fact]
    public void PassingChecksDoNotUnpinADevice()
    {
        var (guardian, _) = CreateTrustedGuardian();
        guardian.Judge(Device(), Workload, [FirstFailingCheck]);

        var accepted = guardian.Judge(Device(), Workload, [PassingCheck]);

        Assert.True(accepted);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
    }

    [Fact]
    public void OnlyTheFirstOfManyConcurrentFailuresIsReported()
    {
        var (guardian, reports) = CreateTrustedGuardian();

        Parallel.For(0, 64, _ => guardian.Judge(Device(), Workload, [FirstFailingCheck]));

        Assert.Single(reports);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
    }

    [Fact]
    public void ExhaustedResourcesPinTheDeviceWithoutAReport()
    {
        var (guardian, reports) = CreateTrustedGuardian();

        guardian.Fail(Device(), Workload, ComputeFailure.ResourceExhausted, new OutOfMemoryException());

        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        Assert.Empty(reports);
    }

    [Theory]
    [InlineData((int)ComputeFailure.DeviceLost)]
    [InlineData((int)ComputeFailure.UnexpectedException)]
    public void AFailureIsReportedWithItsCause(int failureValue)
    {
        var failure = (ComputeFailure)failureValue;
        var (guardian, reports) = CreateTrustedGuardian();
        var cause = new InvalidOperationException("device removed");

        guardian.Fail(Device(), Workload, failure, cause);

        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        var report = Assert.Single(reports);
        Assert.Equal(failure, report.Anomaly.Failure);
        Assert.Null(report.Anomaly.Check);
        Assert.Same(cause, report.InnerException);
    }

    [Fact]
    public void AFailureBeforeAnySelectionStillPinsTheDevice()
    {
        var (guardian, reports) = CreateGuardian();

        guardian.Fail(Device(), Workload, ComputeFailure.DeviceLost, null);

        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        Assert.Single(reports);
    }

    [Fact]
    public void PinningOneAdapterLeavesAnotherUntouched()
    {
        var (guardian, _) = CreateTrustedGuardian();
        guardian.Fail(Device(1L), Workload, ComputeFailure.DeviceLost, null);

        var decision = guardian.Select(Device(2L), Workload, PassingSelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted), decision);
        Assert.Equal(ComputeHealth.Pinned, guardian.HealthOf(1L));
        Assert.Equal(ComputeHealth.Trusted, guardian.HealthOf(2L));
    }

    [Fact]
    public void SeparateGuardiansKeepSeparateStates()
    {
        var (first, _) = CreateTrustedGuardian();
        var (second, _) = CreateGuardian();
        first.Fail(Device(), Workload, ComputeFailure.DeviceLost, null);

        var decision = second.Select(Device(), Workload, PassingSelfTest);

        Assert.Equal(new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted), decision);
    }

    [Fact]
    public void TheReportCarriesTheStackOfTheCaller()
    {
        var (guardian, reports) = CreateTrustedGuardian();

        guardian.Judge(Device(), Workload, [FirstFailingCheck]);

        var report = Assert.Single(reports);
        Assert.NotNull(report.StackTrace);
        Assert.Contains(nameof(ComputeGuardian.Judge), report.StackTrace);
        Assert.Contains(nameof(TheReportCarriesTheStackOfTheCaller), report.StackTrace);
    }

    [Fact]
    public void APassingJudgementAllocatesNothing()
    {
        var (guardian, _) = CreateTrustedGuardian();
        var device = Device();
        ReadOnlySpan<ComputeCheck> checks = [PassingCheck, PassingCheck, PassingCheck];
        for (var warmUp = 0; warmUp < 1000; warmUp++)
            guardian.Judge(device, Workload, checks);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var run = 0; run < 1000; run++)
            guardian.Judge(device, Workload, checks);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void AnOptionWithoutAReportIsRejected()
    {
        Action create = () => _ = new ComputeGuardian(new ComputeGuardianOptions { Report = null! });

        Assert.Throws<ArgumentNullException>(create);
    }
}

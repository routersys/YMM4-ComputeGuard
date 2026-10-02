using System.Collections.Concurrent;

namespace ComputeGuard;

internal enum ComputeHealth
{
    Untested,
    Trusted,
    Pinned,
}

internal sealed class ComputeGuardianOptions
{
    public required Action<ComputeAnomalyException> Report { get; init; }
}

internal sealed class ComputeGuardian
{
    public const double MemoryShare = 0.25;

    private readonly Action<ComputeAnomalyException> report;
    private readonly ConcurrentDictionary<long, HealthCell> cells = new();

    public ComputeGuardian(ComputeGuardianOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Report);
        report = options.Report;
    }

    public ComputeHealth HealthOf(long luid) =>
        cells.TryGetValue(luid, out var cell) ? cell.Health : ComputeHealth.Untested;

    public ComputeDecision Select(ComputeDevice? device, in ComputeWorkload workload, Func<ComputeDevice, IReadOnlyList<ComputeCheck>> selfTest)
    {
        ArgumentNullException.ThrowIfNull(selfTest);

        if (device is not { } candidate)
        {
            return new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.NoDevice);
        }

        var cell = CellOf(candidate.Luid);
        if (cell.Health == ComputeHealth.Pinned)
        {
            return new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned);
        }

        if (candidate.IsSoftware)
        {
            return new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.SoftwareAdapter);
        }

        if (workload.GpuMemoryBytes > candidate.MemoryCapacity * MemoryShare)
        {
            return new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.MemoryLimit);
        }

        if (cell.Health == ComputeHealth.Untested)
        {
            Qualify(candidate, workload, cell, selfTest);
        }

        return cell.Health == ComputeHealth.Trusted
            ? new ComputeDecision(ComputeRoute.Gpu, ComputeRouteReason.Trusted)
            : new ComputeDecision(ComputeRoute.Cpu, ComputeRouteReason.Pinned);
    }

    public bool Judge(ComputeDevice device, in ComputeWorkload workload, ReadOnlySpan<ComputeCheck> checks)
    {
        foreach (var check in checks)
        {
            if (!check.Passes)
            {
                Pin(device, workload, CellOf(device.Luid), ComputeFailure.CheckFailed, check, null);
                return false;
            }
        }

        return true;
    }

    public void Fail(ComputeDevice device, in ComputeWorkload workload, ComputeFailure failure, Exception? exception)
    {
        Pin(device, workload, CellOf(device.Luid), failure, null, exception);
    }

    private HealthCell CellOf(long luid) => cells.GetOrAdd(luid, static _ => new HealthCell());

    private void Qualify(ComputeDevice device, in ComputeWorkload workload, HealthCell cell, Func<ComputeDevice, IReadOnlyList<ComputeCheck>> selfTest)
    {
        lock (cell.Gate)
        {
            if (cell.Health != ComputeHealth.Untested)
            {
                return;
            }

            ComputeCheck? failedCheck = null;
            try
            {
                foreach (var check in selfTest(device))
                {
                    if (!check.Passes)
                    {
                        failedCheck = check;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                Pin(device, workload, cell, ComputeFailure.SelfTestFailed, null, exception);
                return;
            }

            if (failedCheck is not null)
            {
                Pin(device, workload, cell, ComputeFailure.SelfTestFailed, failedCheck, null);
                return;
            }

            cell.Trust();
        }
    }

    private void Pin(ComputeDevice device, in ComputeWorkload workload, HealthCell cell, ComputeFailure failure, ComputeCheck? check, Exception? exception)
    {
        if (!cell.TryPin() || failure == ComputeFailure.ResourceExhausted)
        {
            return;
        }

        report(new ComputeAnomalyException(new ComputeAnomaly(device, workload, failure, check), exception));
    }

    private sealed class HealthCell
    {
        private int health = (int)ComputeHealth.Untested;

        public Lock Gate { get; } = new();

        public ComputeHealth Health => (ComputeHealth)Volatile.Read(ref health);

        public void Trust() =>
            Interlocked.CompareExchange(ref health, (int)ComputeHealth.Trusted, (int)ComputeHealth.Untested);

        public bool TryPin() =>
            Interlocked.Exchange(ref health, (int)ComputeHealth.Pinned) != (int)ComputeHealth.Pinned;
    }
}

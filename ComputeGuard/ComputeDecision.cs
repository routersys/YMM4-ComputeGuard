namespace ComputeGuard;

internal enum ComputeRoute
{
    Cpu,
    Gpu,
}

internal enum ComputeRouteReason
{
    Trusted,
    NoDevice,
    Pinned,
    SoftwareAdapter,
    MemoryLimit,
}

internal readonly record struct ComputeDecision(ComputeRoute Route, ComputeRouteReason Reason);

namespace ComputeGuard;

internal readonly record struct ComputeWorkload(int Width, int Height, long GpuMemoryBytes, ulong SamplingKey);

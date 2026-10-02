namespace ComputeGuard;

internal readonly record struct ComputeCheck(string Name, double Measured, double Reference, ComputeTolerance Tolerance)
{
    public bool Passes => Math.Abs(Measured - Reference) <= Tolerance.Absolute;

    public static ComputeCheck Count(string name, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ComputeCheck(name, count, 0d, ComputeTolerance.Exact);
    }
}

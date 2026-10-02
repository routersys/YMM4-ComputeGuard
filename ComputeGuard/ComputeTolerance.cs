namespace ComputeGuard;

internal readonly record struct ComputeTolerance
{
    public ComputeTolerance(double ulps, double scale, int flushedOperations)
    {
        if (!double.IsFinite(ulps) || ulps < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(ulps));
        }

        if (!double.IsFinite(scale) || scale < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(flushedOperations);

        Ulps = ulps;
        Scale = scale;
        FlushedOperations = flushedOperations;
    }

    public static ComputeTolerance Exact => default;

    public double Ulps { get; }

    public double Scale { get; }

    public int FlushedOperations { get; }

    public double Absolute =>
        Ulps * ComputeErrorModel.UnitInLastPlace * Scale + FlushedOperations * ComputeErrorModel.FlushLimit;
}

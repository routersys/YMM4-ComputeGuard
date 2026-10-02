namespace ComputeGuard;

internal readonly record struct ComputeTolerance
{
    public ComputeTolerance(double ulps, double scale, int flushedOperations)
    {
        if (ulps < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(ulps));
        }

        if (scale < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(flushedOperations);

        Ulps = ulps;
        Scale = scale;
        FlushedOperations = flushedOperations;

        if (!double.IsFinite(Absolute))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
    }

    public static ComputeTolerance Exact => default;

    public double Ulps { get; }

    public double Scale { get; }

    public int FlushedOperations { get; }

    public double Absolute =>
        Ulps * ComputeErrorModel.UnitInLastPlace * Scale + FlushedOperations * ComputeErrorModel.FlushLimit;
}

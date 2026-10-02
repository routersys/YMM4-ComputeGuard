namespace ComputeGuard;

internal static class ComputeErrorModel
{
    public static readonly double GpuOperationError = Math.ScaleB(1d, -23);

    public static readonly double CpuOperationError = Math.ScaleB(1d, -24);

    public static readonly double UnitInLastPlace = Math.ScaleB(1d, -23);

    public static readonly double FlushLimit = Math.ScaleB(1d, -126);

    public static double Gamma(int operations, double operationError)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operations);

        if (!double.IsFinite(operationError) || operationError < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(operationError));
        }

        var product = operations * operationError;
        if (product >= 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(operations));
        }

        return product / (1d - product);
    }

    public static long UlpDistance(float first, float second)
    {
        if (float.IsNaN(first) || float.IsNaN(second))
        {
            return long.MaxValue;
        }

        return Math.Abs((long)Ordered(first) - Ordered(second));
    }

    private static int Ordered(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        return bits < 0 ? int.MinValue - bits : bits;
    }
}

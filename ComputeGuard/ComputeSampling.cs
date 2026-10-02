namespace ComputeGuard;

internal readonly record struct ComputeSample(int X, int Y);

internal static class ComputeSampling
{
    public const int FixedCount = 5;

    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private const ulong FirstMultiplier = 0xBF58476D1CE4E5B9UL;
    private const ulong SecondMultiplier = 0x94D049BB133111EBUL;
    private const int FirstShift = 30;
    private const int SecondShift = 27;
    private const int ThirdShift = 31;
    private const int HalfBits = 32;
    private const ulong LowerHalf = 0xFFFFFFFFUL;

    public static void Fill(ulong key, int width, int height, Span<ComputeSample> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = index switch
            {
                0 => new ComputeSample(0, 0),
                1 => new ComputeSample(width - 1, 0),
                2 => new ComputeSample(0, height - 1),
                3 => new ComputeSample(width - 1, height - 1),
                4 => new ComputeSample(width / 2, height / 2),
                _ => Hashed(key, index, width, height),
            };
        }
    }

    private static ComputeSample Hashed(ulong key, int index, int width, int height)
    {
        var state = Mix(key + (ulong)index * Increment);
        var x = (int)(((state >> HalfBits) * (ulong)width) >> HalfBits);
        var y = (int)(((state & LowerHalf) * (ulong)height) >> HalfBits);
        return new ComputeSample(x, y);
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> FirstShift;
        value *= FirstMultiplier;
        value ^= value >> SecondShift;
        value *= SecondMultiplier;
        value ^= value >> ThirdShift;
        return value;
    }
}

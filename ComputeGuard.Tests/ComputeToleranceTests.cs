namespace ComputeGuard.Tests;

public sealed class ComputeToleranceTests
{
    [Fact]
    public void TheToleranceGrowsWithTheUlpsAndTheScale()
    {
        var tolerance = new ComputeTolerance(4d, 2d, 0);

        var absolute = tolerance.Absolute;

        Assert.Equal(8d * Math.ScaleB(1d, -23), absolute);
    }

    [Fact]
    public void EachFlushedOperationAddsTheSmallestNormalFloat()
    {
        var tolerance = new ComputeTolerance(0d, 0d, 3);

        var absolute = tolerance.Absolute;

        Assert.Equal(3d * Math.ScaleB(1d, -126), absolute);
    }

    [Fact]
    public void TheScaledPartAndTheFlushedPartAreAdded()
    {
        var tolerance = new ComputeTolerance(8d, 1d, 2);

        var absolute = tolerance.Absolute;

        Assert.Equal(Math.ScaleB(1d, -20) + 2d * Math.ScaleB(1d, -126), absolute);
    }

    [Fact]
    public void TheExactToleranceAllowsNothing()
    {
        var exact = ComputeTolerance.Exact;

        Assert.Equal(0d, exact.Absolute);
        Assert.Equal(default, exact);
    }

    [Theory]
    [InlineData(-1d, 1d, 0)]
    [InlineData(double.NaN, 1d, 0)]
    [InlineData(double.PositiveInfinity, 1d, 0)]
    [InlineData(1d, -1d, 0)]
    [InlineData(1d, double.NaN, 0)]
    [InlineData(1d, double.PositiveInfinity, 0)]
    [InlineData(1d, 1d, -1)]
    public void AToleranceWithoutAFiniteBoundIsRejected(double ulps, double scale, int flushedOperations)
    {
        Action create = () => _ = new ComputeTolerance(ulps, scale, flushedOperations);

        Assert.Throws<ArgumentOutOfRangeException>(create);
    }
}

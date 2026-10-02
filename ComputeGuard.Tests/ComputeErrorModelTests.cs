namespace ComputeGuard.Tests;

public sealed class ComputeErrorModelTests
{
    static readonly double SpacingAboveOne = MathF.BitIncrement(1f) - 1f;

    [Fact]
    public void AGpuOperationMayBeOffByOneUlpOfOne()
    {
        var error = ComputeErrorModel.GpuOperationError;

        Assert.Equal(SpacingAboveOne, error);
    }

    [Fact]
    public void ACpuOperationIsOffByAtMostHalfAnUlpOfOne()
    {
        var error = ComputeErrorModel.CpuOperationError;

        Assert.Equal(SpacingAboveOne / 2d, error);
    }

    [Fact]
    public void TheUnitInLastPlaceIsTheSpacingAboveOne()
    {
        var unit = ComputeErrorModel.UnitInLastPlace;

        Assert.Equal(SpacingAboveOne, unit);
    }

    [Fact]
    public void TheFlushLimitIsTheSmallestNormalFloat()
    {
        var limit = ComputeErrorModel.FlushLimit;

        Assert.Equal(BitConverter.Int32BitsToSingle(0x00800000), limit);
        Assert.True(float.IsNormal((float)limit));
        Assert.False(float.IsNormal(MathF.BitDecrement((float)limit)));
    }

    [Theory]
    [InlineData(0, 0.5, 0d)]
    [InlineData(1, 0.25, 1d / 3d)]
    [InlineData(2, 0.25, 1d)]
    [InlineData(3, 0.125, 0.6)]
    public void GammaGrowsWithTheNumberOfOperations(int operations, double operationError, double expected)
    {
        var gamma = ComputeErrorModel.Gamma(operations, operationError);

        Assert.Equal(expected, gamma, 15);
    }

    [Theory]
    [InlineData(-1, 0.1)]
    [InlineData(1, -0.1)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(4, 0.25)]
    [InlineData(5, 0.25)]
    public void GammaRejectsArgumentsWithoutABound(int operations, double operationError)
    {
        Action gamma = () => ComputeErrorModel.Gamma(operations, operationError);

        Assert.Throws<ArgumentOutOfRangeException>(gamma);
    }

    [Fact]
    public void BothZerosAreNoUlpsApart()
    {
        var distance = ComputeErrorModel.UlpDistance(0f, -0f);

        Assert.Equal(0L, distance);
    }

    [Theory]
    [InlineData(0x3F800000)]
    [InlineData(0x3DCCCCCD)]
    [InlineData(unchecked((int)0xC0600000))]
    [InlineData(0x00800000)]
    [InlineData(0x00000001)]
    [InlineData(0x7F7FFFFE)]
    public void NeighbouringFloatsAreOneUlpApart(int bits)
    {
        var value = BitConverter.Int32BitsToSingle(bits);

        var distance = ComputeErrorModel.UlpDistance(value, MathF.BitIncrement(value));

        Assert.Equal(1L, distance);
    }

    [Fact]
    public void TheSmallestFloatsOnBothSidesOfZeroAreTwoUlpsApart()
    {
        var distance = ComputeErrorModel.UlpDistance(float.Epsilon, -float.Epsilon);

        Assert.Equal(2L, distance);
    }

    [Fact]
    public void EveryFloatBetweenOneAndTwoIsCounted()
    {
        var distance = ComputeErrorModel.UlpDistance(1f, 2f);

        Assert.Equal(1L << 23, distance);
    }

    [Fact]
    public void OppositeOnesAreTwiceTheDistanceFromZeroApart()
    {
        var distance = ComputeErrorModel.UlpDistance(-1f, 1f);

        Assert.Equal(2L * 0x3F800000, distance);
    }

    [Theory]
    [InlineData(1f, 2f)]
    [InlineData(-1f, 1f)]
    [InlineData(-0f, float.Epsilon)]
    [InlineData(float.MaxValue, float.MinValue)]
    public void TheDistanceDoesNotDependOnTheOrder(float first, float second)
    {
        var forward = ComputeErrorModel.UlpDistance(first, second);

        var backward = ComputeErrorModel.UlpDistance(second, first);

        Assert.Equal(forward, backward);
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.NaN)]
    [InlineData(float.NaN, float.NaN)]
    [InlineData(float.NaN, float.PositiveInfinity)]
    public void NaNIsInfinitelyFarFromEveryValue(float first, float second)
    {
        var distance = ComputeErrorModel.UlpDistance(first, second);

        Assert.Equal(long.MaxValue, distance);
    }

    [Fact]
    public void TheLargestFloatIsOneUlpBelowInfinity()
    {
        var distance = ComputeErrorModel.UlpDistance(float.MaxValue, float.PositiveInfinity);

        Assert.Equal(1L, distance);
    }
}

namespace ComputeGuard.Tests;

public sealed class ComputeCheckTests
{
    static readonly double Bound = Math.ScaleB(1d, -20);

    static ComputeTolerance BoundTolerance() => new(8d, 1d, 0);

    [Fact]
    public void AValueInsideTheToleranceIsAccepted()
    {
        var check = new ComputeCheck("spot", Bound / 2d, 0d, BoundTolerance());

        var passes = check.Passes;

        Assert.True(passes);
    }

    [Fact]
    public void AValueExactlyAtTheToleranceIsAccepted()
    {
        var check = new ComputeCheck("spot", Bound, 0d, BoundTolerance());

        var passes = check.Passes;

        Assert.True(passes);
    }

    [Fact]
    public void AValueJustBeyondTheToleranceIsRejected()
    {
        var check = new ComputeCheck("spot", Math.BitIncrement(Bound), 0d, BoundTolerance());

        var passes = check.Passes;

        Assert.False(passes);
    }

    [Fact]
    public void AnErrorBelowTheReferenceIsJudgedLikeOneAbove()
    {
        var atBound = new ComputeCheck("spot", -Bound, 0d, BoundTolerance());
        var beyondBound = new ComputeCheck("spot", -Math.BitIncrement(Bound), 0d, BoundTolerance());

        var accepted = atBound.Passes;
        var rejected = beyondBound.Passes;

        Assert.True(accepted);
        Assert.False(rejected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteMeasurementIsNeverAccepted(double measured)
    {
        var check = new ComputeCheck("spot", measured, 0d, new ComputeTolerance(1d, 1e300, 0));

        var passes = check.Passes;

        Assert.False(passes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteReferenceIsNeverAccepted(double reference)
    {
        var check = new ComputeCheck("spot", 0d, reference, new ComputeTolerance(1d, 1e300, 0));

        var passes = check.Passes;

        Assert.False(passes);
    }

    [Fact]
    public void SignedZerosAreEqual()
    {
        var check = new ComputeCheck("zero", -0d, 0d, ComputeTolerance.Exact);

        var passes = check.Passes;

        Assert.True(passes);
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, false)]
    [InlineData(-1L, false)]
    [InlineData(long.MaxValue, false)]
    public void ACountIsAcceptedOnlyWhenNothingWasCounted(long count, bool expected)
    {
        var check = ComputeCheck.Count("nonfinite", count);

        var passes = check.Passes;

        Assert.Equal(expected, passes);
    }

    [Fact]
    public void ACountKeepsItsNameAndValue()
    {
        var check = ComputeCheck.Count("nonfinite", 3L);

        Assert.Equal("nonfinite", check.Name);
        Assert.Equal(3d, check.Measured);
        Assert.Equal(0d, check.Reference);
        Assert.Equal(ComputeTolerance.Exact, check.Tolerance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ACountNeedsAName(string? name)
    {
        Action create = () => ComputeCheck.Count(name!, 0L);

        Assert.ThrowsAny<ArgumentException>(create);
    }
}

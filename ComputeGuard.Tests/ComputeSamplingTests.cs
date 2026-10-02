namespace ComputeGuard.Tests;

public sealed class ComputeSamplingTests
{
    static ComputeSample[] Draw(ulong key, int width, int height, int count)
    {
        var samples = new ComputeSample[count];
        ComputeSampling.Fill(key, width, height, samples);
        return samples;
    }

    [Fact]
    public void TheFirstFiveAreTheCornersAndTheCenter()
    {
        var samples = Draw(7UL, 1920, 1080, 16);

        Assert.Equal(new ComputeSample(0, 0), samples[0]);
        Assert.Equal(new ComputeSample(1919, 0), samples[1]);
        Assert.Equal(new ComputeSample(0, 1079), samples[2]);
        Assert.Equal(new ComputeSample(1919, 1079), samples[3]);
        Assert.Equal(new ComputeSample(960, 540), samples[4]);
    }

    [Fact]
    public void FewerSamplesTakeTheFixedOnesInOrder()
    {
        var samples = Draw(7UL, 100, 50, 3);

        Assert.Equal([new ComputeSample(0, 0), new ComputeSample(99, 0), new ComputeSample(0, 49)], samples);
    }

    [Fact]
    public void TheSameKeyGivesTheSamePositions()
    {
        var first = Draw(12345UL, 1920, 1080, 64);

        var second = Draw(12345UL, 1920, 1080, 64);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentKeyGivesDifferentPositions()
    {
        var first = Draw(12345UL, 1920, 1080, 64);

        var second = Draw(12346UL, 1920, 1080, 64);

        Assert.NotEqual(first.Skip(ComputeSampling.FixedCount), second.Skip(ComputeSampling.FixedCount));
    }

    [Fact]
    public void APositionDoesNotDependOnHowManyAreDrawn()
    {
        var few = Draw(99UL, 1920, 1080, 8);

        var many = Draw(99UL, 1920, 1080, 32);

        Assert.Equal(few, many.Take(8));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 1000)]
    [InlineData(7, 3)]
    [InlineData(8192, 4608)]
    public void EveryPositionLiesInsideTheImage(int width, int height)
    {
        var samples = Enumerable.Range(0, 100).SelectMany(key => Draw((ulong)key, width, height, 64)).ToList();

        Assert.All(samples, sample =>
        {
            Assert.InRange(sample.X, 0, width - 1);
            Assert.InRange(sample.Y, 0, height - 1);
        });
    }

    [Fact]
    public void ThePositionsSpreadOverTheWholeImage()
    {
        var samples = Draw(1UL, 1000, 1000, 256).Skip(ComputeSampling.FixedCount).ToList();

        var quadrants = samples.Select(sample => (sample.X >= 500, sample.Y >= 500)).Distinct().Count();
        var columns = samples.Select(sample => sample.X).Distinct().Count();
        var rows = samples.Select(sample => sample.Y).Distinct().Count();

        Assert.Equal(4, quadrants);
        Assert.True(columns > 200);
        Assert.True(rows > 200);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void AnEmptyImageIsRejected(int width, int height)
    {
        Action draw = () => Draw(1UL, width, height, 8);

        Assert.Throws<ArgumentOutOfRangeException>(draw);
    }
}

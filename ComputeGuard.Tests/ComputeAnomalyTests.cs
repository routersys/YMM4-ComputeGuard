using System.Globalization;

namespace ComputeGuard.Tests;

public sealed class ComputeAnomalyTests
{
    static readonly ComputeDevice Device =
        new(1L, 0x8086, 0xA7A0, 0x00BB1E26, 4, (32L << 48) | (101L << 16) | 7085L, false, 1L << 30, "Test Adapter");

    static readonly ComputeWorkload Workload = new(1952, 1112, 16777216L, 7UL);

    static readonly ComputeCheck FailedCheck = new("spot[3].R", 1.5, 1.25, new ComputeTolerance(8d, 1d, 0));

    [Fact]
    public void TheMessageNamesTheReasonTheCheckTheDeviceAndTheWorkload()
    {
        var anomaly = new ComputeAnomalyException(new ComputeAnomaly(Device, Workload, ComputeFailure.CheckFailed, FailedCheck), null);

        var message = anomaly.Message;

        Assert.Contains("理由=CheckFailed", message);
        Assert.Contains("検査=spot[3].R", message);
        Assert.Contains("測定値=1.5", message);
        Assert.Contains("参照値=1.25", message);
        Assert.Contains("許容値=" + Math.ScaleB(1d, -20).ToString("R", CultureInfo.InvariantCulture), message);
        Assert.Contains("アダプター=Test Adapter", message);
        Assert.Contains("識別=8086:A7A0:00BB1E26:04", message);
        Assert.Contains("ドライバー=32.0.101.7085", message);
        Assert.Contains("作業量=1952x1112", message);
        Assert.Contains("GPU メモリ=16777216", message);
    }

    [Fact]
    public void TheMessageLeavesOutTheCheckWhenThereIsNone()
    {
        var anomaly = new ComputeAnomalyException(new ComputeAnomaly(Device, Workload, ComputeFailure.DeviceLost, null), null);

        var message = anomaly.Message;

        Assert.Contains("理由=DeviceLost", message);
        Assert.DoesNotContain("検査=", message);
    }

    [Fact]
    public void TheNumbersDoNotFollowTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var anomaly = new ComputeAnomalyException(new ComputeAnomaly(Device, Workload, ComputeFailure.CheckFailed, FailedCheck), null);

            var message = anomaly.Message;

            Assert.Contains("測定値=1.5", message);
            Assert.DoesNotContain("1,5", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheCauseIsKeptAsTheInnerException()
    {
        var cause = new InvalidOperationException("device removed");

        var anomaly = new ComputeAnomalyException(new ComputeAnomaly(Device, Workload, ComputeFailure.DeviceLost, null), cause);

        Assert.Same(cause, anomaly.InnerException);
    }

    [Fact]
    public void TheRecordIsKeptForTheCaller()
    {
        var record = new ComputeAnomaly(Device, Workload, ComputeFailure.CheckFailed, FailedCheck);

        var anomaly = new ComputeAnomalyException(record, null);

        Assert.Same(record, anomaly.Anomaly);
    }

    [Fact]
    public void ARecordIsRequired()
    {
        Action create = () => _ = new ComputeAnomalyException(null!, null);

        Assert.Throws<ArgumentNullException>(create);
    }
}

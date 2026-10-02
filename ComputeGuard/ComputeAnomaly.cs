using System.Globalization;

namespace ComputeGuard;

internal enum ComputeFailure
{
    SelfTestFailed,
    CheckFailed,
    DeviceLost,
    ResourceExhausted,
    UnexpectedException,
}

internal sealed record ComputeAnomaly(ComputeDevice Device, ComputeWorkload Workload, ComputeFailure Failure, ComputeCheck? Check);

internal sealed class ComputeAnomalyException : Exception
{
    public ComputeAnomalyException(ComputeAnomaly anomaly, Exception? innerException)
        : base(Describe(anomaly), innerException)
    {
        Anomaly = anomaly;
    }

    public ComputeAnomaly Anomaly { get; }

    private static string Describe(ComputeAnomaly anomaly)
    {
        ArgumentNullException.ThrowIfNull(anomaly);

        var device = anomaly.Device;
        var workload = anomaly.Workload;
        var check = anomaly.Check is { } value
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" 検査={value.Name} 測定値={value.Measured:R} 参照値={value.Reference:R} 許容値={value.Tolerance.Absolute:R}")
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"GPU の計算を CPU に固定しました。理由={anomaly.Failure}{check} アダプター={device.Description} 識別={device.Identity} ドライバー={device.DriverVersionText} ソフトウェア={device.IsSoftware} 作業量={workload.Width}x{workload.Height} GPU メモリ={workload.GpuMemoryBytes}");
    }
}

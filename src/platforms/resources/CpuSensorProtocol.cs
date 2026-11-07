using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CodexSwitch.Sensors;

internal sealed record CpuSample(int Version, string State, double? Celsius, long At, string Sensor, int? Error = null);
[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CpuSample))]
internal partial class CpuJson : JsonSerializerContext { }
internal static class CpuSensorProtocol
{
    public const int MaximumBytes = 1024;
    public const int ConnectTimeoutSeconds = 5, ShutdownTimeoutSeconds = 10;
    public const string Source = "Codex Switch · native CPU sensor";
    public static CpuSample Missing(string state) => new(1, state, null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "");
    public static CpuSample Validate(CpuSample value, DateTimeOffset now)
    {
        if (value.Version != 1 || value.State is not ("Available" or "ProviderMissing" or "AccessDenied" or "SensorUnavailable" or "ReadFailed" or "PlatformUnsupported") ||
            value.At < now.AddSeconds(-30).ToUnixTimeMilliseconds() || value.At > now.AddSeconds(5).ToUnixTimeMilliseconds() ||
            value.Sensor is not ("" or "AMD CPU (Tctl)" or "Intel CPU package" or "Intel CPU core") || value.Error is < 0 or > 65535 ||
            value.State == "Available" && value.Error != null ||
            (value.State == "Available" ? value.Celsius is not double c || !double.IsFinite(c) || c < -50 || c > 150 || value.Sensor.Length == 0 : value.Celsius != null || value.Sensor.Length != 0))
            throw new InvalidDataException("Invalid CPU sensor message.");
        return value;
    }
    public static async Task WriteAsync(Stream stream, CpuSample value, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, CpuJson.Default.CpuSample);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("CPU sensor message is too large.");
        var header = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
    public static async Task<CpuSample> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation);
        var count = BitConverter.ToInt32(header);
        if (count < 1 || count > MaximumBytes) throw new InvalidDataException("Invalid CPU sensor frame size.");
        var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, cancellation);
        return Validate(JsonSerializer.Deserialize(bytes, CpuJson.Default.CpuSample) ?? throw new InvalidDataException("Missing CPU sensor message."), DateTimeOffset.UtcNow);
    }
}

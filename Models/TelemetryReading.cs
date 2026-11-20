namespace Telemetry.Blazor.Models;
public sealed class TelemetryReading
{
    public DateTime Time { get; set; }
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double? Pressure { get; set; }
    public double? Light { get; set; }
    public int? ServoState { get; set; }
}

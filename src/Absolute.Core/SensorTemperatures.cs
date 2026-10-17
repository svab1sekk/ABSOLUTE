namespace Absolute.Core;

public static class SensorTemperatures
{
    public static double? Read(IEnumerable<SensorReading> source,string hardware)
    {
        var readings=source.Where(s=>s.Type.Equals("Temperature",StringComparison.OrdinalIgnoreCase)&&s.Hardware.Contains(hardware,StringComparison.OrdinalIgnoreCase)&&s.Value is >0 and <150&&!s.Name.Contains("distance",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(readings.Length==0)return null;
        var preferred=readings.Where(s=>hardware=="cpu"?(s.Name.Contains("package",StringComparison.OrdinalIgnoreCase)||s.Name.Contains("tdie",StringComparison.OrdinalIgnoreCase)):s.Name.Equals("GPU Core",StringComparison.OrdinalIgnoreCase)||s.Name.Equals("GPU Temperature",StringComparison.OrdinalIgnoreCase)).ToArray();
        return (preferred.Length>0?preferred:readings).Max(s=>(double)s.Value);
    }
}

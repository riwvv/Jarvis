#nullable disable
using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class DailyForecastResponse {
    [JsonPropertyName("latitude")]
    public float Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public float Longitude { get; set; }

    [JsonPropertyName("daily")]
    public DailyData Daily { get; set; }
}

public class DailyData {
    [JsonPropertyName("time")]
    public string[] Time { get; set; }

    [JsonPropertyName("temperature_2m_max")]
    public float[] TemperatureMax { get; set; }

    [JsonPropertyName("temperature_2m_min")]
    public float[] TemperatureMin { get; set; }

    [JsonPropertyName("weathercode")]
    public int[] WeatherCode { get; set; }
}

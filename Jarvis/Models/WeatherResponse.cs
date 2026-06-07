#nullable disable
using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class WeatherResponse {
    [JsonPropertyName("latitude")]
    public float latitude { get; set; }
    [JsonPropertyName("longitude")]
    public float longitude { get; set; }
    [JsonPropertyName("generationtime_ms")]
    public float generationtime_ms { get; set; }
    [JsonPropertyName("utc_offset_seconds")]
    public int utc_offset_seconds { get; set; }
    [JsonPropertyName("timezone")]
    public string timezone { get; set; }
    [JsonPropertyName("timezone_abbreviation")]
    public string timezone_abbreviation { get; set; }
    [JsonPropertyName("elevation")]
    public float elevation { get; set; }
    [JsonPropertyName("current_weather_units")]
    public Current_Weather_Units current_weather_units { get; set; }
    [JsonPropertyName("current_weather")]
    public Current_Weather current_weather { get; set; }
}

public class Current_Weather_Units {
    [JsonPropertyName("time")]
    public string time { get; set; }
    [JsonPropertyName("interval")]
    public string interval { get; set; }
    [JsonPropertyName("temperature")]
    public string temperature { get; set; }
    [JsonPropertyName("windspeed")]
    public string windspeed { get; set; }
    [JsonPropertyName("winddirection")]
    public string winddirection { get; set; }
    [JsonPropertyName("is_day")]
    public string is_day { get; set; }
    [JsonPropertyName("weathercode")]
    public string weathercode { get; set; }
}

public class Current_Weather {
    [JsonPropertyName("time")]
    public string time { get; set; }
    [JsonPropertyName("interval")]
    public int interval { get; set; }
    [JsonPropertyName("temperature")]
    public float temperature { get; set; }
    [JsonPropertyName("windspeed")]
    public float windspeed { get; set; }
    [JsonPropertyName("winddirection")]
    public int winddirection { get; set; }
    [JsonPropertyName("is_day")]
    public int is_day { get; set; }
    [JsonPropertyName("weathercode")]
    public int weathercode { get; set; }
}

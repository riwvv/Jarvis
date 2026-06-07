#nullable disable
using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class GeoResponse {
    [JsonPropertyName("results")]
    public List<GeoResult> results { get; set; } = new();
}

public class GeoResult {
    [JsonPropertyName("id")]
    public int id { get; set; }

    [JsonPropertyName("name")]
    public string name { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double longitude { get; set; }

    [JsonPropertyName("elevation")]
    public double elevation { get; set; }

    [JsonPropertyName("country_code")]
    public string country_code { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string country { get; set; } = string.Empty;

    [JsonPropertyName("admin1")]
    public string admin1 { get; set; } = string.Empty;

    [JsonPropertyName("timezone")]
    public string timezone { get; set; } = string.Empty;
}
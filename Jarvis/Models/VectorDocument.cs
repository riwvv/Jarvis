using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class VectorDocument {
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("source")]
    public string Source { get; set; } = "conversation";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

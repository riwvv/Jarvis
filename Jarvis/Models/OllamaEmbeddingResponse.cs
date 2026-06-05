using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class OllamaEmbeddingResponse {
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}

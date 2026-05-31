using System.Text.Json.Serialization;

namespace Jarvis.Models;

internal class OllamaEmbeddingResponse {
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];
}
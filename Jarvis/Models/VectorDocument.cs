using System.Text.Json.Serialization;

namespace Jarvis.Models {
    public class VectorDocument {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = string.Empty;
        public string Source { get; set; } = "conversation";
        public Dictionary<string, string> Metadata { get; set; } = new();

        [JsonPropertyName("Embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}

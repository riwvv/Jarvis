using Microsoft.Extensions.VectorData;

namespace Jarvis.VectorMemory;

public class ChatMemory {
    [VectorStoreKey] public string Id { get; init; } = Guid.NewGuid().ToString();
    [VectorStoreData] public required string UserPrompt { get; init; }
    [VectorStoreData] public required string AssistantResponse { get; init; }
    [VectorStoreData] public required string Timestamp { get; init; }
    [VectorStoreVector(384)] public ReadOnlyMemory<float> Embedding { get; set; }
}

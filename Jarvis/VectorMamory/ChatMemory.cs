using Microsoft.Extensions.VectorData;

namespace Jarvis.VectorMamory;

public class ChatMemory {
    [VectorStoreKey] public string Id { get; init; } = Guid.NewGuid().ToString();
    [VectorStoreData] public required string IserPrompt { get; init; }
    [VectorStoreData] public required string AssistantResponse { get; init; }
    [VectorStoreData] public required string Timestamp { get; init; }
    [VectorStoreVector(2880)] public ReadOnlyMemory<float> Embedding { get; set; }
}

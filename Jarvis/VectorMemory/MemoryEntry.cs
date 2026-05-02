namespace Jarvis.VectorMemory;

public class MemoryEntry {
    public string Id { get; set; } = "";
    public string UserPrompt { get; set; } = "";
    public string AssistantResponse { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

namespace Jarvis.Interfaces;

public interface IRagMemoryService : IDisposable {
    Task SaveMemoryAsync(string userPrompt, string assistantResponse);
    Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3);
}

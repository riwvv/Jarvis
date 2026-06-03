namespace Jarvis.Interfaces;

public interface IRagMemoryService : IDisposable {
    Task SaveMemoryAsync(string userPrompt, string assistantResponse);
    Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3);
    Task<string?> FindCommandPatternAsync(string command, float minSimilarity = 0.75f);
}

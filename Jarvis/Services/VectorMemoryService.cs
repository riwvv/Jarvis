using StackExchange.Redis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jarvis.Services;

public class VectorMemoryService {
    private readonly IDatabase _database;
    private const string HashKey = "jarvis_simple_memory";

    public VectorMemoryService(IConnectionMultiplexer multiplexer) {
        _database = multiplexer.GetDatabase();
    }

    public async Task SaveMemoryAsync(string userPrompt, string assistantResponse) {
        if (assistantResponse.StartsWith("ERROR") || assistantResponse.StartsWith("WARNING"))
            return;

        var id = Guid.NewGuid().ToString();
        var entry = new {
            Id = id,
            UserPrompt = userPrompt,
            AssistantResponse = assistantResponse,
            Timestamp = DateTime.UtcNow.Ticks
        };
        var json = JsonSerializer.Serialize(entry);
        await _database.HashSetAsync(HashKey, id, json);
        Debug.WriteLine($"Сохранено: {userPrompt} -> {assistantResponse}");
    }

    public async Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3) {
        var allEntries = await _database.HashGetAllAsync(HashKey);
        if (allEntries.Length == 0) return null;

        var results = new List<(string Prompt, string Response, int Score)>();
        var searchWords = userMessage.ToLower().Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in allEntries) {
            var json = entry.Value.ToString();
            using var doc = JsonDocument.Parse(json);
            var prompt = doc.RootElement.GetProperty("UserPrompt").GetString() ?? "";
            var response = doc.RootElement.GetProperty("AssistantResponse").GetString() ?? "";

            var promptWords = prompt.ToLower().Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
            var score = searchWords.Count(w => promptWords.Contains(w));

            if (score > 0) results.Add((prompt, response, score));
        }

        if (!results.Any()) return null;

        var topResults = results.OrderByDescending(r => r.Score).Take(topK).ToList();
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("Вот информация из моей памяти:");
        foreach (var r in topResults) {
            contextBuilder.AppendLine($"- Пользователь сказал: \"{r.Prompt}\"");
            contextBuilder.AppendLine($"  Я ответил: \"{r.Response}\"");
        }
        return contextBuilder.ToString();
    }
}
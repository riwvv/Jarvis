using Jarvis.VectorMemory;
using LiteDB;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis.Services;

public class VectorMemoryService {
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<MemoryEntry> _collection;

    public VectorMemoryService() {
        // Храним базу данных в папке пользователя, не в папке с программой
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "memory.db");
        _db = new LiteDatabase(dbPath);
        _collection = _db.GetCollection<MemoryEntry>("memories");

        // Автоиндексация по времени
        _collection.EnsureIndex(x => x.Timestamp);
    }

    public Task SaveMemoryAsync(string userPrompt, string assistantResponse) {
        if (assistantResponse.StartsWith("ERROR") || assistantResponse.StartsWith("WARNING"))
            return Task.CompletedTask;

        _collection.Insert(new MemoryEntry {
            Id = Guid.NewGuid().ToString(),
            UserPrompt = userPrompt,
            AssistantResponse = assistantResponse,
            Timestamp = DateTime.UtcNow
        });

        Debug.WriteLine($"Сохранено в LiteDB: {userPrompt} -> {assistantResponse}");
        return Task.CompletedTask;
    }

    public Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3) {
        var searchWords = userMessage.ToLower().Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        var all = _collection.Query().ToList();

        if (all.Count == 0) {
            Debug.WriteLine("LiteDB: нет сохранённых записей");
            return Task.FromResult<string?>(null);
        }

        var results = new List<(MemoryEntry entry, int score)>();
        foreach (var entry in all) {
            var promptWords = entry.UserPrompt.ToLower().Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
            var score = searchWords.Count(w => promptWords.Contains(w));
            if (score > 0) results.Add((entry, score));
        }

        if (!results.Any()) return Task.FromResult<string?>(null);

        var topResults = results.OrderByDescending(r => r.score).Take(topK).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Вот что я помню из прошлого:");
        foreach (var (entry, score) in topResults) {
            sb.AppendLine($"- Вы говорили: \"{entry.UserPrompt}\"");
            sb.AppendLine($"  Я ответил: \"{entry.AssistantResponse}\"");
        }

        return Task.FromResult<string?>(sb.ToString());
    }
}
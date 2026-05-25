using System.IO;
using System.Text;
using LiteDB;
using Jarvis.VectorMemory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jarvis.Services;

public class VectorMemoryService {
    private readonly ILogger<VectorMemoryService> _logger;
    private readonly IMemoryCache _cache; // добавить кэш
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<MemoryEntry> _collection;

    public VectorMemoryService(IMemoryCache memoryCache, ILogger<VectorMemoryService> logger) {
        _logger = logger;
        _cache = memoryCache;
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

        _logger.LogInformation($"Сохранено в LiteDB: {userPrompt} -> {assistantResponse}");
        return Task.CompletedTask;
    }

    public Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3) {
        var searchWords = userMessage.ToLower().Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
        var all = _collection.Query().ToList();

        if (all.Count == 0) {
            _logger.LogInformation("LiteDB: нет сохранённых записей");
            return Task.FromResult<string?>(null);
        }

        var results = new List<(MemoryEntry entry, int score)>();
        foreach (var entry in all) {
            var promptWords = entry.UserPrompt.ToLower().Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries);
            var score = searchWords.Count(w => promptWords.Contains(w));
            if (score > 0) results.Add((entry, score));
        }

        if (!results.Any()) return Task.FromResult<string?>(null);

        var topResults = results.OrderByDescending(r => r.score).Take(topK * 2).ToList(); // Берём с запасом

        // Дедупликация: если UserPrompt очень похожи, оставляем один
        var uniqueResults = new List<(MemoryEntry entry, int score)>();
        foreach (var result in topResults) {
            if (!uniqueResults.Any(u => AreSimilar(u.entry.UserPrompt, result.entry.UserPrompt))) {
                uniqueResults.Add(result);
                if (uniqueResults.Count >= topK) break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("Вот что я помню из прошлого:");
        foreach (var (entry, score) in topResults) {
            sb.AppendLine($"- Вы говорили: \"{entry.UserPrompt}\"");
            sb.AppendLine($"  Я ответил: \"{entry.AssistantResponse}\"");
        }

        return Task.FromResult<string?>(sb.ToString());
    }

    private bool AreSimilar(string a, string b) {
        if (a == b) return true;
        if (Math.Abs(a.Length - b.Length) > 5) return false;
        var aWords = a.Split(' ');
        var bWords = b.Split(' ');
        var common = aWords.Intersect(bWords).Count();
        return common > Math.Min(aWords.Length, bWords.Length) * 0.7; // 70% совпадения
    }
}
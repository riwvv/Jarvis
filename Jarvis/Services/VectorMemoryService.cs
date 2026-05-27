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

    public VectorMemoryService(ILogger<VectorMemoryService> logger) {
        _logger = logger;
        _cache = memoryCache;
        // Храним базу данных в папке пользователя, не в папке с программой
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "memory.db");
        _db = new LiteDatabase(dbPath);
        _collection = _db.GetCollection<MemoryEntry>("memories");

        _collection.EnsureIndex(x => x.Timestamp);
        _collection.EnsureIndex(x => x.UserPrompt);

        _logger.LogInformation("VectorMemoryService инициализирован с индексами");
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
        if (string.IsNullOrWhiteSpace(userMessage))
            return Task.FromResult<string?>(null);

        var searchWords = userMessage.ToLower()
            .Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

        if (searchWords.Count == 0) {
            return Task.FromResult<string?>(null);
        }

        try {
            // ========== ОПТИМИЗАЦИЯ 1: Поиск через индексы БД ==========
            // Строим запрос, который использует индексы LiteDB
            var query = _collection.Query();

            // Добавляем условия для каждого слова (AND логика)
            // Используем Contains для полнотекстового поиска
            foreach (var word in searchWords) {
                // Ищем записи, где UserPrompt содержит это слово
                // $.* - поиск по всему документу, но можно уточнить до $.UserPrompt
                query = query.Where($"$.UserPrompt LIKE '%{word}%'");
            }

            // Ограничиваем количество кандидатов для оценки
            // Берём в 3 раза больше topK для лучшего покрытия
            var candidates = query
                .OrderByDescending(x => x.Timestamp) // Сначала свежие записи
                .Limit(topK * 3)
                .ToList();

            if (candidates.Count == 0) {
                _logger.LogDebug("LiteDB: нет записей, содержащих слова из запроса");
                return Task.FromResult<string?>(null);
            }

            _logger.LogDebug($"Найдено {candidates.Count} кандидатов для оценки");

            // ========== ОПТИМИЗАЦИЯ 2: Оценка релевантности (только для кандидатов) ==========
            var results = new List<(MemoryEntry entry, int score)>();

            foreach (var entry in candidates) {
                // Кэшируем разбитые слова для каждого entry? 
                // Для простоты пока оставим как есть, но можно добавить кэш
                var promptWords = entry.UserPrompt.ToLower()
                    .Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

                // Считаем количество совпадающих слов
                var score = searchWords.Count(w => promptWords.Contains(w));

                // Добавляем бонус за свежесть (более новые записи получают +1)
                var ageBonus = IsRecent(entry.Timestamp) ? 1 : 0;
                score += ageBonus;

                if (score > 0) {
                    results.Add((entry, score));
                }
            }

            if (results.Count == 0) {
                return Task.FromResult<string?>(null);
            }

            // ========== ОПТИМИЗАЦИЯ 3: Быстрая сортировка и дедупликация ==========
            var topResults = results
                .OrderByDescending(r => r.score)
                .Take(topK * 2)
                .ToList();

            // Дедупликация с оптимизацией (используем HashSet для ускорения)
            var uniqueResults = new List<(MemoryEntry entry, int score)>();
            var seenHashes = new HashSet<int>();

            foreach (var result in topResults) {
                // Быстрая проверка через хэш (дополнительно к AreSimilar)
                var hash = result.entry.UserPrompt.GetHashCode();
                if (!seenHashes.Contains(hash)) {
                    // Проверяем на семантическую похожесть (только если хэши разные)
                    bool isDuplicate = uniqueResults.Any(u => AreSimilar(u.entry.UserPrompt, result.entry.UserPrompt));

                    if (!isDuplicate) {
                        uniqueResults.Add(result);
                        seenHashes.Add(hash);

                        if (uniqueResults.Count >= topK) break;
                    }
                }
            }

            // ========== ФОРМИРОВАНИЕ ОТВЕТА ==========
            var sb = new StringBuilder();
            sb.AppendLine("Вот что я помню из прошлого:");

            foreach (var (entry, score) in uniqueResults) {
                // Ограничиваем длину ответов, чтобы не переполнить контекст
                var userPromptTruncated = TruncateText(entry.UserPrompt, 100);
                var assistantResponseTruncated = TruncateText(entry.AssistantResponse, 150);

                sb.AppendLine($"- Вы говорили: \"{userPromptTruncated}\"");
                sb.AppendLine($"  Я ответил: \"{assistantResponseTruncated}\"");
            }

            _logger.LogDebug($"Возвращено {uniqueResults.Count} релевантных воспоминаний");
            return Task.FromResult<string?>(sb.ToString());
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при поиске в памяти");
            return Task.FromResult<string?>(null);
        }
    }

    private bool IsRecent(DateTime timestamp) => (DateTime.UtcNow - timestamp).TotalHours < 24;

    private string TruncateText(string text, int maxLength) {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text ?? string.Empty;

        return text.Substring(0, maxLength) + "...";
    }

    // Оптимизированная версия AreSimilar (с ранним выходом)
    private bool AreSimilar(string a, string b) {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (a == b) return true;

        // Быстрая проверка на длину
        if (Math.Abs(a.Length - b.Length) > 10)
            return false;

        var aWords = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bWords = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Если одно слово сильно отличается по количеству слов
        if (Math.Abs(aWords.Length - bWords.Length) > 3)
            return false;

        var common = aWords.Intersect(bWords, StringComparer.OrdinalIgnoreCase).Count();
        var minLength = Math.Min(aWords.Length, bWords.Length);

        return common > minLength * 0.7;
    }
}
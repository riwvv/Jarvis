using Jarvis.Interfaces;
using Jarvis.Models;
using Microsoft.Extensions.Logging;
using RAGSharp.Embeddings.Tokenizers;
using RAGSharp.IO;
using RAGSharp.RAG;
using RAGSharp.Stores;
using RAGSharp.Text;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Jarvis.Services;

public class RagMemoryService : IRagMemoryService {
    private readonly ILogger<RagMemoryService> _logger;
    private readonly string _vectorPath;
    private readonly OllamaEmbeddingClient _embeddingClient;
    private readonly FileVectorStore _store;
    private readonly RagRetriever _retriever;

    public RagMemoryService(ILogger<RagMemoryService> logger) {
        _logger = logger;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        Directory.CreateDirectory(appData);
        _vectorPath = Path.Combine(appData, "vectors.json");

        _embeddingClient = new OllamaEmbeddingClient("qwen3-embedding:4b", logger: _logger);
        _store = new FileVectorStore(_vectorPath);

        var tokenizer = new SharpTokenTokenizer("gpt-4");
        var splitter = new RecursiveTextSplitter(tokenizer, chunkSize: 200, chunkOverlap: 100);

        _retriever = new RagRetriever(embeddings: _embeddingClient, store: _store, splitter: splitter);

        _ = Task.Run(() => LoadExistingVectors());

        _logger.LogInformation($"RagMemoryService инициализирован. Хранилище: {_vectorPath}");
    }

    private async Task LoadExistingVectors() {
        // Проверяем, существует ли папка (не файл!)
        if (!Directory.Exists(_vectorPath)) {
            _logger.LogInformation("Папка векторов не существует, начнём с пустой памяти");
            return;
        }

        var kbFilePath = Path.Combine(_vectorPath, "kb.json");
        if (!File.Exists(kbFilePath)) {
            _logger.LogInformation("Файл kb.json не найден, память пуста");
            return;
        }

        try {
            var json = await File.ReadAllTextAsync(kbFilePath);
            var documents = JsonSerializer.Deserialize<List<VectorDocument>>(json);

            if (documents != null && documents.Any()) {
                _logger.LogInformation($"Найдено {documents.Count} сохранённых документов, загружаем...");

                foreach (var doc in documents) {
                    var ragDocument = new Document(
                        source: doc.Source ?? "conversation",
                        content: doc.Content,
                        metadata: doc.Metadata ?? new Dictionary<string, string>()
                    );

                    await _retriever.AddDocumentAsync(ragDocument);
                }

                _logger.LogInformation($"✅ Загружено {documents.Count} сохранённых векторов из {kbFilePath}");
            }
            else {
                _logger.LogInformation("Файл kb.json пуст");
            }

            // Тестовый поиск для проверки
            var testResults = await _retriever.Search("тест", 1);
            _logger.LogInformation($"Тестовый поиск вернул {testResults.Count()} результатов");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при загрузке существующих векторов");
        }
    }

    public async Task SaveMemoryAsync(string userPrompt, string assistantResponse) {
        if (assistantResponse.StartsWith("ERROR") || assistantResponse.StartsWith("WARNING"))
            return;

        try {
            var content = $"""
                Пользователь спросил: {userPrompt}
                Джарвис ответил: {assistantResponse}
                """;

            var metadata = new Dictionary<string, string> {
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["userQuery"] = userPrompt,
                ["intent"] = "conversation"
            };

            var document = new Document(
                source: "conversation",
                content: content,
                metadata: metadata
            );
            await _retriever.AddDocumentAsync(document);
            _logger.LogDebug("Сохранён диалог в RAG");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при сохранении в RAG");
        }
    }

    public async Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3) {
        _logger.LogInformation($"🔍 [SEARCH] ВЫЗОВ с запросом: {userMessage}");

        if (string.IsNullOrWhiteSpace(userMessage)) {
            _logger.LogWarning("🔍 [SEARCH] Пустой запрос");
            return null;
        }

        try {
            // Проверяем, есть ли что-то в хранилище
            var testResults = await _retriever.Search("", 1);
            _logger.LogInformation($"🔍 [SEARCH] В хранилище есть документы? {testResults.Any()}");

            var results = await _retriever.Search(userMessage, topK);
            var resultsList = results.ToList();

            _logger.LogInformation($"🔍 [SEARCH] Найдено результатов: {resultsList.Count}");

            foreach (var r in resultsList) {
                _logger.LogInformation($"🔍 [SEARCH] Результат: Score={r.Score:F2}, Content={r.Content[..Math.Min(100, r.Content.Length)]}");
            }

            if (!resultsList.Any()) {
                _logger.LogWarning("❌ [SEARCH] Ничего не найдено");
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Вот что я помню из прошлого:");

            foreach (var result in resultsList) {
                var content = result.Content.Length > 300
                    ? result.Content[..300] + "..."
                    : result.Content;

                sb.AppendLine($"- [релевантность: {result.Score:F2}] {content}");
            }

            _logger.LogInformation($"✅ [SEARCH] Возвращаю контекст длиной {sb.Length}");
            return sb.ToString();
        }
        catch (Exception ex) {
            _logger.LogError(ex, $"❌ [SEARCH] Ошибка: {ex.Message}");
            return null;
        }
    }

    public void Dispose() {
        _embeddingClient?.Dispose();
    }
}

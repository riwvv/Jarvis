using Microsoft.Extensions.Logging;
using RAGSharp.Embeddings.Tokenizers;
using RAGSharp.IO;
using RAGSharp.RAG;
using RAGSharp.Stores;
using RAGSharp.Text;
using System.IO;
using System.Text;
using System.Text.Json;
using Jarvis.Interfaces;
using Jarvis.Models;
using Microsoft.Extensions.Configuration;
using Jarvis.Configuration;

namespace Jarvis.Services;

public class RagMemoryService : IRagMemoryService {
    private readonly ILogger<RagMemoryService> _logger;
    private readonly string _embeddingId;
    private readonly string _embeddingEndpoint;
    private readonly OllamaEmbeddingClient _embeddingClient;
    private readonly FileVectorStore _store;
    private readonly RagRetriever _retriever;
    private readonly string _vectorPath;

    private static readonly JsonSerializerOptions _jsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public RagMemoryService(IConfiguration configuration, ILogger<RagMemoryService> logger) {
        _logger = logger;

        _embeddingId = configuration.GetSection("AISettings").Get<AISettings>()!.EmbeddingModelId;
        _embeddingEndpoint = configuration.GetSection("AISettings").Get<AISettings>()!.EmbeddingEndpoint;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        Directory.CreateDirectory(appData);
        _vectorPath = Path.Combine(appData, "vectors.json");

        _embeddingClient = new OllamaEmbeddingClient(_embeddingId, _embeddingEndpoint, logger: _logger);
        _store = new FileVectorStore(_vectorPath);

        var tokenizer = new SharpTokenTokenizer("gpt-4");
        var splitter = new RecursiveTextSplitter(tokenizer, chunkSize: 200, chunkOverlap: 100);

        _retriever = new RagRetriever(embeddings: _embeddingClient, store: _store, splitter: splitter);

        _ = Task.Run(() => LoadExistingVectorsAsync());

        _logger.LogInformation("RagMemoryService инициализирован");
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
            _logger.LogDebug("Сохранён диалог в память");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при сохранении в память");
        }
    }

    public async Task<string?> SearchRelevantContextAsync(string userMessage, int topK = 3) {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        try {
            var results = await _retriever.Search(userMessage, topK);
            var resultsList = results.ToList();

            if (!resultsList.Any())
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("Вот что я помню из прошлого:");

            foreach (var result in resultsList) {
                var content = result.Content.Length > 300
                    ? result.Content[..300] + "..."
                    : result.Content;
                sb.AppendLine($"- [релевантность: {result.Score:F2}] {content}");
            }

            _logger.LogDebug($"Найдено {resultsList.Count} релевантных записей");
            return sb.ToString();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при поиске в памяти");
            return null;
        }
    }

    private async Task LoadExistingVectorsAsync() {
        try {
            if (!Directory.Exists(_vectorPath)) {
                _logger.LogDebug("Папка векторов не существует, пустая память");
                return;
            }

            var kbFilePath = Path.Combine(_vectorPath, "kb.json");
            if (!File.Exists(kbFilePath)) {
                _logger.LogDebug("Файл kb.json не найден, память пуста");
                return;
            }

            var json = await File.ReadAllTextAsync(kbFilePath);
            var documents = JsonSerializer.Deserialize<List<VectorDocument>>(json, _jsonOptions);

            if (documents != null && documents.Any()) {
                foreach (var doc in documents) {
                    var ragDocument = new Document(
                        source: doc.Source ?? "conversation",
                        content: doc.Content,
                        metadata: doc.Metadata ?? new Dictionary<string, string>()
                    );
                    await _retriever.AddDocumentAsync(ragDocument);
                }
                _logger.LogInformation($"Загружено {documents.Count} записей из памяти");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при загрузке памяти");
        }
    }

    public void Dispose() => _embeddingClient?.Dispose();
}
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RAGSharp.Embeddings;
using Jarvis.Models;

namespace Jarvis.Services;

public class OllamaEmbeddingClient : IEmbeddingClient, IDisposable {
    private readonly HttpClient _httpClient;
    private readonly string _defaultModel;
    private readonly string _endpoint;
    private readonly ILogger? _logger;

    public OllamaEmbeddingClient(string defaultModel, string endpoint, ILogger? logger = null) {
        _defaultModel = defaultModel;
        _endpoint = endpoint;
        _logger = logger;
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<float[]> GetEmbeddingAsync(string input, string? model = null) {
        var usedModel = model ?? _defaultModel;
        _logger?.LogDebug($"Запрос эмбеддинга для: {input}");

        var request = new { model = usedModel, prompt = input };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_endpoint}/api/embeddings", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Логируем сырой ответ (первые 200 символов)
        _logger?.LogDebug($"Ответ Ollama: {responseBody[..Math.Min(200, responseBody.Length)]}");

        if (!response.IsSuccessStatusCode) {
            _logger?.LogError($"Ошибка Ollama: {response.StatusCode}, {responseBody}");
            return Array.Empty<float>();
        }

        var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseBody);

        if (result?.Embedding == null || result.Embedding.Length == 0) {
            _logger?.LogError($"Получен пустой вектор. Ответ: {responseBody}");
            return Array.Empty<float>();
        }

        _logger?.LogInformation($"✅ Получен вектор длиной {result.Embedding.Length}");
        return result.Embedding;
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> inputs, string? model = null) {
        var results = new List<float[]>();

        foreach (var input in inputs) {
            var embedding = await GetEmbeddingAsync(input, model);
            results.Add(embedding);
        }

        return results;
    }

    public void Dispose() => _httpClient?.Dispose();
}
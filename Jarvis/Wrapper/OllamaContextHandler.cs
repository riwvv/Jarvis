using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Jarvis.Wrapper;

public class OllamaContextHandler : DelegatingHandler {
    private readonly int _contextLength;

    public OllamaContextHandler(int contextLength = 16384)  // 16K для RAG
    {
        _contextLength = contextLength;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        // Перехватываем запрос к Ollama
        if (request.Content != null && request.RequestUri?.PathAndQuery.Contains("/chat/completions") == true) {
            var bodyJson = await request.Content.ReadAsStringAsync(cancellationToken);
            var bodyObj = JsonSerializer.Deserialize<Dictionary<string, object>>(bodyJson);

            if (bodyObj == null)
                throw new Exception("Ошибка получения контента");

            // Добавляем num_ctx в options
            if (!bodyObj.ContainsKey("options"))
                bodyObj["options"] = new Dictionary<string, object>();

            ((Dictionary<string, object>)bodyObj["options"])["num_ctx"] = _contextLength;

            // Обновляем тело запроса
            var newContent = new StringContent(
                JsonSerializer.Serialize(bodyObj),
                Encoding.UTF8,
                "application/json");
            request.Content = newContent;
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Jarvis.Wrapper;

public class OllamaContextHandler : DelegatingHandler {
    private readonly int _contextLength;
    private readonly ILogger<OllamaContextHandler>? _logger;

    public OllamaContextHandler(int contextLength = 16384, ILogger<OllamaContextHandler>? logger = null) {
        _contextLength = contextLength;
        _logger = logger;
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        try {
            // Перехватываем запрос к Ollama
            if (request.Content != null && request.RequestUri?.PathAndQuery.Contains("/chat/completions") == true) {
                var bodyJson = await request.Content.ReadAsStringAsync(cancellationToken);
                var bodyObj = JsonSerializer.Deserialize<Dictionary<string, object>>(bodyJson);

                if (bodyObj == null)
                    throw new InvalidOperationException("Ошибка десериализации контента запроса");

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
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested) {
            // Отмена по запросу пользователя (нормальное поведение)
            _logger?.LogDebug(ex, "Запрос к Ollama отменён (токен отмены)");
            throw; // Пробрасываем дальше для обработки на верхнем уровне
        }
        catch (TaskCanceledException ex) {
            // Таймаут HTTP клиента
            _logger?.LogWarning(ex, "Таймаут при запросе к Ollama");
            throw new TimeoutException("Превышен таймаут ожидания ответа от Ollama", ex);
        }
        catch (HttpRequestException ex) {
            // Сетевые ошибки (Ollama не запущен, отказано в соединении и т.д.)
            _logger?.LogError(ex, "Сетевая ошибка при запросе к Ollama");
            throw new InvalidOperationException("Не удалось соединиться с Ollama. Убедитесь, что Ollama запущен.", ex);
        }
        catch (JsonException ex) {
            // Ошибка парсинга JSON
            _logger?.LogError(ex, "Ошибка парсинга JSON при запросе к Ollama");
            throw new InvalidOperationException("Ошибка формата данных при обмене с Ollama", ex);
        }
        catch (Exception ex) {
            // Другие неожиданные ошибки
            _logger?.LogError(ex, "Неожиданная ошибка при запросе к Ollama");
            throw;
        }
    }
}
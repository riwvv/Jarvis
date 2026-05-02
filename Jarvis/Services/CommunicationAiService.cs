using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;

namespace Jarvis.Services;

public class CommunicationAiService : IDisposable {
    public event Action<string>? OnExecute;
    public event Action<string>? OnResult;

    private readonly IChatCompletionService _chat;
    private readonly ChatHistory _history;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly Kernel _kernel;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly IServiceProvider _serviceProvider;
    private readonly VectorMemoryService? _memoryService;

    public CommunicationAiService(IServiceProvider serviceProvider, VectorMemoryService? vectorMemoryService = null) {
        _serviceProvider = serviceProvider;
        _memoryService = vectorMemoryService;
        _kernel = _serviceProvider.GetRequiredService<Kernel>();
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = new();
        _settings = new OpenAIPromptExecutionSettings {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.1,
            MaxTokens = 256,
            TopP = 0.9,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.1
        };
        // Упрощённый промпт
        _history.AddSystemMessage("Ты Джарвис. Отвечай кратко, начинай с DONE/WARNING/ERROR. Если в истории есть сообщение System с информацией из памяти — используй её.");
    }

    public async Task<string?> GetRequestUser(string userQuery, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(userQuery)) {
            OnResult?.Invoke("ERROR: Пустой запрос");
            return null;
        }

        if (!await _semaphore.WaitAsync(0, cancellationToken)) {
            OnResult?.Invoke("WARNING: Предыдущий запрос еще обрабатывается");
            return null;
        }

        try {
            OnExecute?.Invoke("EXECUTE");
            Debug.WriteLine($"Отправка запроса к AI: {userQuery}");

            // 1. Поиск в памяти и добавление контекста
            if (_memoryService != null) {
                var longTermContext = await _memoryService.SearchRelevantContextAsync(userQuery);
                if (!string.IsNullOrEmpty(longTermContext)) {
                    // Добавляем как SystemMessage (самый приоритетный)
                    _history.Insert(0, new ChatMessageContent(AuthorRole.System, longTermContext));
                    Debug.WriteLine("Добавлен контекст из памяти в начало истории");
                }
            }

            // 2. Добавляем вопрос пользователя
            _history.AddUserMessage(userQuery);

            // 3. Диагностика: выводим контекст
            Debug.WriteLine("=== КОНТЕКСТ ПЕРЕД ОТПРАВКОЙ ===");
            foreach (var message in _history) {
                var content = message.Content ?? "";
                Debug.WriteLine($"[{message.Role}]: {content.Substring(0, Math.Min(150, content.Length))}");
            }

            // 4. Запрос к модели
            var response = await _chat.GetChatMessageContentAsync(_history, _settings, _kernel, cancellationToken);

            if (response != null && !string.IsNullOrEmpty(response.Content)) {
                _history.AddAssistantMessage(response.Content);

                // Сохраняем только успешные ответы
                if (_memoryService != null && !response.Content.StartsWith("ERROR")) {
                    await _memoryService.SaveMemoryAsync(userQuery, response.Content);
                    Debug.WriteLine("Диалог сохранён в память");
                }

                string status = ExtractStatusFromResponse(response.Content);
                OnResult?.Invoke(status);
                Debug.WriteLine($"Ответ AI: {response.Content}");
                return response.Content;
            }

            OnResult?.Invoke("ERROR: Модель вернула пустой ответ");
            return null;
        }
        catch (OperationCanceledException) {
            OnResult?.Invoke("WARNING: Запрос был отменен");
            return null;
        }
        catch (Exception ex) {
            OnResult?.Invoke($"ERROR: {ex.Message}");
            Debug.WriteLine(ex.Message);
            return null;
        }
        finally {
            _semaphore.Release();
        }
    }

    private string ExtractStatusFromResponse(string response) {
        if (string.IsNullOrEmpty(response)) return "DONE";
        string upper = response.ToUpperInvariant();
        if (upper.StartsWith("DONE")) return "DONE";
        if (upper.StartsWith("WARNING")) return "WARNING";
        if (upper.StartsWith("ERROR")) return "ERROR";
        return "DONE";
    }

    public void Dispose() => _semaphore?.Dispose();
}
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Jarvis.Services;

public class CommunicationAiService : IDisposable {
    public event Action<string>? OnExecute;
    public event Action<string>? OnResult;

    private readonly ILogger<CommunicationAiService> _logger;
    private readonly IChatCompletionService _chat;
    private readonly Kernel _kernel;
    private readonly ChatHistory _history;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TrayService? _trayService;

    private readonly string _systemPrompt = @"Ты — Джарвис, голосовой ассистент для Windows.

        У тебя есть инструменты (плагины) для выполнения действий:
        - ApplicationPlugin: запуск программ и игр
        - BrowserPlugin: открытие сайтов
        - SystemAudioPlugin: управление громкостью
        - MediaPlayerPlugin: усправление музыкой (пауза/далее, следующая/предыдущая, информация о текущей)
        - SystemCommandPlugin: системные команды (свернуть окна, блокировка, выключение)
        - FilePlugin: работа с файлами
        - PrankPlugin: шутки
        - RagPlugin: долговременная память (SearchMemory, SaveToMemory)

        ## ВАЖНО: ПРАВИЛО СОХРАНЕНИЯ В ПАМЯТЬ
        После успешного выполнения ЛЮБОГО действия ты ОБЯЗАН вызвать RagPlugin.SaveToMemory, чтобы запомнить что сделал.

        Пример правильной последовательности:
        1. Пользователь: 'открой ютуб'
        2. Ты вызываешь BrowserPlugin.OpenUrl
        3. Получаешь результат 'Открыто: https://youtube.com'
        4. Ты вызываешь RagPlugin.SaveToMemory с параметрами:
           - userAction: 'открой ютуб'
           - assistantResponse: 'DONE: Открыл YouTube'

        ## КОГДА НУЖНО СОХРАНЯТЬ:
        ✅ Команда выполнена успешно → сохранить
        ✅ Пользователь сообщил факт о себе ('меня зовут') → сохранить
        ❌ Ошибка или предупреждение → НЕ сохранять
        ❌ Вопрос о прошлом ('о чем я просил') → НЕ сохранять

        ## КОГДА НУЖНО ИСКАТЬ В ПАМЯТИ:
        - 'о чем я тебя просил' → SearchMemory
        - 'как меня зовут' → SearchMemory
        - 'что я делал вчера' → SearchMemory

        Формат ответа: DONE:/WARNING:/ERROR: + сообщение

        Так же в ответе никогда не используй эмодзи и старайся сделать ответ кратким. Ответ может быть развёрнутым только если пользователь попросил о чём то рассказать.

        Запомни: ЛЮБОЕ успешное действие должно быть сохранено в память через SaveToMemory!";

    public CommunicationAiService(TrayService trayService, Kernel kernel, ILogger<CommunicationAiService> logger) {
        _logger = logger;
        _trayService = trayService;
        _kernel = kernel;

        OnResult += (status) => _trayService.HideOverlayAfterCommand();

        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = new();
        _settings = new OpenAIPromptExecutionSettings {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.5,
            MaxTokens = 1024,
            TopP = 0.8,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.1
        };

        _history.AddSystemMessage(_systemPrompt);
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
            _trayService?.CommandReceived();
            OnExecute?.Invoke("EXECUTE");
            _logger.LogInformation($"Запрос: {userQuery}");

            // Формируем историю с текущим запросом
            var currentHistory = new ChatHistory();
            foreach (var msg in _history)
                currentHistory.Add(msg);
            currentHistory.AddUserMessage(userQuery);

            // Отправляем запрос (LLM сама решит, вызывать ли RagPlugin)
            var response = await _chat.GetChatMessageContentAsync(currentHistory, _settings, _kernel, cancellationToken);
            var responseContent = response?.Content ?? string.Empty;

            // Сохраняем диалог в историю
            _history.AddUserMessage(userQuery);
            _history.AddAssistantMessage(responseContent);

            string status = ExtractStatusFromResponse(responseContent);
            OnResult?.Invoke(status);
            _logger.LogInformation($"Ответ: {responseContent}");
            return responseContent;
        }
        catch (OperationCanceledException) {
            OnResult?.Invoke("WARNING: Запрос отменён");
            return null;
        }
        catch (Exception ex) {
            OnResult?.Invoke($"ERROR: {ex.Message}");
            _logger.LogError(ex.Message);
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
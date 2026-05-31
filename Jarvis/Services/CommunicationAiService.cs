using Jarvis.Interfaces;
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
    private readonly IRagMemoryService? _memoryService;
    private readonly ChatHistory _history;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TrayService? _trayService;

    private readonly string _systemPrompt = @"Ты — Джарвис, голосовой ассистент для Windows. Работаешь полностью локально.

ТВОЯ ГЛАВНАЯ ЗАДАЧА:
Выполнять голосовые команды пользователя через вызов функций.

ПРАВИЛА ФОРМАТА И СТАТУСА (САМОЕ ВАЖНОЕ):
1. Всегда начинай ответ ровно с одного из трёх слов:
   - DONE — если команда успешно выполнена.
   - WARNING — если команда выполнена частично или есть нюанс.
   - ERROR — если команда не выполнена или непонятна.
2. После статуса ставь пробел и пиши краткий ответ на русском.

ПАМЯТЬ (ВАЖНО!):
Твои ответы могут содержать блок с информацией из прошлых диалогов. Он выглядит так:
'Вот что я помню из прошлого: ...'

ЭТОТ БЛОК — ТВОЯ ДОЛГОВРЕМЕННАЯ ПАМЯТЬ. ИСПОЛЬЗУЙ ЕГО!
- Если пользователь спрашивает о прошлом (например, 'о чем я тебя просил'), ОБЯЗАТЕЛЬНО посмотри в этот блок.
- Если там есть информация — используй её для ответа.
- Если там пусто — честно скажи: 'ERROR Я не помню этого разговора.'

СИСТЕМНЫЕ ДЕЙСТВИЯ (ЧТО ТЫ УМЕЕШЬ):
- Открывать приложения и сайты.
- Управлять громкостью.
- Отвечать на вопросы, используя память.

СТИЛЬ ОТВЕТА:
- Кратко и по делу. Только суть.

ТЕПЕРЬ ВЫПОЛНИ КОМАНДУ ПОЛЬЗОВАТЕЛЯ.";

    public CommunicationAiService(TrayService trayService, Kernel kernel, ILogger<CommunicationAiService> logger, IRagMemoryService? ragMemoryService = null) {
        _logger = logger;

        _trayService = trayService;
        OnResult += (status) => _trayService.HideOverlayAfterCommand();

        _memoryService = ragMemoryService;
        _kernel = kernel;

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
            _logger.LogInformation($"Отправка запроса к AI: {userQuery}");

            // ========== ОПТИМИЗАЦИЯ: Запускаем поиск в памяти параллельно ==========
            // Запускаем задачу поиска в памяти (не ждём её завершения)
            Task<string?>? memoryTask = null;
            if (_memoryService != null) {
                memoryTask = _memoryService.SearchRelevantContextAsync(userQuery);
                _logger.LogDebug("Запущен параллельный поиск в памяти");
            }

            // ========== ОПТИМИЗАЦИЯ: Сразу добавляем вопрос пользователя в историю ==========
            // Не ждём память, чтобы LLM могла начать обработку
            _history.AddUserMessage(userQuery);

            // ========== ОПТИМИЗАЦИЯ: Диагностика контекста (быстрая) ==========
            _logger.LogInformation("=== КОНТЕКСТ ПЕРЕД ОТПРАВКОЙ ===");
            foreach (var message in _history) {
                var content = message.Content ?? "";
                // Оптимизация: не создаём подстроку, если не нужно
                if (content.Length > 150) {
                    _logger.LogInformation($"[{message.Role}]: {content.AsSpan(0, 150)}...");
                }
                else {
                    _logger.LogInformation($"[{message.Role}]: {content}");
                }
            }

            // ========== ОПТИМИЗАЦИЯ: Запрос к модели и ожидание памяти ПАРАЛЛЕЛЬНО ==========
            // Запускаем запрос к LLM (это самый долгий этап)
            var llmTask = _chat.GetChatMessageContentAsync(_history, _settings, _kernel, cancellationToken);

            // Ждём завершения И LLM, И поиска памяти (если он был запущен)
            if (memoryTask != null)
                await Task.WhenAll(llmTask, memoryTask);
            else
                await llmTask;

            var response = await llmTask;
            var longTermContext = memoryTask?.Result;

            // ========== ОПТИМИЗАЦИЯ: Добавляем контекст памяти ПОСЛЕ ответа LLM ==========
            // Важное замечание: контекст памяти будет использован в СЛЕДУЮЩЕМ запросе
            if (!string.IsNullOrEmpty(longTermContext)) {
                // Очищаем старые контексты
                var oldMemoryMessages = _history.Where(m =>
                    m.Role == AuthorRole.System &&
                    m.Content != null &&
                    m.Content.Contains("Вот что я помню из прошлого")
                ).ToList();

                foreach (var old in oldMemoryMessages)
                    _history.Remove(old);

                // Добавляем новый контекст
                _history.Insert(0, new ChatMessageContent(AuthorRole.System, longTermContext));

                // 🔥 Добавляем явное напоминание для LLM
                _history.Insert(1, new ChatMessageContent(AuthorRole.System,
                    "ВНИМАНИЕ: Выше приведена информация из долговременной памяти. Используй её для ответа на вопрос пользователя, если это уместно."));

                _logger.LogInformation("✅ [RAG] Контекст памяти добавлен в историю");
            }

            if (response != null && !string.IsNullOrEmpty(response.Content)) {
                _history.AddAssistantMessage(response.Content);

                // Сохраняем только успешные ответы
                if (_memoryService != null && !response.Content.StartsWith("ERROR")) {
                    await _memoryService.SaveMemoryAsync(userQuery, response.Content);
                    _logger.LogInformation("Диалог сохранён в память");
                }

                string status = ExtractStatusFromResponse(response.Content);
                OnResult?.Invoke(status);
                _logger.LogInformation($"Ответ AI: {response.Content}");
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
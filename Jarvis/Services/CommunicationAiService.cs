using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Jarvis.Services;

public class CommunicationAiService : IDisposable {
    public event Action<string>? OnExecute;
    public event Action<string>? OnResult;

    private readonly IChatCompletionService _chat;
    private readonly ChatHistory _history;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly Kernel _kernel;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<CommunicationAiService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly VectorMemoryService? _memoryService;

    public CommunicationAiService(IServiceProvider serviceProvider, ILogger<CommunicationAiService> logger, VectorMemoryService? vectorMemoryService = null) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryService = vectorMemoryService;
        _kernel = _serviceProvider.GetRequiredService<Kernel>();
        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = new();
        _settings = new OpenAIPromptExecutionSettings {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.1,
            MaxTokens = 1800,
            TopP = 0.9,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.1
        };

        _history.AddSystemMessage("Ты — Джарвис, голосовой ассистент для Windows. Работаешь полностью локально.\r\n\r\nТВОЯ ГЛАВНАЯ ЗАДАЧА:\r\nВыполнять голосовые команды пользователя через вызов функций. Используй информацию из памяти (раздел System), чтобы отвечать на вопросы о прошлом.\r\n\r\nПРАВИЛА ФОРМАТА И СТАТУСА (САМОЕ ВАЖНОЕ):\r\n1. Всегда начинай ответ ровно с одного из трёх слов:\r\n   - DONE — если команда успешно выполнена.\r\n   - WARNING — если команда выполнена частично или есть нюанс.\r\n   - ERROR — если команда не выполнена или непонятна.\r\n2. После статуса ставь пробел и пиши краткий ответ на русском.\r\n3. НИКОГДА не показывай JSON функции как пример. Ты должен ВЫЗЫВАТЬ функцию.\r\n4. НИКОГДА не объясняй, что ты собираешься сделать. Сразу делай.\r\n\r\nПРИМЕРЫ ПРАВИЛЬНОГО ОТВЕТА:\r\n- DONE Открыл Chrome.\r\n- DONE Громкость установлена на 50%.\r\n- WARNING Не нашел программу \"Photoshop\", но открыл Paint.\r\n- ERROR Не понял команду.\r\n\r\nПАМЯТЬ (RAG):\r\nВ истории сообщений есть блок \"System с информацией из памяти\". Он содержит прошлые диалоги.\r\n- Если пользователь спрашивает о прошлом (например, \"Как меня зовут?\"), используй эту информацию для ответа.\r\n- Если памяти по теме нет, честно скажи: ERROR Я не помню этого разговора.\r\n- Не выдумывай то, чего нет в памяти.\r\n\r\nСИСТЕМНЫЕ ДЕЙСТВИЯ (ЧТО ТЫ УМЕЕШЬ):\r\n- Открывать приложения и сайты.\r\n- Управлять громкостью (проценты).\r\n- Сворачивать окна.\r\n- Отвечать на вопросы, используя память.\r\n- Выполнять другие функции, которые переданы тебе через Semantic Kernel.\r\n\r\nСТИЛЬ ОТВЕТА:\r\n- Кратко и по делу. Без \"пожалуйста\" и лишних слов.\r\n- Голосовой ассистент — только суть.\r\n\r\nТЕПЕРЬ ВЫПОЛНИ КОМАНДУ ПОЛЬЗОВАТЕЛЯ.");
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
            _logger.LogInformation($"Отправка запроса к AI: {userQuery}");

            // 1. Поиск в памяти и добавление контекста
            if (_memoryService != null) {
                var longTermContext = await _memoryService.SearchRelevantContextAsync(userQuery);
                if (!string.IsNullOrEmpty(longTermContext)) {
                    // Удаляем предыдущие системные сообщения с памятью (начинаются с "Вот что я помню")
                    var oldMemoryMessages = _history.Where(m =>
                        m.Role == AuthorRole.System &&
                        m.Content != null &&
                        m.Content.StartsWith("Вот что я помню из прошлого")
                    ).ToList();

                    foreach (var old in oldMemoryMessages) {
                        _history.Remove(old);
                    }

                    // Добавляем новый контекст в начало
                    _history.Insert(0, new ChatMessageContent(AuthorRole.System, longTermContext));
                    _logger.LogInformation("Добавлен контекст из памяти (старые удалены)");
                }
            }

            // 2. Добавляем вопрос пользователя
            _history.AddUserMessage(userQuery);

            // 3. Диагностика: выводим контекст
            _logger.LogInformation("=== КОНТЕКСТ ПЕРЕД ОТПРАВКОЙ ===");
            foreach (var message in _history) {
                var content = message.Content ?? "";
                _logger.LogInformation($"[{message.Role}]: {content.Substring(0, Math.Min(150, content.Length))}");
            }

            // 4. Запрос к модели
            var response = await _chat.GetChatMessageContentAsync(_history, _settings, _kernel, cancellationToken);

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
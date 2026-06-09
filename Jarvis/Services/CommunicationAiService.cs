using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Jarvis.Interfaces;

namespace Jarvis.Services;

public class CommunicationAiService : IDisposable {
    public event Action<string>? OnExecute;
    public event Action<string>? OnResult;

    private readonly ILogger<CommunicationAiService> _logger;
    private readonly IRagMemoryService _memoryService;
    private readonly IChatCompletionService _chat;
    private readonly Kernel _kernel;
    private readonly ChatHistory _history;
    private readonly OpenAIPromptExecutionSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TrayService? _trayService;
    private readonly Dictionary<string, Func<string, Task<string>>> _fastCommands;
    
    private const int REMIDER_INTERVAL = 10;
    private int _messageCount = 0;

    private readonly string _systemPrompt = @$"Ты — Джарвис, голосовой ассистент для Windows.

        У тебя есть инструменты (плагины) для выполнения действий:
        - ApplicationPlugin: запуск программ и игр
        - BrowserPlugin: открытие сайтов
        - SystemAudioPlugin: управление громкостью
        - MediaPlayerPlugin: управление музыкой
        - SystemCommandPlugin: системные команды
        - FilePlugin: работа с файлами
        - PrankPlugin: шутки
        - RagPlugin: долговременная память
        - ReminderPlugin: создание/удаление временных и переодических напоминаний
        - WeatherPlugin: погода

        ## КОГДА НУЖНО ИСКАТЬ В ПАМЯТИ (RagPlugin.SearchMemory):
        - вопросы о прошлом: 'о чём я тебя просил', 'что я делал вчера', 'как меня зовут'
        - если пользователь спрашивает о предыдущих командах

        ## ПРАВИЛА:
        1. Всегда используй плагины для выполнения действий. НЕ ОПИСЫВАЙ, ЧТО НАДО СДЕЛАТЬ — ВЫЗОВИ ПЛАГИН!
        2. Формат ответа: DONE: / WARNING: / ERROR: + сообщение о результате
        3. Не используй эмодзи. Будь кратким. Ответ может быть развёрнутым только если пользователь попросил о чём-то рассказать.
        4. Так же помни, сейча {DateTime.Now.ToString("D")}";

    public CommunicationAiService(TrayService trayService, Kernel kernel, IRagMemoryService memoryService, ILogger<CommunicationAiService> logger) {
        _logger = logger;
        _memoryService = memoryService;
        _trayService = trayService;
        _kernel = kernel;

        OnResult += (status) => _trayService.HideOverlayAfterCommand();

        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = [];
        _settings = new OpenAIPromptExecutionSettings {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.45,
            MaxTokens = 1024,
            TopP = 0.8,
            FrequencyPenalty = 0.1,
            PresencePenalty = 0.1
        };

        _history.AddSystemMessage(_systemPrompt);

        _fastCommands = new Dictionary<string, Func<string, Task<string>>>(StringComparer.OrdinalIgnoreCase) {
            ["сверни все окна"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "MinimizeAllWindows");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Окна свернуты";
            },

            ["открой диспетчер задач"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "OpenTaskManager");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Диспетчер задач открыт";
            },

            ["выключи компьютер"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "Shutdown");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Компьютер выключается";
            },

            ["перезагрузи компьютер"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "Restart");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Компьютер перезагружается";
            },

            ["отмени выключение"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "CancelShutdown");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Выключение отменено";
            },

            ["заблокируй экран"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemCommandPlugin", "LockScreen");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Экран заблокирован";
            },

            ["создай список приложений"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("ApplicationPlugin", "CreateAppListFile");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Список приложений создан";
            },

            ["открой гугл"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("BrowserPlugin", "OpenUrl");
                var args = new KernelArguments { ["url"] = "https://google.com" };
                var result = await _kernel.InvokeAsync(function, args);
                return result.GetValue<string>() ?? "Google открыт";
            },

            ["открой ютуб"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("BrowserPlugin", "OpenUrl");
                var args = new KernelArguments { ["url"] = "https://youtube.com" };
                var result = await _kernel.InvokeAsync(function, args);
                return result.GetValue<string>() ?? "YouTube открыт";
            },

            ["что играет"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MediaPlayerPlugin", "GetCurrentTrackInfo");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Ничего не играет";
            },

            ["продолжи"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MediaPlayerPlugin", "PlayPause");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Воспроизведение возобновлено";
            },

            ["дальше"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MediaPlayerPlugin", "NextTrack");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Следующий трек";
            },

            ["предыдущую"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MediaPlayerPlugin", "PreviousTrack");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Предыдущий трек";
            },

            ["пауза"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MediaPlayerPlugin", "Stop");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Пауза";
            },

            ["давай пошалим"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("PrankPlugin", "OpenJoke");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? ":)";
            },

            ["какая погода"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("WeatherPlugin", "GetWeatherAtCurrentLocation");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Погода не найдена";
            },

            ["погода"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("WeatherPlugin", "GetWeatherAtCurrentLocation");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Погода не найдена";
            },

            ["какая погода завтра"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("WeatherPlugin", "GetTomorrowForecastAtCurrentLocation");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Погода не найдена";
            },

            ["погода завтра"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("WeatherPlugin", "GetTomorrowForecastAtCurrentLocation");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Погода не найдена";
            },

            ["выключи звук"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemAudioPlugin", "VolumeTurnOff");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Звук выключен";
            }
        };
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

            foreach (var (key, action) in _fastCommands) {
                if (userQuery.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                    _logger.LogInformation($"Быстрая команда: {key}");
                    var result = await action(userQuery);
                    OnResult?.Invoke("DONE");
                    
                    _ = Task.Run(async () => {
                        try {
                            await _memoryService.SaveMemoryAsync(userQuery, result);
                        }
                        catch (Exception ex) {
                            _logger.LogDebug($"Автосохранение пропущено: {ex.Message}");
                        }
                    });

                    return $"DONE: {result}";
                }
            }

            _messageCount++;
            if (_messageCount >= REMIDER_INTERVAL) {
                _messageCount = 0;
                _history.AddAssistantMessage("Напоминаю: у меня есть плагины ApplicationPlugin, BrowserPlugin, MediaPlayerPlugin и другие. Используй их для выполнения действий.");
            }

            var currentHistory = new ChatHistory();
            foreach (var msg in _history)
                currentHistory.Add(msg);
            currentHistory.AddUserMessage(userQuery);

            var response = await _chat.GetChatMessageContentAsync(currentHistory, _settings, _kernel, cancellationToken);
            var responseContent = response?.Content ?? string.Empty;

            _history.AddUserMessage(userQuery);
            _history.AddAssistantMessage(responseContent);

            while (_history.Count > 11)
                _history.RemoveAt(1);

            string status = ExtractStatusFromResponse(responseContent);
            OnResult?.Invoke(status);
            _logger.LogInformation($"Ответ: {responseContent}");

            if (!string.IsNullOrWhiteSpace(userQuery) && responseContent.StartsWith("DONE")) {
                var query = userQuery;
                var resp = responseContent;

                _ = Task.Run(async () => {
                    try {
                        await _memoryService.SaveMemoryAsync(query, resp);
                        _logger.LogDebug("Автосохранение: {Query}", query.Length > 50 ? query[..50] + "..." : query);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Ошибка автосохранения");
                    }
                });
            }

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

    private static string ExtractStatusFromResponse(string response) {
        if (string.IsNullOrEmpty(response)) return "DONE";
        string upper = response.ToUpperInvariant();
        if (upper.StartsWith("DONE")) return "DONE";
        if (upper.StartsWith("WARNING")) return "WARNING";
        if (upper.StartsWith("ERROR")) return "ERROR";
        return "DONE";
    }

    public void Dispose() => _semaphore?.Dispose();
}
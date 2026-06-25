using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Text.Json;
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
    private readonly OllamaPromptExecutionSettings _settings;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TrayService? _trayService;
    private readonly TaskCompletionSource<bool> _initializationTcs = new();
    private Dictionary<string, Func<string, Task<string>>>? _fastCommands;

    // Храним результаты действий агента
    private readonly List<ActionResult> _actionResults = [];
    private string? _lastActionResult;

    private const int REMIDER_INTERVAL = 10;
    private int _messageCount = 0;

    private readonly string _systemPrompt = @$"Ты — Джарвис, голосовой ИИ агент для Windows.

        Используй доступные плагины для выполнения действий. Не описывай, ЧТО надо сделать — вызови плагин.
        Если для достижения цели нужно выполнить несколько действий — спланируй и выполни их последовательно.

        ## КОГДА НУЖНО ИСКАТЬ В ПАМЯТИ (RagPlugin.SearchMemory):
        - вопросы о прошлом: 'о чём я тебя просил', 'что я делал вчера', 'как меня зовут'
        - если пользователь спрашивает о предыдущих командах

        ## ПРАВИЛА:
        1. Формат ответа: DONE: / WARNING: / ERROR: + сообщение о результате.
        2. Не используй эмодзи. Будь кратким. Ответ может быть развёрнутым только если пользователь попросил о чём-то рассказать.
        3. Так же в ответе старайся избегать сложные/длинные названия файлов или путей, помни что твой ответ озвучивается пользователю.
        
        ## ВАЖНО:
        - Когда плагин возвращает JSON, используй поле 'message' для ответа пользователю.
        - Не включай JSON напрямую в речь.

        Текущая дата: {DateTime.Now:D}";

    public CommunicationAiService(
        TrayService trayService,
        Kernel kernel,
        IRagMemoryService memoryService,
        ILogger<CommunicationAiService> logger) {
        _logger = logger;
        _memoryService = memoryService;
        _trayService = trayService;
        _kernel = kernel;

        OnResult += (status) => _trayService.HideOverlayAfterCommand();

        _chat = _kernel.GetRequiredService<IChatCompletionService>();
        _history = [];
        _settings = new OllamaPromptExecutionSettings {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.6333f,
            TopP = 0.8f,
            TopK = 20,
            NumPredict = 8192
        };

        _history.AddSystemMessage(_systemPrompt);

        InitializeFastCommands();

        _initializationTcs.TrySetResult(true);
    }

    public async Task WaitForInitializationComplete() => await _initializationTcs.Task;

    public async Task<string?> GetRequestUser(string userQuery, CancellationToken cancellationToken = default) {
        _logger.LogInformation($"🚀 НАЧАЛО ОБРАБОТКИ: {userQuery}");

        if (string.IsNullOrWhiteSpace(userQuery)) {
            _logger.LogWarning("❌ ПУСТОЙ ЗАПРОС");
            OnResult?.Invoke("ERROR: Пустой запрос");
            return null;
        }

        if (!await _semaphore.WaitAsync(0, cancellationToken)) {
            _logger.LogWarning("⏳ ПРЕДЫДУЩИЙ ЗАПРОС ЕЩЁ ОБРАБАТЫВАЕТСЯ");
            OnResult?.Invoke("WARNING: Предыдущий запрос еще обрабатывается");
            return null;
        }

        try {
            _trayService?.CommandReceived();
            OnExecute?.Invoke("EXECUTE");
            _logger.LogInformation($"📝 ЗАПРОС: {userQuery}");

            // Очищаем историю действий для новой команды
            _actionResults.Clear();
            _lastActionResult = null;

            // 1. Проверяем быстрые команды
            _logger.LogInformation($"🔍 ПРОВЕРКА БЫСТРЫХ КОМАНД...");
            if (_fastCommands != null && _fastCommands.Count != 0) {
                foreach (var (key, action) in _fastCommands) {
                    if (userQuery.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                        _logger.LogInformation($"⚡ БЫСТРАЯ КОМАНДА: {key}");
                        var result = await action(userQuery);

                        // Сохраняем результат
                        var actionResult = new ActionResult {
                            Plugin = key,
                            Result = result,
                            Message = ExtractMessageFromJson(result)
                        };
                        _actionResults.Add(actionResult);
                        _lastActionResult = result;

                        _logger.LogInformation($"💾 РЕЗУЛЬТАТ СОХРАНЁН: {actionResult.Message} (действий: {_actionResults.Count})");

                        OnResult?.Invoke("DONE");

                        _ = Task.Run(async () => {
                            try {
                                await _memoryService.SaveMemoryAsync(userQuery, result);
                            }
                            catch (Exception ex) {
                                _logger.LogError($"❌ АВТОСОХРАНЕНИЕ ПРОПУЩЕНО: {ex.Message}");
                            }
                        });

                        var finalResponse = $"DONE: {actionResult.Message}";
                        return finalResponse;
                    }
                }
            }
            _logger.LogInformation($"⏭️ БЫСТРЫЕ КОМАНДЫ НЕ ПОДОШЛИ");

            // 2. Напоминание о плагинах
            _messageCount++;
            if (_messageCount >= REMIDER_INTERVAL) {
                _messageCount = 0;
                _history.AddAssistantMessage("Напоминаю: у меня есть плагины ApplicationPlugin, BrowserPlugin, MediaPlayerPlugin и другие. Используй их для выполнения действий.");
            }

            // 3. Запрос к LLM
            _logger.LogInformation($"🧠 ЗАПРОС К LLM... (история: {_history.Count} сообщений)");
            var currentHistory = new ChatHistory();
            foreach (var msg in _history)
                currentHistory.Add(msg);
            currentHistory.AddUserMessage(userQuery);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(150));

            var response = await _chat.GetChatMessageContentAsync(currentHistory, _settings, _kernel, cts.Token);
            var responseContent = response?.Content ?? string.Empty;

            _logger.LogInformation($"📨 ОТВЕТ ОТ LLM: {(string.IsNullOrEmpty(responseContent) ? "ПУСТОЙ" : $"'{responseContent}'")}");

            // 4. Обработка ответа LLM
            responseContent = await ProcessResponseContent(responseContent, userQuery);

            // 5. Обновляем историю
            _history.AddUserMessage(userQuery);
            if (!string.IsNullOrWhiteSpace(responseContent)) {
                _history.AddAssistantMessage(responseContent);
            }

            // Ограничиваем историю
            while (_history.Count > 7) {
                _history.RemoveAt(1);
                _logger.LogInformation($"🗑️ УДАЛЕНО СТАРОЕ СООБЩЕНИЕ ИЗ ИСТОРИИ (осталось {_history.Count})");
            }

            // 6. Извлекаем статус и логируем
            string status = ExtractStatusFromResponse(responseContent);
            _logger.LogInformation($"🏷️ СТАТУС: {status} | ДЕЙСТВИЙ: {_actionResults.Count} | ОТВЕТ: {responseContent}");
            OnResult?.Invoke(status);

            // 7. Сохраняем в RAG
            if (!string.IsNullOrWhiteSpace(userQuery) && IsSuccessfulResponse(responseContent)) {
                _ = Task.Run(async () => {
                    try {
                        await _memoryService.SaveMemoryAsync(userQuery, responseContent);
                    }
                    catch (Exception ex) {
                        _logger.LogError($"❌ RAG: Ошибка автосохранения: {ex.Message}");
                    }
                });
            }

            return responseContent;
        }
        catch (OperationCanceledException) {
            _logger.LogWarning($"⏹️ ЗАПРОС ОТМЕНЁН");
            OnResult?.Invoke("WARNING: Запрос отменён");

            if (_actionResults.Count != 0) {
                var fallback = _actionResults.Last().Message;
                _logger.LogWarning($"🔄 ИСПОЛЬЗОВАН ПОСЛЕДНИЙ РЕЗУЛЬТАТ: {fallback}");
                return $"DONE: {fallback}";
            }
            return null;
        }
        catch (Exception ex) {
            _logger.LogError($"💥 ИСКЛЮЧЕНИЕ: {ex.Message}");
            OnResult?.Invoke($"ERROR: {ex.Message}");

            if (_actionResults.Count != 0) {
                var fallback = _actionResults.Last().Message;
                _logger.LogWarning($"🔄 ИСПОЛЬЗОВАН ПОСЛЕДНИЙ РЕЗУЛЬТАТ: {fallback}");
                return $"DONE: {fallback}";
            }

            return null;
        }
        finally {
            _semaphore.Release();
        }
    }

    private async Task<string> ProcessResponseContent(string responseContent, string userQuery) {
        // Проверяем, не является ли ответ результатом вызова плагина
        if (IsJsonResponse(responseContent)) {
            var message = ExtractMessageFromJson(responseContent);
            if (!string.IsNullOrEmpty(message)) {
                _actionResults.Add(new ActionResult {
                    Plugin = "LLM",
                    Result = responseContent,
                    Message = message
                });
                _logger.LogInformation($"📦 ОБНАРУЖЕН JSON, ИЗВЛЕЧЕНО СООБЩЕНИЕ: {message}");
                return message;
            }
        }

        // Если ответ пустой, но есть результаты действий
        if (string.IsNullOrWhiteSpace(responseContent) && _actionResults.Count != 0) {
            var lastResult = _actionResults.Last().Message;
            _logger.LogWarning($"⚠️ LLM ВЕРНУЛА ПУСТОЙ ОТВЕТ, ИСПОЛЬЗОВАН РЕЗУЛЬТАТ: {lastResult}");
            return lastResult;
        }

        // Если ответ невалидный, но есть результаты действий
        if (!string.IsNullOrWhiteSpace(responseContent) && !IsValidResponse(responseContent) && _actionResults.Count != 0) {
            var lastResult = _actionResults.Last().Message;
            _logger.LogWarning($"⚠️ LLM ВЕРНУЛА НЕВАЛИДНЫЙ ОТВЕТ, ИСПОЛЬЗОВАН РЕЗУЛЬТАТ: {lastResult}");
            return lastResult;
        }

        // Если ответ валидный, но начинается с префикса и содержит JSON
        if (responseContent.StartsWith("DONE:") || responseContent.StartsWith("WARNING:") || responseContent.StartsWith("ERROR:")) {
            var parts = responseContent.Split(' ', 2);
            if (parts.Length == 2) {
                var content = parts[1];
                if (IsJsonResponse(content)) {
                    var message = ExtractMessageFromJson(content);
                    if (!string.IsNullOrEmpty(message)) {
                        _actionResults.Add(new ActionResult {
                            Plugin = "LLM",
                            Result = content,
                            Message = message
                        });
                        return $"{parts[0]} {message}";
                    }
                }
            }
        }

        // Если ответ валидный
        if (!string.IsNullOrWhiteSpace(responseContent) && IsValidResponse(responseContent)) {
            _actionResults.Add(new ActionResult {
                Plugin = "LLM",
                Result = responseContent,
                Message = responseContent
            });
            return responseContent;
        }

        // Если ничего не подошло
        _logger.LogWarning($"⚠️ НЕ УДАЛОСЬ ОБРАБОТАТЬ ОТВЕТ: {responseContent}");
        return "WARNING: Команда не распознана. Пожалуйста, уточните запрос.";
    }

    #region Вспомогательные методы

    private static string ExtractMessageFromJson(string result) {
        if (string.IsNullOrWhiteSpace(result)) return result;

        if (IsJsonResponse(result)) {
            try {
                using var json = JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("message", out var messageProp)) {
                    return messageProp.GetString() ?? result;
                }
                if (json.RootElement.TryGetProperty("description", out var descProp)) {
                    return descProp.GetString() ?? result;
                }
            }
            catch { }
        }

        return result;
    }

    private static bool IsValidResponse(string response) {
        if (string.IsNullOrWhiteSpace(response)) return false;
        return response.StartsWith("DONE:") ||
               response.StartsWith("WARNING:") ||
               response.StartsWith("ERROR:") ||
               IsJsonResponse(response);
    }

    private static bool IsJsonResponse(string response) {
        if (string.IsNullOrWhiteSpace(response)) return false;
        var trimmed = response.Trim();
        return trimmed.StartsWith("{") && trimmed.EndsWith("}");
    }

    private static bool IsSuccessfulResponse(string response) {
        if (string.IsNullOrWhiteSpace(response)) return false;
        return response.StartsWith("DONE") || IsJsonResponse(response);
    }

    private static string ExtractStatusFromResponse(string response) {
        if (string.IsNullOrEmpty(response)) return "DONE";

        if (IsJsonResponse(response)) {
            try {
                using var json = JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("status", out var statusProp)) {
                    return statusProp.GetString() ?? "DONE";
                }
            }
            catch { }
        }

        string upper = response.ToUpperInvariant();
        if (upper.StartsWith("DONE")) return "DONE";
        if (upper.StartsWith("WARNING")) return "WARNING";
        if (upper.StartsWith("ERROR")) return "ERROR";
        return "DONE";
    }

    private class ActionResult {
        public string Plugin { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    #endregion

    private void InitializeFastCommands() {
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

            ["брось кубик"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MiniGamePlugin", "RollTheDice");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Перелёт";
            },

            ["брось монетку"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MiniGamePlugin", "FlipCoin");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Ой, встала на ребро";
            },

            ["случайное число"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("MiniGamePlugin", "RandomNumberToTen");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? $"Случайное число: {new Random().Next(1, 11)}";
            },

            ["выключи звук"] = async (_) => {
                var function = _kernel.Plugins.GetFunction("SystemAudioPlugin", "VolumeTurnOff");
                var result = await _kernel.InvokeAsync(function);
                return result.GetValue<string>() ?? "Звук выключен";
            }
        };
    }

    public void Dispose() => _semaphore?.Dispose();
}
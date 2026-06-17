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
    private readonly List<string> _actionResults = [];
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
        // === НАЧАЛО МЕТОДА ===
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
            _logger.LogInformation($"🧹 ИСТОРИЯ ДЕЙСТВИЙ ОЧИЩЕНА");

            // 1. Проверяем быстрые команды
            _logger.LogInformation($"🔍 ПРОВЕРКА БЫСТРЫХ КОМАНД...");
            if (_fastCommands != null && _fastCommands.Count != 0) {
                foreach (var (key, action) in _fastCommands) {
                    if (userQuery.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                        _logger.LogInformation($"⚡ БЫСТРАЯ КОМАНДА: {key}");
                        var result = await action(userQuery);

                        // Сохраняем результат действия
                        _lastActionResult = result;
                        _actionResults.Add(result);
                        _logger.LogInformation($"💾 РЕЗУЛЬТАТ СОХРАНЁН: {result} (количество действий: {_actionResults.Count})");

                        OnResult?.Invoke("DONE");

                        _ = Task.Run(async () => {
                            try {
                                await _memoryService.SaveMemoryAsync(userQuery, result);
                                _logger.LogInformation($"💾 АВТОСОХРАНЕНИЕ ВЫПОЛНЕНО");
                            }
                            catch (Exception ex) {
                                _logger.LogError($"❌ АВТОСОХРАНЕНИЕ ПРОПУЩЕНО: {ex.Message}");
                            }
                        });

                        var finalResponse = $"DONE: {result}";
                        _logger.LogInformation($"✅ ОТВЕТ (БЫСТРАЯ КОМАНДА): {finalResponse}");
                        return finalResponse;
                    }
                }
            }
            _logger.LogInformation($"⏭️ БЫСТРЫЕ КОМАНДЫ НЕ ПОДОШЛИ");

            // 2. Напоминание о плагинах (каждые 10 сообщений)
            _messageCount++;
            if (_messageCount >= REMIDER_INTERVAL) {
                _messageCount = 0;
                _history.AddAssistantMessage("Напоминаю: у меня есть плагины ApplicationPlugin, BrowserPlugin, MediaPlayerPlugin и другие. Используй их для выполнения действий.");
                _logger.LogInformation($"📢 ДОБАВЛЕНО НАПОМИНАНИЕ О ПЛАГИНАХ (счётчик {_messageCount})");
            }
            else {
                _logger.LogInformation($"📊 СЧЁТЧИК СООБЩЕНИЙ: {_messageCount}/{REMIDER_INTERVAL}");
            }

            // 3. Запрос к LLM
            _logger.LogInformation($"🧠 ЗАПРОС К LLM...");
            var currentHistory = new ChatHistory();
            foreach (var msg in _history)
                currentHistory.Add(msg);
            currentHistory.AddUserMessage(userQuery);
            _logger.LogInformation($"📚 ИСТОРИЯ СООБЩЕНИЙ: {_history.Count} сообщений");

            var response = await _chat.GetChatMessageContentAsync(currentHistory, _settings, _kernel, cancellationToken);
            var responseContent = response?.Content ?? string.Empty;

            _logger.LogInformation($"📨 ОТВЕТ ОТ LLM ПОЛУЧЕН: {(string.IsNullOrEmpty(responseContent) ? "ПУСТОЙ" : $"'{responseContent}'")}");

            // 4. Обработка ответа
            _logger.LogInformation($"🔄 ОБРАБОТКА ОТВЕТА...");
            _logger.LogInformation($"📊 СОСТОЯНИЕ: actionResults.Count={_actionResults.Count}, IsEmpty={string.IsNullOrWhiteSpace(responseContent)}");

            if (string.IsNullOrWhiteSpace(responseContent) && _actionResults.Count == 0) {
                // LLM не поняла команду и ничего не сделала
                _logger.LogWarning("⚠️ LLM ВЕРНУЛА ПУСТОЙ ОТВЕТ И НЕ ВЫПОЛНИЛА НИ ОДНОГО ДЕЙСТВИЯ");
                responseContent = "WARNING: Команда не распознана. Пожалуйста, уточните запрос.";
            }
            else if (string.IsNullOrWhiteSpace(responseContent) && _actionResults.Count != 0) {
                responseContent = _actionResults.Last();
                _logger.LogWarning($"⚠️ LLM ВЕРНУЛА ПУСТОЙ ОТВЕТ, ИСПОЛЬЗОВАН РЕЗУЛЬТАТ ПОСЛЕДНЕГО ДЕЙСТВИЯ: {responseContent}");
            }
            else if (!string.IsNullOrWhiteSpace(responseContent) && !IsValidResponse(responseContent) && _actionResults.Count != 0) {
                responseContent = _actionResults.Last();
                _logger.LogWarning($"⚠️ LLM ВЕРНУЛА НЕВАЛИДНЫЙ ОТВЕТ, ИСПОЛЬЗОВАН РЕЗУЛЬТАТ ДЕЙСТВИЯ: {responseContent}");
            }
            else {
                _logger.LogInformation($"✅ ОТВЕТ LLM КОРРЕКТНЫЙ: {responseContent}");
            }

            // 5. Обновляем историю
            _history.AddUserMessage(userQuery);
            if (!string.IsNullOrWhiteSpace(responseContent)) {
                _history.AddAssistantMessage(responseContent);
                _logger.LogInformation($"📝 ДОБАВЛЕНО В ИСТОРИЮ: {responseContent}");
            }
            else {
                _logger.LogWarning($"⚠️ ПУСТОЙ ОТВЕТ НЕ ДОБАВЛЕН В ИСТОРИЮ");
            }

            // Ограничиваем историю
            while (_history.Count > 11) {
                _history.RemoveAt(1);
                _logger.LogInformation($"🗑️ УДАЛЕНО СТАРОЕ СООБЩЕНИЕ ИЗ ИСТОРИИ (осталось {_history.Count})");
            }

            // 6. Извлекаем статус и логируем
            string status = ExtractStatusFromResponse(responseContent);
            _logger.LogInformation($"🏷️ СТАТУС: {status}");
            OnResult?.Invoke(status);
            _logger.LogInformation($"📤 ФИНАЛЬНЫЙ ОТВЕТ: {responseContent}");

            // 7. Сохраняем в RAG, если команда успешна
            if (!string.IsNullOrWhiteSpace(userQuery) && IsSuccessfulResponse(responseContent)) {
                var query = userQuery;
                var resp = responseContent;

                _ = Task.Run(async () => {
                    try {
                        await _memoryService.SaveMemoryAsync(query, resp);
                        _logger.LogInformation($"💾 RAG: Автосохранение: {query}");
                    }
                    catch (Exception ex) {
                        _logger.LogError($"❌ RAG: Ошибка автосохранения: {ex.Message}");
                    }
                });
            }

            _logger.LogInformation($"🏁 ЗАВЕРШЕНИЕ МЕТОДА. ВОЗВРАЩАЕТСЯ: {responseContent}");
            return responseContent;
        }
        catch (OperationCanceledException) {
            _logger.LogWarning($"⏹️ ЗАПРОС ОТМЕНЁН");
            OnResult?.Invoke("WARNING: Запрос отменён");
            return null;
        }
        catch (Exception ex) {
            _logger.LogError($"💥 ИСКЛЮЧЕНИЕ: {ex.Message}");
            OnResult?.Invoke($"ERROR: {ex.Message}");

            if (_actionResults.Any()) {
                var fallbackResult = _actionResults.Last();
                _logger.LogWarning($"🔄 ИСПОЛЬЗОВАН FALLBACK РЕЗУЛЬТАТ: {fallbackResult}");
                return $"DONE: {fallbackResult}";
            }

            return null;
        }
        finally {
            _semaphore.Release();
            _logger.LogInformation($"🔓 СЕМАФОР ОСВОБОЖДЁН");
        }
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
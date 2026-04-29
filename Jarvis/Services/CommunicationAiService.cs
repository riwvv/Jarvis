using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;

namespace Jarvis.Services {
    public class CommunicationAiService : IDisposable {
        public event Action<string>? OnExecute;
        public event Action<string>? OnResult;

        private readonly IChatCompletionService _chat;
        private readonly ChatHistory _history;
        private readonly OpenAIPromptExecutionSettings _settings;
        private readonly Kernel _kernel;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly string _systemPrompt = "Ты — голосовой ассистент Джарвис для Windows.У тебя есть доступ к системным функциям. Если пользователь просит выполнить действие (открыть сайт, запустить программу, свернуть окна), ты **обязан** вызвать соответствующую функцию из твоего списка инструментов. " +
                "Твои строгие правила:\n" +
                "1. Всегда на русском языке. Так же следуй правилу: каждый твой ответ должен начинаться на слово-состояние результата выполнения команды:" +
                "DONE - успешно выполнил команду;" +
                "WARNING - возникли небольшие проблемы или нужно уточнение;" +
                "ERROR - не можешь выполнить команду или команда вызвала исключение;\n" +
                "2. Запомни, ты можешь отвечать МАКСИМАЛЬНО КРАТКИМ текстом (1-2 предложения), но каждый ответ обязан начинаться на одно из этих слов по ситуации\n" +
                "3. НИКОГДА не выводи теги <tool_call> или JSON как обычный текст.";

        private readonly IServiceProvider _serviceProvider;

        public CommunicationAiService(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
            _kernel = _serviceProvider.GetRequiredService<Kernel>();
            _chat = _kernel.GetRequiredService<IChatCompletionService>();
            _history = new();
            _settings = new OpenAIPromptExecutionSettings {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = 0.1,
                MaxTokens = 256,
                TopP = 0.9
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
                OnExecute?.Invoke("EXECUTE");
                Debug.WriteLine($"Отправка запроса к AI: {userQuery}");

                _history.AddUserMessage(userQuery);

                cancellationToken.ThrowIfCancellationRequested();

                var response = await _chat.GetChatMessageContentAsync(
                    _history,
                    _settings,
                    _kernel,
                    cancellationToken);

                if (response != null && !string.IsNullOrEmpty(response.Content)) {
                    _history.AddAssistantMessage(response.Content);

                    string status = ExtractStatusFromResponse(response.Content);
                    OnResult?.Invoke(status);

                    Debug.WriteLine($"Получен ответ от AI: {response.Content}");
                    return response.Content;
                }

                OnResult?.Invoke("ERROR: Модель вернула пустой ответ");
                return null;
            }
            catch (OperationCanceledException) {
                OnResult?.Invoke("WARNING: Запрос был отменен");
                Debug.WriteLine("Запрос к AI отменен");
                return null;
            }
            catch (Exception ex) {
                var errorMsg = $"Ошибка при обращении к модели: {ex.Message}";
                OnResult?.Invoke($"ERROR: {errorMsg}");
                Debug.WriteLine(errorMsg);

                if (ex.StackTrace != null)
                    Debug.WriteLine(ex.StackTrace);

                return null;
            }
            finally {
                _semaphore.Release();
            }
        }

        private string ExtractStatusFromResponse(string response) {
            if (string.IsNullOrEmpty(response)) return "DONE";

            string upperResponse = response.ToUpperInvariant();

            if (upperResponse.StartsWith("DONE")) return "DONE";
            if (upperResponse.StartsWith("WARNING")) return "WARNING";
            if (upperResponse.StartsWith("ERROR")) return "ERROR";

            return "DONE";
        }

        public void Dispose() {
            _semaphore?.Dispose();
        }
    }
}
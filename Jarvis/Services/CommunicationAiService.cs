using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Jarvis.Services
{
    public class CommunicationAiService
    {
        public event Action<string>? OnExecute;
        public event Action<string>? OnResult;

        private readonly IChatCompletionService _chat;
        private readonly ChatHistory _history;
        private readonly OpenAIPromptExecutionSettings _settings;
        private readonly Kernel _kernel;
        private bool _isProcessing;

        public CommunicationAiService()
        {
            _chat = App.KernelCore.GetRequiredService<OpenAIChatCompletionService>();
            _kernel = App.KernelCore;
            _isProcessing = false;
            _history = new();
            _settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = 0.7,
                MaxTokens = 1024
            };
            _history.AddSystemMessage("Всегда на русском языке. Так же следуй правилу: каждый твой ответ должен начинаться на слово-состояние результата выполнения команды:" +
            "DONE - успешно выполнил команду;" +
            "WARNING - возникли небольшие проблемы или нужно уточнение;" +
            "ERROR - не можешь выполнить команду или команда вызвала исключение;" +
            "Запомни, ты можешь отвечать МАКСИМАЛЬНО КРАТКИМ текстом (1-2 предложения), но каждый ответ обязан начинаться на одно из этих слов по ситуации");
        }
        public IReadOnlyList<ChatMessageContent> GetChatHistory()
        {
            return _history.AsReadOnly();
        }


        public async Task<string> GetRequestUser(string userQuery, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
            {
                OnResult?.Invoke("ERROR: Пустой запрос");
                return null;
            }

            if (_isProcessing)
            {
                OnResult?.Invoke("WARNING: Предыдущий запрос еще обрабатывается");
                return null;
            }

            _isProcessing = true;

            try
            {
                // Оповещаем о начале выполнения
                OnExecute?.Invoke("EXECUTE");
                Debug.WriteLine($"Отправка запроса: {userQuery}");

                // Добавляем сообщение пользователя в историю
                _history.AddUserMessage(userQuery);

                // Проверяем отмену перед запросом
                cancellationToken.ThrowIfCancellationRequested();

                // Получаем ответ от модели
                var response = await _chat.GetChatMessageContentAsync(
                    _history,
                    _settings,
                    _kernel,
                    cancellationToken);

                // Проверяем ответ
                if (response != null && !string.IsNullOrEmpty(response.Content))
                {
                    // Добавляем ответ ассистента в историю
                    _history.AddAssistantMessage(response.Content);

                    // Оповещаем об успешном результате
                    OnResult?.Invoke("DONE");
                    Debug.WriteLine($"Получен ответ: {response.Content}");

                    return response.Content;
                }

                // Пустой ответ
                OnResult?.Invoke("ERROR: Модель вернула пустой ответ");
                return null;
            }
            catch (OperationCanceledException)
            {
                OnResult?.Invoke("WARNING: Запрос был отменен");
                Debug.WriteLine("Запрос отменен");
                return null;
            }
            catch (Exception ex)
            {
                // Обработка ошибок
                var errorMsg = $"Ошибка при обращении к модели: {ex.Message}";
                OnResult?.Invoke($"ERROR: {errorMsg}");
                Debug.WriteLine(errorMsg);

                if (ex.StackTrace != null)
                    Debug.WriteLine(ex.StackTrace);

                return null;
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }
}

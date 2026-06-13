using MessageBox = System.Windows.MessageBox;
using System.Windows;
using Serilog;
using Jarvis.Interfaces;

namespace Jarvis.Wrapper;

public class OllamaConnectionValidator(IOllamaHealthCheck healthCheck) : IOllamaConnectionValidator {
    public async Task<bool> ValidateWithRetryAsync() {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++) {
            if (await healthCheck.IsOllamaRunningAsync()) {
                Log.Information("Соединение с Ollama успешно установлено");
                return true;
            }

            Log.Warning($"Ollama connection failed, attempt {attempt}/{maxRetries}");

            if (attempt < maxRetries) {
                var result = MessageBox.Show(
                    $"Ollama не запущена! (Попытка {attempt} из {maxRetries})\n\n" +
                    $"Пожалуйста, запустите Ollama из меню 'Пуск' или скачайте с ollama.ai\n\n" +
                    $"Попробовать снова?",
                    "Ошибка подключения",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result != MessageBoxResult.Yes) {
                    return false;
                }

                await Task.Delay(1000);
            }
            else {
                MessageBox.Show(
                    $"Ollama не запущена после {maxRetries} попыток.\n\n" +
                    $"Пожалуйста, запустите Ollama и перезапустите приложение.",
                    "Ошибка подключения",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        return false;
    }
}

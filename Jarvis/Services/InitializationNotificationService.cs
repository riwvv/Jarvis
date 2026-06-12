using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jarvis.Services;

public class InitializationNotificationService(IServiceProvider _sp, TextToSpeechService _tts, ILogger<InitializationNotificationService> _logger) {
    public async Task InitializeAllAsync() {
		try {
			await _tts.SpeakAsync("Привет! Я Джарвис, твой голосовой ассистент. Начинаю инициализацию, это может занять несколько минут. Когда всё будет готово, я сообщу.");

            await _tts.WaitForInitializationComplete();
            _logger.LogInformation("TTS готов");

            var stt = _sp.GetRequiredService<SpeechToTextService>();
            await stt.WaitForInitializationComplete();
            await _tts.SpeakAsync("Модуль распознавания речи подключен.");
            _logger.LogInformation("STT готов");

            var ai = _sp.GetRequiredService<CommunicationAiService>();
            await ai.WaitForInitializationComplete();
            await _tts.SpeakAsync("Нейросеть активирована.");
            _logger.LogInformation("AI готов");

            await _tts.SpeakAsync("Джарвис полностью готов к работе.");
            _logger.LogInformation("Ассистент полностью готов");
        }
		catch (Exception ex) {
			_logger.LogError(ex, "Ошибка при инициализации сервисов");
			await _tts.SpeakAsync("Произошла ошибка при инициализации. Перезапустите программу");
		}
    }
}

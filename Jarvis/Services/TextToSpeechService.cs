using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Speech.Synthesis;
using Jarvis.Configuration;

namespace Jarvis.Services {
    public class TextToSpeechService : IDisposable {
        private readonly SpeechSynthesizer _synthesizer;
        private bool _isSpeaking = false;
        private readonly SemaphoreSlim _speakSemaphore = new(1, 1);
        private readonly ILogger<TextToSpeechService> _logger;
        private readonly SpeechSettings _speechSettings;

        public event Action? OnStartedSpeaking;
        public event Action? OnFinishedSpeaking;
        public event Action<string>? OnError;

        public TextToSpeechService(IOptions<SpeechSettings> settings, ILogger<TextToSpeechService> logger) {
            _logger = logger;
            _speechSettings = settings.Value;
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.Rate = _speechSettings.TtsRate;
            _synthesizer.Volume = _speechSettings.TtsVolume;

            _synthesizer.SpeakStarted += (s, e) => {
                _isSpeaking = true;
                OnStartedSpeaking?.Invoke();
                _logger.LogInformation("TTS: Начало воспроизведения");
            };

            _synthesizer.SpeakCompleted += (s, e) => {
                _isSpeaking = false;
                OnFinishedSpeaking?.Invoke();
                _logger.LogInformation("TTS: Воспроизведение завершено");
            };

            ConfigureRussianVoice();
            _logger.LogInformation("TextToSpeechService инициализирован");
        }

        private void ConfigureRussianVoice() {
            try {
                var russianVoices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.VoiceInfo.Culture.Name.StartsWith("ru"))
                    .ToList();

                if (russianVoices.Any()) {
                    _synthesizer.SelectVoice(russianVoices.First().VoiceInfo.Name);
                    _logger.LogInformation($"TTS: Выбран голос '{russianVoices.First().VoiceInfo.Name}'");
                }
                else {
                    _logger.LogInformation("TTS: Предупреждение — Русский голос не найден");
                }
            }
            catch (Exception ex) {
                _logger.LogInformation($"TTS: Ошибка при настройке голоса: {ex.Message}");
            }
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(text)) {
                _logger.LogInformation("TTS: Пустой текст, озвучивание отменено");
                return;
            }

            string cleanText = CleanTextForSpeech(text);

            await _speakSemaphore.WaitAsync(cancellationToken);

            try {
                if (_isSpeaking) {
                    _synthesizer.SpeakAsyncCancelAll();
                    await Task.Delay(50, cancellationToken);
                }

                _logger.LogInformation($"TTS: Озвучивание: \"{cleanText}\"");
                await Task.Run(() => _synthesizer.SpeakAsync(cleanText), cancellationToken);
            }
            catch (OperationCanceledException) {
                _logger.LogInformation("TTS: Озвучивание отменено");
                _synthesizer.SpeakAsyncCancelAll();
            }
            catch (Exception ex) {
                _logger.LogInformation($"TTS: Ошибка при озвучивании: {ex.Message}");
                OnError?.Invoke(ex.Message);
            }
            finally {
                _speakSemaphore.Release();
            }
        }

        public void StopSpeaking() {
            try {
                if (_isSpeaking) {
                    _synthesizer.SpeakAsyncCancelAll();
                    _logger.LogInformation("TTS: Воспроизведение остановлено");
                }
            }
            catch (Exception ex) {
                _logger.LogInformation($"TTS: Ошибка при остановке: {ex.Message}");
            }
        }

        private string CleanTextForSpeech(string text) {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string cleaned = text;

            if (cleaned.StartsWith("DONE:", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(5).TrimStart();
            else if (cleaned.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(8).TrimStart();
            else if (cleaned.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(6).TrimStart();

            if (string.IsNullOrWhiteSpace(cleaned) || cleaned == ".")
                return "Команда выполнена";

            return cleaned;
        }

        public bool IsSpeaking => _isSpeaking;

        public void Dispose() {
            StopSpeaking();
            _speakSemaphore?.Dispose();
            _synthesizer?.Dispose();
            _logger.LogInformation("TextToSpeechService освобождён");
        }
    }
}
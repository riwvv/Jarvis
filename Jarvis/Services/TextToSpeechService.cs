using Jarvis.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Speech.Synthesis;

namespace Jarvis.Services {
    public class TextToSpeechService : IDisposable {
        private readonly SpeechSynthesizer _synthesizer;
        private bool _isSpeaking = false;
        private readonly SemaphoreSlim _speakSemaphore = new(1, 1);
        private readonly SpeechSettings _speechSettings;

        public event Action? OnStartedSpeaking;
        public event Action? OnFinishedSpeaking;
        public event Action<string>? OnError;

        public TextToSpeechService(IOptions<SpeechSettings> settings) {
            _speechSettings = settings.Value;
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.Rate = _speechSettings.TtsRate;
            _synthesizer.Volume = _speechSettings.TtsVolume;

            _synthesizer.SpeakStarted += (s, e) => {
                _isSpeaking = true;
                OnStartedSpeaking?.Invoke();
                Debug.WriteLine("TTS: Начало воспроизведения");
            };

            _synthesizer.SpeakCompleted += (s, e) => {
                _isSpeaking = false;
                OnFinishedSpeaking?.Invoke();
                Debug.WriteLine("TTS: Воспроизведение завершено");
            };

            ConfigureRussianVoice();
            Debug.WriteLine("TextToSpeechService инициализирован");
        }

        private void ConfigureRussianVoice() {
            try {
                var russianVoices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.VoiceInfo.Culture.Name.StartsWith("ru"))
                    .ToList();

                if (russianVoices.Any()) {
                    _synthesizer.SelectVoice(russianVoices.First().VoiceInfo.Name);
                    Debug.WriteLine($"TTS: Выбран голос '{russianVoices.First().VoiceInfo.Name}'");
                }
                else {
                    Debug.WriteLine("TTS: Предупреждение — Русский голос не найден");
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"TTS: Ошибка при настройке голоса: {ex.Message}");
            }
        }

        public async Task SpeakAsync(string text, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(text)) {
                Debug.WriteLine("TTS: Пустой текст, озвучивание отменено");
                return;
            }

            string cleanText = CleanTextForSpeech(text);

            await _speakSemaphore.WaitAsync(cancellationToken);

            try {
                if (_isSpeaking) {
                    _synthesizer.SpeakAsyncCancelAll();
                    await Task.Delay(50, cancellationToken);
                }

                Debug.WriteLine($"TTS: Озвучивание: \"{cleanText}\"");
                await Task.Run(() => _synthesizer.SpeakAsync(cleanText), cancellationToken);
            }
            catch (OperationCanceledException) {
                Debug.WriteLine("TTS: Озвучивание отменено");
                _synthesizer.SpeakAsyncCancelAll();
            }
            catch (Exception ex) {
                Debug.WriteLine($"TTS: Ошибка при озвучивании: {ex.Message}");
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
                    Debug.WriteLine("TTS: Воспроизведение остановлено");
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"TTS: Ошибка при остановке: {ex.Message}");
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
            Debug.WriteLine("TextToSpeechService освобождён");
        }
    }
}
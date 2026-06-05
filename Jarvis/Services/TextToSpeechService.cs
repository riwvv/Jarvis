using Microsoft.Extensions.Logging;
using System.Speech.Synthesis;
using Jarvis.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jarvis.Services;

public class TextToSpeechService : IDisposable {
    private SpeechSynthesizer? _synthesizer;
    private bool _isSpeaking = false;
    private readonly SemaphoreSlim _speakSemaphore = new(1, 1);
    private readonly ILogger<TextToSpeechService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _targetVoiceName;

    public event Action? OnStartedSpeaking;
    public event Action? OnFinishedSpeaking;
    public event Action<string>? OnError;

    public TextToSpeechService(IConfiguration configuration, ILogger<TextToSpeechService> logger) {
        _logger = logger;
        _configuration = configuration;
        _targetVoiceName = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.VoiceName;

        InitializeSynthesizer();
        ConfigureRussianVoice();
    }

    private void InitializeSynthesizer() {
        _synthesizer?.Dispose();
        _synthesizer = new SpeechSynthesizer();
        _synthesizer.SetOutputToDefaultAudioDevice();
        _synthesizer.Rate = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.TtsRate;
        _synthesizer.Volume = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.TtsVolume;

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
    }

    private void ConfigureRussianVoice() {
        if (_synthesizer == null) return;

        try {
            var russianVoices = _synthesizer.GetInstalledVoices()
                .Where(v => v.VoiceInfo.Culture.Name.StartsWith("ru"))
                .ToList();

            var targetVoice = russianVoices.FirstOrDefault(v => v.VoiceInfo.Name == _targetVoiceName);

            if (targetVoice != null) {
                _synthesizer.SelectVoice(targetVoice.VoiceInfo.Name);
                _logger.LogInformation($"TTS: Выбран голос '{_targetVoiceName}'");
            }
            else if (russianVoices.Any()) {
                var fallback = russianVoices.First();
                _synthesizer.SelectVoice(fallback.VoiceInfo.Name);
                _logger.LogWarning($"TTS: {_targetVoiceName} не найден. Выбран '{fallback.VoiceInfo.Name}'");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "TTS: Ошибка настройки голоса");
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default) {
        if (_synthesizer == null) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        string cleanText = CleanTextForSpeech(text);
        await _speakSemaphore.WaitAsync(cancellationToken);

        try {
            if (_isSpeaking) _synthesizer.SpeakAsyncCancelAll();
            await Task.Run(() => _synthesizer.SpeakAsync(cleanText), cancellationToken);
        }
        catch (OperationCanceledException) {
            _synthesizer.SpeakAsyncCancelAll();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "TTS: Ошибка озвучивания");
            OnError?.Invoke(ex.Message);
        }
        finally {
            _speakSemaphore.Release();
        }
    }

    public void StopSpeaking() {
        try {
            if (_isSpeaking) {
                _synthesizer?.SpeakAsyncCancelAll();
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "TTS: Ошибка остановки");
        }
    }

    private string CleanTextForSpeech(string text) {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        ReadOnlySpan<char> span = text.AsSpan();
        int spaceIndex = span.IndexOf(' ');
        if (spaceIndex > 0) {
            string firstWord = span[..spaceIndex].ToString();
            if (firstWord.Equals("DONE:", StringComparison.OrdinalIgnoreCase) ||
                firstWord.Equals("WARNING:", StringComparison.OrdinalIgnoreCase) ||
                firstWord.Equals("ERROR:", StringComparison.OrdinalIgnoreCase)) {
                span = span[(spaceIndex + 1)..];
            }
        }
        return span.IsEmpty ? "Команда выполнена" : span.ToString();
    }

    public bool IsSpeaking => _isSpeaking;

    public void Dispose() {
        StopSpeaking();
        _speakSemaphore?.Dispose();
        _synthesizer?.Dispose();
    }
}
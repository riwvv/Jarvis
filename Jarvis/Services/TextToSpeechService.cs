using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Speech.Synthesis;
using Jarvis.Configuration;

namespace Jarvis.Services;

public class TextToSpeechService : IDisposable {
    public event Action? OnStartedSpeaking;
    public event Action? OnFinishedSpeaking;
    public event Action<string>? OnError;

    private readonly ILogger<TextToSpeechService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TaskCompletionSource<bool> _initializationTcs = new();
    private readonly string _targetVoiceName;

    private readonly Queue<string> _messageQueue = new();
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private bool _isProcessingQueue = false;

    private TaskCompletionSource<bool>? _currentMessageTcs;

    private SpeechSynthesizer? _synthesizer;
    private bool _isSpeaking = false;

    public TextToSpeechService(IConfiguration configuration, ILogger<TextToSpeechService> logger) {
        _logger = logger;
        _configuration = configuration;
        _targetVoiceName = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.VoiceName;

        InitializeSynthesizer();
        ConfigureRussianVoice();

        _initializationTcs.TrySetResult(true);
    }

    public async Task WaitForInitializationComplete() => await _initializationTcs.Task;

    public async Task SpeakAsync(string text) {
        var tcs = new TaskCompletionSource<bool>();

        await _queueLock.WaitAsync();
        try {
            _messageQueue.Enqueue(text);
            _currentMessageTcs = tcs;

            if (!_isProcessingQueue) {
                _ = ProcessQueueAsync();
            }
        }
        finally {
            _queueLock.Release();
        }

        await tcs.Task;
    }

    public void StopSpeaking() {
        try {
            _synthesizer?.SpeakAsyncCancelAll();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "TTS: Ошибка остановки");
        }
    }

    private async Task ProcessQueueAsync() {
        await _queueLock.WaitAsync();
        try {
            _isProcessingQueue = true;
        }
        finally {
            _queueLock.Release();
        }

        while (true) {
            string? nextMessage = null;
            TaskCompletionSource<bool>? currentTcs = null;

            await _queueLock.WaitAsync();
            try {
                if (_messageQueue.Count == 0) {
                    _isProcessingQueue = false;
                    _currentMessageTcs = null;
                    break;
                }
                nextMessage = _messageQueue.Dequeue();
                currentTcs = _currentMessageTcs;
            }
            finally {
                _queueLock.Release();
            }

            await SpeakInternalAsync(nextMessage);

            currentTcs?.TrySetResult(true);
        }
    }

    private async Task SpeakInternalAsync(string text) {
        if (_synthesizer == null) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        string cleanText = CleanTextForSpeech(text);

        var tcs = new TaskCompletionSource<bool>();

        void OnSpeakCompleted(object? s, SpeakCompletedEventArgs e) {
            _isSpeaking = false;
            OnFinishedSpeaking?.Invoke();
            _logger.LogInformation("TTS: Воспроизведение завершено");
            tcs.TrySetResult(true);
        }

        void OnSpeakStarted(object? s, SpeakStartedEventArgs e) {
            _isSpeaking = true;
            OnStartedSpeaking?.Invoke();
            _logger.LogInformation($"TTS: Начало воспроизведения: {cleanText}");
        }

        _synthesizer.SpeakStarted += OnSpeakStarted;
        _synthesizer.SpeakCompleted += OnSpeakCompleted;

        try {
            _synthesizer.SpeakAsync(cleanText);
            await tcs.Task;
        }
        finally {
            _synthesizer.SpeakStarted -= OnSpeakStarted;
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
        }
    }

    private void InitializeSynthesizer() {
        _synthesizer?.Dispose();
        _synthesizer = new SpeechSynthesizer();
        _synthesizer.SetOutputToDefaultAudioDevice();

        var ttsSettings = _configuration.GetSection("TTSSettings").Get<TTSSettings>();
        if (ttsSettings != null) {
            _synthesizer.Rate = ttsSettings.TtsRate;
            _synthesizer.Volume = ttsSettings.TtsVolume;
        }
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
        _queueLock?.Dispose();
        _synthesizer?.Dispose();
    }
}
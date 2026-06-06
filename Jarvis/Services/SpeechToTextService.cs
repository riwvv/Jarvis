using Jarvis.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using SileroVad;
using System.IO;
using System.Text.Json;
using System.Windows;
using Vosk;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Threading.Timer;

namespace Jarvis.Services;

public class SpeechToTextService : IDisposable {
    // События
    public event Action<string>? OnWakeUp;
    public event Action<string>? OnProcessingText;
    public event Action<string>? OnSpeechRecognized;
    public event Action<string>? OnTimeout;
    public event Action? OnWakeWordDetected;

    // Константы
    private const int CHANNELS = 1;
    private const int BUFFER_MS = 250;
    private const string WAKE_WORD = "джарвис";
    private const int INACTIVITY_TIMEOUT_MS = 10000;
    private const int VAD_SILENCE_TIMEOUT_MS = 700;  // 0.7 секунды тишины = конец фразы

    private readonly List<string> _wakeWordVariants = ["джарвис", "джарвис ", "джарвиз", "джарвіс"];

    private enum RecognizerState { Sleeping, Listening, Processing }
    private RecognizerState _state = RecognizerState.Sleeping;

    // Vosk компоненты
    private Model? _model;
    private VoskRecognizer? _wakeWordRecognizer;
    private VoskRecognizer? _commandRecognizer;

    // Аудио
    private WaveInEvent? _microphone;
    private readonly Lock _audioLock = new();
    private bool _isRecording;

    // Silero VAD (новая версия)
    private Vad? _vad;
    private List<float> _vadAudioBuffer = [];
    private DateTime _lastSpeechEndTime;
    private bool _isVadAvailable;
    private bool _wasSpeechDetected;

    // Таймауты
    private Timer? _inactivityTimer;

    private readonly ILogger<SpeechToTextService> _logger;
    private readonly STTSettings _settings;
    private readonly int _sampleRate;

    public SpeechToTextService(TrayService trayService, IOptions<STTSettings> settings, ILogger<SpeechToTextService> logger) {
        _logger = logger;
        _settings = settings.Value;
        _sampleRate = _settings.SttSampleRate;

        OnWakeWordDetected += trayService.ShowAsOverlay;

        InitializeVosk();
        InitializeMicrophone();
        InitializeVad();
    }

    public void Start() => StartListening();

    #region Инициализация

    private void InitializeVosk() {
        try {
            string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.SttModelPath);
            if (!Directory.Exists(modelPath))
                throw new DirectoryNotFoundException($"Модель Vosk не найдена: {modelPath}");

            _logger.LogInformation($"Загрузка Vosk модели из: {modelPath}");
            _model = new Model(modelPath);

            _wakeWordRecognizer = new VoskRecognizer(_model, _sampleRate, BuildWakeWordGrammar());
            _commandRecognizer = new VoskRecognizer(_model, _sampleRate);

            _logger.LogInformation("Vosk инициализирован");
        }
        catch (Exception ex) {
            MessageBox.Show($"Ошибка загрузки Vosk: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    private string BuildWakeWordGrammar() {
        var entries = _wakeWordVariants.Select(v => $"\"{v}\"").ToList();
        entries.Add("\"[unk]\"");
        return $"[{string.Join(", ", entries)}]";
    }

    private void InitializeMicrophone() {
        _microphone = new WaveInEvent {
            WaveFormat = new WaveFormat(_sampleRate, CHANNELS),
            BufferMilliseconds = BUFFER_MS
        };
        _microphone.DataAvailable += OnAudioDataAvailable;
        _microphone.RecordingStopped += OnRecordingStopped;
    }

    private void InitializeVad() {
        try {
            _vad = new Vad();
            _vadAudioBuffer = [];
            _isVadAvailable = true;
            _logger.LogInformation("Silero VAD инициализирован");
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Не удалось инициализировать Silero VAD, работаю без VAD (таймаут 10 сек)");
            _isVadAvailable = false;
        }
    }

    #endregion

    #region Управление записью

    private void StartListening() {
        if (_microphone == null) return;

        lock (_audioLock) {
            if (_isRecording) return;

            try {
                _microphone.StartRecording();
                _isRecording = true;
                ResetInactivityTimer();
                _logger.LogInformation("Микрофон запущен");
            }
            catch (InvalidOperationException) {
                _logger.LogWarning("Перезапуск микрофона");
                StopListening();
                Task.Delay(50).ContinueWith(_ => StartListening());
            }
        }
    }

    public void StopListening() {
        lock (_audioLock) {
            if (_microphone != null && _isRecording) {
                _microphone.StopRecording();
                _isRecording = false;
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) {
        lock (_audioLock) _isRecording = false;
        _logger.LogInformation("Запись микрофона остановлена");
    }

    #endregion

    #region Обработка аудио

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e) {
        lock (_audioLock)
            ProcessAudioData(e.Buffer, e.BytesRecorded);
    }

    private void ProcessAudioData(byte[] buffer, int bytesRecorded) {
        switch (_state) {
            case RecognizerState.Sleeping:
                ProcessWakeWordDetection(buffer, bytesRecorded);
                break;
            case RecognizerState.Listening:
            case RecognizerState.Processing:
                ProcessCommandRecognition(buffer, bytesRecorded);
                break;
        }
    }

    private void ProcessWakeWordDetection(byte[] audioData, int bytesRecorded) {
        if (_wakeWordRecognizer?.AcceptWaveform(audioData, bytesRecorded) != true) return;

        string recognizedText = ExtractTextFromResult(_wakeWordRecognizer.Result());
        if (!IsWakeWordMatch(NormalizeText(recognizedText))) return;

        OnWakeWordDetected?.Invoke();
        _state = RecognizerState.Listening;
        OnWakeUp?.Invoke("LISTENING");
        ResetInactivityTimer();
        _logger.LogInformation($"Распознан wake word: {recognizedText}");
        _commandRecognizer?.Reset();

        _vadAudioBuffer?.Clear();
        _lastSpeechEndTime = DateTime.MinValue;
        _wasSpeechDetected = false;
    }

    private void ProcessCommandRecognition(byte[] audioData, int bytesRecorded) {
        if (_commandRecognizer == null) return;

        if (_state == RecognizerState.Listening) {
            _state = RecognizerState.Processing;
            OnProcessingText?.Invoke("PROCESSING");
        }

        bool hasRecognized = _commandRecognizer.AcceptWaveform(audioData, bytesRecorded);

        if (hasRecognized) {
            string recognizedText = ExtractTextFromResult(_commandRecognizer.Result());
            if (!string.IsNullOrWhiteSpace(recognizedText)) {
                OnSpeechRecognized?.Invoke(recognizedText);
                _logger.LogInformation($"Команда распознана: {recognizedText}");
                GoToSleep();
                return;
            }
        }

        if (_isVadAvailable && _vad != null) {
            ProcessVad(audioData, bytesRecorded);
        }
    }

    private void ProcessVad(byte[] audioData, int bytesRecorded) {
        var floats = new float[bytesRecorded / 2];
        Buffer.BlockCopy(audioData, 0, floats, 0, bytesRecorded);
        _vadAudioBuffer.AddRange(floats);

        if (_vadAudioBuffer.Count < 1024)
            return;

        try {
            var audioSpan = _vadAudioBuffer.ToArray().AsSpan();

            var speechTimestamps = _vad!.GetSpeechTimestamps(
                audio: audioSpan,
                threshold: 0.5f,
                min_speech_duration_ms: 250,      // речь должна быть не короче 250 мс
                max_speech_duration_s: float.PositiveInfinity,
                min_silence_duration_ms: 700,     // 700 мс тишины = конец фразы
                window_size_samples: 1024,
                speech_pad_ms: 100
            );

            bool hasSpeech = speechTimestamps != null && speechTimestamps.Count > 0;

            if (hasSpeech) {
                _lastSpeechEndTime = DateTime.UtcNow;
                _wasSpeechDetected = true;
                _logger.LogDebug("VAD: речь");

                int maxBufferSize = _sampleRate * 2;
                if (_vadAudioBuffer.Count > maxBufferSize) {
                    _vadAudioBuffer = [.. _vadAudioBuffer.Skip(_vadAudioBuffer.Count - maxBufferSize)];
                }
            }
            else if (_state != RecognizerState.Sleeping && _wasSpeechDetected) {
                if (_lastSpeechEndTime == DateTime.MinValue)
                    _lastSpeechEndTime = DateTime.UtcNow;

                var silenceDuration = (DateTime.UtcNow - _lastSpeechEndTime).TotalMilliseconds;

                if (silenceDuration >= VAD_SILENCE_TIMEOUT_MS) {
                    _logger.LogDebug($"VAD: тишина {silenceDuration} мс, завершаю команду");

                    string finalText = ExtractTextFromResult(_commandRecognizer?.Result() ?? "{}");
                    if (!string.IsNullOrWhiteSpace(finalText)) {
                        OnSpeechRecognized?.Invoke(finalText);
                        _logger.LogInformation($"Команда распознана (VAD): {finalText}");
                        GoToSleep();
                    }
                    else {
                        GoToSleep();
                    }
                }
            }
        }
        catch (Exception ex) {
            _logger.LogDebug($"VAD processing error: {ex.Message}");
        }
    }

    #endregion

    #region Таймауты и сон

    private void GoToSleep() {
        if (_state == RecognizerState.Sleeping) return;

        _state = RecognizerState.Sleeping;
        _commandRecognizer?.Reset();
        _vadAudioBuffer?.Clear();
        _lastSpeechEndTime = DateTime.MinValue;
        _wasSpeechDetected = false;
        OnTimeout?.Invoke("SLEEP");
        _logger.LogInformation("Jarvis уснул");
    }

    private void ResetInactivityTimer() {
        lock (_audioLock) {
            if (_inactivityTimer == null)
                _inactivityTimer = new Timer(_ => OnInactivityTimeout(), null, INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
            else
                _inactivityTimer.Change(INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
        }
    }

    private void OnInactivityTimeout() {
        lock (_audioLock) {
            if (_state != RecognizerState.Sleeping) {
                _logger.LogInformation("Таймаут бездействия, переход в режим сна");
                GoToSleep();
            }
        }
    }

    #endregion

    #region Вспомогательные методы

    private string ExtractTextFromResult(string result) {
        try {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        }
        catch (JsonException ex) {
            _logger.LogError(ex, "Ошибка парсинга JSON: {Result}", result);
            return string.Empty;
        }
    }

    private static string NormalizeText(string text) {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.ToLowerInvariant().Replace(" ", "").Replace("ё", "е").Trim();
    }

    private bool IsWakeWordMatch(string normalizedText) {
        if (NormalizeText(WAKE_WORD) == normalizedText) return true;
        return _wakeWordVariants.Any(v => NormalizeText(v) == normalizedText);
    }

    #endregion

    #region IDisposable

    public void Dispose() {
        StopListening();

        _inactivityTimer?.Dispose();

        if (_microphone != null) {
            _microphone.DataAvailable -= OnAudioDataAvailable;
            _microphone.RecordingStopped -= OnRecordingStopped;
            _microphone.Dispose();
        }

        _wakeWordRecognizer?.Dispose();
        _commandRecognizer?.Dispose();
        _model?.Dispose();
        _vad?.Dispose();
    }

    #endregion
}
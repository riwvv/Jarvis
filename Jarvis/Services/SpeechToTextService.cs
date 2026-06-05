using Jarvis.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using System.IO;
using System.Windows;
using Vosk;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Threading.Timer;

namespace Jarvis.Services {
    public class SpeechToTextService : IDisposable {
        public event Action<string>? OnWakeUp;           // Проснулся
        public event Action<string>? OnProcessingText;   // Распознаёт текст
        public event Action<string>? OnSpeechRecognized; // Команда распознана
        public event Action<string>? OnTimeout;          // Уснул (таймаут)
        public event Action? OnWakeWordDetected;

        private int SAMPLE_RATE;
        private const int CHANNELS = 1;
        private const int BUFFER_MS = 250;

        private bool _isRecording = false;

        // Wake word настройки
        private const string WAKE_WORD = "джарвис";
        private readonly List<string> _wakeWordVariants =
        [
            "джарвис",
            "джарвис ",
            "джарвиз",
            "джарвіс"
        ];

        private enum RecognizerState {
            Sleeping,
            Listening,
            Processing
        }
        private RecognizerState _state = RecognizerState.Sleeping;

        // Vosk компоненты
        private Model? _model;
        private VoskRecognizer? _wakeWordRecognizer;   // Для детекции wake word (грамматический режим)
        private VoskRecognizer? _commandRecognizer;    // Для распознавания команд (общий режим)

        // NAudio
        private WaveInEvent? _microphone;
        private readonly object _lockObject = new();
        private readonly object _micLock = new();

        // Таймаут бездействия (10 секунд)
        private System.Threading.Timer? _inactivityTimer;
        private const int INACTIVITY_TIMEOUT_MS = 10000;
        private readonly ILogger<SpeechToTextService> _logger;
        private readonly STTSettings _settings;

        public SpeechToTextService(TrayService trayService, IOptions<STTSettings> settings, ILogger<SpeechToTextService> logger) {
            _logger = logger;
            _settings = settings.Value;
            OnWakeWordDetected += trayService.ShowAsOverlay;
            SAMPLE_RATE = _settings.SttSampleRate;
            InitializeVosk();
            InitializeMicrophone();
        }

        private void InitializeVosk() {
            try {
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.SttModelPath);
                if (!Directory.Exists(modelPath)) {
                    throw new DirectoryNotFoundException($"Vosk модель не найдена: {modelPath}");
                }

                _logger.LogInformation($"Загрузка модели Vosk 0.42 из: {modelPath}");
                _model = new Model(modelPath);

                // 1. Wake Word распознаватель (грамматический режим)
                string grammarJson = BuildWakeWordGrammar();
                _wakeWordRecognizer = new VoskRecognizer(_model, SAMPLE_RATE, grammarJson);

                // 2. Командный распознаватель - без грамматики (общий режим)
                _commandRecognizer = new VoskRecognizer(_model, SAMPLE_RATE);
                _logger.LogInformation($"Vosk 0.42 инициализирован. Wake word грамматика: {grammarJson}");
            }
            catch (DirectoryNotFoundException ex) {
                MessageBox.Show($"Ollama не запущена! Пожалуйста, запустите Ollama и попробуйте снова.\nОшибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
            catch (Exception ex) {
                MessageBox.Show($"Ошибка загрузки модели Vosk.\n{ex.Message}",
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                WaveFormat = new WaveFormat(SAMPLE_RATE, CHANNELS),
                BufferMilliseconds = BUFFER_MS
            };
            _microphone.DataAvailable += OnAudioDataAvailable;
            _microphone.RecordingStopped += OnRecordingStopped;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e) {
            lock (_micLock) {
                _isRecording = false;
                _logger.LogInformation("Запись микрофона остановлена");
            }
        }

        private void StartListening() {
            if (_microphone == null) {
                _logger.LogInformation("Микрофон не инициализирован");
                return;
            }

            lock (_micLock) {
                if (_isRecording) {
                    _logger.LogInformation("Микрофон уже записывает, пропускаем запуск");
                    return;
                }

                try {
                    _microphone.StartRecording();
                    _isRecording = true;
                    ResetInactivityTimer();
                    _logger.LogInformation("Микрофон запущен");
                }
                catch (InvalidOperationException ex) {
                    _logger.LogWarning(ex, "Микрофон уже записывает, перезапуск");
                    StopListening();
                    _ = Task.Delay(50).ContinueWith(_ => {
                        lock (_micLock) {
                            if (_microphone != null && !_isRecording) {
                                try {
                                    _microphone.StartRecording();
                                    _isRecording = true;
                                }
                                catch (Exception innerEx) {
                                    _logger.LogError(innerEx, "Не удалось перезапустить микрофон");
                                }
                            }
                        }
                    });
                }
            }
        }

        public void StopListening() {
            lock (_micLock) {
                if (_microphone != null && _isRecording) {
                    _microphone.StopRecording();
                }
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e) {
            lock (_lockObject) 
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
            if (_wakeWordRecognizer == null) return;

            if (_wakeWordRecognizer.AcceptWaveform(audioData, audioData.Length)) {
                string result = _wakeWordRecognizer.Result();
                string recognizedText = ExtractTextFromResult(result);
                string normalized = NormalizeText(recognizedText);

                if (IsWakeWordMatch(normalized)) {
                    OnWakeWordDetected?.Invoke();
                    _state = RecognizerState.Listening;
                    OnWakeUp?.Invoke("LISTENING");
                    ResetInactivityTimer();
                    _logger.LogInformation($"Распознан wake word: {recognizedText}");
                    _commandRecognizer?.Reset();
                }
            }
        }

        private void ProcessCommandRecognition(byte[] audioData, int bytesRecorded) {
            if (_commandRecognizer == null) return;

            if (_state == RecognizerState.Listening) {
                _state = RecognizerState.Processing;
                OnProcessingText?.Invoke("PROCESSING");
            }

            if (_commandRecognizer.AcceptWaveform(audioData, audioData.Length)) {
                string result = _commandRecognizer.Result();
                string recognizedText = ExtractTextFromResult(result);

                if (!string.IsNullOrWhiteSpace(recognizedText)) {
                    ResetInactivityTimer();
                    OnSpeechRecognized?.Invoke(recognizedText);
                    _logger.LogInformation($"Команда распознана: {recognizedText}");
                    GoToSleep();
                }
            }
            else {
                string partialResult = _commandRecognizer.PartialResult();
                string partialText = ExtractPartialText(partialResult);

#if DEBUG
                if (!string.IsNullOrWhiteSpace(partialText)) {
                    _logger.LogDebug($"Частичный результат: {partialText}");
                }
#endif
            }
        }

        private void GoToSleep() {
            if (_state != RecognizerState.Sleeping) {
                _state = RecognizerState.Sleeping;
                _commandRecognizer?.Reset();
                OnTimeout?.Invoke("SLEEP");
                _logger.LogInformation("Jarvis уснул");
            }
        }

        private void ResetInactivityTimer() {
            lock (_lockObject) {
                if (_inactivityTimer == null) {
                    _inactivityTimer = new Timer(_ => OnInactivityTimeout(), null, INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
                }
                else {
                    _inactivityTimer.Change(INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
                }
            }
        }

        private void OnInactivityTimeout() {
            lock (_lockObject) {
                if (_state != RecognizerState.Sleeping) {
                    _logger.LogInformation("Таймаут бездействия, переход в режим сна");
                    GoToSleep();
                }
            }
        }

        private string ExtractTextFromResult(string result) {
            try {
                var json = System.Text.Json.JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("text", out var textProp))
                    return textProp.GetString() ?? string.Empty;
            }
            catch (System.Text.Json.JsonException ex) {
                _logger.LogError(ex, "Ошибка парсинга JSON результата: {Result}", result);
            }
            return string.Empty;
        }

        private string ExtractPartialText(string partialResult) {
            try {
                var json = System.Text.Json.JsonDocument.Parse(partialResult);
                if (json.RootElement.TryGetProperty("partial", out var partialProp)) {
                    return partialProp.GetString() ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private string NormalizeText(string text) {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return text
                .ToLowerInvariant()
                .Replace(" ", "")
                .Replace("ё", "е")
                .Trim();
        }

        private bool IsWakeWordMatch(string normalizedText) {
            string normalizedWakeWord = NormalizeText(WAKE_WORD);
            if (normalizedText == normalizedWakeWord) return true;

            return _wakeWordVariants.Any(variant => NormalizeText(variant) == normalizedText);
        }

        public void Start() => StartListening();

        public void Dispose() {
            lock (_micLock)
                StopListening();

            lock (_lockObject) {
                _inactivityTimer?.Dispose();
                _inactivityTimer = null;
            }

            if (_microphone != null) {
                _microphone.DataAvailable -= OnAudioDataAvailable;
                _microphone.RecordingStopped -= OnRecordingStopped;
                _microphone.Dispose();
                _microphone = null;
            }

            _wakeWordRecognizer?.Dispose();
            _commandRecognizer?.Dispose();
            _model?.Dispose();
        }
    }
}
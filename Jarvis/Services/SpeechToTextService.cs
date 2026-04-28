using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using Vosk;

namespace Jarvis.Services {
    public class SpeechToTextService : IDisposable {
        private const int SAMPLE_RATE = 16000;
        private const int CHANNELS = 1;
        private const int BUFFER_MS = 100;

        private bool _isRecording = false;

        // Wake word настройки
        private const string WAKE_WORD = "джарвис";
        private readonly List<string> _wakeWordVariants = new()
        {
            "джарвис",
            "джарвис ",
            "джарвиз",
            "джарвіс"
        };

        // События для взаимодействия с ViewModel
        public event Action<string>? OnWakeUp;           // Проснулся
        public event Action<string>? OnProcessingText;   // Распознаёт текст
        public event Action<string>? OnSpeechRecognized; // Команда распознана
        public event Action<string>? OnTimeout;          // Уснул (таймаут)

        // Состояния
        private enum RecognizerState {
            Sleeping,    // Спит, ждёт wake word
            Listening,   // Проснулся, слушает команду
            Processing   // Обрабатывает речь
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
        private Timer? _inactivityTimer;
        private const int INACTIVITY_TIMEOUT_MS = 10000;

        public SpeechToTextService() {
            InitializeVosk();
            InitializeMicrophone();
        }

        private void InitializeVosk() {
            try {
                // Путь к модели Vosk 0.42 внутри проекта
                string modelPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", "vosk-model-ru-0.42"));

                if (!Directory.Exists(modelPath)) {
                    // Пробуем альтернативный путь
                    string alternativePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "vosk-model-ru-0.42"));
                    if (Directory.Exists(alternativePath)) {
                        modelPath = alternativePath;
                    }
                    else {
                        throw new DirectoryNotFoundException($"Vosk модель 0.42 не найдена. Проверьте пути:\n{modelPath}\n{alternativePath}");
                    }
                }

                Debug.WriteLine($"Загрузка модели Vosk 0.42 из: {modelPath}");
                _model = new Model(modelPath);

                // 1. Wake Word распознаватель (грамматический режим)
                string grammarJson = BuildWakeWordGrammar();
                _wakeWordRecognizer = new VoskRecognizer(_model, SAMPLE_RATE, grammarJson);

                // 2. Командный распознаватель - без грамматики (общий режим)
                _commandRecognizer = new VoskRecognizer(_model, SAMPLE_RATE);

                Debug.WriteLine($"Vosk 0.42 инициализирован. Wake word грамматика: {grammarJson}");
            }
            catch (Exception ex) {
                Debug.WriteLine($"Ошибка инициализации Vosk: {ex.Message}");
                throw;
            }
        }

        private string BuildWakeWordGrammar() {
            // Только варианты пробуждения + [unk]
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
                Debug.WriteLine("Запись микрофона остановлена");
            }
        }

        private void StartListening() {
            if (_microphone == null) {
                Debug.WriteLine("Микрофон не инициализирован");
                return;
            }

            lock (_micLock) {
                if (_isRecording) {
                    Debug.WriteLine("Микрофон уже записывает, пропускаем запуск");
                    return;
                }

                try {
                    _microphone.StartRecording();
                    _isRecording = true;
                    ResetInactivityTimer();
                    Debug.WriteLine("Микрофон запущен");
                }
                catch (InvalidOperationException ex) {
                    Debug.WriteLine($"Микрофон уже записывает: {ex.Message}");
                    StopListening();
                    Thread.Sleep(100);
                    _microphone.StartRecording();
                    _isRecording = true;
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Ошибка запуска микрофона: {ex.Message}");
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
            lock (_lockObject) {
                byte[] audioData = e.Buffer.Take(e.BytesRecorded).ToArray();

                switch (_state) {
                    case RecognizerState.Sleeping:
                        ProcessWakeWordDetection(audioData);
                        break;

                    case RecognizerState.Listening:
                    case RecognizerState.Processing:
                        ProcessCommandRecognition(audioData);
                        break;
                }
            }
        }

        private void ProcessWakeWordDetection(byte[] audioData) {
            if (_wakeWordRecognizer == null) return;

            if (_wakeWordRecognizer.AcceptWaveform(audioData, audioData.Length)) {
                string result = _wakeWordRecognizer.Result();
                string recognizedText = ExtractTextFromResult(result);
                string normalized = NormalizeText(recognizedText);

                // Проверяем, является ли распознанное слово wake word
                if (IsWakeWordMatch(normalized)) {
                    _state = RecognizerState.Listening;
                    OnWakeUp?.Invoke("LISTENING");
                    ResetInactivityTimer();
                    Debug.WriteLine($"Распознан wake word: {recognizedText}");
                    _commandRecognizer?.Reset();
                }
            }
        }

        private void ProcessCommandRecognition(byte[] audioData) {
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
                    Debug.WriteLine($"Команда распознана: {recognizedText}");
                    GoToSleep();
                }
            }
            else {
                // Частичный результат (опционально)
                string partialResult = _commandRecognizer.PartialResult();
                string partialText = ExtractPartialText(partialResult);

                if (!string.IsNullOrWhiteSpace(partialText)) {
                    Debug.WriteLine($"Частичный результат: {partialText}");
                }
            }
        }

        private void GoToSleep() {
            if (_state != RecognizerState.Sleeping) {
                _state = RecognizerState.Sleeping;
                _commandRecognizer?.Reset();
                OnTimeout?.Invoke("SLEEP");
                Debug.WriteLine("Jarvis уснул");
            }
        }

        private void ResetInactivityTimer() {
            lock (_lockObject) {
                _inactivityTimer?.Dispose();
                _inactivityTimer = new Timer(_ => {
                    lock (_lockObject) {
                        if (_state != RecognizerState.Sleeping) {
                            Debug.WriteLine("Таймаут бездействия, переход в режим сна");
                            GoToSleep();
                        }
                    }
                }, null, INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
            }
        }

        private string ExtractTextFromResult(string result) {
            try {
                var json = System.Text.Json.JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("text", out var textProp)) {
                    return textProp.GetString() ?? string.Empty;
                }
            }
            catch { }
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

            return _wakeWordVariants.Any(variant =>
                NormalizeText(variant) == normalizedText);
        }

        public void Start() {
            StartListening();
        }

        public void Dispose() {
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
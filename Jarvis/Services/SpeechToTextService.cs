using NAudio.CoreAudioApi;
using NAudio.Wave;
using SileroVad;
using System.Diagnostics;
using System.IO;
using System.Text;
using Vosk;

namespace Jarvis.Services
{
    public class SpeechToTextService : IDisposable
    {
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

        // Команды, которые распознаются сразу без Wake Word
        private readonly List<string> _instantCommands = new()
    {
        "стоп",
        "выход",
        "заверши работу",
        "спать"
    };

        // События для взаимодействия с ViewModel
        public event Action<string>? OnWakeUp;        // Проснулся
        public event Action<string>? OnProcessingText; // Распознаёт текст
        public event Action<string>? OnSpeechRecognized; // Команда распознана
        public event Action<string>? OnTimeout;        // Уснул (таймаут)

        // Состояния
        private enum RecognizerState
        {
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

        // Таймаут бездействия (10 секунд)
        private Timer? _inactivityTimer;
        private const int INACTIVITY_TIMEOUT_MS = 10000;

        public SpeechToTextService()
        {
            InitializeVosk();
            InitializeMicrophone();
        }

        private void InitializeVosk()
        {
            try
            {
                // Указываем путь к модели
                string modelPath = "vosk-model-ru-0.22";

                if (!Directory.Exists(modelPath))
                {
                    throw new DirectoryNotFoundException($"Vosk модель не найдена по пути: {modelPath}");
                }

                _model = new Model(modelPath);

                // 1. Wake Word распознаватель (грамматический режим — лёгкий и быстрый)
                var wakeGrammar = new List<string>(_wakeWordVariants);
                wakeGrammar.AddRange(_instantCommands);
                wakeGrammar.Add("[unk]"); // Важно: добавляем [unk] для стабильности

                string grammarJson = System.Text.Json.JsonSerializer.Serialize(wakeGrammar);

                // ✅ Передаём грамматику в конструктор
                _wakeWordRecognizer = new VoskRecognizer(_model, SAMPLE_RATE, grammarJson);

                // Включите частичные результаты для wake word распознавателя, если нужно
                // _wakeWordRecognizer.SetPartialWords(true); // Опционально

                // 2️⃣ Командный распознаватель - без грамматики (общий режим)
                _commandRecognizer = new VoskRecognizer(_model, SAMPLE_RATE);
                // _commandRecognizer.SetMaxAlternatives(0); // Необязательные настройки
                // _commandRecognizer.SetWords(false);

                Debug.WriteLine($"Vosk инициализирован. Wake word грамматика: {grammarJson}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка инициализации Vosk: {ex.Message}");
                throw;
            }
        }

        private string BuildWakeWordGrammar()
        {
            // Собираем все варианты пробуждения
            var allWakeVariants = new List<string>(_wakeWordVariants);
            allWakeVariants.AddRange(_instantCommands);

            // Добавляем [unk] — обязательный элемент, чтобы распознаватель не "зависал"
            // Если его не добавить, Vosk может перестать реагировать на нераспознанные слова [citation:2][citation:7]
            var entries = allWakeVariants.Select(v => $"\"{v}\"").ToList();
            entries.Add("\"[unk]\"");

            return $"[{string.Join(", ", entries)}]";
        }

        private void InitializeMicrophone()
        {
            _microphone = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SAMPLE_RATE, CHANNELS),
                BufferMilliseconds = BUFFER_MS
            };
            _microphone.DataAvailable += OnAudioDataAvailable;
            _microphone.RecordingStopped += OnRecordingStopped;
        }
        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isRecording = false;
            Debug.WriteLine("Запись микрофона остановлена");
        }

        private void StartListening()
        {
            if (_microphone == null)
            {
                Debug.WriteLine("Микрофон не инициализирован");
                return;
            }

            if (_isRecording)
            {
                Debug.WriteLine("Микрофон уже записывает, пропускаем запуск");
                return;
            }

            try
            {
                _microphone.StartRecording();
                _isRecording = true;
                ResetInactivityTimer();
                Debug.WriteLine("Микрофон запущен");
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Микрофон уже записывает: {ex.Message}");
                // Пытаемся остановить и запустить заново
                StopListening();
                Thread.Sleep(100);
                _microphone.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка запуска микрофона: {ex.Message}");
            }
        }

        public void StopListening()
        {
            if (_microphone != null && _isRecording)
            {
                _microphone.StopRecording();
                // Не устанавливаем _isRecording = false здесь,
                // это сделает OnRecordingStopped
            }
        }

        private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_lockObject)
            {
                byte[] audioData = e.Buffer.Take(e.BytesRecorded).ToArray();

                switch (_state)
                {
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

        private void ProcessWakeWordDetection(byte[] audioData)
        {
            if (_wakeWordRecognizer == null) return;

            if (_wakeWordRecognizer.AcceptWaveform(audioData, audioData.Length))
            {
                string result = _wakeWordRecognizer.Result();
                string recognizedText = ExtractTextFromResult(result);

                // Нормализуем текст (убираем пробелы для сравнения) [citation:2]
                string normalized = NormalizeText(recognizedText);

                // Проверяем, является ли распознанное слово wake word
                if (IsWakeWordMatch(normalized))
                {
                    // Просыпаемся!
                    _state = RecognizerState.Listening;
                    OnWakeUp?.Invoke("LISTENING");
                    ResetInactivityTimer();
                    Debug.WriteLine($"Распознан wake word: {recognizedText}");

                    // Очищаем командный распознаватель для новой сессии
                    _commandRecognizer?.Reset();
                }
                // Проверяем мгновенные команды (даже в спящем режиме)
                else if (_instantCommands.Any(cmd => normalized.Contains(NormalizeText(cmd))))
                {
                    OnSpeechRecognized?.Invoke(recognizedText);
                    OnTimeout?.Invoke("SLEEP");
                    Debug.WriteLine($"Мгновенная команда: {recognizedText}");
                }
            }
        }

        private void ProcessCommandRecognition(byte[] audioData)
        {
            if (_commandRecognizer == null) return;

            if (_state == RecognizerState.Listening)
            {
                _state = RecognizerState.Processing;
                OnProcessingText?.Invoke("PROCESSING");
            }

            if (_commandRecognizer.AcceptWaveform(audioData, audioData.Length))
            {
                string result = _commandRecognizer.Result();
                string recognizedText = ExtractTextFromResult(result);

                if (!string.IsNullOrWhiteSpace(recognizedText))
                {
                    ResetInactivityTimer();
                    OnSpeechRecognized?.Invoke(recognizedText);
                    Debug.WriteLine($"Команда распознана: {recognizedText}");

                    // После успешного распознавания команды — засыпаем
                    GoToSleep();
                }
            }
            else
            {
                // Частичный результат — можно использовать для отображения в реальном времени
                string partialResult = _commandRecognizer.PartialResult();
                string partialText = ExtractPartialText(partialResult);

                if (!string.IsNullOrWhiteSpace(partialText))
                {
                    // Опционально: отправляем частичный результат для отображения
                    // OnPartialResult?.Invoke(partialText);
                    Debug.WriteLine($"Частичный результат: {partialText}");
                }
            }
        }

        private void GoToSleep()
        {
            _state = RecognizerState.Sleeping;
            _commandRecognizer?.Reset();
            OnTimeout?.Invoke("SLEEP");
            Debug.WriteLine("Jarvis уснул");
        }

        private void ResetInactivityTimer()
        {
            _inactivityTimer?.Dispose();
            _inactivityTimer = new Timer(_ =>
            {
                lock (_lockObject)
                {
                    if (_state != RecognizerState.Sleeping)
                    {
                        Debug.WriteLine("Таймаут бездействия, переход в режим сна");
                        GoToSleep();
                    }
                }
            }, null, INACTIVITY_TIMEOUT_MS, Timeout.Infinite);
        }

        private string ExtractTextFromResult(string result)
        {
            try
            {
                // Vosk возвращает JSON: {"text": "распознанный текст"}
                var json = System.Text.Json.JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString() ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private string ExtractPartialText(string partialResult)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(partialResult);
                if (json.RootElement.TryGetProperty("partial", out var partialProp))
                {
                    return partialProp.GetString() ?? string.Empty;
                }
            }
            catch { }
            return string.Empty;
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return text
                .ToLowerInvariant()
                .Replace(" ", "")     // Убираем пробелы
                .Replace("ё", "е")    // Упрощаем букву ё
                .Trim();
        }

        private bool IsWakeWordMatch(string normalizedText)
        {
            string normalizedWakeWord = NormalizeText(WAKE_WORD);
            if (normalizedText == normalizedWakeWord) return true;

            return _wakeWordVariants.Any(variant =>
                NormalizeText(variant) == normalizedText);
        }

        public void Start()
        {
            StartListening();
        }

        public void Dispose()
        {
            StopListening();
            _inactivityTimer?.Dispose();

            if (_microphone != null)
            {
                _microphone.DataAvailable -= OnAudioDataAvailable;
                _microphone.RecordingStopped -= OnRecordingStopped;
                _microphone.Dispose();
            }

            _wakeWordRecognizer?.Dispose();
            _commandRecognizer?.Dispose();
            _model?.Dispose();
        }
    }
}

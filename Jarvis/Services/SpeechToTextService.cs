using NAudio.Wave;
using Pv;
using SileroVad;
using System.Diagnostics;
using System.IO;
using System.Text;
using Whisper.net;


namespace Jarvis.Services
{
    public class SpeechToTextService : IDisposable
    {
        public event Action<string>? OnWakeUp;
        public event Action<string>? OnTimeout;
        public event Action<string>? OnProccessingText;
        public event Action<string>? OnSpeechRecognized;

        private readonly string _porcupineApiKey = "iTVHQmgjOSX0qPtqkZguw6PMkYr9ZqNJIWrgrV/VGVR0SfH9nOcwWA==";
        private int _porcupineFrameLength;
        private WaveInEvent? _microphone;
        private MemoryStream? _currentSpeech;
        private readonly Vad _vad;
        private Porcupine? _porcupine;

        private readonly string _model = "ggml-medium.bin";

        private readonly List<float> _audioBuffer = new(16000 * 5);
        private readonly List<short> _wakeWordBuffer = new(512);
        private short[] _frameCache = Array.Empty<short>();
        private short[] _samplesBuffer = Array.Empty<short>();
        private float[] _floatSamplesCache = Array.Empty<float>();

        private readonly object _audioBufferLock = new object();

        private bool _isSpeaking;
        private bool _isSending;
        private bool _isAwake;
        private DateTime _lastSpeechTime;
        private DateTime _lastCommandTime = DateTime.MinValue;

        private const int SAMPLE_RATE = 16000;
        private const float THRESHOLD = 0.225f;
        private const int MIN_SILENCE_MS = 250;
        private const int PRE_ROLL_MS = 100;
        private const int SLEEP_TIMEOUT_MS = 500;
        private const int MAX_BUFFER_SECONDS = 5;

        public SpeechToTextService()
        {
            _vad = new Vad();

            try
            {
                InitializePorcupine();
                InitializeMicrophone();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка инициализации: {ex.Message}");
            }
        }

        private void InitializePorcupine()
        {
            _porcupine = Porcupine.FromBuiltInKeywords(_porcupineApiKey,
                new List<BuiltInKeyword> { BuiltInKeyword.PICOVOICE },
                sensitivities: new List<float> { 0.7f });

            _porcupineFrameLength = _porcupine.FrameLength;
            _frameCache = new short[_porcupineFrameLength];
            Debug.WriteLine("Porcupine инициализирован");
        }

        private void InitializeMicrophone()
        {
            _microphone = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SAMPLE_RATE, 16, 1),
                BufferMilliseconds = 70
            };
            _microphone.DataAvailable += OnAudioData;
        }

        public void Start() => _microphone?.StartRecording();

        public void OnAudioData(object? sender, WaveInEventArgs e)
        {
            try
            {
                int sampleCount = e.BytesRecorded / 2;

                if (_samplesBuffer.Length < sampleCount)
                {
                    _samplesBuffer = new short[sampleCount];
                }

                Buffer.BlockCopy(e.Buffer, 0, _samplesBuffer, 0, e.BytesRecorded);

                if (_isAwake && _lastCommandTime != DateTime.MinValue && !_isSpeaking && !_isSending)
                {
                    if ((DateTime.Now - _lastCommandTime).TotalMilliseconds > SLEEP_TIMEOUT_MS)
                    {
                        _isAwake = false;
                        _wakeWordBuffer.Clear();
                        Debug.WriteLine("😴 Ассистент уснул (таймаут)");
                        OnTimeout?.Invoke("SLEEP");
                    }
                }

                if (!_isAwake && _porcupine != null)
                {
                    ProcessWakeWord(_samplesBuffer.AsSpan(0, sampleCount));
                }

                if (_isAwake)
                {
                    ProcessSpeech(_samplesBuffer.AsSpan(0, sampleCount), e.Buffer, e.BytesRecorded);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка в OnAudioData: {ex.Message}");
            }
        }

        public void ProcessWakeWord(ReadOnlySpan<short> samples)
        {
            if (_porcupine == null) return;

            foreach (var sample in samples)
            {
                _wakeWordBuffer.Add(sample);
            }

            while (_wakeWordBuffer.Count >= _porcupineFrameLength)
            {
                for (int i = 0; i < _porcupineFrameLength; i++)
                {
                    _frameCache[i] = _wakeWordBuffer[i];
                }
                _wakeWordBuffer.RemoveRange(0, _porcupineFrameLength);

                if (_porcupine.Process(_frameCache) >= 0)
                {
                    _isAwake = true;
                    _lastCommandTime = DateTime.MinValue;
                    _wakeWordBuffer.Clear();
                    Debug.WriteLine("🔊 Ключевое слово найдено");
                    OnWakeUp?.Invoke("LISTENING");
                    break;
                }
            }
        }

        public void ProcessSpeech(ReadOnlySpan<short> samples, byte[] rawBuffer, int bytesRecorded)
        {
            if (_floatSamplesCache.Length < samples.Length)
            {
                _floatSamplesCache = new float[samples.Length];
            }

            for (int i = 0; i < samples.Length; i++)
            {
                _floatSamplesCache[i] = samples[i] / 32768f;
            }

            lock (_audioBufferLock)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    _audioBuffer.Add(_floatSamplesCache[i]);
                }

                if (_audioBuffer.Count > SAMPLE_RATE * MAX_BUFFER_SECONDS)
                {
                    _audioBuffer.RemoveRange(0, _audioBuffer.Count - SAMPLE_RATE * MAX_BUFFER_SECONDS);
                }
            }

            bool hasRecentSpeech = DetectRecentSpeech();

            if (hasRecentSpeech)
            {
                if (!_isSpeaking)
                {
                    _isSpeaking = true;
                    _currentSpeech = new MemoryStream();

                    int preRollSamples = (PRE_ROLL_MS * SAMPLE_RATE) / 1000;
                    lock (_audioBufferLock)
                    {
                        if (_audioBuffer.Count > preRollSamples + bytesRecorded / 2)
                        {
                            int startIndex = _audioBuffer.Count - preRollSamples - bytesRecorded / 2;
                            for (int i = 0; i < preRollSamples; i++)
                            {
                                short val = (short)(_audioBuffer[startIndex + i] * 32768f);
                                _currentSpeech.WriteByte((byte)(val & 0xFF));
                                _currentSpeech.WriteByte((byte)((val >> 8) & 0xFF));
                            }
                        }
                    }
                }

                _currentSpeech?.Write(rawBuffer, 0, bytesRecorded);
                _lastSpeechTime = DateTime.Now;
            }
            else if (_isSpeaking && !_isSending)
            {
                _currentSpeech?.Write(rawBuffer, 0, bytesRecorded);

                if ((DateTime.Now - _lastSpeechTime).TotalMilliseconds > MIN_SILENCE_MS)
                {
                    OnProccessingText?.Invoke("PROCESSING");
                    _ = SendSpeechAsync();
                }
            }
        }

        private bool DetectRecentSpeech()
        {
            int analyzeSamples;
            float[] recentSamples;
            int bufferCount;

            lock (_audioBufferLock)
            {
                bufferCount = _audioBuffer.Count;
                if (bufferCount < SAMPLE_RATE) return false;

                analyzeSamples = Math.Min(SAMPLE_RATE * 3, bufferCount);
                recentSamples = new float[analyzeSamples];

                for (int i = 0; i < analyzeSamples; i++)
                {
                    recentSamples[i] = _audioBuffer[bufferCount - analyzeSamples + i];
                }
            }

            var timestamps = _vad.GetSpeechTimestamps(recentSamples, threshold: THRESHOLD);

            if (timestamps.Count == 0) return false;

            float currentTimeMs;
            lock (_audioBufferLock)
            {
                currentTimeMs = (_audioBuffer.Count / (float)SAMPLE_RATE) * 1000;
            }

            float bufferStartMs = ((bufferCount - analyzeSamples) / (float)SAMPLE_RATE) * 1000;

            foreach (var ts in timestamps)
            {
                if (bufferStartMs + ts.End > currentTimeMs - 500)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task SendSpeechAsync()
        {
            if (_currentSpeech == null || _currentSpeech.Length == 0)
                return;

            _isSending = true;
            MemoryStream? speechToSend = _currentSpeech;
            _currentSpeech = null;

            try
            {
                speechToSend.Position = 0;
                var text = await SpeechToTextAsync(speechToSend, SAMPLE_RATE);

                if (!string.IsNullOrEmpty(text))
                {
                    OnSpeechRecognized?.Invoke(text);
                    _lastCommandTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка отправки команды: {ex}");
            }
            finally
            {
                _isSpeaking = false;
                _isSending = false;
                speechToSend?.Dispose();
            }
        }

        public async Task<string> SpeechToTextAsync(MemoryStream stream, int sample_rate)
        {
            if (stream == null || stream.Length == 0)
                return "";

            stream.Position = 0;

            using var wavStream = new MemoryStream();
            WriteWavHeader(wavStream, (int)stream.Length, sample_rate);
            await stream.CopyToAsync(wavStream);
            wavStream.Position = 0;

            using var factory = WhisperFactory.FromPath(_model);
            using var process = factory.CreateBuilder().WithLanguage("auto").Build();
            var resultBuilder = "";

            await foreach (var item in process.ProcessAsync(stream))
                resultBuilder += item.Text;
            resultBuilder = resultBuilder.Trim();

            if (resultBuilder == "" || string.IsNullOrWhiteSpace(resultBuilder))
                return "";

            return resultBuilder;
        }

        private void WriteWavHeader(Stream stream, int dataLength, int rate)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(Encoding.UTF8.GetBytes("RIFF"));
            writer.Write(dataLength + 36);
            writer.Write(Encoding.UTF8.GetBytes("WAVE"));
            writer.Write(Encoding.UTF8.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.UTF8.GetBytes("data"));
            writer.Write(dataLength);
        }

        public void Dispose()
        {
            _microphone?.Dispose();
            _porcupine?.Dispose();
            _currentSpeech?.Dispose();
            _vad?.Dispose();
        }
    }
}

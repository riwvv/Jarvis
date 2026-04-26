using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jarvis.Services;

public class TextToSpeechService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private bool _isSpeaking;
    private readonly object _lockObject = new object();
    private CancellationTokenSource? _currentCts;

    public event Action? OnSpeechStarted;
    public event Action? OnSpeechCompleted;
    public event Action<string>? OnSpeechError;

    public static class VoiceSettings
    {
        public static int Rate { get; set; } = 0;
        public static int Volume { get; set; } = 100;
        public static string? VoiceName { get; set; } = "Microsoft David Desktop";
    }

    public TextToSpeechService()
    {
        try
        {
            //скорость
            _synthesizer.Rate = Math.Clamp(VoiceSettings.Rate, -10, 10);

            //громкость
            _synthesizer.Volume = Math.Clamp(VoiceSettings.Volume, 0, 100);

            TrySelectMaleVoice();
            _isSpeaking = false;


            Debug.WriteLine("TextToSpeechService инициализирован");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка инициализации TTS: {ex.Message}");
            throw;
        }
    }

    private void TrySelectMaleVoice()
    {
        try
        {
            var installedVoices = _synthesizer.GetInstalledVoices();
            var maleVoices = installedVoices
                .Where(v => v.VoiceInfo.Gender == VoiceGender.Male)
                .ToList();

            if (maleVoices.Any())
            {
                _synthesizer.SelectVoice(maleVoices.First().VoiceInfo.Name);
                Debug.WriteLine($"Выбран мужской голос: {_synthesizer.Voice?.Name}");
            }
            else
            {
                // Если мужских голосов нет, используем первый доступный
                var defaultVoice = installedVoices.FirstOrDefault();
                if (defaultVoice != null)
                {
                    _synthesizer.SelectVoice(defaultVoice.VoiceInfo.Name);
                    Debug.WriteLine($"Мужских голосов не найдено, выбран: {defaultVoice.VoiceInfo.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка выбора мужского голоса: {ex.Message}");
        }
    }

    private string CleanTextForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string cleaned = text;

        cleaned = Regex.Replace(cleaned, @"^\s*(DONE|WARNING|ERROR|SUCCESS|EXECUTE|NONE)\s*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*(DONE|WARNING|ERROR|SUCCESS|EXECUTE|NONE)\s*$", "", RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\*\*([^*]+)\*\*", "$1"); // Жирный
        cleaned = Regex.Replace(cleaned, @"\*([^*]+)\*", "$1");     // Курсив
        cleaned = Regex.Replace(cleaned, @"__([^_]+)__", "$1");     // Альтернативный жирный
        cleaned = Regex.Replace(cleaned, @"_([^_]+)_", "$1");       // Альтернативный курсив
        cleaned = Regex.Replace(cleaned, @"`([^`]+)`", "$1");       // Код

        cleaned = Regex.Replace(cleaned, @"\[([^\]]+)\]\([^\)]+\)", "$1"); // [текст](url) -> текст
        cleaned = Regex.Replace(cleaned, @"https?:\/\/[^\s]+", "");         // URL полностью

        cleaned = Regex.Replace(cleaned, @"[\uD800-\uDBFF][\uDC00-\uDFFF]", ""); // Эмодзи
        cleaned = Regex.Replace(cleaned, @"[^\w\s\.\,\!\?\-\;\(\\)\#]", " ");    // Оставляем только нужные символы

        cleaned = Regex.Replace(cleaned, @"\s+", " "); // Несколько пробелов в один
        cleaned = cleaned.Trim();

        cleaned = Regex.Replace(cleaned, @"\b(DONE|WARNING|ERROR|SUCCESS)\b", "", RegexOptions.IgnoreCase);

        cleaned = cleaned.Replace("&quot;", "\"")
                       .Replace("&amp;", "и")
                       .Replace("&lt;", "<")
                       .Replace("&gt;", ">")
                       .Replace("\\n", ". ")
                       .Replace("\n", ". ");

        cleaned = Regex.Replace(cleaned, @"\.{2,}", ".");

        cleaned = Regex.Replace(cleaned, @"^[\s\-_\.\,]+", "");
        cleaned = Regex.Replace(cleaned, @"[\s\-_\.\,]+$", "");

        if (!string.IsNullOrEmpty(cleaned) && !".!?".Contains(cleaned.Last()))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    public void StopSpeaking()
    {
        lock (_lockObject)
        {
            _currentCts?.Cancel();
            _synthesizer.SpeakAsyncCancelAll();
        }
        Debug.WriteLine("TTS: Озвучивание остановлено");
    }

    public async Task Speak(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string cleanedText = CleanTextForSpeech(text);

        // Отменяем предыдущее озвучивание
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            OnSpeechStarted?.Invoke();

            await Task.Run(() =>
            {
                var prompt = new Prompt(cleanedText);
                _synthesizer.SpeakAsync(prompt);

                // Ждем завершения с проверкой отмены
                while (_synthesizer.State == SynthesizerState.Speaking)
                {
                    if (_currentCts.Token.IsCancellationRequested)
                    {
                        _synthesizer.SpeakAsyncCancelAll();
                        break;
                    }
                    Thread.Sleep(10);
                }
            }, _currentCts.Token);

            OnSpeechCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            OnSpeechError?.Invoke("Отменено");
        }
        finally
        {
            _isSpeaking = false;
        }
    }


    public void Dispose()
    {
        _synthesizer.SpeakAsyncCancelAll();
        _synthesizer.Dispose();
        _currentCts?.Dispose();
    }
}

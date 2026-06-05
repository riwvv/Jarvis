using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Microsoft.Extensions.Logging;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Jarvis.Plugins;
using Jarvis.Services;

namespace Jarvis.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable {
    [ObservableProperty] private string _state = "SLEEP";
    [ObservableProperty] private bool _isSpeaking = false; // используется для речевого синтезатора

    private readonly ILogger<MainViewModel> _logger;
    private readonly SpeechToTextService _speechToTextService;
    private readonly CommunicationAiService _communicationAiService;
    private readonly TextToSpeechService _textToSpeechService;

    public MainViewModel(SpeechToTextService speechToTextService, CommunicationAiService communicationAiService, TextToSpeechService textToSpeechService, ILogger<MainViewModel> logger) {
        _logger = logger;

        _speechToTextService = speechToTextService;
        _communicationAiService = communicationAiService;
        _textToSpeechService = textToSpeechService;

        _textToSpeechService.OnStartedSpeaking += () => IsSpeaking = true; // начал говорить
        _textToSpeechService.OnFinishedSpeaking += () => IsSpeaking = false; // закончил говорить
        _textToSpeechService.OnError += (error) => _logger.LogInformation($"TTS Error: {error}"); // пасхалка на случай ошибок 

        _speechToTextService.OnSpeechRecognized += async (text) => {
            if (string.IsNullOrWhiteSpace(text)) return;

            _logger.LogInformation($"Распознана команда: {text}");
            var response = await _communicationAiService.GetRequestUser(text);

            if (!string.IsNullOrEmpty(response)) {
                await _textToSpeechService.SpeakAsync(response);
            }
        }; // обрабатывает/отправляет/озвучивает

        _speechToTextService.OnWakeUp += (text) => State = text; // проснулся
        _speechToTextService.OnProcessingText += (text) => State = text; // слушает
        _speechToTextService.OnTimeout += async (text) => {
            State = text;
            await Task.Delay(2000);
            State = "SLEEP";
        }; // засыпает

        _communicationAiService.OnExecute += (text) => State = text; // выполняет
        _communicationAiService.OnResult += (text) => State = text; // выполнил

        _speechToTextService.Start();
    }

    private void OnRestartRequired(object? sender, EventArgs e) {
        Application.Current.Dispatcher.Invoke(() => {
            var result = MessageBox.Show(
                "Для улучшения качества голоса был установлен дополнительный компонент.\n\n" +
                "Пожалуйста, перезапустите приложение, чтобы изменения вступили в силу.",
                "Требуется перезапуск",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    public void Dispose() {
        _speechToTextService?.Dispose();
        _communicationAiService?.Dispose();
        _textToSpeechService?.Dispose();
    }
}
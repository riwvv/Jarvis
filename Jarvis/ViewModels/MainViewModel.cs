using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Jarvis.Views.Windows;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Jarvis.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable {

    private Window _window = Application.Current.MainWindow;

    [ObservableProperty] private string _state = "SLEEP";

    private readonly SpeechToTextService _speechToTextService;
    private readonly CommunicationAiService _communicationAiService;

    public MainViewModel(SpeechToTextService speechToTextService, CommunicationAiService communicationAiService) {
        _speechToTextService = speechToTextService;
        _communicationAiService = communicationAiService;
        _speechToTextService.OnSpeechRecognized += async (text) => {
            if (!string.IsNullOrWhiteSpace(text)) {
                try {
                    await _communicationAiService.GetRequestUser(text);
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }; // отправка запроса к AI
        _speechToTextService.OnWakeUp += (text) => State = text; // проснулся|слушает
        _speechToTextService.OnProccessingText += (text) => State = text; // обрабатывает (Speech => Text)
        _communicationAiService.OnExecute += (text) => State = text; // выполняет
        _communicationAiService.OnResult += (text) => State = text; // результат (Done|Warning|Error)
        _speechToTextService.OnTimeout += async (text) => {
            State = text;
            await Task.Delay(2000);
            State = "SLEEP";
        }; // уснул

        ContextMenu.Items.Add(OpenMenu);
        ContextMenu.Items.Add(ExitMenu);

        _window.Closing += (s, e) =>
        {
            e.Cancel = true;
            _window.Hide();
        };

    }

    public void Dispose() {
        
    }

    private void Closing()
    {
        Application.Current.Shutdown();
    }

    private void Show()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}

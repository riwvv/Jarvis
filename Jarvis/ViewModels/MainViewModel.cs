using CommunityToolkit.Mvvm.ComponentModel;

namespace Jarvis.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable {
    [ObservableProperty] private string _state = "SLEEP";

    public MainViewModel() {

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

        _speechToTextService.Start();
    }

    public void Dispose() {
        
    }
}

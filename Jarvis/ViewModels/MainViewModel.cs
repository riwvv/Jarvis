using CommunityToolkit.Mvvm.ComponentModel;

namespace Jarvis.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable {
    [ObservableProperty] private string _state = "SLEEP";

    public MainViewModel() {

    }

    public void Dispose() {
        
    }
}

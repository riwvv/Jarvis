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
    

    [ObservableProperty] private ContextMenu _contextMenu = new();

    [ObservableProperty] private MenuItem _openMenu = new();
    [ObservableProperty] private MenuItem _exitMenu = new();

    

    public MainViewModel() {
        OpenMenu.Header = "Открыть";
        OpenMenu.Click += (s, e) => Show();

        ExitMenu.Header = "Выход";
        ExitMenu.Click += (s, e) => Closing();

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

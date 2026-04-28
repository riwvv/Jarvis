using Hardcodet.Wpf.TaskbarNotification;
using Jarvis.Plugins;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Timers;
using System.IO;

namespace Jarvis;

public partial class App : Application {
    public static Kernel? KernelCore { get; private set; }
    public static IServiceProvider? Services { get; private set; }
    private IHost? _host;

    private TaskbarIcon _trayIcon;
    private MainWindow _mainWindow;
    private System.Timers.Timer _autoHideTimer;
    private bool _isAutoMode = true;

    public App() {
        InitializedSemanticKernel();
        InitializedDI();
    }

    private void InitializedSemanticKernel() {
        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();

        builder.AddOpenAIChatCompletion(modelId: "qwen2.5:7b", endpoint: new Uri("http://localhost:11434/v1"), apiKey: "dummy");
        KernelCore = builder.Build();
    }


    private void InitializedDI() {
        if (KernelCore == null)
            throw new InvalidOperationException("KernelCore не инициализирован!");

        _host = Host.CreateDefaultBuilder().ConfigureServices((context, services) => {
            services.AddSingleton(KernelCore!);
            services.AddSingleton<SpeechToTextService>();
            services.AddSingleton<TextToSpeechService>();
            services.AddSingleton<CommunicationAiService>();

            services.AddSingleton<MainViewModel>();

            services.AddSingleton<MainWindow>();
        }).Build();

        Services = _host.Services;
    }

    private void InitializeSystemTray()
    {

        string iconPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Images", "JarvisImg.ico"));
        _trayIcon = new TaskbarIcon
        {
            Icon = new Icon(iconPath),
            ToolTipText = "Jarvis"
        };

        var contextMenu = new ContextMenu();

        var openItem = new MenuItem { Header = "Открыть" };
        openItem.Click += (s, e) => ShowNormalWindow();

        var autoModeItem = new MenuItem { Header = "Авто-режим" };
        autoModeItem.Click += (s, e) => SetAutoMode();

        var exitItem = new MenuItem { Header = "Выход" };
        exitItem.Click += (s, e) => ExitApplication();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(autoModeItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;

    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;

        HideToTray();
    }

    private void SetupAutoHideTimer()
    {
        _autoHideTimer = new System.Timers.Timer(2500);
        _autoHideTimer.Elapsed += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_isAutoMode && !_mainWindow.IsActive)
                {
                    HideToTray();
                }
            });
        };
        _autoHideTimer.AutoReset = false;
    }
    private void ShowNormalWindow()
    {
        _isAutoMode = false;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();
    }

    private void SetAutoMode()
    {
        _isAutoMode = true;
        HideToTray();
    }

    private void HideToTray()
    {
        _mainWindow.Hide();
        _mainWindow.ShowInTaskbar = false;
    }
    public void ShowAsOverlay()
    {
        if (!_isAutoMode) return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Topmost = true;
        _mainWindow.ShowInTaskbar = false;

        _mainWindow.Left = SystemParameters.WorkArea.Width - _mainWindow.Width - 20;
        _mainWindow.Top = SystemParameters.WorkArea.Height - _mainWindow.Height - 20;

        _mainWindow.Activate();
        _autoHideTimer.Start();
    }

    private void ExitApplication()
    {
        _autoHideTimer?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }

    public void OnVoiceWakeWord()
    {
        Dispatcher.Invoke(() => ShowAsOverlay());
    }


    protected override async void OnStartup(StartupEventArgs e) {
        await _host!.StartAsync();
        _mainWindow = _host.Services.GetRequiredService<MainWindow>();
        _mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        _mainWindow.Closing += MainWindow_Closing;

        base.OnStartup(e);
        InitializeSystemTray();
        SetupAutoHideTimer();
        
    }

    protected override async void OnExit(ExitEventArgs e) {
        await _host!.StopAsync();
        _host.Dispose();

        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Jarvis")) {
            if (proc.Id != Environment.ProcessId)
                proc.Kill();
        }

        base.OnExit(e);
    }
}

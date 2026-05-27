using Hardcodet.Wpf.TaskbarNotification;
using Jarvis.Extensions;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Serilog;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Jarvis;

public partial class App : Application {
    private IHost? _host;
    private Kernel? _kernelCore;
    private IConfiguration? _configuration;
    private SpeechToTextService? _speechToTextService;

    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private System.Timers.Timer? _autoHideTimer;
    private bool _isAutoMode = true;

    public App() {
        LoadConfiguration();
        _host = CreateHostBuilder().Build();
    }

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => {
            config.ReadFrom.Configuration(_configuration!)
                .WriteTo.Debug()
                .WriteTo.File("logs/jarvis-logs.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
        })
        .ConfigureServices((context, services) => {
            services.AddSingleton(_configuration!);

            services.AddSemanticKernel(_configuration!);
            services.AddOllamaHealthCheck(_configuration!);

            services.AddConfigure(_configuration!)
                    .AddServices()
                    .AddViewModels()
                    .AddViews();
        });

    private void LoadConfiguration() {
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables();
        _configuration = builder.Build();
    }

    private void InitializeSystemTray() {
        if (_trayIcon != null) return;
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "JarvisImg.ico");
        _trayIcon = new TaskbarIcon {
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

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
        e.Cancel = true;

        HideToTray();
    }

    private void SetupAutoHideTimer() {
        _autoHideTimer = new System.Timers.Timer(2500);
        _autoHideTimer.Elapsed += (s, e) => {
            Dispatcher.Invoke(() => {
                if (_isAutoMode && !_mainWindow!.IsActive) {
                    HideToTray();
                }
            });
        };
        _autoHideTimer.AutoReset = false;
    }

    private void ShowNormalWindow() {
        _isAutoMode = false;
        _mainWindow!.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Topmost = false;
        _mainWindow.Activate();
    }

    private void SetAutoMode() {
        _isAutoMode = true;
        HideToTray();
    }

    private void HideToTray() {
        _mainWindow!.Hide();
        _mainWindow.ShowInTaskbar = false;
    }

    private void ShowAsOverlay() {
        if (!_isAutoMode) return;

        _mainWindow!.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Topmost = true;
        _mainWindow.ShowInTaskbar = false;

        _mainWindow.Left = SystemParameters.WorkArea.Width - _mainWindow.Width - 20;
        _mainWindow.Top = SystemParameters.WorkArea.Height - _mainWindow.Height - 20;

        _mainWindow.Activate();
        _autoHideTimer?.Start();
    }

    private void ExitApplication() {
        _autoHideTimer?.Dispose();
        _trayIcon?.Dispose();

        if (_speechToTextService != null) {
            _speechToTextService.OnWakeWordDetected -= OnVoiceWakeWord;
        }

        Shutdown();
    }

    private void OnVoiceWakeWord() => Dispatcher.Invoke(ShowAsOverlay);

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        if (_host == null) {
            Log.Error("При запуске хост равен нулю.");
            Shutdown();
            return;
        }

        try {
            await _host!.StartAsync();

            Log.Information("Приложение запускается...");

            _kernelCore = await _host.Services.InitializeKernelWithValidationAsync();

            if (_kernelCore == null) {
                Shutdown();
                return;
            }

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            _mainWindow.Closing += MainWindow_Closing;

            try {
                _speechToTextService = _host.Services.GetRequiredService<SpeechToTextService>();
                _speechToTextService.OnWakeWordDetected += OnVoiceWakeWord;
            }
            catch (Exception ex) {
                Log.Warning(ex, "Не удалось подписаться на событие пробуждения");
            }

            InitializeSystemTray();
            SetupAutoHideTimer();

            Log.Information("Приложение успешно запущено");
        }
        catch (Exception ex) {
            Log.Error(ex, "Критическая ошибка при запуске приложения");

            MessageBox.Show(
                $"Ошибка запуска: {ex.Message}\n\nПодробности в логах: logs/jarvis-logs.txt",
                "Критическая ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        Log.Information("Приложение завершает работу...");

        try {
            if (_host != null) {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при остановке хоста");
        }

        var currentProcessId = Environment.ProcessId;
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Jarvis")) {
            if (proc.Id != currentProcessId) {
                try {
                    proc.Kill();
                    Log.Information($"Завершен старый процесс с ID: {proc.Id}");
                }
                catch (Exception ex) {
                    Log.Warning(ex, $"Не удалось завершить процесс {proc.Id}");
                }
            }
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) {
        ExitApplication();
        base.OnSessionEnding(e);
    }
}
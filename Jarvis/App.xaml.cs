using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using System.Windows.Controls;
using System.Windows;
using System.Drawing;
using System.IO;
using System.Net.Http;
using Jarvis.Plugins;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Configuration;
using Jarvis.Extensions;

namespace Jarvis;

public partial class App : Application {
    private IHost? _host;
    private Kernel? _kernelCore;
    private IConfiguration? _configuration;
    private TrayService? _trayService;
    private SpeechToTextService _speechToTextService;

    private MainWindow? _mainWindow;

    public App() => _host = CreateHostBuilder().Build();

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => {
            config.ReadFrom.Configuration(context.Configuration)
                .WriteTo.Debug()
                .WriteTo.File("logs/jarvis-logs.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
        })
        .ConfigureServices((context, services) => {
            services.AddSemanticKernel(context.Configuration);
            services.AddOllamaHealthCheck();

            services.AddConfigure(context.Configuration)
                    .AddServices()
                    .AddViewModels()
                    .AddViews();
        });

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (_host == null)
        {
            Log.Error("При запуске хост равен нулю.");
            Shutdown();
            return;
        }

        try
        {
            await _host!.StartAsync();

            Log.Information("Приложение запускается...");

            _kernelCore = await _host.Services.InitializeKernelWithValidationAsync();

            if (_kernelCore == null)
            {
                Shutdown();
                return;
            }

            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            _trayService = _host.Services.GetRequiredService<TrayService>();
            _trayService.Initialize(_mainWindow);
            var aiService = _host.Services.GetRequiredService<CommunicationAiService>();

            aiService.SetTrayService(_trayService);

            aiService.OnResult += (status) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _trayService?.HideOverlayAfterCommand();
                });
            };

            var speechService = _host.Services.GetRequiredService<SpeechToTextService>();
            speechService.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(() => _trayService?.ShowAsOverlay());
            };

            Log.Information("Приложение успешно запущено");
        }
        catch (Exception ex)
        {
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
        base.OnSessionEnding(e);
    }
}
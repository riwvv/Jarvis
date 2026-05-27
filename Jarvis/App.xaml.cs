using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Extensions;

namespace Jarvis;

public partial class App : Application {
    private IHost? _host;

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

            var kernelCore = await _host.Services.InitializeKernelWithValidationAsync();
            if (kernelCore == null) {
                Shutdown();
                return;
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var trayService = _host.Services.GetRequiredService<TrayService>();
            var aiService = _host.Services.GetRequiredService<CommunicationAiService>();
            var speechService = _host.Services.GetRequiredService<SpeechToTextService>();

            mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            trayService.Initialize(mainWindow);
            aiService.SetTrayService(trayService);
            aiService.OnResult += (status) => Dispatcher.Invoke(() => trayService.HideOverlayAfterCommand());
            speechService.OnWakeWordDetected += () => Dispatcher.Invoke(() => trayService.ShowAsOverlay());

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
}

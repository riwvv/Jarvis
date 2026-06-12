using Jarvis.Extensions;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Jarvis;

public partial class App : Application {
    private static readonly Mutex _mutex = new(true, "Jarvis_Unique_App_Mutex");
    private readonly IHost? _host;

    public App() => _host = CreateHostBuilder().Build();

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => config.ReadFrom.Configuration(context.Configuration)
                .WriteTo.Debug()
                .WriteTo.File("logs/jarvis-logs.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7))
        .ConfigureServices((context, services) => services.AddServices()
                .AddSemanticKernel(context.Configuration)
                .AddOllamaHealthCheck()
                .AddConfigure(context.Configuration)
                .AddHttpClients()
                .AddViewModels()
                .AddViews());

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        if (!_mutex.WaitOne(TimeSpan.Zero, true)) {
            MessageBox.Show("Jarvis уже запущен!", "Предупреждение",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        if (_host == null) {
            Log.Error("При запуске хост равен нулю.");
            Shutdown();
            return;
        }

        try {
            await _host.StartAsync();
            Log.Information("Приложение запускается...");

            var initService = _host.Services.GetRequiredService<InitializationNotificationService>();
            await initService.InitializeAllAsync();

            var kernelCore = await _host.Services.InitializeKernelWithValidationAsync();
            if (kernelCore == null) {
                Shutdown();
                return;
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();

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

        _mutex.ReleaseMutex();
        _mutex.Dispose();

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

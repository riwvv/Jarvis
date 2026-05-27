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

namespace Jarvis;

public partial class App : Application {
    private IHost? _host;
    private Kernel? _kernelCore;
    private IConfiguration? _configuration;
    private TrayService? _trayService;

    private MainWindow? _mainWindow;

    public App() {
        LoadConfiguration();
        InitializedSemanticKernel();
        InitializedDI();

    }

    private void LoadConfiguration() {
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables();
        _configuration = builder.Build();
    }

    private async void InitializedSemanticKernel() {
        if (!CheckOllamaConnectSync()) {
            var result = MessageBox.Show(
                "Ollama не запущена!\n\n" +
                "Пожалуйста, запустите Ollama из меню 'Пуск' или скачайте с ollama.ai\n\n" +
                "Попробовать снова?",
                "Ошибка подключения",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes) {
                if (!CheckOllamaConnectSync()) {
                    Environment.Exit(1);
                }
            }
            else {
                Environment.Exit(1);
            }
        }
        var aiSettings = _configuration!.GetSection("AISettings").Get<AISettings>();

        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FilePlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();
        builder.Plugins.AddFromType<PornoPlugin>();

        builder.AddOpenAIChatCompletion(modelId: aiSettings!.ModelId, endpoint: new Uri(aiSettings.Endpoint), apiKey: aiSettings.ApiKey);
        _kernelCore = builder.Build();
    }

    private void InitializedDI() {
        if (_kernelCore == null)
            throw new InvalidOperationException("KernelCore не инициализирован!");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((context, config) => {
                config.ReadFrom.Configuration(_configuration!)
                .WriteTo.Debug()
                .WriteTo.File("logs/jarvis-logs.txt", rollingInterval: RollingInterval.Day);
            })
            .ConfigureServices((services) => {
                services.AddMemoryCache();

                services.Configure<AISettings>(_configuration!.GetSection("AISettings"));
                services.Configure<SpeechSettings>(_configuration!.GetSection("SpeechSettings"));

                services.AddSingleton(_kernelCore!);
                services.AddSingleton<SpeechToTextService>();
                services.AddSingleton<TextToSpeechService>();
                services.AddSingleton<CommunicationAiService>();
                services.AddSingleton<VectorMemoryService>();
                services.AddSingleton<TrayService>();

                services.AddSingleton<MainViewModel>();

                services.AddSingleton<MainWindow>();
            }).Build();
    }

    private bool CheckOllamaConnectSync() {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        try {
            var response = client.GetAsync("http://localhost:11434/api/tags").GetAwaiter().GetResult(); ;
            return response.IsSuccessStatusCode;
        }
        catch(Exception ex) {
            MessageBox.Show(ex.Message);
            return false;
        }
    }

    private async void CheckOllamaConnect() {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        try {
            var response = await client.GetAsync("http://localhost:11434/api/tags");
            if (!response.IsSuccessStatusCode)
                throw new Exception();
        }
        catch {
            MessageBox.Show("Ollama не запущена! Пожалуйста, запустите Ollama и попробуйте снова.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
        }
    }

    protected override async void OnStartup(StartupEventArgs e) {
        try {
            await _host!.StartAsync();
            _mainWindow = _host.Services.GetRequiredService<MainWindow>();
            _mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
            _trayService = _host.Services.GetRequiredService<TrayService>();
            var aiService = _host.Services.GetRequiredService<CommunicationAiService>();

            _trayService.Initialize(_mainWindow);

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

            base.OnStartup(e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при запуске");
            MessageBox.Show($"Ошибка запуска: {ex.Message}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
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
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Jarvis.Configuration;

namespace Jarvis;

public partial class App : Application {
    public static IServiceProvider? Services { get; private set; }
    
    private IHost? _host;
    private Kernel? _kernelCore;
    private IConfiguration? _configuration;

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

    private void InitializedSemanticKernel() {
        var aiSettings = _configuration!.GetSection("AISettings").Get<AISettings>();

        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FilePlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();

        builder.AddOpenAIChatCompletion(modelId: aiSettings!.ModelId, endpoint: new Uri(aiSettings.Endpoint), apiKey: aiSettings.ApiKey);
        _kernelCore = builder.Build();
    }

    private void InitializedDI() {
        if (_kernelCore == null)
            throw new InvalidOperationException("KernelCore не инициализирован!");

        _host = Host.CreateDefaultBuilder().ConfigureServices((context, services) => {
            services.Configure<AISettings>(_configuration!.GetSection("AISettings"));
            services.Configure<SpeechSettings>(_configuration!.GetSection("SpeechSettings"));

            services.AddSingleton(_kernelCore!);
            services.AddSingleton<SpeechToTextService>();
            services.AddSingleton<TextToSpeechService>();
            services.AddSingleton<CommunicationAiService>();

            services.AddSingleton<MainViewModel>();

            services.AddSingleton<MainWindow>();
        }).Build();

        Services = _host.Services;
    }

    protected override async void OnStartup(StartupEventArgs e) {
        await _host!.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();

        base.OnStartup(e);
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

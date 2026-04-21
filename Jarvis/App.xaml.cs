using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System.Windows;

namespace Jarvis;

public partial class App : Application {
    public static Kernel? KernelCore { get; private set; }
    public static IServiceProvider? Services { get; private set; }
    private readonly IHost _host;

    public App() {
        // регистрация DI
        _host = Host.CreateDefaultBuilder().ConfigureServices((context, services) => {
            // тут добавить все сервисы
            // services.AddSingleton<Service>(); // типо так
            services.AddSingleton<SpeechToTextService>();

            services.AddSingleton<MainViewModel>();

            services.AddSingleton<MainWindow>();
        }).Build();

        Services = _host.Services;

        // регистрация SemanticKernel
        InitializedSemanticKernel();
    }

    private void InitializedSemanticKernel() {
        var builder = Kernel.CreateBuilder();

        // builder.Plugins.AddFromType<Plugin>(); // тут добавляем все плагины

        builder.AddOpenAIChatCompletion(modelId: "qwen2.5:7b", endpoint: new Uri("http://localhost/11434/v1"), apiKey: "dummy");
        KernelCore = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e) {
        await _host.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e) {
        await _host.StopAsync();
        _host.Dispose();

        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Jarvis")) {
            if (proc.Id != Environment.ProcessId)
                proc.Kill();
        }

        base.OnExit(e);
    }
}

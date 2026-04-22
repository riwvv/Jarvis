using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using System.Windows;
using Jarvis.Models;

namespace Jarvis;

public partial class App : Application {
    public static Kernel? KernelCore { get; private set; }
    public static IServiceProvider? Services { get; private set; }
    private IHost _host;

    public App() {
        // регистрация DI
        InitializedDI();

        // регистрация SemanticKernel
        InitializedSemanticKernel();
    }

    private void InitializedDI() {
        _host = Host.CreateDefaultBuilder().ConfigureServices((context, services) => {
            // services.AddSingleton<Service>(); // таким же образом регистрируем все будущие сервисы
            services.AddSingleton<SpeechToTextService>();

            services.AddSingleton<CommunicationAiService>();

            services.AddSingleton<MainViewModel>();

            services.AddSingleton<MainWindow>();
        }).Build();

        Services = _host.Services;
    }

    private void InitializedSemanticKernel() {
        var builder = Kernel.CreateBuilder();

        //builder.Plugins.AddFromType<Plugin>(); // таким же образом регистрируем все будущие плагины
        builder.Plugins.AddFromType<InstalledApplication>(); 
        builder.Plugins.AddFromType<SystemAudioPlugin>(); 

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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Jarvis.Views.Windows;
using Jarvis.Configuration;
using Jarvis.ViewModels;
using Jarvis.Interfaces;
using Jarvis.Services;
using Jarvis.Plugins;

namespace Jarvis.Extensions;

public static class AppExtensions {
    public static IServiceCollection AddConfigure(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<AISettings>(configuration.GetSection("AISettings"));
        services.Configure<STTSettings>(configuration.GetSection("STTSettings"));
        services.Configure<TTSSettings>(configuration.GetSection("TTSSettings"));

        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services) {
        services.AddSingleton<InitializationNotificationService>();
        services.AddSingleton<TextToSpeechService>();
        services.AddSingleton<IRagMemoryService, RagMemoryService>();
        services.AddSingleton<SpeechToTextService>();
        services.AddSingleton<CommunicationAiService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<ReminderService>();
        services.AddTransient<WeatherPlugin>();
        services.AddHostedService<VoiceInstallerService>();

        return services;
    }

    public static IServiceCollection AddViewModels(this IServiceCollection services) {
        services.AddSingleton<MainViewModel>();

        return services;
    }

    public static IServiceCollection AddViews(this IServiceCollection services) {
        services.AddSingleton<MainWindow>();

        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services, IConfiguration configuration) {
        services.AddHttpClient("Open-Meteo", client => {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddHttpClient("Ollama", client => {
            client.Timeout = TimeSpan.FromSeconds(120);
            client.BaseAddress = new Uri(configuration.GetSection("AISettings").Get<AISettings>()!.Endpoint);
            client.DefaultRequestHeaders.ConnectionClose = true;
        });

        return services;
    }
}

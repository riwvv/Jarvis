using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Jarvis.Configuration;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Interfaces;

namespace Jarvis.Extensions;

public static class AppExtensions {
    public static IServiceCollection AddConfigure(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<AISettings>(configuration.GetSection("AISettings"));
        services.Configure<STTSettings>(configuration.GetSection("SpeechSettings"));

        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services) {
        services.AddSingleton<SpeechToTextService>();
        services.AddSingleton<TextToSpeechService>();
        services.AddSingleton<IRagMemoryService, RagMemoryService>();
        services.AddSingleton<CommunicationAiService>();
        services.AddSingleton<TrayService>();
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
}

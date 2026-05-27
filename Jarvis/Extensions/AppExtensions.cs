using Jarvis.Configuration;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Extensions;

public static class AppExtensions {
    public static IServiceCollection AddConfigure(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<AISettings>(configuration.GetSection("AISettings"));
        services.Configure<SpeechSettings>(configuration.GetSection("SpeechSettings"));

        return services;
    }

    public static IServiceCollection AddServices(this IServiceCollection services) {
        services.AddSingleton<SpeechToTextService>();
        services.AddSingleton<TextToSpeechService>();
        services.AddSingleton<VectorMemoryService>();
        services.AddSingleton<CommunicationAiService>();

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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.Net.Http;
using Jarvis.Configuration;
using Jarvis.Interfaces;
using Jarvis.Plugins;
using Jarvis.Services;

namespace Jarvis.Extensions;

public static class SemanticKernelExtensions {
    public static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration configuration) {
        var aiSettings = configuration.GetSection("AISettings").Get<AISettings>();

        if (aiSettings == null)
            throw new InvalidOperationException("AISettings not found in configuration");

        services.AddSingleton(aiSettings);

        services.AddSingleton(sp => {
            var ragMemory = sp.GetRequiredService<IRagMemoryService>();
            var reminderService = sp.GetRequiredService<ReminderService>();
            var weatherPlugin = sp.GetRequiredService<WeatherPlugin>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama");
            return BuildKernel(aiSettings, ragMemory, reminderService, weatherPlugin, httpClient);
        });

        services.AddTransient<ApplicationPlugin>();
        services.AddTransient<SystemAudioPlugin>();
        services.AddTransient<BrowserPlugin>();
        services.AddTransient<FilePlugin>();
        services.AddTransient<SystemCommandPlugin>();
        services.AddTransient<PrankPlugin>();
        services.AddTransient<MediaPlayerPlugin>();
        services.AddTransient<ClipboardPlugin>();
        services.AddTransient<WeatherPlugin>();
        services.AddTransient<MiniGamePlugin>();
        services.AddTransient<FileSystemPlugin>();

        return services;
    }

    private static Kernel BuildKernel(AISettings aiSettings, IRagMemoryService ragMemory, ReminderService reminderService, WeatherPlugin weatherPlugin, HttpClient httpClient) {
        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FilePlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();
        builder.Plugins.AddFromType<MediaPlayerPlugin>();
        builder.Plugins.AddFromType<PrankPlugin>();
        builder.Plugins.AddFromType<ClipboardPlugin>();
        builder.Plugins.AddFromType<MiniGamePlugin>();
        builder.Plugins.AddFromType<FileSystemPlugin>();

        builder.Plugins.AddFromObject(weatherPlugin);
        builder.Plugins.AddFromObject(new ReminderPlugin(reminderService));
        builder.Plugins.AddFromObject(new RagPlugin(ragMemory));

        builder.AddOllamaChatCompletion(
            modelId: aiSettings.ModelId,
            httpClient: httpClient);

        return builder.Build();
    }
}
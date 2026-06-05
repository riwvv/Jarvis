using Jarvis.Configuration;
using Jarvis.Interfaces;
using Jarvis.Plugins;
using Jarvis.Services;
using Jarvis.Wrapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.Net.Http;

namespace Jarvis.Extensions;

public static class SemanticKernelExtensions {
    public static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration configuration) {
        var aiSettings = configuration.GetSection("AISettings").Get<AISettings>();

        if (aiSettings == null)
            throw new InvalidOperationException("AISettings not found in configuration");

        services.AddSingleton(aiSettings);

        services.AddSingleton(sp => {
            var ragMemory = sp.GetRequiredService<IRagMemoryService>();
            return BuildKernel(aiSettings, ragMemory, sp);
        });

        services.AddTransient<ApplicationPlugin>();
        services.AddTransient<SystemAudioPlugin>();
        services.AddTransient<BrowserPlugin>();
        services.AddTransient<FilePlugin>();
        services.AddTransient<SystemCommandPlugin>();
        services.AddTransient<PrankPlugin>();
        services.AddTransient<ReminderPlugin>();

        return services;
    }

    private static Kernel BuildKernel(AISettings aiSettings, IRagMemoryService ragMemory, IServiceProvider sp) {
        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FilePlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();
        builder.Plugins.AddFromType<PrankPlugin>();

        var reminderPlugin = sp.GetRequiredService<ReminderPlugin>();
        builder.Plugins.AddFromObject(reminderPlugin);

        builder.Plugins.AddFromObject(new RagPlugin(ragMemory));

        builder.AddOpenAIChatCompletion(
            modelId: aiSettings.ModelId,
            endpoint: new Uri(aiSettings.Endpoint),
            apiKey: aiSettings.ApiKey,
            httpClient: new HttpClient(new OllamaContextHandler(16384)));

        return builder.Build();
    }
}
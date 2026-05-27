using Jarvis.Configuration;
using Jarvis.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Jarvis.Extensions;

public static class SemanticKernelExtensions {
    public static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration configuration) {
        var aiSettings = configuration.GetSection("AISettings").Get<AISettings>();

        if (aiSettings == null)
            throw new InvalidOperationException("AISettings not found in configuration");

        services.AddSingleton(aiSettings);
        services.AddSingleton(sp => BuildKernel(aiSettings));

        services.AddTransient<ApplicationPlugin>();
        services.AddTransient<SystemAudioPlugin>();
        services.AddTransient<BrowserPlugin>();
        services.AddTransient<FilePlugin>();
        services.AddTransient<SystemCommandPlugin>();
        services.AddTransient<PornoPlugin>();

        return services;
    }

    private static Kernel BuildKernel(AISettings aiSettings) {
        var builder = Kernel.CreateBuilder();

        builder.Plugins.AddFromType<ApplicationPlugin>();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.Plugins.AddFromType<BrowserPlugin>();
        builder.Plugins.AddFromType<FilePlugin>();
        builder.Plugins.AddFromType<SystemCommandPlugin>();
        builder.Plugins.AddFromType<PornoPlugin>();

        builder.AddOpenAIChatCompletion(modelId: aiSettings!.ModelId, endpoint: new Uri(aiSettings.Endpoint), apiKey: aiSettings.ApiKey);
        return builder.Build();
    }
}

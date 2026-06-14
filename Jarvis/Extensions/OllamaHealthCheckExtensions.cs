using Microsoft.Extensions.DependencyInjection;
using Jarvis.Interfaces;
using Jarvis.Wrapper;

namespace Jarvis.Extensions;

public static class OllamaHealthCheckExtensions {
    public static IServiceCollection AddOllamaHealthCheck(this IServiceCollection services) {
        services.AddSingleton<IOllamaHealthCheck, OllamaHealthCheck>();
        services.AddSingleton<IOllamaConnectionValidator, OllamaConnectionValidator>();

        return services;
    }

    public static async Task<bool> ValidateOllamaConnectionAsync(this IServiceProvider sp) {
        var validator = sp.GetRequiredService<IOllamaConnectionValidator>();
        return await validator.ValidateWithRetryAsync();
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Serilog;

namespace Jarvis.Extensions;

public static class KernelInitializationExtensions {
    public static async Task<Kernel?> InitializeKernelWithValidationAsync(this IServiceProvider sp) {
        var isValid = await sp.ValidateOllamaConnectionAsync();

        if (!isValid) {
            Log.Error("Проверка подключения Ollama завершилась неудачей");
            return null;
        }

        var kernel = sp.GetRequiredService<Kernel>();
        Log.Information("Semantic Kernel успешно инициализирован");
        return kernel;
    }
}

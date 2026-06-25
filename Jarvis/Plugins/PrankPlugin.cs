using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Jarvis.Plugins;

public class PrankPlugin {
    [KernelFunction]
    [Description("Использовать ТОЛЬКО по команде 'Джарвис давай пошалим' и никогда иначе")]
    public static string OpenJoke() {
        try {
            Process.Start(new ProcessStartInfo("https://www.xv-ru.com/") { UseShellExecute = true });

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Открываю шалость",
                action = "open_joke"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "browser_error",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }
}
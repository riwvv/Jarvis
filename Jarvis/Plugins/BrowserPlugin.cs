using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Jarvis.Plugins;

public class BrowserPlugin {
    [KernelFunction]
    [Description("Открывает URL в браузере по умолчанию")]
    public static string OpenUrl([Description("Полный URL адрес, включая https://")] string url) {
        try {
            url = url.Trim();

            if (string.IsNullOrWhiteSpace(url)) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "empty_url",
                    description = "URL не может быть пустым"
                });
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "invalid_url",
                    description = $"URL '{url}' имеет неверный формат"
                });
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Страница открыта",
                fillUrl = url
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "browser_error",
                description = $"Ошибка открытия: {ex.Message}",
                fillUrl = url
            });
        }
    }
}

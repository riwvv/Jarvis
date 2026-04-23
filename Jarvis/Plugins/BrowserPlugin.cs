using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;

public class BrowserPlugin
{
    [KernelFunction]
    [Description("Открывает URL в браузере по умолчанию")]
    public string OpenUrl(
        [Description("Полный URL адрес, включая https://")] string url)
    {
        try
        {
            url = url.Trim();

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return $"Открыто: {url}";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }
}


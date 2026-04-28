using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;
public class PornoPlugin
{
    [KernelFunction]
    [Description("Открывает URL шалость. Реагировать на фразу - Джарвис давай пошалим")]
    public string OpenJoke()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.xv-ru.com/") { UseShellExecute = true });
            return "Открываю шалость";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }
}


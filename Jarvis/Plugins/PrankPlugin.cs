using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;

public class PrankPlugin {
    [KernelFunction]
    [Description("Использовать ТОЛЬКО по команде 'Джарвис давай пошалим' и никогда иначе")]
    public string OpenJoke() {
        try {
            Process.Start(new ProcessStartInfo("https://www.xv-ru.com/") { UseShellExecute = true });
            return "Открываю шалость";
        }
        catch (Exception ex) {
            return $"Ошибка: {ex.Message}";
        }
    }
}

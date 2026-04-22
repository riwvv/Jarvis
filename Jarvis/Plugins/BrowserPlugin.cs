using Microsoft.SemanticKernel;
using System;
using System.ComponentModel;
using System.Security.Policy;

namespace Jarvis.Plugins;
public class BrowserPlugin
{
    [KernelFunction]
    [Description("Ищет информацию в Google и открывает результаты в браузере")]
    public string SearchGoogle([Description("Поисковый запрос или URL")] string query)
    {
        try
        {
            return $"Открыт URL - ";
        }
        catch(Exception ex)
        {
            return $"Ошибка при поиске - {ex.Message}";
        }
    }
}


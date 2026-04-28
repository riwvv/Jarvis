using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;
public class SystemCommandPlugin
{
    [KernelFunction]
    [Description("Выполняет команду через CMD. Для выключения: shutdown, перезагрузки: shutdown /r, блокировки: rundll32, свернуть окна: powershell команду")]
    public async Task<string> ExecuteCMD([Description("Полная CMD команда со всеми аргументами")] string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
                return "Не удалось выполнить команду:(";

            // Асинхронное чтение
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (!string.IsNullOrEmpty(error))
                return $"Ошибка: {error}";

            return string.IsNullOrEmpty(output) ? "Готово" : output.Trim();
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Выполняет команду через PowerShell")]
    public async Task<string> ExecutePowerShell(
        [Description("PowerShell команда")] string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
                return "Не удалось выполнить команду";

            // Асинхронное чтение
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (!string.IsNullOrEmpty(error))
                return $"Ошибка: {error}";

            return string.IsNullOrEmpty(output) ? "Готово" : output.Trim();
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Получение имени текущего пользователя Windows")]
    public string GetCurrentUsername() => Environment.UserName;
}


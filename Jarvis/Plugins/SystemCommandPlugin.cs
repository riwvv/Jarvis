using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;

public class SystemCommandPlugin
{
    [KernelFunction]
    [Description("Выполняет CMD команду. Только для команд которые не закрыты другими функциями: ipconfig, ping, tasklist")]
    public async Task<string> ExecuteCMD(
        [Description("CMD команда")] string command)
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
            if (process == null) return "Не удалось выполнить";

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error)) return $"Ошибка: {error}";
            return string.IsNullOrEmpty(output) ? "Готово" : output.Trim();
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Выполняет PowerShell команду. Используй для: Clear-RecycleBin, Get-Date, Get-Process")]
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
            if (process == null) return "Не удалось выполнить";

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string output = await outputTask;
            string error = await errorTask;

            if (!string.IsNullOrEmpty(error)) return $"Ошибка: {error}";
            return string.IsNullOrEmpty(output) ? "Готово" : output.Trim();
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Блокирует компьютер. Вызови эту функцию когда пользователь говорит: заблокируй экран, блокировка, lock screen")]
    public string LockScreen()
    {
        try
        {
            Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            return "Экран заблокирован";
        }
        catch (Exception ex)
        {
            return $"Ошибка блокировки: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Сворачивает все окна. Вызови когда пользователь говорит: сверни окна, покажи рабочий стол, minimize all")]
    public string MinimizeAllWindows()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(New-Object -ComObject Shell.Application).MinimizeAll()\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return "Все окна свёрнуты";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает диспетчер задач. Вызови когда пользователь говорит: открой диспетчер задач, task manager")]
    public string OpenTaskManager()
    {
        try
        {
            Process.Start("taskmgr.exe");
            return "Диспетчер задач открыт";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Выключает компьютер. Вызови когда пользователь говорит: выключи компьютер, shutdown")]
    public string Shutdown()
    {
        try
        {
            Process.Start("shutdown", "/s /t 5 /c \"Джарвис выключает компьютер\"");
            return "Выключаю компьютер через 5 секунд";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Перезагружает компьютер. Вызови когда пользователь говорит: перезагрузи, restart, reboot")]
    public string Restart()
    {
        try
        {
            Process.Start("shutdown", "/r /t 5 /c \"Джарвис перезагружает компьютер\"");
            return "Перезагружаю компьютер через 5 секунд";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Отменяет выключение. Вызови когда пользователь говорит: отмени выключение, cancel shutdown")]
    public string CancelShutdown()
    {
        try
        {
            Process.Start("shutdown", "/a");
            return "Выключение отменено";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }
}
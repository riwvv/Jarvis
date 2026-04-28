using Microsoft.SemanticKernel;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.IO;
using Jarvis.Models;

namespace Jarvis.Plugins;

public class ApplicationPlugin
{
    private readonly StringComparer _comparer = StringComparer.OrdinalIgnoreCase;

    private readonly string[] _registryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    // Системные пути для фильтрации
    private readonly string[] _systemPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET"),
        @"C:\Program Files\WindowsApps",
        @"C:\Program Files\ModifiableWindowsApps"
    ];

    // Ключевые слова для фильтрации системных компонентов
    private readonly string[] _systemKeywords =
    [
        "Microsoft Visual C++",
        "Microsoft .NET",
        "Microsoft Edge",
        "Microsoft OneDrive",
        "Microsoft Update Health",
        "Windows SDK",
        "Windows Software Development",
        "Driver",
        "Update for",
        "Security Update",
        "Hotfix",
        "Service Pack"
    ];

    [KernelFunction]
    [Description("Создаёт на рабочем столе файл 'Список приложений.txt' со списком всех установленных пользовательских приложений и путями к ним")]
    public async Task<string> GetInstalledApplications()
    {
        try
        {
            var applications = ScanRegistry();

            if (applications.Count == 0)
            {
                return "Пользовательские приложения не найдены или произошла ошибка чтения реестра.";
            }

            var content = BuildReportContent(applications);
            string filePath = await SaveReportToDesktop(content);

            return $"Файл успешно создан: {filePath}\nНайдено приложений: {applications.Count}";
        }
        catch (Exception ex)
        {
            return $"Ошибка при создании списка прииложений: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает файл или приложение по указанному полному пути")]
    public string OpenFileOrApplication(
        [Description("Полный путь к исполняемому файлу или документу")] string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return "Ошибка: Путь не указан";

        if (!File.Exists(fullPath))
            return $"Ошибка: Файл не найден по пути '{fullPath}'";

        try
        {
            var startInfo = new ProcessStartInfo(fullPath)
            {
                UseShellExecute = true,
                Verb = "open"
            };

            Process.Start(startInfo);
            return $"Успешно открыто: {Path.GetFileName(fullPath)}";
        }
        catch (Exception ex)
        {
            return $"Ошибка при открытии: {ex.Message}";
        }
    }

    private Dictionary<string, InstalledApplication> ScanRegistry()
    {
        var applications = new Dictionary<string, InstalledApplication>(_comparer);

        // Сканируем HKLM
        foreach (var path in _registryPaths)
        {
            ScanRegistryKey(RegistryHive.LocalMachine, path, applications);
        }

        // Сканируем HKCU
        ScanRegistryKey(RegistryHive.CurrentUser, _registryPaths[0], applications);

        return applications;
    }

    private void ScanRegistryKey(RegistryHive hive, string subPath, Dictionary<string, InstalledApplication> apps)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subPath);

            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var app = ParseRegistryKey(subKey);
                if (app != null && IsValidUserApplication(app))
                {
                    // Используем DisplayName как ключ, если дубликат - перезаписываем
                    apps[app.DisplayName!] = app;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка сканирования {hive}\\{subPath}: {ex.Message}");
        }
    }

    private InstalledApplication? ParseRegistryKey(RegistryKey key)
    {
        var displayName = key.GetValue("DisplayName") as string;

        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        var displayIcon = key.GetValue("DisplayIcon") as string;

        return new InstalledApplication
        {
            DisplayName = displayName,
            DisplayVersion = key.GetValue("DisplayVersion") as string,
            Publisher = key.GetValue("Publisher") as string,
            InstallLocation = key.GetValue("InstallLocation") as string,
            DisplayIcon = displayIcon,
            UninstallString = key.GetValue("UninstallString") as string,
            ExecutablePath = ExtractExecutablePath(displayIcon)
        };
    }

    private string? ExtractExecutablePath(string? displayIcon)
    {
        if (string.IsNullOrEmpty(displayIcon))
            return null;

        // Убираем параметры после запятой (например, "path.exe,0")
        var iconPath = displayIcon.Split(',')[0].Trim();

        // Проверяем расширение
        if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
            return iconPath;

        return null;
    }

    private bool IsValidUserApplication(InstalledApplication app)
    {
        // Проверка 1: Должен быть исполняемый файл
        if (string.IsNullOrEmpty(app.ExecutablePath))
            return false;

        // Проверка 2: Не системный путь
        foreach (var systemPath in _systemPaths)
        {
            if (app.ExecutablePath.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase) ||
                (app.InstallLocation?.StartsWith(systemPath, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return false;
            }
        }

        // Проверка 3: Не системное название
        foreach (var keyword in _systemKeywords)
        {
            if (app.DisplayName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                return false;
        }

        return true;
    }

    private string BuildReportContent(Dictionary<string, InstalledApplication> applications)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== СПИСОК УСТАНОВЛЕННЫХ ПРИЛОЖЕНИЙ ===");
        sb.AppendLine($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Всего приложений: {applications.Count}");
        sb.AppendLine();

        var index = 1;
        foreach (var app in applications.Values.OrderBy(a => a.DisplayName))
        {
            sb.AppendLine($" {index++}. {app.DisplayName}");
            sb.AppendLine($"   Версия: {app.DisplayVersion ?? "не указана"}");
            sb.AppendLine($"   Издатель: {app.Publisher ?? "не указан"}");
            sb.AppendLine($"   Путь: {app.ExecutablePath}");

            if (!string.IsNullOrEmpty(app.InstallLocation))
                sb.AppendLine($"   Папка установки: {app.InstallLocation}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> SaveReportToDesktop(string content)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string fileName = $"Список приложений {DateTime.Now:yyyy-MM-dd}.txt";
        string fullPath = Path.Combine(desktopPath, fileName);

        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

        return fullPath;
    }
}


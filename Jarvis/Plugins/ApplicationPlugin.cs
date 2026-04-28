using Jarvis.Models;
using Microsoft.SemanticKernel;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis.Plugins;

public class ApplicationPlugin
{
    private List<InstalledApplication>? _installedApps;
    private bool _isLoaded;

    private readonly string[] _registryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly string[] _excludeKeywords =
    [
        "Microsoft Visual C++",
        "Microsoft .NET",
        "Redistributable",
        "Update for",
        "Security Update",
        "Hotfix",
        "Service Pack"
    ];

    // ========== ПУБЛИЧНЫЕ МЕТОДЫ ДЛЯ НЕЙРОСЕТИ ==========

    [KernelFunction]
    [Description("Запускает приложение по названию. Сканирует реестр один раз при первом обращении")]
    public async Task<string> LaunchApp(
        [Description("Название приложения: Steam, Telegram, Google Chrome, Visual Studio")] string appName)
    {
        if (!_isLoaded)
            await ScanSystemAsync();

        if (_installedApps == null || _installedApps.Count == 0)
            return "Приложения не найдены";

        var app = SearchApp(appName.Trim());

        if (app == null)
            return $"Приложение \"{appName}\" не найдено";

        try
        {
            Process.Start(new ProcessStartInfo(app.ExecutablePath!) { UseShellExecute = true });
            return $"Запущено: {app.DisplayName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка запуска: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Создаёт текстовый файл на рабочем столе со списком всех установленных приложений")]
    public async Task<string> CreateAppListFile()
    {
        if (!_isLoaded)
            await ScanSystemAsync();

        if (_installedApps == null || _installedApps.Count == 0)
            return "Приложения не найдены";

        try
        {
            string report = GenerateTextReport();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = Path.Combine(desktop, $"Список приложений {DateTime.Now:yyyy-MM-dd}.txt");

            await File.WriteAllTextAsync(filePath, report, Encoding.UTF8);
            return $"Файл создан: {filePath}\nНайдено: {_installedApps.Count} приложений";
        }
        catch (Exception ex)
        {
            return $"Ошибка создания файла: {ex.Message}";
        }
    }

    // ========== ПРИВАТНЫЕ МЕТОДЫ ==========

    private async Task ScanSystemAsync()
    {
        _installedApps = new List<InstalledApplication>();
        await Task.Run(() => ScanRegistry());
        _isLoaded = true;
    }

    private void ScanRegistry()
    {
        foreach (var path in _registryPaths)
            ScanRegistryKey(RegistryHive.LocalMachine, path);

        ScanRegistryKey(RegistryHive.CurrentUser, _registryPaths[0]);
    }

    private void ScanRegistryKey(RegistryHive hive, string subPath)
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

                var app = ReadRegistryEntry(subKey);
                if (app != null)
                    _installedApps!.Add(app);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Scan error: {ex.Message}");
        }
    }

    private InstalledApplication? ReadRegistryEntry(RegistryKey key)
    {
        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (_excludeKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return null;

        var icon = key.GetValue("DisplayIcon") as string;
        var exePath = ResolveExePath(key, icon, name);

        if (string.IsNullOrEmpty(exePath)) return null;
        if (IsSystemPath(exePath)) return null;

        return new InstalledApplication
        {
            DisplayName = name,
            DisplayVersion = key.GetValue("DisplayVersion") as string,
            Publisher = key.GetValue("Publisher") as string,
            InstallLocation = key.GetValue("InstallLocation") as string,
            DisplayIcon = icon,
            UninstallString = key.GetValue("UninstallString") as string,
            ExecutablePath = exePath
        };
    }

    private string? ResolveExePath(RegistryKey key, string? icon, string appName)
    {
        if (!string.IsNullOrEmpty(icon))
        {
            var path = icon.Split(',')[0].Trim().Trim('"');
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path)
                && !path.Contains("unins", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        var location = key.GetValue("InstallLocation") as string;
        if (!string.IsNullOrEmpty(location) && Directory.Exists(location))
        {
            var exes = Directory.GetFiles(location, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.Contains("unins", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains("redist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exes.Count == 1) return exes[0];
            if (exes.Count > 1)
            {
                var word = appName.Split(' ')[0];
                return exes.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(word, StringComparison.OrdinalIgnoreCase))
                    ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();
            }
        }

        return null;
    }

    private bool IsSystemPath(string path)
    {
        string[] systemPaths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            @"C:\Program Files\WindowsApps"
        ];

        return systemPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private InstalledApplication? SearchApp(string appName)
    {
        var exact = _installedApps!.FirstOrDefault(a =>
            a.DisplayName?.Equals(appName, StringComparison.OrdinalIgnoreCase) == true);
        if (exact != null) return exact;

        var key = appName.ToLower().Replace(" ", "");
        return _installedApps!.FirstOrDefault(a =>
            (a.DisplayName?.ToLower().Replace(" ", "") ?? "").Contains(key)
            || key.Contains(a.DisplayName?.ToLower().Replace(" ", "") ?? ""));
    }

    private string GenerateTextReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== УСТАНОВЛЕННЫЕ ПРИЛОЖЕНИЯ ===");
        sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Всего: {_installedApps!.Count}\n");

        int index = 1;
        foreach (var app in _installedApps.OrderBy(a => a.DisplayName))
        {
            sb.AppendLine($"{index++}. {app.DisplayName}");
            sb.AppendLine($"   Версия: {app.DisplayVersion ?? "—"}");
            sb.AppendLine($"   Издатель: {app.Publisher ?? "—"}");
            sb.AppendLine($"   Путь: {app.ExecutablePath}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
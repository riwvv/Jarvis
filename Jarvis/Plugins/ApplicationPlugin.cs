using Microsoft.SemanticKernel;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.IO;
using Jarvis.Models;

namespace Jarvis.Plugins;

public class ApplicationPlugin {
    private List<InstalledApplication>? _installedApps;
    private bool _isLoaded;

    private Dictionary<string, string>? _steamGames;
    private bool _steamScanned;

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

    #region Публичные методы с JSON-возвратом

    [KernelFunction]
    [Description("Запускает приложение по названию. Сканирует реестр один раз при первом обращении")]
    public async Task<string> LaunchApp([Description("Название приложения: Steam, Telegram, Google Chrome, Visual Studio")] string appName) {
        if (!_isLoaded)
            await ScanSystemAsync();

        if (_installedApps == null || _installedApps.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "scan_failed",
                description = "Не удалось найти установленные приложения"
            });
        }

        var app = SearchApp(appName.Trim());

        if (app == null) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = appName,
                description = $"Приложение '{appName}' не найдено в системе"
            });
        }

        try {
            Process.Start(new ProcessStartInfo(app.ExecutablePath!) { UseShellExecute = true });

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Запущено: {app.DisplayName}",
                appName = app.DisplayName,
                executablePath = app.ExecutablePath
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "launch_failed",
                description = $"Ошибка запуска: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Создаёт текстовый файл на рабочем столе со списком всех установленных приложений")]
    public async Task<string> CreateAppListFile() {
        if (!_isLoaded)
            await ScanSystemAsync();

        if (_installedApps == null || _installedApps.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "scan_failed",
                description = "Не удалось найти установленные приложения"
            });
        }

        try {
            string report = GenerateTextReport();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string filePath = Path.Combine(desktop, $"Список приложений {DateTime.Now:yyyy-MM-dd}.txt");

            await File.WriteAllTextAsync(filePath, report, Encoding.UTF8);

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Файл со списком приложений создан на рабочем столе",
                filePath = filePath,
                apps = _installedApps,
                appCount = _installedApps.Count
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "file_create_failed",
                description = $"Ошибка создания файла: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Запускает игру из Steam по точному названию")]
    public async Task<string> LaunchSteamGame([Description("Точное название игры на английском, как в Steam: Satisfactory, Counter-Strike 2, Dota 2, Cyberpunk 2077")] string gameName) {
        if (!_steamScanned)
            await ScanSteamGamesAsync();

        if (_steamGames == null || _steamGames.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "steam_not_found",
                description = "Игры в Steam не найдены. Убедитесь, что Steam установлен и есть хотя бы одна игра."
            });
        }

        var exactMatch = _steamGames.Keys.FirstOrDefault(g =>
            string.Equals(g, gameName?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (exactMatch == null) {
            var suggestions = _steamGames.Keys.Take(5).ToList();
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = gameName,
                description = $"Игра '{gameName}' не найдена",
                suggestions = suggestions,
                totalGames = _steamGames.Count
            });
        }

        try {
            Process.Start(new ProcessStartInfo($"steam://rungameid/{_steamGames[exactMatch]}") {
                UseShellExecute = true
            });

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Запускаю: {exactMatch}",
                gameName = exactMatch,
                gameId = _steamGames[exactMatch]
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "launch_failed",
                description = $"Ошибка запуска: {ex.Message}. Убедитесь, что Steam запущен."
            });
        }
    }

    [KernelFunction]
    [Description("Возвращает список установленных в Steam игр")]
    public async Task<string> ListSteamGames() {
        if (!_steamScanned)
            await ScanSteamGamesAsync();

        if (_steamGames == null || _steamGames.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "steam_not_found",
                description = "Игры в Steam не найдены"
            });
        }

        var gameList = _steamGames.Keys.OrderBy(x => x).ToList();

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Найдено {_steamGames.Count} игр в Steam",
            games = gameList,
            total = _steamGames.Count
        });
    }

    #endregion

    #region Приватные методы (без изменений)

    private async Task ScanSystemAsync() {
        _installedApps = [];
        await Task.Run(() => ScanRegistry());
        _isLoaded = true;
    }

    private void ScanRegistry() {
        foreach (var path in _registryPaths)
            ScanRegistryKey(RegistryHive.LocalMachine, path);

        ScanRegistryKey(RegistryHive.CurrentUser, _registryPaths[0]);
    }

    private void ScanRegistryKey(RegistryHive hive, string subPath) {
        try {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames()) {
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var app = ReadRegistryEntry(subKey);
                if (app != null)
                    _installedApps!.Add(app);
            }
        }
        catch (Exception ex) {
            Debug.WriteLine($"Scan error: {ex.Message}");
        }
    }

    private InstalledApplication? ReadRegistryEntry(RegistryKey key) {
        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (_excludeKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return null;

        var icon = key.GetValue("DisplayIcon") as string;
        var exePath = ResolveExePath(key, icon, name);

        if (string.IsNullOrEmpty(exePath)) return null;
        if (IsSystemPath(exePath)) return null;

        return new InstalledApplication {
            DisplayName = name,
            DisplayVersion = key.GetValue("DisplayVersion") as string,
            Publisher = key.GetValue("Publisher") as string,
            InstallLocation = key.GetValue("InstallLocation") as string,
            DisplayIcon = icon,
            UninstallString = key.GetValue("UninstallString") as string,
            ExecutablePath = exePath
        };
    }

    private static string? ResolveExePath(RegistryKey key, string? icon, string appName) {
        if (!string.IsNullOrEmpty(icon)) {
            var path = icon.Split(',')[0].Trim().Trim('"');
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path)
                && !path.Contains("unins", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        var location = key.GetValue("InstallLocation") as string;
        if (!string.IsNullOrEmpty(location) && Directory.Exists(location)) {
            var exes = Directory.GetFiles(location, "*.exe", SearchOption.AllDirectories)
                .Where(f => !f.Contains("unins", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains("redist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exes.Count == 1) return exes[0];
            if (exes.Count > 1) {
                var word = appName.Split(' ')[0];
                return exes.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).Contains(word, StringComparison.OrdinalIgnoreCase))
                    ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();
            }
        }

        return null;
    }

    private static bool IsSystemPath(string path) {
        string[] systemPaths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            @"C:\Program Files\WindowsApps"
        ];

        return systemPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private InstalledApplication? SearchApp(string appName) {
        var exact = _installedApps!.FirstOrDefault(a =>
            a.DisplayName?.Equals(appName, StringComparison.OrdinalIgnoreCase) == true);
        if (exact != null) return exact;

        var key = appName.ToLower().Replace(" ", "");
        return _installedApps!.FirstOrDefault(a =>
            (a.DisplayName?.ToLower().Replace(" ", "") ?? "").Contains(key)
            || key.Contains(a.DisplayName?.ToLower().Replace(" ", "") ?? ""));
    }

    private string GenerateTextReport() {
        var sb = new StringBuilder();
        sb.AppendLine("=== УСТАНОВЛЕННЫЕ ПРИЛОЖЕНИЯ ===");
        sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Всего: {_installedApps!.Count}\n");

        int index = 1;
        foreach (var app in _installedApps.OrderBy(a => a.DisplayName)) {
            sb.AppendLine($"{index++}. {app.DisplayName}");
            sb.AppendLine($"   Версия: {app.DisplayVersion ?? "—"}");
            sb.AppendLine($"   Издатель: {app.Publisher ?? "—"}");
            sb.AppendLine($"   Путь: {app.ExecutablePath}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task ScanSteamGamesAsync() {
        _steamGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() => {
            try {
                var steamPath = GetSteamPath();
                if (string.IsNullOrEmpty(steamPath)) return;

                var steamAppsPaths = new List<string> { Path.Combine(steamPath, "steamapps") };
                steamAppsPaths.AddRange(GetExtraLibraries(steamPath));

                foreach (var appsPath in steamAppsPaths.Where(Directory.Exists)) {
                    foreach (var manifest in Directory.GetFiles(appsPath, "appmanifest_*.acf")) {
                        ParseManifest(manifest);
                    }
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"Steam scan error: {ex.Message}");
            }
        });

        _steamScanned = true;
    }

    private static string? GetSteamPath() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

    private static List<string> GetExtraLibraries(string steamPath) {
        var libraries = new List<string>();
        var configPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(configPath)) return libraries;

        try {
            var content = File.ReadAllText(configPath);
            var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");

            foreach (Match match in matches) {
                var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                var appsPath = Path.Combine(libPath, "steamapps");

                if (Directory.Exists(appsPath))
                    libraries.Add(appsPath);
            }
        }
        catch (Exception ex) {
            Debug.WriteLine($"Ошибка чтения libraryfolders.vdf: {ex.Message}");
        }

        return libraries;
    }

    private void ParseManifest(string path) {
        try {
            var content = File.ReadAllText(path);

            var appId = Regex.Match(content, @"""appid""\s+""(\d+)""");
            var name = Regex.Match(content, @"""name""\s+""([^""]+)""");

            if (appId.Success && name.Success) {
                var gameName = name.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(gameName) && !_steamGames!.ContainsKey(gameName))
                    _steamGames!.Add(gameName, appId.Groups[1].Value);
            }
        }
        catch (Exception ex) {
            Debug.WriteLine($"Ошибка парсинга манифеста {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    #endregion
}
//using Microsoft.SemanticKernel;
//using Microsoft.Win32;
//using System.ComponentModel;
//using System.Diagnostics;
//using System.IO;
//using System.Text;
//using System.Text.RegularExpressions;
//using Jarvis.Models;

//namespace Jarvis.Plugins;

//public class ApplicationPlugin {
//    private List<InstalledApplication>? _installedApps;
//    private bool _isLoaded;

//    private Dictionary<string, string>? _steamGames;
//    private bool _steamScanned;

//    private readonly string[] _registryPaths =
//    [
//        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
//        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
//    ];

//    private readonly string[] _excludeKeywords =
//    [
//        "Microsoft Visual C++",
//        "Microsoft .NET",
//        "Redistributable",
//        "Update for",
//        "Security Update",
//        "Hotfix",
//        "Service Pack"
//    ];

//    [KernelFunction]
//    [Description("Запускает приложение по названию. Сканирует реестр один раз при первом обращении")]
//    public async Task<string> LaunchApp([Description("Название приложения: Steam, Telegram, Google Chrome, Visual Studio")] string appName) {
//        if (!_isLoaded)
//            await ScanSystemAsync();

//        if (_installedApps == null || _installedApps.Count == 0)
//            return "Приложения не найдены";

//        var app = SearchApp(appName.Trim());

//        if (app == null)
//            return $"Приложение \"{appName}\" не найдено";

//        try {
//            Process.Start(new ProcessStartInfo(app.ExecutablePath!) { UseShellExecute = true });
//            return $"Запущено: {app.DisplayName}";
//        }
//        catch (Exception ex) {
//            return $"Ошибка запуска: {ex.Message}";
//        }
//    }

//    [KernelFunction]
//    [Description("Создаёт текстовый файл на рабочем столе со списком всех установленных приложений")]
//    public async Task<string> CreateAppListFile() {
//        if (!_isLoaded)
//            await ScanSystemAsync();

//        if (_installedApps == null || _installedApps.Count == 0)
//            return "Приложения не найдены";

//        try {
//            string report = GenerateTextReport();
//            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
//            string filePath = Path.Combine(desktop, $"Список приложений {DateTime.Now:yyyy-MM-dd}.txt");

//            await File.WriteAllTextAsync(filePath, report, Encoding.UTF8);
//            return $"Файл создан на рабочем столе";
//        }
//        catch (Exception ex) {
//            return $"Ошибка создания файла: {ex.Message}";
//        }
//    }

//    private async Task ScanSystemAsync() {
//        _installedApps = new List<InstalledApplication>();
//        await Task.Run(() => ScanRegistry());
//        _isLoaded = true;
//    }

//    private void ScanRegistry() {
//        foreach (var path in _registryPaths)
//            ScanRegistryKey(RegistryHive.LocalMachine, path);

//        ScanRegistryKey(RegistryHive.CurrentUser, _registryPaths[0]);
//    }

//    private void ScanRegistryKey(RegistryHive hive, string subPath) {
//        try {
//            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
//            using var key = baseKey.OpenSubKey(subPath);
//            if (key == null) return;

//            foreach (var subKeyName in key.GetSubKeyNames()) {
//                using var subKey = key.OpenSubKey(subKeyName);
//                if (subKey == null) continue;

//                var app = ReadRegistryEntry(subKey);
//                if (app != null)
//                    _installedApps!.Add(app);
//            }
//        }
//        catch (Exception ex) {
//            Debug.WriteLine($"Scan error: {ex.Message}");
//        }
//    }

//    private InstalledApplication? ReadRegistryEntry(RegistryKey key) {
//        var name = key.GetValue("DisplayName") as string;
//        if (string.IsNullOrWhiteSpace(name)) return null;

//        if (_excludeKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
//            return null;

//        var icon = key.GetValue("DisplayIcon") as string;
//        var exePath = ResolveExePath(key, icon, name);

//        if (string.IsNullOrEmpty(exePath)) return null;
//        if (IsSystemPath(exePath)) return null;

//        return new InstalledApplication {
//            DisplayName = name,
//            DisplayVersion = key.GetValue("DisplayVersion") as string,
//            Publisher = key.GetValue("Publisher") as string,
//            InstallLocation = key.GetValue("InstallLocation") as string,
//            DisplayIcon = icon,
//            UninstallString = key.GetValue("UninstallString") as string,
//            ExecutablePath = exePath
//        };
//    }

//    private string? ResolveExePath(RegistryKey key, string? icon, string appName) {
//        if (!string.IsNullOrEmpty(icon)) {
//            var path = icon.Split(',')[0].Trim().Trim('"');
//            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
//                && File.Exists(path)
//                && !path.Contains("unins", StringComparison.OrdinalIgnoreCase))
//                return path;
//        }

//        var location = key.GetValue("InstallLocation") as string;
//        if (!string.IsNullOrEmpty(location) && Directory.Exists(location)) {
//            var exes = Directory.GetFiles(location, "*.exe", SearchOption.AllDirectories)
//                .Where(f => !f.Contains("unins", StringComparison.OrdinalIgnoreCase))
//                .Where(f => !f.Contains("redist", StringComparison.OrdinalIgnoreCase))
//                .ToList();

//            if (exes.Count == 1) return exes[0];
//            if (exes.Count > 1) {
//                var word = appName.Split(' ')[0];
//                return exes.FirstOrDefault(f =>
//                    Path.GetFileNameWithoutExtension(f).Contains(word, StringComparison.OrdinalIgnoreCase))
//                    ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();
//            }
//        }

//        return null;
//    }

//    private bool IsSystemPath(string path) {
//        string[] systemPaths =
//        [
//            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
//            Environment.GetFolderPath(Environment.SpecialFolder.System),
//            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
//            @"C:\Program Files\WindowsApps"
//        ];

//        return systemPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
//    }

//    private InstalledApplication? SearchApp(string appName) {
//        var exact = _installedApps!.FirstOrDefault(a =>
//            a.DisplayName?.Equals(appName, StringComparison.OrdinalIgnoreCase) == true);
//        if (exact != null) return exact;

//        var key = appName.ToLower().Replace(" ", "");
//        return _installedApps!.FirstOrDefault(a =>
//            (a.DisplayName?.ToLower().Replace(" ", "") ?? "").Contains(key)
//            || key.Contains(a.DisplayName?.ToLower().Replace(" ", "") ?? ""));
//    }

//    private string GenerateTextReport() {
//        var sb = new StringBuilder();
//        sb.AppendLine("=== УСТАНОВЛЕННЫЕ ПРИЛОЖЕНИЯ ===");
//        sb.AppendLine($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
//        sb.AppendLine($"Всего: {_installedApps!.Count}\n");

//        int index = 1;
//        foreach (var app in _installedApps.OrderBy(a => a.DisplayName)) {
//            sb.AppendLine($"{index++}. {app.DisplayName}");
//            sb.AppendLine($"   Версия: {app.DisplayVersion ?? "—"}");
//            sb.AppendLine($"   Издатель: {app.Publisher ?? "—"}");
//            sb.AppendLine($"   Путь: {app.ExecutablePath}");
//            sb.AppendLine();
//        }

//        return sb.ToString();
//    }

//    [KernelFunction]
//    [Description("Запускает игру из Steam по точному названию")]
//    public async Task<string> LaunchSteamGame([Description("Точное название игры на английском, как в Steam: Satisfactory, Counter-Strike 2, Dota 2, Cyberpunk 2077")] string gameName) {
//        if (!_steamScanned)
//            await ScanSteamGamesAsync();

//        if (_steamGames == null || _steamGames.Count == 0)
//            return "Игры в Steam не найдены. Убедитесь, что Steam установлен и есть хотя бы одна игра.";

//        var exactMatch = _steamGames.Keys.FirstOrDefault(g =>
//            string.Equals(g, gameName?.Trim(), StringComparison.OrdinalIgnoreCase));

//        if (exactMatch == null) {
//            var suggestions = _steamGames.Keys.Take(5);
//            return $"Игра \"{gameName}\" не найдена. Установленные игры: {string.Join(", ", suggestions)}...";
//        }

//        try {
//            Process.Start(new ProcessStartInfo($"steam://rungameid/{_steamGames[exactMatch]}") {
//                UseShellExecute = true
//            });
//            return $"Запускаю: {exactMatch}";
//        }
//        catch (Exception ex) {
//            return $"Ошибка запуска: {ex.Message}. Убедитесь, что Steam запущен.";
//        }
//    }

//    [KernelFunction]
//    [Description("Возвращает список установленных в Steam игр")]
//    public async Task<string> ListSteamGames() {
//        if (!_steamScanned)
//            await ScanSteamGamesAsync();

//        if (_steamGames == null || _steamGames.Count == 0)
//            return "Игры не найдены";

//        return string.Join(", ", _steamGames.Keys.OrderBy(x => x));
//    }

//    private async Task ScanSteamGamesAsync() {
//        _steamGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

//        await Task.Run(() => {
//            try {
//                var steamPath = GetSteamPath();
//                if (string.IsNullOrEmpty(steamPath)) return;

//                var steamAppsPaths = new List<string> { Path.Combine(steamPath, "steamapps") };
//                steamAppsPaths.AddRange(GetExtraLibraries(steamPath));

//                foreach (var appsPath in steamAppsPaths.Where(Directory.Exists)) {
//                    foreach (var manifest in Directory.GetFiles(appsPath, "appmanifest_*.acf")) {
//                        ParseManifest(manifest);
//                    }
//                }
//            }
//            catch (Exception ex) {
//                Debug.WriteLine($"Steam scan error: {ex.Message}");
//            }
//        });

//        _steamScanned = true;
//    }

//    private static string? GetSteamPath() => Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
//            ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;

//    private static List<string> GetExtraLibraries(string steamPath) {
//        var libraries = new List<string>();
//        var configPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

//        if (!File.Exists(configPath)) return libraries;

//        try {
//            var content = File.ReadAllText(configPath);
//            var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");

//            foreach (Match match in matches) {
//                var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
//                var appsPath = Path.Combine(libPath, "steamapps");

//                if (Directory.Exists(appsPath))
//                    libraries.Add(appsPath);
//            }
//        }
//        catch (Exception ex) {
//            Debug.WriteLine($"Ошибка чтения libraryfolders.vdf: {ex.Message}");
//        }

//        return libraries;
//    }

//    private void ParseManifest(string path) {
//        try {
//            var content = File.ReadAllText(path);

//            var appId = Regex.Match(content, @"""appid""\s+""(\d+)""");
//            var name = Regex.Match(content, @"""name""\s+""([^""]+)""");

//            if (appId.Success && name.Success) {
//                var gameName = name.Groups[1].Value.Trim();
//                if (!string.IsNullOrEmpty(gameName) && !_steamGames!.ContainsKey(gameName))
//                    _steamGames!.Add(gameName, appId.Groups[1].Value);
//            }
//        }
//        catch (Exception ex) {
//            Debug.WriteLine($"Ошибка парсинга манифеста {Path.GetFileName(path)}: {ex.Message}");
//        }
//    }
//}
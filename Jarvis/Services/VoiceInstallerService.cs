using Jarvis.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Jarvis.Services;

public class VoiceInstallerService : IHostedService {
    private readonly string TargetVoiceName;
    private readonly string InstallerFileName;
    private readonly string InstallerSubPath;
    private readonly ILogger<VoiceInstallerService> _logger;
    private readonly IConfiguration _configuration;

    public VoiceInstallerService(IConfiguration configuration, ILogger<VoiceInstallerService> logger) {
        _logger = logger;
        _configuration = configuration;

        TargetVoiceName = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.VoiceName;
        InstallerFileName = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.InstallerFileName;
        InstallerSubPath = _configuration.GetSection("TTSSettings").Get<TTSSettings>()!.InstallerSubPath;

    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("VoiceInstallerService: проверка голоса...");

        try {
            await Task.Delay(500, cancellationToken);

            if (IsVoiceInstalled()) {
                _logger.LogInformation($"Голос '{TargetVoiceName}' уже установлен");
                return;
            }

            _logger.LogWarning($"Голос '{TargetVoiceName}' не найден. Начинаю установку...");
            await InstallVoice(cancellationToken);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка в VoiceInstallerService");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool IsVoiceInstalled() {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices().Any(v => v.VoiceInfo.Name == TargetVoiceName);
    }

    private async Task InstallVoice(CancellationToken cancellationToken) {
        string installerPath = GetInstallerPath();

        if (!File.Exists(installerPath)) {
            _logger.LogError($"Установщик не найден: {installerPath}");
            ShowErrorNotification($"Установщик голоса не найден по пути: {installerPath}");
            return;
        }

        try {
            UnblockFile(installerPath);

            var processInfo = new ProcessStartInfo {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };

            _logger.LogInformation("Запуск установщика с правами администратора...");

            using var process = Process.Start(processInfo);
            if (process == null) {
                _logger.LogError("Не удалось запустить процесс установки");
                return;
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0) {
                _logger.LogInformation($"Голос {TargetVoiceName} успешно установлен!");
                ShowNotificationAndExit();
            }
            else {
                _logger.LogError($"Установка завершилась с кодом ошибки: {process.ExitCode}");
                ShowErrorNotification($"Ошибка установки голоса (код: {process.ExitCode})");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // 1223 = пользователь нажал "Нет" в UAC
        {
            _logger.LogWarning("Пользователь отказал в предоставлении прав администратора");
            ShowErrorNotification("Для установки голоса требуются права администратора. Пожалуйста, разрешите запуск установщика.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740) // 740 = недостаточно прав
        {
            _logger.LogError(ex, "Недостаточно прав для запуска установщика");
            ShowErrorNotification("Не удалось получить права администратора. Попробуйте запустить Jarvis от имени администратора.");
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка установки");
            ShowErrorNotification($"Ошибка при установке: {ex.Message}");
        }
    }

    private void UnblockFile(string filePath) {
        try {
            if (!File.Exists(filePath)) return;

            var zoneFile = filePath + ":Zone.Identifier";
            if (File.Exists(zoneFile)) {
                File.WriteAllText(zoneFile, "[ZoneTransfer]\r\nZoneId=0\r\n");
                _logger.LogDebug($"Файл разблокирован: {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception ex) {
            _logger.LogDebug($"Не удалось разблокировать файл: {ex.Message}");
        }
    }

    private string GetInstallerPath() {
        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDirectory, InstallerSubPath, InstallerFileName);
    }

    private void ShowErrorNotification(string message) {
        try {
            MessageBox.Show(
                message + "\n\nВы можете установить голос вручную с сайта nvda.ru",
                "Jarvis - Ошибка установки",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при показе уведомления");
        }
    }

    private void ShowNotificationAndExit() {
        try {
            MessageBox.Show(
                "Голос Evgeniy-Rus успешно установлен!\n\n" +
                "Для применения нового голоса необходимо перезапустить Jarvis.\n\n" +
                "Приложение будет закрыто. Запустите его снова.",
                "Jarvis - Установка завершена",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Application.Current.Dispatcher.Invoke(() => {
                if (Application.Current.MainWindow != null)
                    Application.Current.MainWindow.Close();
                Application.Current.Shutdown();
            });
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка при показе уведомления");
            Environment.Exit(0);
        }
    }
}
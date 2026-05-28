using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;

namespace Jarvis.Services;

public class VoiceInstallerService(ILogger<VoiceInstallerService> _logger) : IHostedService {
    private const string TargetVoiceName = "Evgeniy-Rus";
    private const string InstallerFileName = "RHVoice-voice-Russian-Evgeniy-Rus-v4.0.2017.22-setup.exe";
    private const string InstallerSubPath = "Resources";

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
            var processInfo = new ProcessStartInfo {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };

            _logger.LogInformation("Запуск установщика...");

            using var process = Process.Start(processInfo);
            if (process == null) return;

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0) {
                _logger.LogInformation("Голос Evgeniy-Rus успешно установлен!");
                ShowNotificationAndExit();
            }
            else {
                _logger.LogError($"Установка завершилась с кодом ошибки: {process.ExitCode}");
                ShowErrorNotification($"Ошибка установки голоса (код: {process.ExitCode})");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Ошибка установки");
            ShowErrorNotification($"Ошибка при установке: {ex.Message}");
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
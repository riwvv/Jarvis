using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using System.Windows.Controls;
using System.Windows;
using System.Drawing;
using System.IO;
using System.Net.Http;
using Jarvis.Plugins;
using Jarvis.Services;
using Jarvis.ViewModels;
using Jarvis.Views.Windows;
using Jarvis.Configuration;

namespace Jarvis;

public partial class App : Application {
    private IHost? _host;
    private Kernel? _kernelCore;
    private IConfiguration? _configuration;
    private TrayService? _trayService;

    private MainWindow? _mainWindow;

    public App() => _host = CreateHostBuilder().Build();

    private IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
        .UseSerilog((context, services, config) => {
            config.ReadFrom.Configuration(context.Configuration)
                .WriteTo.Debug()
                .WriteTo.File("logs/jarvis-logs.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
        })
        .ConfigureServices((context, services) => {
            services.AddSemanticKernel(context.Configuration);
            services.AddOllamaHealthCheck();

            services.AddConfigure(context.Configuration)
                    .AddServices()
                    .AddViewModels()
                    .AddViews();
        });

    private void InitializeSystemTray() {
        if (_trayIcon != null) return;
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "JarvisImg.ico");
        _trayIcon = new TaskbarIcon {
            Icon = new Icon(iconPath),
            ToolTipText = "Jarvis"
        };

        var contextMenu = new ContextMenu();

        var openItem = new MenuItem { Header = "Открыть" };
        openItem.Click += (s, e) => ShowNormalWindow();

        var autoModeItem = new MenuItem { Header = "Авто-режим" };
        autoModeItem.Click += (s, e) => SetAutoMode();

        var exitItem = new MenuItem { Header = "Выход" };
        exitItem.Click += (s, e) => ExitApplication();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(autoModeItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
    }

            _trayService.Initialize(_mainWindow);

            aiService.SetTrayService(_trayService);

            aiService.OnResult += (status) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _trayService?.HideOverlayAfterCommand();
                });
            };

            var speechService = _host.Services.GetRequiredService<SpeechToTextService>();
            speechService.OnWakeWordDetected += () =>
            {
                Dispatcher.Invoke(() => _trayService?.ShowAsOverlay());
            };

            base.OnStartup(e);
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при запуске");
            MessageBox.Show($"Ошибка запуска: {ex.Message}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        Log.Information("Приложение завершает работу...");

        try {
            if (_host != null) {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Ошибка при остановке хоста");
        }

        var currentProcessId = Environment.ProcessId;
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Jarvis")) {
            if (proc.Id != currentProcessId) {
                try {
                    proc.Kill();
                    Log.Information($"Завершен старый процесс с ID: {proc.Id}");
                }
                catch (Exception ex) {
                    Log.Warning(ex, $"Не удалось завершить процесс {proc.Id}");
                }
            }
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) {
        ExitApplication();
        base.OnSessionEnding(e);
    }
}
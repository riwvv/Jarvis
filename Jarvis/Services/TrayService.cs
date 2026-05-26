using Hardcodet.Wpf.TaskbarNotification;
using Jarvis.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Jarvis.Services
{
    public class TrayService
    {
        private TaskbarIcon? _trayIcon;
        private MainWindow? _mainWindow;
        private System.Timers.Timer? _autoHideTimer;
        private bool _isAutoMode = true;
        private bool _isOverlayVisible = false;
        private const int OVERLAY_AUTO_HIDE_SECONDS = 3;

        public bool IsAutoMode => _isAutoMode;

        private readonly IServiceProvider _serviceProvider;

        public TrayService(/*IServiceProvider serviceProvider,*/ MainWindow mainWindow)
        {
            //_serviceProvider = serviceProvider;

            //_serviceProvider.GetRequiredService<SpeechToTextService>().OnWakeWordDetected += () => Dispatcher.CurrentDispatcher.Invoke(ShowAsOverlay);

            _mainWindow = mainWindow;
            _mainWindow.Closing += (s, e) => HideToTray();

            InitializeSystemTray();
            SetupAutoHideTimer();
        }

        //public void Initialize(MainWindow mainWindow)
        //{
        //    _mainWindow = mainWindow;
        //    InitializeSystemTray();
        //    SetupAutoHideTimer();
        //}

        private void InitializeSystemTray()
        {
            if (_trayIcon != null) return;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "JarvisImg.ico");

            if (!File.Exists(iconPath))
            {
                _trayIcon = new TaskbarIcon();
            }
            else
            {
                _trayIcon = new TaskbarIcon
                {
                    Icon = new Icon(iconPath),
                    ToolTipText = "Jarvis - Голосовой ассистент"
                };
            }

            var contextMenu = new ContextMenu();

            var openItem = new MenuItem { Header = "Открыть" };
            openItem.Click += (s, e) => ShowNormalWindow();

            var autoModeItem = new MenuItem { Header = "Авто-режим" };
            autoModeItem.Click += (s, e) => ToggleAutoMode();

            var hideItem = new MenuItem { Header = "Скрыть" };
            hideItem.Click += (s, e) => HideToTray();

            var separator = new Separator();

            var exitItem = new MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => ExitApplication();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(autoModeItem);
            contextMenu.Items.Add(hideItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.TrayMouseDoubleClick += (s, e) => ShowNormalWindow();

            UpdateAutoModeMenuItem(autoModeItem);
        }

        private void SetupAutoHideTimer()
        {
            _autoHideTimer = new System.Timers.Timer(OVERLAY_AUTO_HIDE_SECONDS * 1000);
            _autoHideTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_isAutoMode && _isOverlayVisible)
                    {
                        HideToTray();
                        _isOverlayVisible = false;
                    }
                });
            };
            _autoHideTimer.AutoReset = false;
        }

        public void ShowNormalWindow()
        {
            if (_mainWindow == null) return;

            _isAutoMode = false;
            _isOverlayVisible = false;

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Topmost = false;
            _mainWindow.Activate();

            _autoHideTimer?.Stop();

            UpdateTrayTooltip("Jarvis - Ручной режим");
            UpdateContextMenu();
        }

        public void HideToTray()
        {
            if (_mainWindow == null) return;

            _mainWindow.Hide();
            _mainWindow.ShowInTaskbar = false;
            _isOverlayVisible = false;

            _autoHideTimer?.Stop();
        }

        public void ShowAsOverlay()
        {
            if (!_isAutoMode || _mainWindow == null) return;

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Topmost = true;
            _mainWindow.ShowInTaskbar = false;

            _mainWindow.Left = SystemParameters.WorkArea.Width - _mainWindow.Width - 20;
            _mainWindow.Top = SystemParameters.WorkArea.Height - _mainWindow.Height - 20;

            _mainWindow.Activate();
            _isOverlayVisible = true;

            _autoHideTimer?.Stop();
            _autoHideTimer?.Start();

            UpdateTrayTooltip("Jarvis - Авто-режим (активен)");
        }

        public void SetAutoMode()
        {
            if (_isAutoMode) return;

            _isAutoMode = true;
            HideToTray();
            UpdateTrayTooltip("Jarvis - Авто-режим");
            UpdateContextMenu();
        }

        public void SetManualMode()
        {
            if (!_isAutoMode) return;

            ShowNormalWindow();
        }

        public void ToggleAutoMode()
        {
            if (_isAutoMode)
            {
                SetManualMode();
            }
            else
            {
                SetAutoMode();
            }
        }

        private void UpdateTrayTooltip(string text)
        {
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = text;
            }
        }

        private void UpdateContextMenu()
        {
            if (_trayIcon?.ContextMenu == null) return;

            foreach (var item in _trayIcon.ContextMenu.Items)
            {
                if (item is MenuItem menuItem && menuItem.Header.ToString() == "Авто-режим")
                {
                    UpdateAutoModeMenuItem(menuItem);
                    break;
                }
            }
        }

        private void UpdateAutoModeMenuItem(MenuItem autoModeItem)
        {
            if (_isAutoMode)
            {
                autoModeItem.Header = "✓ Авто-режим";
            }
            else
            {
                autoModeItem.Header = "Авто-режим";
            }
        }

        public void ExitApplication()
        {
            _autoHideTimer?.Dispose();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _autoHideTimer?.Dispose();
            _trayIcon?.Dispose();
        }
    }
}


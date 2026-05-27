using Hardcodet.Wpf.TaskbarNotification;
using Jarvis.Views.Windows;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Jarvis.Services
{
    public class TrayService
    {
        private TaskbarIcon? _trayIcon;
        private MainWindow? _mainWindow;
        private System.Timers.Timer? _waitCommandTimer;
        private bool _isAutoMode = true;
        private bool _isOverlayVisible = false;
        private bool _isWaitingForCommand = false;
        private const int WAIT_COMMAND_SECONDS = 5;

        public bool IsAutoMode => _isAutoMode;

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeSystemTray();
            SetupWaitCommandTimer();
        }

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

        private void SetupWaitCommandTimer()
        {
            _waitCommandTimer = new System.Timers.Timer(WAIT_COMMAND_SECONDS * 1000);
            _waitCommandTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_isAutoMode && _isOverlayVisible && _isWaitingForCommand)
                    {
                        _isWaitingForCommand = false;
                        HideToTray();
                    }
                });
            };
            _waitCommandTimer.AutoReset = false;
        }

        public void ShowNormalWindow()
        {
            if (_mainWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _isAutoMode = false;
                _isOverlayVisible = false;
                _isWaitingForCommand = false;

                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.ShowInTaskbar = true;
                _mainWindow.Topmost = false;
                _mainWindow.Activate();

                _waitCommandTimer?.Stop();

                UpdateTrayTooltip("Jarvis - Ручной режим");
                UpdateContextMenu();
            });
        }

        public void HideToTray()
        {
            if (_mainWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow.Hide();
                _mainWindow.ShowInTaskbar = false;
                _isOverlayVisible = false;
                _isWaitingForCommand = false;

                _waitCommandTimer?.Stop();
            });
        }

        public void ShowAsOverlay()
        {
            if (!_isAutoMode || _mainWindow == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Topmost = true;
                _mainWindow.ShowInTaskbar = false;

                _mainWindow.Left = SystemParameters.WorkArea.Width - _mainWindow.Width - 20;
                _mainWindow.Top = SystemParameters.WorkArea.Height - _mainWindow.Height - 20;

                _mainWindow.Activate();
                _isOverlayVisible = true;
                _isWaitingForCommand = true;

                _waitCommandTimer?.Stop();
                _waitCommandTimer?.Start();

                UpdateTrayTooltip("Jarvis - Слушаю команду...");
            });
        }

        public void CommandReceived()
        {
            if (!_isAutoMode || !_isOverlayVisible) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _isWaitingForCommand = false;
                _waitCommandTimer?.Stop();

                UpdateTrayTooltip("Jarvis - Выполняю команду...");
            });
        }

        public void HideOverlayAfterCommand()
        {
            if (_isAutoMode && _isOverlayVisible)
            {
                HideToTray();
                UpdateTrayTooltip("Jarvis - Авто-режим");
            }
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
            _waitCommandTimer?.Dispose();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _waitCommandTimer?.Dispose();
            _trayIcon?.Dispose();
        }
    }
}


using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Media.Control;

namespace Jarvis.Plugins
{
    public class MediaPlayerPlugin
    {
        private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private static GlobalSystemMediaTransportControlsSession? _currentSession;

        private static async Task<GlobalSystemMediaTransportControlsSessionManager> GetSessionManager()
        {
            if (_sessionManager == null)
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            return _sessionManager;
        }

        private static async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSession()
        {
            var manager = await GetSessionManager();
            _currentSession = manager.GetCurrentSession();
            return _currentSession;
        }

        [KernelFunction]
        [Description("Получает информацию о текущем треке: исполнитель и название")]
        public async Task<string> GetCurrentTrackInfo()
        {
            try
            {
                var session = await GetCurrentSession();
                if (session == null)
                    return "Ни один плеер сейчас не воспроизводит музыку";

                var mediaProperties = await session.TryGetMediaPropertiesAsync();
                if (mediaProperties == null)
                    return "Не удалось получить информацию о треке";

                string title = mediaProperties.Title ?? "Неизвестно";
                string artist = mediaProperties.Artist ?? "Неизвестный исполнитель";

                return $"{artist} - {title}";
            }
            catch (Exception ex)
            {
                return $"Ошибка получения информации: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
        public async Task<string> PlayPause()
        {
            try
            {
                var session = await GetCurrentSession();
                if (session == null)
                    return "Нет активного плеера";

                var playbackInfo = session.GetPlaybackInfo();

                if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await session.TryPauseAsync();
                else
                    await session.TryPlayAsync();

                return "Готово";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Переключает на следующий трек")]
        public async Task<string> NextTrack()
        {
            try
            {
                var session = await GetCurrentSession();
                if (session == null)
                    return "Нет активного плеера";

                await session.TrySkipNextAsync();
                return "Следующий трек";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Переключает на предыдущий трек")]
        public async Task<string> PreviousTrack()
        {
            try
            {
                var session = await GetCurrentSession();
                if (session == null)
                    return "Нет активного плеера";

                await session.TrySkipPreviousAsync();
                return "Предыдущий трек";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Останавливает воспроизведение")]
        public async Task<string> Stop()
        {
            try
            {
                var session = await GetCurrentSession();
                if (session == null)
                    return "Нет активного плеера";

                await session.TryStopAsync();
                return "Воспроизведение остановлено";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Увеличивает громкость системы")]
        public string VolumeUp()
        {
            try
            {
                keybd_event(0xAF, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(0xAF, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                return "Громкость увеличена";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }

        [KernelFunction]
        [Description("Уменьшает громкость системы")]
        public string VolumeDown()
        {
            try
            {
                keybd_event(0xAE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(0xAE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                return "Громкость уменьшена";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }


        // WinAPI для громкости
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    }
}

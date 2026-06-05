using Microsoft.SemanticKernel;
using System.ComponentModel;
using Windows.Media.Control;

namespace Jarvis.Plugins;

public class MediaPlayerPlugin {
    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static GlobalSystemMediaTransportControlsSession? _currentSession;

    [KernelFunction]
    [Description("Получает информацию о текущем треке: исполнитель и название")]
    public async Task<string> GetCurrentTrackInfo() {
        try {
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
        catch (Exception ex) {
            return $"Ошибка получения информации: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
    public async Task<string> PlayPause() {
        try {
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
        catch (Exception ex) {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Переключает на следующий трек")]
    public async Task<string> NextTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
                return "Нет активного плеера";

            await session.TrySkipNextAsync();
            return "Следующий трек";
        }
        catch (Exception ex) {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Переключает на предыдущий трек")]
    public async Task<string> PreviousTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
                return "Нет активного плеера";

            await session.TrySkipPreviousAsync();
            return "Предыдущий трек";
        }
        catch (Exception ex) {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Останавливает воспроизведение")]
    public async Task<string> Stop() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
                return "Нет активного плеера";

            await session.TryStopAsync();
            return "Воспроизведение остановлено";
        }
        catch (Exception ex) {
            return $"Ошибка: {ex.Message}";
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager> GetSessionManager() {
        if (_sessionManager == null) {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        return _sessionManager;
    }

    private static async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSession() {
        var manager = await GetSessionManager();
        _currentSession = manager.GetCurrentSession();
        return _currentSession;
    }
}

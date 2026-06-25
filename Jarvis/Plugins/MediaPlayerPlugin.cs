using Windows.Media.Control;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace Jarvis.Plugins;

public class MediaPlayerPlugin {
    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static GlobalSystemMediaTransportControlsSession? _currentSession;

    [KernelFunction]
    [Description("Получает информацию о текущем треке: исполнитель и название")]
    public static async Task<string> GetCurrentTrackInfo() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "WARNING",
                    cause = "no_player",
                    description = "Ни один плеер сейчас не воспроизводит музыку"
                });
            }

            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            if (mediaProperties == null) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "no_media_info",
                    description = "Не удалось получить информацию о треке"
                });
            }

            string title = mediaProperties.Title ?? "Неизвестно";
            string artist = mediaProperties.Artist ?? "Неизвестный исполнитель";

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"{artist} - {title}",
                titleMusic = title,
                artistMusic = artist,
                fullTrack = $"{artist} - {title}"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "exception",
                description = $"Ошибка получения информации: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
    public static async Task<string> PlayPause() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            var playbackInfo = session.GetPlaybackInfo();
            var currentStatus = playbackInfo.PlaybackStatus;

            if (currentStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) {
                await session.TryPauseAsync();
                return JsonSerializer.Serialize(new {
                    status = "DONE",
                    message = "Воспроизведение поставлено на паузу",
                    action = "pause",
                    currentStatus = "paused"
                });
            }
            else {
                await session.TryPlayAsync();
                return JsonSerializer.Serialize(new {
                    status = "DONE",
                    message = "Воспроизведение возобновлено",
                    action = "play",
                    currentStatus = "playing"
                });
            }
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Переключает на следующий трек")]
    public static async Task<string> NextTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            await session.TrySkipNextAsync();

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Следующий трек",
                action = "next"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Переключает на предыдущий трек")]
    public static async Task<string> PreviousTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            await session.TrySkipPreviousAsync();

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Предыдущий трек",
                action = "previous"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Останавливает воспроизведение")]
    public static async Task<string> Stop() {
        try {
            var session = await GetCurrentSession();
            if (session == null) {
                return JsonSerializer.Serialize(new {
                    status = "ERROR",
                    cause = "no_player",
                    description = "Нет активного плеера"
                });
            }

            await session.TryStopAsync();

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Воспроизведение остановлено",
                action = "stop"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "exception",
                description = $"Ошибка: {ex.Message}"
            });
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
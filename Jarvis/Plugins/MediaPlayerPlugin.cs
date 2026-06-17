using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
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
            {
                var error = new
                {
                    status = "ERROR",
                    cause = session,
                    description = "Не удалсь найти действующий плеер на этом устройстве"
                };
                return JsonSerializer.Serialize(error);
            }

                
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            if (mediaProperties == null)
            {
                var error = new
                {
                    status = "ERROR",
                    cause = mediaProperties,
                    description = $"Не может получить инофрмацию о треке - {mediaProperties}"
                };
                return JsonSerializer.Serialize(error);
            }
                

            string title = mediaProperties.Title ?? "Неизвестно";
            string artist = mediaProperties.Artist ?? "Неизвестный исполнитель";

            var result = new
            {
                status = "DONE",
                message = $"В плеере {session} удалось найти информацию о треке",
                currentArtist = artist,
                currentTitle = title
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new
            {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Ставит музыку на паузу или возобновляет воспроизведение")]
    public async Task<string> PlayPause() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
            {
                var error = new
                {
                    status = "ERROR",
                    cause = session,
                    description = "Не удалсь найти действующий плеер на этом устройстве"
                };
                return JsonSerializer.Serialize(error);
            }

            var playbackInfo = session.GetPlaybackInfo();

            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await session.TryPauseAsync();
            else
                await session.TryPlayAsync();

            var result = new
            {
                status = "DONE",
                message = $"Состояние текущего плеера - {playbackInfo.PlaybackStatus}",
                currentPlaybackStatus = playbackInfo.PlaybackStatus,
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new
            {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Переключает на следующий трек")]
    public async Task<string> NextTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
            {
                var error = new
                {
                    status = "ERROR",
                    cause = session,
                    description = "Не удалсь найти действующий плеер на этом устройстве"
                };
                return JsonSerializer.Serialize(error);
            }

            await session.TrySkipNextAsync();
            var result = new
            {
                status = "DONE",
                message = "Переключен на следующий трек",
                currentTrack = session.TryGetMediaPropertiesAsync().ToString()
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new
            {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Переключает на предыдущий трек")]
    public async Task<string> PreviousTrack() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
            {
                var error = new
                {
                    status = "ERROR",
                    cause = session,
                    description = "Не удалсь найти действующий плеер на этом устройстве"
                };
                return JsonSerializer.Serialize(error);
            }

            await session.TrySkipPreviousAsync();
            var result = new
            {
                status = "DONE",
                message = "Переключен на предыдущий трек",
                currentTrack = session.TryGetMediaPropertiesAsync().ToString()
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new
            {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Останавливает воспроизведение")]
    public async Task<string> Stop() {
        try {
            var session = await GetCurrentSession();
            if (session == null)
            {
                var error = new
                {
                    status = "ERROR",
                    cause = session,
                    description = "Не удалсь найти действующий плеер на этом устройстве"
                };
                return JsonSerializer.Serialize(error);
            }

            await session.TryStopAsync();
            var result = new
            {
                status = "DONE",
                message = $"Текущий плеер - {session} приостановлен"
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new
            {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
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

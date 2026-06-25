using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace Jarvis.Plugins;

public class SystemAudioPlugin {
    [KernelFunction]
    [Description("Изменение уровня громкости системы")]
    public static string ChangeVolume([Description("Значение от 0.0 до 1.0. Например: 'громкость 50%' значит 0.5f")] float volume) {
        try {
            var clampedVolume = Math.Clamp(volume, 0.0f, 1.0f);

            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = clampedVolume;

            var percent = (int)(clampedVolume * 100);

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Громкость установлена на {percent}%",
                volume = clampedVolume,
                percentVolume = percent
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "audio_error",
                description = $"Ошибка изменения громкости: {ex.Message}"
            });
        }
    }

    [KernelFunction]
    [Description("Отключение громкости системы")]
    public static string VolumeTurnOff() {
        try {
            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = true;

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Звук выключен",
                isMuted = true
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "audio_error",
                description = $"Ошибка отключения звука: {ex.Message}"
            });
        }
    }
}
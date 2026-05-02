using Microsoft.SemanticKernel;
using System.ComponentModel;
using NAudio.CoreAudioApi;

namespace Jarvis.Plugins;
public class SystemAudioPlugin
{
    [KernelFunction]
    [Description("Изменение уровня громкости системы")]
    public string ChangeVolume([Description("Значение от 0.0 до 1.0. Например: 'громкость 50%' значит 0.5f")] float volume)
    {
        try
        {
            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;

            return "Done";
        }
        catch
        {
            return "Error";
        }
    }

    [KernelFunction]
    [Description("Отключение громкости системы")]
    public string VolumeTurnOff()
    {
        try
        {
            var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = true;

            return "Done";
        }
        catch
        {
            return "Error";
        }
    }
}


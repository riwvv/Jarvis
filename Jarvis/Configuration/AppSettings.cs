using System.IO;

namespace Jarvis.Configuration;

public class AppSettings {
    public AISettings AISettings { get; set; } = new();
    public SpeechSettings SpeechSettings { get; set; } = new();
}

public class AISettings {
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string ModelId { get; set; } = "qwen2.5:7b";
    public string ApiKey { get; set; } = "dummy";
}

public class SpeechSettings {
    public string SttModelPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\vosk-model-ru-0.42");
    public int SttSampleRate { get; set; } = 16000;
    public int TtsRate { get; set; } = 1;
    public int TtsVolume { get; set; } = 75;
}

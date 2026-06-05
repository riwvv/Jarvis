namespace Jarvis.Configuration;

public class AISettings {
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string ModelId { get; set; } = "qwen2.5:7b";
    public string ApiKey { get; set; } = "dummy";
    public string EmbeddingEndpoint { get; set; } = "http://localhost:11434";
    public string EmbeddingModelId { get; set; } = "qwen3-embedding:4b";
}

public class STTSettings {
    public string SttModelPath { get; set; } = "Models\\vosk-model-ru-0.42";
    public int SttSampleRate { get; set; } = 16000;
}

public class TTSSettings {
    public string VoiceName { get; set; } = "Pavel";
    public string InstallerFileName { get; set; } = "RHVoice-voice-Russian-Pavel-v4.0.2017.22-setup.exe";
    public string InstallerSubPath { get; set; } = "Resources";
    public int TtsRate { get; set; } = 1;
    public int TtsVolume { get; set; } = 75;
}
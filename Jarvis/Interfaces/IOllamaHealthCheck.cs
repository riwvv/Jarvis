namespace Jarvis.Interfaces;

public interface IOllamaHealthCheck {
    Task<bool> IsOllamaRunningAsync();
}

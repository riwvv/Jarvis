namespace Jarvis.Interfaces;

public interface IOllamaConnectionValidator {
    Task<bool> ValidateWithRetryAsync();
}

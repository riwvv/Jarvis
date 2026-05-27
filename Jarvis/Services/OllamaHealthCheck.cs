using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Jarvis.Configuration;
using Jarvis.Interfaces;

namespace Jarvis.Services;

public class OllamaHealthCheck(IConfiguration configuration) : IOllamaHealthCheck {
    public async Task<bool> IsOllamaRunningAsync() {
        try {
            var endpoint = configuration.GetSection("AISettings").Get<AISettings>()!.Endpoint;
            using var client = new HttpClient() {
                Timeout = TimeSpan.FromSeconds(3)
            };
            var response = await client.GetAsync($"{endpoint[..^3]}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch {
            return false;
        }
    }
}

using FluentAssertions;
using Jarvis.Plugins;
using Jarvis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace TestJarvis.Tests;

public class CommunicationAIServiceTest {
    
    
    [Fact]
    public async Task TryGetResponseFromAI_Test() {
        var services = new ServiceCollection();

        var builder = Kernel.CreateBuilder();
        builder.Plugins.AddFromType<SystemAudioPlugin>();
        builder.AddOpenAIChatCompletion(modelId: "qwen2.5:7b", endpoint: new Uri("http://localhost/11434/v1"), apiKey: "dummy");
        
        var kernel = builder.Build();

        services.AddSingleton(kernel);
        services.AddSingleton<CommunicationAiService>();

        var serviceProvider = services.BuildServiceProvider();
        var testService = serviceProvider.GetRequiredService<CommunicationAiService>();
        var testRequest = "выключи звук";

        var response = testService.GetRequestUser(testRequest);

        response.Should().NotBeNull();
    }
}

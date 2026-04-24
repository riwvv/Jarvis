using FluentAssertions;
using Jarvis.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;

namespace JarvisTest.Tests;

public class CommunicationAIServiceTest {
    [Fact]
    public async Task GetResponseByAI_Test() {
        var service = new CommunicationAiService();
        string testRequest = "выключи звук";

        var result = await service.GetRequestUser(testRequest);

        result.Should().NotBeNull();
        Debug.WriteLine($"Результат: {result}");
    }
}

using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using Jarvis.Interfaces;

namespace Jarvis.Plugins;

public class RagPlugin(IRagMemoryService _ragMemory) {
    [KernelFunction]
    [Description("Ищет в долговременной памяти информацию о прошлых диалогах, фактах о пользователе, успешных командах. Используй эту функцию, когда нужно вспомнить что-то из прошлого.")]
    public async Task<string> SearchMemory([Description("Что нужно найти. Например: 'просил открыть ютуб', 'меня зовут', 'какую программу открывал вчера'")] string query) {
        var result = await _ragMemory.SearchRelevantContextAsync(query, topK: 3);

        if (string.IsNullOrEmpty(result)) {
            return JsonSerializer.Serialize(new {
                status = "WARNING",
                cause = "no_results",
                description = "Ничего не найдено в памяти",
                originalQuery = query
            });
        }

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = "Найдены релевантные записи в памяти",
            originalQuery = query,
            memory = result,
            source = "RAG"
        });
    }
}
using Jarvis.Interfaces;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace Jarvis.Plugins;

public class RagPlugin(IRagMemoryService _ragMemory) {
    [KernelFunction]
    [Description("Ищет в долговременной памяти информацию о прошлых диалогах, фактах о пользователе, успешных командах. Используй эту функцию, когда нужно вспомнить что-то из прошлого.")]
    public async Task<string> SearchMemory([Description("Что нужно найти. Например: 'просил открыть ютуб', 'меня зовут', 'какую программу открывал вчера'")] string query) {
        var result = await _ragMemory.SearchRelevantContextAsync(query, topK: 3);
        return string.IsNullOrEmpty(result) ? "Ничего не найдено в памяти" : result;
    }
}

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

    [KernelFunction]
    [Description("Сохраняет важную информацию в долговременную память. Используй когда пользователь сообщает факты о себе или команда выполнена успешно.")]
    public async Task<string> SaveToMemory([Description("Что пользователь сказал или сделал")] string userAction, [Description("Как ты на это отреагировал или что сделал")] string assistantResponse) {
        await _ragMemory.SaveMemoryAsync(userAction, assistantResponse);
        return "Информация сохранена в память";
    }
}

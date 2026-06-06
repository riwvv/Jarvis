using Microsoft.SemanticKernel;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;

namespace Jarvis.Plugins;

public class ClipboardPlugin {
    [KernelFunction]
    [Description("Читает текст из буфера обмена")]
    public async Task<string> GetTextFromClipBoard() => await ExecuteOnStaThreadAsync(() => {
        try {
            if (!Clipboard.ContainsText())
                return "Буфер обмена не содержит текста";

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
                return "Буфер обмена содержит пустой текст";

            if (text.Length > 500)
                return $"Текст слишком длинный ({text.Length} символов). Первые 500 символов:\n{text[..500]}...";

            return text;
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return "Буфер обмена временно недоступен, попробуйте ещё раз";
        }
        catch (Exception) {
            return $"Ошибка доступа к буферу обмена";
        }
    });

    [KernelFunction]
    [Description("Записывает текст в буфер обмена")]
    public async Task<string> SetTextToClipboard([Description("Текст для записи в буфер обмена")] string primatyText) => await ExecuteOnStaThreadAsync(() => {
        if (string.IsNullOrWhiteSpace(primatyText))
            return "Копировать нечего: текст пуст";
        try {
            Clipboard.SetText(primatyText);
            var preview = primatyText.Length > 20 ? primatyText[..20] + "..." : primatyText;
            return $"Скопировано в буфер обмена. Начинается на: {preview}";

        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return "Буфер обмена временно недоступен, попробуйте ещё раз";
        }
        catch (Exception) {
            return "Ошибка записи в буфер обмена";
        }
    });

    [KernelFunction]
    [Description("Очищает буфер обмена")]
    public async Task<string> ClearClipboard() => await ExecuteOnStaThreadAsync(() => {
        try {
            Clipboard.Clear();
            return "Буфер обмена очищен";
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return "Буфер обмена временно недоступен, попробуйте ещё раз";
        }
        catch (Exception) {
            return "Ошибка очистки буфера обмена";
        }
    });

    [KernelFunction]
    [Description("Возвращает информацию о содержимом буфера обмена")]
    public async Task<string> GetInfoFromClipboard() => await ExecuteOnStaThreadAsync(() => {
        try {
            var info = new StringBuilder();

            info.AppendLine("Информация о буфере обмена:");
            info.AppendLine($"- Текст: {(Clipboard.ContainsText() ? "есть" : "нет")}");
            info.AppendLine($"- Изображения: {(Clipboard.ContainsImage() ? "есть" : "нет")}");
            info.AppendLine($"- Файлы: {(Clipboard.ContainsFileDropList() ? "есть" : "нет")}");
            info.AppendLine($"- Аудио: {(Clipboard.ContainsAudio() ? "есть" : "нет")}");

            if (Clipboard.ContainsText()) {
                var text = Clipboard.GetText();
                info.AppendLine($"- Длина текста: {text.Length}");
                if (text.Length > 0)
                    info.AppendLine($"- Начало текста: {(text.Length > 50 ? text[..50] : text)}");
            }

            return info.ToString();
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return "Буфер обмена временно недоступен, попробуйте ещё раз";
        }
        catch (Exception) {
            return "Ошибка получения информации о содержимом буфера обмена";
        }
    });

    private async Task<T> ExecuteOnStaThreadAsync<T>(Func<T> action) {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() => {
            try {
                var result = action();
                tcs.SetResult(result);
            }
            catch (Exception ex) {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await tcs.Task;
    }
}

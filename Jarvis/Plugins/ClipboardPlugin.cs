using Microsoft.SemanticKernel;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text.Json;

namespace Jarvis.Plugins;

public class ClipboardPlugin {
    [KernelFunction]
    [Description("Читает текст из буфера обмена")]
    public static async Task<string> GetTextFromClipBoard() => await ExecuteOnStaThreadAsync(() => {
        try {
            if (!Clipboard.ContainsText()) {
                return JsonSerializer.Serialize(new {
                    status = "WARNING",
                    cause = "empty_clipboard",
                    description = "Буфер обмена не содержит текста"
                });
            }

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) {
                return JsonSerializer.Serialize(new {
                    status = "WARNING",
                    cause = "empty_text",
                    description = "Буфер обмена содержит пустой текст"
                });
            }

            var isTruncated = text.Length > 500;
            var content = isTruncated ? text[..500] + "..." : text;

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = isTruncated
                    ? $"Текст слишком длинный ({text.Length} символов). Первые 500 символов..."
                    : "Текст прочитан из буфера обмена",
                text = content,
                fullLength = text.Length,
                truncated = isTruncated
            });
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "clipboard_busy",
                description = "Буфер обмена временно недоступен, попробуйте ещё раз"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "access_denied",
                description = $"Ошибка доступа к буферу обмена: {ex.Message}"
            });
        }
    });

    [KernelFunction]
    [Description("Записывает текст в буфер обмена")]
    public static async Task<string> SetTextToClipboard([Description("Текст для записи в буфер обмена")] string primaryText) => await ExecuteOnStaThreadAsync(() => {
        if (string.IsNullOrWhiteSpace(primaryText)) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "empty_text",
                description = "Нечего копировать: текст пуст"
            });
        }

        try {
            Clipboard.SetText(primaryText);
            var preview = primaryText.Length > 20 ? primaryText[..20] + "..." : primaryText;

            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = $"Текст скопирован в буфер обмена",
                text = primaryText,
                previewText = preview,
                length = primaryText.Length
            });
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "clipboard_busy",
                description = "Буфер обмена временно недоступен, попробуйте ещё раз"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "access_denied",
                description = $"Ошибка записи в буфер обмена: {ex.Message}"
            });
        }
    });

    [KernelFunction]
    [Description("Очищает буфер обмена")]
    public static async Task<string> ClearClipboard() => await ExecuteOnStaThreadAsync(() => {
        try {
            Clipboard.Clear();
            return JsonSerializer.Serialize(new {
                status = "DONE",
                message = "Буфер обмена очищен"
            });
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "clipboard_busy",
                description = "Буфер обмена временно недоступен, попробуйте ещё раз"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "access_denied",
                description = $"Ошибка очистки буфера обмена: {ex.Message}"
            });
        }
    });

    [KernelFunction]
    [Description("Возвращает информацию о содержимом буфера обмена")]
    public static async Task<string> GetInfoFromClipboard() => await ExecuteOnStaThreadAsync(() => {
        try {
            var hasText = Clipboard.ContainsText();
            var hasImage = Clipboard.ContainsImage();
            var hasFiles = Clipboard.ContainsFileDropList();
            var hasAudio = Clipboard.ContainsAudio();

            var result = new {
                status = "DONE",
                message = "Информация о буфере обмена получена",
                contains = new {
                    text = hasText,
                    image = hasImage,
                    files = hasFiles,
                    audio = hasAudio
                },
                textInfo = hasText ? new {
                    length = Clipboard.GetText().Length,
                    preview = Clipboard.GetText().Length > 50
                        ? Clipboard.GetText()[..50] + "..."
                        : Clipboard.GetText()
                } : null
            };

            return JsonSerializer.Serialize(result);
        }
        catch (ExternalException ex) when (ex.ErrorCode == -2147221040) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "clipboard_busy",
                description = "Буфер обмена временно недоступен, попробуйте ещё раз"
            });
        }
        catch (Exception ex) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "access_denied",
                description = $"Ошибка получения информации о буфере обмена: {ex.Message}"
            });
        }
    });

    private static async Task<T> ExecuteOnStaThreadAsync<T>(Func<T> action) {
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
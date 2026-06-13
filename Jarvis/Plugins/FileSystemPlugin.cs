using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis.Plugins;

public class FileSystemPlugin {
    [KernelFunction]
    [Description("Получает данные о папке")]
    public async Task<string> GetInfoFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName) {
        if (string.IsNullOrWhiteSpace(folderName)) return "Укажите корректное название папки";

        try {
            var path = GetFullFolderPath(folderName);
            Debug.WriteLine(path);

            if (!Directory.Exists(path)) return $"Папка {folderName} не найдена";

            var result = new StringBuilder();
            result.AppendLine($"В папке {folderName} объектов найдено: {Directory.GetFileSystemEntries(path).Length}\n");
            result.AppendLine($"Файлы: {Directory.GetFiles(path).Length}");
            result.AppendLine($"Папки: {Directory.GetDirectories(path).Length}");

            Debug.WriteLine(result.ToString());
            return result.ToString();
        }
        catch (ArgumentException ex) {
            return ex.Message;
        }
        catch (Exception) {
            return "Неизвестная ошибка";
        }
    }

    private string GetFullFolderPath(string folderName) => folderName.ToLower() switch {
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => throw new ArgumentException($"Неизвестный тип папки: {folderName}")
    };
}

using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace Jarvis.Plugins;

public class FileSystemPlugin {
    [KernelFunction]
    [Description("Получает данные о папке")]
    public async Task<string> GetInfoFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName) {
        if (string.IsNullOrWhiteSpace(folderName)) return "Укажите корректное название папки";

        try {
            var path = GetFullFolderPath(folderName);

            if (!Directory.Exists(path)) return $"Папка {folderName} не найдена";

            var dir = new DirectoryInfo(path);
            var size = RecursivelyGettingTheSize(dir);
            var stringSize = ConversionToTheOptimalUnit(size);

            var result = new StringBuilder();
            result.AppendLine($"В папке {folderName} объектов найдено: {Directory.GetFileSystemEntries(path).Length}\n");
            result.AppendLine($"Файлы: {Directory.GetFiles(path).Length}");
            result.AppendLine($"Папки: {Directory.GetDirectories(path).Length}");
            result.AppendLine($"Общий размер: {stringSize}");

            return result.ToString();
        }
        catch (ArgumentException ex) {
            return ex.Message;
        }
        catch (Exception) {
            return "Неизвестная ошибка";
        }
    }

    private string ConversionToTheOptimalUnit(long bytes) => bytes switch {
        < 1000 => $"{bytes} байт",
        >= 1000 and < 1000000 => $"{bytes / 1000} килобайт",
        >= 1000000 and < 1000000000 => $"{bytes / 1000000} мегабайт",
        >= 1000000000 and < 1000000000000 => $"{bytes / 1000000000} гигабайт",
        >= 1000000000000 and < 1000000000000000 => $"{bytes / 1000000000000} терабайт",
        >= 1000000000000000 and < 1000000000000000000 => $"{bytes / 1000000000000000} петабайт",
        _ => $"{bytes} байт"
    };

    private long RecursivelyGettingTheSize(DirectoryInfo dir) {
        long size = dir.GetFiles().Sum(file => file.Length);

        size += dir.GetDirectories().Sum(RecursivelyGettingTheSize);

        return size;
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

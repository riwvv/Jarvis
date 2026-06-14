using Microsoft.SemanticKernel;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace Jarvis.Plugins;

public class FileSystemPlugin {
    private readonly List<string> _folderNames = ["downloads", "documents", "pictures", "videos", "music", "desktop"];
    private readonly Dictionary<string, List<string>> _fileTypes = new() {
        { "текстовик", [".txt"] },
        { "текстовый документ", [".txt"] },
        { "фото", [".png", ".jpg", ".jpeg"] },
        { "фотка", [".png", ".jpg", ".jpeg"] },
        { "видео", [".mp4", ".mkv"] },
        { "видос", [".mp4", ".mkv"] },
        { "презентация", [".pptx", ".ppt", ".pdf"] },
        { "ворд", [".docx"] },
        { "документ", [".docx"] },
        { "таблица", [".xlsx", ".ods", ".csv"] }
    };

    [KernelFunction]
    [Description("Получает данные о папке")]
    public async Task<string> GetInfoFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName) {
        if (!SpecialFolderValidation(folderName)) return "Укажите корректное название папки";

        try {
            var path = GetFullFolderPath(folderName);

            if (!Directory.Exists(path)) return $"Папка {folderName} не найдена";

            var dir = new DirectoryInfo(path);
            var size = RecursivelyGettingTheDirectorySize(dir);
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

    [KernelFunction]
    [Description("Открывает файл в указаной специальной папке")]
    public async Task<string> OpenFileInFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName, [Description("Название файла")] string fileName, [Description("Тип файла, например: текстовик, презентация, фото, видео и т.д. (если указан, но не обязателен, иначе оставить как null)")] string? fileType = null) {
        if (!SpecialFolderValidation(folderName)) return "Укажите корректное название папки";
        if (string.IsNullOrWhiteSpace(fileName)) return "Укажите корректное название файла";

        try {
            string path = GetFullFolderPath(folderName);
            string executableFilePath = SearchForRelevantFile(path, fileName, fileType);

            if (string.IsNullOrEmpty(executableFilePath)) return "Файл не найден";

            Process.Start(new ProcessStartInfo(executableFilePath) { UseShellExecute = true });
            return $"Файл открыт";
        }
        catch (Exception) {
            return "Ошибка при открытии файла";
        }
    }

    private List<string>? GetRecognizedFileType(string? type = null) {
        if (type == null || string.IsNullOrWhiteSpace(type))
            return null;

        type = type.ToLower();
        foreach (var (key, value) in _fileTypes) {
            if (type == key)
                return value;
        }

        return null;
    }

    private string SearchForRelevantFile(string path, string targetFileName, string? targetFileType = null) {
        List<string> files = [..Directory.GetFiles(path)];

        List<string>? targetFileTypes = GetRecognizedFileType(targetFileType);
        string patern = targetFileTypes == null ? @$"{FormattingFileName(targetFileName)}(\w*)" : @$"{FormattingFileName(targetFileName)}(\w*){string.Join("|", targetFileTypes!.Select(Regex.Escape))}";
        Regex regex = new(patern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        Dictionary<string, double> results = [];

        for (int i = 0; i < files.Count; i++) {
            string fileName = files[i][(path.Length + 1)..];
            if (regex.IsMatch(fileName))
                results[files[i]] = GetPercentageOfRelevantSimilarity(fileName, targetFileName);
        }

        return results.Count == 0 ? string.Empty : results.OrderByDescending(x => x.Value).First().Key;
    }

    private bool SpecialFolderValidation(string folder) => !string.IsNullOrWhiteSpace(folder) && _folderNames.Contains(folder.ToLower());

    private static string FormattingFileName(string target) {
        int lastDot = target.LastIndexOf('.');
        return lastDot == -1 ? target : target[..lastDot];
    }

    private static double GetPercentageOfRelevantSimilarity(string source, string target) {
        int distance = GetLevenshteinDistanceForDesiredFileName(source, target);
        int maxLength = Math.Max(source.Length, target.Length);

        if (maxLength == 0) return 100.0d;

        double result = (1.0d - (Convert.ToDouble(distance) / maxLength)) * 100.0d;

        return result;
    }

    private static int GetLevenshteinDistanceForDesiredFileName(string source, string target) {
        if (string.IsNullOrEmpty(source)) return string.IsNullOrEmpty(target) ? 0 : target.Length;
        if (string.IsNullOrEmpty(target)) return source.Length;

        int[,] d = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; d[i, 0] = i++) ;
        for (int j = 0; j <= target.Length; d[0, j] = j++) ;

        for (int i = 1; i <= source.Length; i++) {
            for (int j = 1; j <= target.Length; j++) {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[source.Length, target.Length];
    }

    private static string ConversionToTheOptimalUnit(long bytes) => bytes switch {
        < 1000 => $"{bytes} байт",
        >= 1000 and < 1000000 => $"{bytes / 1000} килобайт",
        >= 1000000 and < 1000000000 => $"{bytes / 1000000} мегабайт",
        >= 1000000000 and < 1000000000000 => $"{bytes / 1000000000} гигабайт",
        >= 1000000000000 and < 1000000000000000 => $"{bytes / 1000000000000} терабайт",
        >= 1000000000000000 and < 1000000000000000000 => $"{bytes / 1000000000000000} петабайт",
        _ => $"{bytes} байт"
    };

    private static long RecursivelyGettingTheDirectorySize(DirectoryInfo dir) {
        long size = dir.GetFiles().Sum(file => file.Length);

        size += dir.GetDirectories().Sum(RecursivelyGettingTheDirectorySize);

        return size;
    }

    private static string GetFullFolderPath(string folderName) => folderName.ToLower() switch {
        "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        _ => throw new ArgumentException($"Неизвестный тип папки: {folderName}")
    };
}

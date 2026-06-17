using Microsoft.SemanticKernel;
using Microsoft.VisualBasic.FileIO;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
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
        if (!SpecialFolderValidation(folderName)) {
            var error = new {
                status = "ERROR",
                cause = folderName,
                description = $"Указано не корректное название папки, не соответствует поддерживаемому списку: {_folderNames}"
            };

            return JsonSerializer.Serialize(error);
        }

        try {
            var path = GetFullFolderPath(folderName);

            if (!Directory.Exists(path)) {
                var error = new {
                    status = "ERROR",
                    cause = path,
                    description = $"Папка {folderName} по пути {path} не найдена"
                };

                return JsonSerializer.Serialize(error);
            }

            var dir = new DirectoryInfo(path);
            var size = RecursivelyGettingTheDirectorySize(dir);
            var stringSize = ConversionToTheOptimalUnit(size);

            var result = new {
                status = "DONE",
                message = $"В папке {folderName} {Directory.GetFileSystemEntries(path).Length} объектов, размер {stringSize}",
                totalObject = Directory.GetFileSystemEntries(path).Length,
                amountFiles = Directory.GetFiles(path).Length,
                files = Directory.GetFiles(path),
                amountDirectories = Directory.GetDirectories(path).Length,
                directories = Directory.GetDirectories(path),
                totalSize = stringSize
            };

            return JsonSerializer.Serialize(result);
        }
        catch (ArgumentException ex) {
            var error = new {
                status = "ERROR",
                cause = "ArgumentException",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
        catch (Exception ex) {
            var error = new {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };
            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Открывает файл в указаной специальной папке")]
    public async Task<string> OpenFileInFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName, [Description("Название файла")] string fileName, [Description("Тип файла, например: текстовик, презентация, фото, видео и т.д. (если указан, но не обязателен, иначе оставить как null)")] string? fileType = null) {
        if (!SpecialFolderValidation(folderName)) {
            var error = new {
                status = "ERROR",
                cause = folderName,
                description = $"Указано не корректное название папки, не соответствует поддерживаемому списку: {_folderNames}"
            };

            return JsonSerializer.Serialize(error);
        }
        if (string.IsNullOrWhiteSpace(fileName)) {
            var error = new {
                status = "ERROR",
                cause = fileName,
                description = $"Указано не корректное название файла"
            };

            return JsonSerializer.Serialize(error);
        }

        try {
            string path = GetFullFolderPath(folderName);
            string executableFilePath = SearchForRelevantFile(path, fileName, fileType);

            if (string.IsNullOrEmpty(executableFilePath)) {
                var error = new {
                    status = "ERROR",
                    cause = executableFilePath,
                    description = $"Файл не найден"
                };

                return JsonSerializer.Serialize(error);
            }

            Process.Start(new ProcessStartInfo(executableFilePath) { UseShellExecute = true });

            string name = GetFullFileNameFromPath(executableFilePath);
            var result = new {
                status = "DONE",
                message = $"Файл {name} успешно открыт",
                openedFile = name,
                fullFilePath = executableFilePath
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };

            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Удаляет файл в указаной специальной папке")]
    public async Task<string> DeleteFileInFolder([Description("Название одной из папок: downloads, documents, pictures, videos, music, desktop")] string folderName, [Description("Название файла")] string fileName, [Description("Тип файла, например: текстовик, презентация, фото, видео и т.д. (если указан, но не обязателен, иначе оставить как null)")] string? fileType = null) {
        if (!SpecialFolderValidation(folderName)) {
            var error = new {
                status = "ERROR",
                cause = folderName,
                description = $"Указано не корректное название папки, не соответствует поддерживаемому списку: {_folderNames}"
            };

            return JsonSerializer.Serialize(error);
        }
        if (string.IsNullOrWhiteSpace(fileName)) {
            var error = new {
                status = "ERROR",
                cause = fileName,
                description = "Указано не корректное название файла"
            };

            return JsonSerializer.Serialize(error);
        }

        try {
            string path = GetFullFolderPath(folderName);
            string deletedFilePath = SearchForRelevantFile(path, fileName, fileType);

            if (string.IsNullOrEmpty(deletedFilePath)) {
                var errorPath = new {
                    status = "ERROR",
                    cause = deletedFilePath,
                    description = "Не корректный путь файла"
                };

                return JsonSerializer.Serialize(errorPath);
            }
            if (!File.Exists(deletedFilePath)) {
                var errorExists = new {
                    status = "ERROR",
                    cause = deletedFilePath,
                    description = "Файл не найден"
                };

                return JsonSerializer.Serialize(errorExists);
            }

            DialogResult confirmation = MessageBox.Show(
                $"Вы уверены, что хотите удалить файл?\n{deletedFilePath}",
                "Подтверждение действия",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation == DialogResult.Yes) {
                FileSystem.DeleteFile(deletedFilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                string name = GetFullFileNameFromPath(deletedFilePath);
                var result = new {
                    status = "DONE",
                    message = $"Файл {name} успешно удалён",
                    deletedFile = name,
                    fullFilePath = deletedFilePath
                };

                return JsonSerializer.Serialize(result);
            }

            var error = new {
                status = "WARNING",
                cause = confirmation,
                description = "Пользователь нажал отказ на удаление"
            };

            return JsonSerializer.Serialize(error);
        }
        catch (Exception ex) {
            var error = new {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };

            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Перемещает файл из одной папки в другую (в рамках специальных папок), например: перемести текстовик тест из загрузок на рабочий стол")]
    public async Task<string> MoveFileFromFolderToFolder(
        [Description("Название начальной папки, одной из: downloads, documents, pictures, videos, music, desktop")] string startFolder, 
        [Description("Название конечной папки, одной из: downloads, documents, pictures, videos, music, desktop")] string finishFolder, 
        [Description("Название файла")] string fileName, 
        [Description("Тип файла, например: текстовик, презентация, фото, видео и т.д. (если указан, но не обязателен, иначе оставить как null)")] string? fileType = null) {
        if (!SpecialFolderValidation(startFolder)) {
            var error = new {
                status = "ERROR",
                cause = startFolder,
                description = $"Указано не корректное название папки, не соответствует поддерживаемому списку: {_folderNames}"
            };

            return JsonSerializer.Serialize(error);
        }
        if (!SpecialFolderValidation(finishFolder)) {
            var error = new {
                status = "ERROR",
                cause = finishFolder,
                description = $"Указано не корректное название папки, не соответствует поддерживаемому списку: {_folderNames}"
            };

            return JsonSerializer.Serialize(error);
        }
        if (string.IsNullOrWhiteSpace(fileName)) {
            var error = new {
                status = "ERROR",
                cause = fileName,
                description = "Указано не корректное название файла"
            };

            return JsonSerializer.Serialize(error);
        }

        try {
            string movedFilePath = SearchForRelevantFile(GetFullFolderPath(startFolder), fileName, fileType);
            string foundFileName = GetFullFileNameFromPath(movedFilePath);

            string finishPath = GetFullFolderPath(finishFolder);
            string finishFilePath = Path.Combine(finishPath, foundFileName);

            if (File.Exists(finishFilePath)) {
                var error = new {
                    status = "WARNING",
                    cause = finishFilePath,
                    description = "Файл с таким названием уже существует в папке назначения"
                };

                return JsonSerializer.Serialize(error);
            }

            File.SetAttributes(movedFilePath, FileAttributes.Normal);
            File.Move(movedFilePath, finishFilePath);

            var result = new {
                status = "DONE",
                message = $"Файл {foundFileName} успешно перемещён",
                movedFile = foundFileName,
                originalPath = movedFilePath,
                finitePath = finishFilePath
            };

            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex) {
            var error = new {
                status = "ERROR",
                cause = "Exception",
                description = ex.Message
            };

            return JsonSerializer.Serialize(error);
        }
    }

    [KernelFunction]
    [Description("Создание файла в специальной папке с указанным типом (если не указан, по умолчанию текстовик)")]
    public async Task<string> CreateNewFileInSpecialFolder([Description("Название одной из специальных папок: downloads, documents, pictures, videos, music, desktop")] string folderName,
        [Description("Название файла (без расширения)")] string fileName,
        [Description("Тип файла, например: текстовик, презентация, фото, видео и т.д.")] string? fileType = null) {
        if (!SpecialFolderValidation(folderName)) {
            var error = new {
                status = "ERROR",
                cause = folderName,
                description = $"Некорректное название папки. Поддерживаемые: {string.Join(", ", _folderNames)}"
            };

            return JsonSerializer.Serialize(error);
        }
        if (string.IsNullOrWhiteSpace(fileName)) {
            var error = new {
                status = "ERROR",
                cause = fileName,
                description = "Название файла не может быть пустым"
            };

            return JsonSerializer.Serialize(error);
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.Any(c => invalidChars.Contains(c))) {
            var error = new {
                status = "ERROR",
                cause = fileName,
                description = $"Имя файла содержит недопустимые символы: {string.Join(", ", fileName.Where(c => invalidChars.Contains(c)).Distinct())}"
            };

            return JsonSerializer.Serialize(error);
        }

        try {
            string primaryPath = GetFullFolderPath(folderName);

            if (!Directory.Exists(primaryPath)) {
                var error = new {
                    status = "ERROR",
                    cause = primaryPath,
                    description = $"Папка {folderName} не найдена по пути {primaryPath}"
                };

                return JsonSerializer.Serialize(error);
            }

            string extension;
            if (!string.IsNullOrEmpty(fileType)) {
                if (TryGetRecognizedFileType(fileType, out string? resultType))
                    extension = resultType!;
                else
                    extension = fileType.StartsWith(".") ? fileType : "." + fileType;
            }
            else
                extension = _fileTypes.TryGetValue("текстовик", out var types) && types.Count != 0 ? types.First() : ".txt";

            string cleanFileName = Path.GetFileNameWithoutExtension(fileName);
            string fullFileName = cleanFileName + extension;
            string fullPath = Path.Combine(primaryPath, fullFileName);

            if (File.Exists(fullPath)) {
                var warning = new {
                    status = "WARNING",
                    cause = fullPath,
                    description = $"Файл '{fullFileName}' уже существует в папке {folderName}"
                };

                return JsonSerializer.Serialize(warning);
            }

            using FileStream fs = File.Create(fullPath);

            var result = new {
                status = "DONE",
                message = $"Файл '{fullFileName}' успешно создан в папке '{folderName}'",
                name = cleanFileName,
                folder = primaryPath,
                fullFileName = fullFileName,
                fullFilePath = fullPath,
                fileType = extension
            };

            return JsonSerializer.Serialize(result);
        }
        catch (UnauthorizedAccessException ex) {
            var error = new {
                status = "ERROR",
                cause = "AccessDenied",
                description = $"Нет прав на создание файла в папке {folderName}: {ex.Message}"
            };
            return JsonSerializer.Serialize(error);
        }
        catch (IOException ex) {
            var error = new {
                status = "ERROR",
                cause = "IOError",
                description = $"Ошибка ввода-вывода: {ex.Message}"
            };
            return JsonSerializer.Serialize(error);
        }
        catch (Exception ex) {
            var error = new {
                status = "ERROR",
                cause = "UnexpectedError",
                description = $"Произошла непредвиденная ошибка при создании файла: {ex.Message}"
            };
            return JsonSerializer.Serialize(error);
        }
    }

    private bool TryGetRecognizedFileType(string? type, out string? outputType) {
        outputType = null;

        if (string.IsNullOrWhiteSpace(type))
            return false;

        type = type.Trim().ToLower();

        foreach (var kvp in _fileTypes) {
            if (type == kvp.Key) {
                outputType = kvp.Value.FirstOrDefault();
                return outputType != null;
            }
        }

        return false;
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

    private static string GetFullFileNameFromPath(string path) {
        int lastSlash = path.LastIndexOf(@"\");
        return path[(lastSlash + 1)..];
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

using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis.Plugins;
public class FilePlugin
{
    [KernelFunction]
    [Description("Создаёт файл с содержимым")]
    public async Task<string> CreateFile(
        [Description("Полный путь к файлу")] string filePath,
        [Description("Содержимое файла")] string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
            return $"Файл создан: {filePath}";
        }
        catch (Exception ex)
        {
            return $"Ошибка создания: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Читает содержимое файла")]
    public async Task<string> ReadFile(
        [Description("Полный путь к файлу")] string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"Файл не найден: {filePath}";

            string content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return string.IsNullOrEmpty(content) ? "Файл пуст" : content;
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Заменяет текст в файле")]
    public async Task<string> ReplaceInFile(
        [Description("Полный путь к файлу")] string filePath,
        [Description("Что заменить")] string oldText,
        [Description("На что заменить")] string newText)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"Файл не найден: {filePath}";

            string content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            content = content.Replace(oldText, newText);
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
            return $"Текст заменён в: {filePath}";
        }
        catch (Exception ex)
        {
            return $"Ошибка редактирования: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Добавляет текст в конец файла")]
    public async Task<string> AppendToFile(
        [Description("Полный путь к файлу")] string filePath,
        [Description("Текст для добавления")] string content)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"Файл не найден: {filePath}";

            await File.AppendAllTextAsync(filePath, content, Encoding.UTF8);
            return $"Текст добавлен: {filePath}";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Удаляет файл")]
    public string DeleteFile(
        [Description("Полный путь к файлу")] string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"Файл не найден: {filePath}";

            File.Delete(filePath);
            return $"Файл удалён: {filePath}";
        }
        catch (Exception ex)
        {
            return $"Ошибка удаления: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает файл в программе по умолчанию")]
    public string OpenFile(
        [Description("Полный путь к файлу")] string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return $"Файл не найден: {filePath}";

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return $"Открыто: {filePath}";
        }
        catch (Exception ex)
        {
            return $"Ошибка открытия: {ex.Message}";
        }
    }
}


using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Jarvis.Plugins;

public class FilePlugin
{
    private static string DesktopPath => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    [KernelFunction]
    [Description("Создаёт файл на рабочем столе")]
    public async Task<string> CreateFile(
        [Description("Имя файла (например: заметки.txt)")] string fileName,
        [Description("Содержимое файла")] string content)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            return $"Файл создан на рабочем столе: {fileName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка создания: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Читает файл с рабочего стола")]
    public async Task<string> ReadFile(
        [Description("Имя файла на рабочем столе")] string fileName)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            if (!File.Exists(fullPath))
                return $"Файл не найден: {fileName}";

            string content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            return string.IsNullOrEmpty(content) ? "Файл пуст" : content;
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Заменяет текст в файле на рабочем столе")]
    public async Task<string> ReplaceInFile(
        [Description("Имя файла")] string fileName,
        [Description("Что заменить")] string oldText,
        [Description("На что заменить")] string newText)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            if (!File.Exists(fullPath))
                return $"Файл не найден: {fileName}";

            string content = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            content = content.Replace(oldText, newText);
            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            return $"Текст заменён в: {fileName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка редактирования: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Добавляет текст в конец файла на рабочем столе")]
    public async Task<string> AppendToFile(
        [Description("Имя файла")] string fileName,
        [Description("Текст для добавления")] string content)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            if (!File.Exists(fullPath))
                return $"Файл не найден: {fileName}";

            await File.AppendAllTextAsync(fullPath, content, Encoding.UTF8);
            return $"Текст добавлен в: {fileName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Удаляет файл с рабочего стола")]
    public string DeleteFile(
        [Description("Имя файла на рабочем столе")] string fileName)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            if (!File.Exists(fullPath))
                return $"Файл не найден: {fileName}";

            File.Delete(fullPath);
            return $"Файл удалён: {fileName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка удаления: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает файл с рабочего стола")]
    public string OpenFile(
        [Description("Имя файла на рабочем столе")] string fileName)
    {
        try
        {
            string fullPath = Path.Combine(DesktopPath, fileName);

            if (!File.Exists(fullPath))
                return $"Файл не найден: {fileName}";

            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            return $"Открыто: {fileName}";
        }
        catch (Exception ex)
        {
            return $"Ошибка открытия: {ex.Message}";
        }
    }
}
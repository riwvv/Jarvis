using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.SemanticKernel;
using Jarvis.Models;

namespace Jarvis.Plugins;

public class ReminderPlugin : IDisposable
{
    private List<ReminderItem> _reminders = new();
    private readonly object _lock = new object();
    private Timer? _timer;
    private readonly string _storageFile;
    private bool _disposed;

    public event Action<string>? OnNotification;

    public ReminderPlugin()
    {
        _storageFile = Path.Combine(AppContext.BaseDirectory, "reminders.json");
        LoadReminders();
        StartTimer();
        Debug.WriteLine($"ReminderPlugin инициализирован. Загружено напоминаний: {_reminders.Count}");
    }

    [KernelFunction]
    [Description("Создаёт напоминание на определённое время. Примеры: 'через 15 минут', 'в 15:30', 'через 2 часа'")]
    public async Task<string> SetReminder(
        [Description("Время в формате: 'через 15 минут', 'в 15:30', 'через 2 часа'")] string when,
        [Description("Текст напоминания")] string message)
    {
        try
        {
            var reminder = ParseReminder(when, message);
            if (reminder == null)
                return "Не удалось распознать время. Используйте: 'через 15 минут', 'в 15:30' или 'через 2 часа'";

            lock (_lock)
            {
                _reminders.Add(reminder);
                SaveReminders();
            }

            Debug.WriteLine($"Добавлено напоминание: {reminder.Message} на {reminder.ScheduledTime:HH:mm:ss}");
            return $"Напоминание создано: {reminder.Message} в {reminder.ScheduledTime:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetReminder error: {ex.Message}");
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Создаёт периодическое напоминание. Примеры: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")]
    public async Task<string> SetRecurringReminder(
        [Description("Период: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")] string interval,
        [Description("Текст напоминания")] string message)
    {
        try
        {
            var recurring = ParseRecurring(interval, message);
            if (recurring == null)
                return "Не удалось распознать период. Используйте: 'каждый час', 'каждые 30 минут', 'каждый день в 9:00'";

            lock (_lock)
            {
                _reminders.Add(recurring);
                SaveReminders();
            }

            Debug.WriteLine($"Добавлено периодическое напоминание: {recurring.Message}");
            return $"Периодическое напоминание создано: {recurring.Message} ({GetIntervalText(recurring)})";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetRecurringReminder error: {ex.Message}");
            return $"Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Показывает все активные напоминания")]
    public async Task<string> ShowReminders()
    {
        lock (_lock)
        {
            if (_reminders.Count == 0)
                return "Нет активных напоминаний";

            var sb = new StringBuilder();
            sb.AppendLine($"Активные напоминания ({_reminders.Count}):");
            sb.AppendLine();

            int index = 1;
            foreach (var r in _reminders)
            {
                string timeInfo = r.IsRecurring
                    ? GetIntervalText(r)
                    : $"{r.ScheduledTime:HH:mm:ss}";

                sb.AppendLine($"{index++}. {timeInfo} — {r.Message}");
            }

            return sb.ToString();
        }
    }

    [KernelFunction]
    [Description("Удаляет напоминание по номеру или тексту")]
    public async Task<string> RemoveReminder(
        [Description("Номер напоминания из списка или ключевое слово")] string identifier)
    {
        lock (_lock)
        {
            if (_reminders.Count == 0)
                return "Нет активных напоминаний";

            ReminderItem? toRemove = null;

            if (int.TryParse(identifier, out int index) && index > 0 && index <= _reminders.Count)
            {
                toRemove = _reminders[index - 1];
            }
            else
            {
                toRemove = _reminders.FirstOrDefault(r =>
                    r.Message.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (toRemove == null)
                return $"Напоминание \"{identifier}\" не найдено";

            _reminders.Remove(toRemove);
            SaveReminders();
            Debug.WriteLine($"Удалено напоминание: {toRemove.Message}");
            return $"Удалено: {toRemove.Message}";
        }
    }

    [KernelFunction]
    [Description("Останавливает все периодические напоминания")]
    public async Task<string> StopAllRecurringReminders()
    {
        lock (_lock)
        {
            var recurring = _reminders.Where(r => r.IsRecurring).ToList();
            if (recurring.Count == 0)
                return "Нет периодических напоминаний";

            foreach (var r in recurring)
                _reminders.Remove(r);

            SaveReminders();
            Debug.WriteLine($"Остановлено {recurring.Count} периодических напоминаний");
            return $"Остановлено {recurring.Count} периодических напоминаний";
        }
    }

    [KernelFunction]
    [Description("Очищает все напоминания")]
    public async Task<string> ClearAllReminders()
    {
        lock (_lock)
        {
            int count = _reminders.Count;
            _reminders.Clear();
            SaveReminders();
            Debug.WriteLine($"Очищено {count} напоминаний");
            return $"Удалено {count} напоминаний";
        }
    }

    private void StartTimer()
    {
        _timer = new Timer(CheckReminders, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private void CheckReminders(object? state)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var due = _reminders.Where(r => !r.IsRecurring && r.ScheduledTime <= now).ToList();

            foreach (var reminder in due)
            {
                Debug.WriteLine($"СРАБОТАЛО: {reminder.Message}");
                OnNotification?.Invoke(reminder.Message);
                _reminders.Remove(reminder);
            }

            var recurringDue = _reminders.Where(r => r.IsRecurring && r.ScheduledTime <= now).ToList();

            foreach (var reminder in recurringDue)
            {
                Debug.WriteLine($"СРАБОТАЛО (период): {reminder.Message}");
                OnNotification?.Invoke(reminder.Message);
                reminder.UpdateNextOccurrence();
            }

            if (due.Count > 0 || recurringDue.Count > 0)
                SaveReminders();
        }
    }

    private ReminderItem? ParseReminder(string when, string message)
    {
        when = when.ToLower().Trim();

        var throughMatch = Regex.Match(when, @"через\s+(\d+)\s+(минут|минуту|минуты|час|часа|часов)");
        if (throughMatch.Success)
        {
            int value = int.Parse(throughMatch.Groups[1].Value);
            string unit = throughMatch.Groups[2].Value;

            var delay = unit.Contains("минут") ? TimeSpan.FromMinutes(value) : TimeSpan.FromHours(value);
            return new ReminderItem(DateTime.Now.Add(delay), message, false);
        }

        var timeMatch = Regex.Match(when, @"(\d{1,2}):(\d{2})");
        if (timeMatch.Success)
        {
            int hour = int.Parse(timeMatch.Groups[1].Value);
            int minute = int.Parse(timeMatch.Groups[2].Value);
            var scheduled = DateTime.Today.AddHours(hour).AddMinutes(minute);

            if (scheduled <= DateTime.Now)
                scheduled = scheduled.AddDays(1);

            return new ReminderItem(scheduled, message, false);
        }

        return null;
    }

    private ReminderItem? ParseRecurring(string interval, string message)
    {
        interval = interval.ToLower().Trim();

        if (interval.Contains("каждый час"))
            return new ReminderItem(DateTime.Now.AddHours(1), message, true, "hourly");

        if (interval.Contains("каждый день"))
            return new ReminderItem(DateTime.Now.AddDays(1), message, true, "daily");

        var everyMatch = Regex.Match(interval, @"каждые?\s+(\d+)\s+минут");
        if (everyMatch.Success)
        {
            int minutes = int.Parse(everyMatch.Groups[1].Value);
            return new ReminderItem(DateTime.Now.AddMinutes(minutes), message, true, "minute", minutes);
        }

        var dailyMatch = Regex.Match(interval, @"каждый день в (\d{1,2}):(\d{2})");
        if (dailyMatch.Success)
        {
            int hour = int.Parse(dailyMatch.Groups[1].Value);
            int minute = int.Parse(dailyMatch.Groups[2].Value);
            var scheduled = DateTime.Today.AddHours(hour).AddMinutes(minute);
            if (scheduled <= DateTime.Now)
                scheduled = scheduled.AddDays(1);

            return new ReminderItem(scheduled, message, true, "dailyAtTime", null, hour, minute);
        }

        return null;
    }

    private string GetIntervalText(ReminderItem r)
    {
        return r.RecurringType switch
        {
            "hourly" => "каждый час",
            "daily" => "каждый день",
            "dailyAtTime" => $"каждый день в {r.RecurringHour:D2}:{r.RecurringMinute:D2}",
            "minute" => $"каждые {r.RecurringValue} минут",
            _ => "периодическое"
        };
    }

    private void LoadReminders()
    {
        try
        {
            if (File.Exists(_storageFile))
            {
                var json = File.ReadAllText(_storageFile);
                _reminders = JsonSerializer.Deserialize<List<ReminderItem>>(json) ?? new List<ReminderItem>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Load reminders error: {ex.Message}");
            _reminders = new List<ReminderItem>();
        }
    }

    private void SaveReminders()
    {
        try
        {
            var json = JsonSerializer.Serialize(_reminders, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storageFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Save reminders error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _timer?.Dispose();
            _disposed = true;
        }
    }
}
using Jarvis.Models;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Services;

public class ReminderService : IDisposable
{
    private List<ReminderItem> _reminders = new();
    private readonly object _lock = new object();
    private Timer? _timer;
    private readonly string _storageFile;
    private bool _disposed;
    private readonly TextToSpeechService _tts;

    public ReminderService(TextToSpeechService tts)
    {
        _tts = tts;
        _storageFile = Path.Combine(AppContext.BaseDirectory, "reminders.json");
        LoadReminders();
        StartTimer();
    }

    public ReminderItem? AddReminder(DateTime scheduledTime, string message, bool isRecurring = false, string? recurringType = null, int? value = null, int hour = 0, int minute = 0)
    {
        var reminder = new ReminderItem(scheduledTime, message, isRecurring, recurringType, value, hour, minute);

        lock (_lock)
        {
            _reminders.Add(reminder);
            SaveReminders();
        }

        return reminder;
    }

    public List<ReminderItem> GetAllReminders()
    {
        lock (_lock)
        {
            return _reminders.ToList();
        }
    }

    public bool RemoveReminder(Func<ReminderItem, bool> predicate)
    {
        lock (_lock)
        {
            var toRemove = _reminders.FirstOrDefault(predicate);
            if (toRemove != null)
            {
                _reminders.Remove(toRemove);
                SaveReminders();
                return true;
            }
        }
        return false;
    }

    public int RemoveAllRecurring()
    {
        lock (_lock)
        {
            var recurring = _reminders.Where(r => r.IsRecurring).ToList();
            int count = recurring.Count;
            foreach (var r in recurring)
                _reminders.Remove(r);

            if (count > 0)
                SaveReminders();

            return count;
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _reminders.Clear();
            SaveReminders();
        }
    }

    private void StartTimer()
    {
        _timer = new Timer(CheckReminders, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private void CheckReminders(object? state)
    {
        List<string> messagesToSpeak = new();

        lock (_lock)
        {
            var now = DateTime.Now;
            var due = _reminders.Where(r => !r.IsRecurring && r.ScheduledTime <= now).ToList();

            foreach (var reminder in due)
            {
                messagesToSpeak.Add(reminder.Message);
                _reminders.Remove(reminder);
            }

            var recurringDue = _reminders.Where(r => r.IsRecurring && r.ScheduledTime <= now).ToList();

            foreach (var reminder in recurringDue)
            {
                messagesToSpeak.Add(reminder.Message);
                reminder.UpdateNextOccurrence();
            }

            if (due.Count > 0 || recurringDue.Count > 0)
                SaveReminders();
        }

        // Озвучиваем НЕ ДОЖИДАЯСЬ (fire and forget)
        foreach (var msg in messagesToSpeak)
        {
            _ = Task.Run(async () => await _tts.SpeakAsync(msg));
        }
    }

    public ReminderItem? ParseReminder(string when, string message)
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

    public ReminderItem? ParseRecurring(string interval, string message)
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

    public string GetIntervalText(ReminderItem r)
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
        catch
        {
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
        catch { }
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
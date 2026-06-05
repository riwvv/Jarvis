using Jarvis.Services;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;

namespace Jarvis.Plugins;

public class ReminderPlugin
{
    private readonly ReminderService _reminderService;

    public ReminderPlugin(ReminderService reminderService)
    {
        _reminderService = reminderService;
    }

    [KernelFunction]
    [Description("Создаёт напоминание на определённое время. Примеры: 'через 15 минут', 'в 15:30', 'через 2 часа'")]
    public async Task<string> SetReminder(
        [Description("Время в формате: 'через 15 минут', 'в 15:30', 'через 2 часа'")] string when,
        [Description("Текст напоминания")] string message)
    {
        var reminder = _reminderService.ParseReminder(when, message);
        if (reminder == null)
            return "Не удалось распознать время. Используйте: 'через 15 минут', 'в 15:30' или 'через 2 часа'";

        _reminderService.AddReminder(reminder.ScheduledTime, reminder.Message, false);
        return $"Напоминание создано: {reminder.Message} в {reminder.ScheduledTime:HH:mm:ss}";
    }

    [KernelFunction]
    [Description("Создаёт периодическое напоминание. Примеры: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")]
    public async Task<string> SetRecurringReminder(
        [Description("Период: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")] string interval,
        [Description("Текст напоминания")] string message)
    {
        var recurring = _reminderService.ParseRecurring(interval, message);
        if (recurring == null)
            return "Не удалось распознать период. Используйте: 'каждый час', 'каждые 30 минут', 'каждый день в 9:00'";

        _reminderService.AddReminder(recurring.ScheduledTime, recurring.Message, true,
            recurring.RecurringType, recurring.RecurringValue,
            recurring.RecurringHour ?? 0, recurring.RecurringMinute ?? 0);

        return $"Периодическое напоминание создано: {recurring.Message} ({_reminderService.GetIntervalText(recurring)})";
    }

    [KernelFunction]
    [Description("Показывает все активные напоминания")]
    public async Task<string> ShowReminders()
    {
        var reminders = _reminderService.GetAllReminders();

        if (reminders.Count == 0)
            return "Нет активных напоминаний";

        var sb = new StringBuilder();
        sb.AppendLine($"Активные напоминания ({reminders.Count}):");
        sb.AppendLine();

        int index = 1;
        foreach (var r in reminders)
        {
            string timeInfo = r.IsRecurring
                ? _reminderService.GetIntervalText(r)
                : $"{r.ScheduledTime:HH:mm:ss}";

            sb.AppendLine($"{index++}. {timeInfo} — {r.Message}");
        }

        return sb.ToString();
    }

    [KernelFunction]
    [Description("Удаляет напоминание по номеру или тексту")]
    public async Task<string> RemoveReminder(
        [Description("Номер напоминания из списка или ключевое слово")] string identifier)
    {
        bool removed = false;

        if (int.TryParse(identifier, out int index))
        {
            var reminders = _reminderService.GetAllReminders();
            if (index > 0 && index <= reminders.Count)
            {
                var toRemove = reminders[index - 1];
                removed = _reminderService.RemoveReminder(r => r.Id == toRemove.Id);
                if (removed)
                    return $"Удалено: {toRemove.Message}";
            }
        }
        else
        {
            removed = _reminderService.RemoveReminder(r =>
                r.Message.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (removed)
                return $"Удалено напоминание с текстом: {identifier}";
        }

        return $"Напоминание \"{identifier}\" не найдено";
    }

    [KernelFunction]
    [Description("Останавливает все периодические напоминания")]
    public async Task<string> StopAllRecurringReminders()
    {
        int count = _reminderService.RemoveAllRecurring();

        if (count == 0)
            return "Нет периодических напоминаний";

        return $"Остановлено {count} периодических напоминаний";
    }

    [KernelFunction]
    [Description("Очищает все напоминания")]
    public async Task<string> ClearAllReminders()
    {
        var count = _reminderService.GetAllReminders().Count;
        _reminderService.ClearAll();
        return $"Удалено {count} напоминаний";
    }
}
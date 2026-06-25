using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using Jarvis.Services;

namespace Jarvis.Plugins;

public class ReminderPlugin(ReminderService _reminderService) {
    [KernelFunction]
    [Description("Создаёт напоминание на определённое время. Примеры: 'через 15 минут', 'в 15:30', 'через 2 часа'")]
    public async Task<string> SetReminder([Description("Время в формате: 'через 15 минут', 'в 15:30', 'через 2 часа'")] string when, [Description("Текст напоминания")] string message) {
        var reminder = _reminderService.ParseReminder(when, message);
        if (reminder == null) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "invalid_time",
                description = "Не удалось распознать время. Используйте: 'через 15 минут', 'в 15:30' или 'через 2 часа'",
                input = when
            });
        }

        var added = _reminderService.AddReminder(reminder.ScheduledTime, reminder.Message, false);
        if (added == null) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "add_failed",
                description = "Не удалось создать напоминание"
            });
        }

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Напоминание создано: {reminder.Message} в {reminder.ScheduledTime:HH:mm:ss}",
            id = added.Id,
            text = reminder.Message,
            time = reminder.ScheduledTime.ToString("HH:mm:ss"),
            scheduledTime = reminder.ScheduledTime,
            isRecurring = false
        });
    }

    [KernelFunction]
    [Description("Создаёт периодическое напоминание. Примеры: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")]
    public async Task<string> SetRecurringReminder([Description("Период: 'каждый час', 'каждый день в 9:00', 'каждые 30 минут'")] string interval, [Description("Текст напоминания")] string message) {
        var recurring = _reminderService.ParseRecurring(interval, message);
        if (recurring == null) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "invalid_interval",
                description = "Не удалось распознать период. Используйте: 'каждый час', 'каждые 30 минут', 'каждый день в 9:00'",
                input = interval
            });
        }

        var added = _reminderService.AddReminder(recurring.ScheduledTime, recurring.Message, true,
            recurring.RecurringType, recurring.RecurringValue,
            recurring.RecurringHour ?? 0, recurring.RecurringMinute ?? 0);

        if (added == null) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "add_failed",
                description = "Не удалось создать периодическое напоминание"
            });
        }

        var intervalText = _reminderService.GetIntervalText(recurring);

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Периодическое напоминание создано: {recurring.Message} ({intervalText})",
            id = added.Id,
            text = recurring.Message,
            interval = intervalText,
            recurringType = recurring.RecurringType,
            isRecurring = true
        });
    }

    [KernelFunction]
    [Description("Показывает все активные напоминания")]
    public async Task<string> ShowReminders() {
        var reminders = _reminderService.GetAllReminders();

        if (reminders.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "WARNING",
                cause = "no_reminders",
                description = "Нет активных напоминаний",
                count = 0
            });
        }

        var list = reminders.Select((r, i) => new {
            index = i + 1,
            id = r.Id,
            text = r.Message,
            time = r.IsRecurring ? _reminderService.GetIntervalText(r) : r.ScheduledTime.ToString("HH:mm:ss"),
            isRecurring = r.IsRecurring
        });

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Активные напоминания ({reminders.Count}):",
            count = reminders.Count,
            reminders = list
        });
    }

    [KernelFunction]
    [Description("Удаляет напоминание по номеру или тексту")]
    public async Task<string> RemoveReminder([Description("Номер напоминания из списка или ключевое слово")] string identifier) {
        if (string.IsNullOrWhiteSpace(identifier)) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "empty_identifier",
                description = "Укажите номер или текст напоминания для удаления"
            });
        }

        var reminders = _reminderService.GetAllReminders();
        if (reminders.Count == 0) {
            return JsonSerializer.Serialize(new {
                status = "WARNING",
                cause = "no_reminders",
                description = "Нет активных напоминаний для удаления"
            });
        }

        bool removed = false;
        string removedText = "";

        if (int.TryParse(identifier, out int index) && index > 0 && index <= reminders.Count) {
            var toRemove = reminders[index - 1];
            removedText = toRemove.Message;
            removed = _reminderService.RemoveReminder(r => r.Id == toRemove.Id);
        }
        else {
            var toRemove = reminders.FirstOrDefault(r =>
                r.Message.Contains(identifier, StringComparison.OrdinalIgnoreCase));
            if (toRemove != null) {
                removedText = toRemove.Message;
                removed = _reminderService.RemoveReminder(r => r.Id == toRemove.Id);
            }
        }

        if (!removed) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "not_found",
                description = $"Напоминание '{identifier}' не найдено",
                id = identifier
            });
        }

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Напоминание удалено: {removedText}",
            removed = removedText
        });
    }

    [KernelFunction]
    [Description("Останавливает все периодические напоминания")]
    public async Task<string> StopAllRecurringReminders() {
        var allReminders = _reminderService.GetAllReminders();
        var recurringCount = allReminders.Count(r => r.IsRecurring);

        if (recurringCount == 0) {
            return JsonSerializer.Serialize(new {
                status = "WARNING",
                cause = "no_recurring",
                description = "Нет периодических напоминаний для остановки"
            });
        }

        int count = _reminderService.RemoveAllRecurring();

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Остановлено {count} периодических напоминаний",
            stopped = count
        });
    }

    [KernelFunction]
    [Description("Очищает все напоминания")]
    public async Task<string> ClearAllReminders() {
        var count = _reminderService.GetAllReminders().Count;

        if (count == 0) {
            return JsonSerializer.Serialize(new {
                status = "WARNING",
                cause = "no_reminders",
                description = "Нет напоминаний для очистки"
            });
        }

        _reminderService.ClearAll();

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Удалено {count} напоминаний",
            cleared = count
        });
    }
}
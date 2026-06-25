using System.Text.Json.Serialization;

namespace Jarvis.Models;

public class ReminderItem {
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("scheduledTime")]
    public DateTime ScheduledTime { get; set; }

    [JsonPropertyName("isRecurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("recurringType")]
    public string? RecurringType { get; set; }

    [JsonPropertyName("recurringValue")]
    public int? RecurringValue { get; set; }

    [JsonPropertyName("recurringHour")]
    public int? RecurringHour { get; set; }

    [JsonPropertyName("recurringMinute")]
    public int? RecurringMinute { get; set; }

    public ReminderItem() { }

    public ReminderItem(DateTime scheduledTime, string message, bool isRecurring, string? recurringType = null, int? value = null, int hour = 0, int minute = 0) {
        ScheduledTime = scheduledTime;
        Message = message;
        IsRecurring = isRecurring;
        RecurringType = recurringType;
        RecurringValue = value;
        RecurringHour = hour;
        RecurringMinute = minute;
    }

    public DateTime NextOccurrence => ScheduledTime;

    public void UpdateNextOccurrence() {
        var now = DateTime.Now;

        ScheduledTime = RecurringType switch {
            "hourly" => now.AddHours(1),
            "daily" => now.AddDays(1),
            "dailyAtTime" => DateTime.Today.AddHours(RecurringHour ?? 0).AddMinutes(RecurringMinute ?? 0),
            "minute" => now.AddMinutes(RecurringValue ?? 1),
            _ => now.AddHours(1)
        };

        if (RecurringType == "dailyAtTime" && ScheduledTime <= now)
            ScheduledTime = ScheduledTime.AddDays(1);
    }
}
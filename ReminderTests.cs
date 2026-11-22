using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class ReminderTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        check(ReminderTimers.TryDuration("5", out var plain) && plain == TimeSpan.FromMinutes(5), "A plain reminder duration means minutes");
        check(ReminderTimers.TryDuration("1 год 30 хв", out var combined) && combined == TimeSpan.FromMinutes(90), "Ukrainian hours and minutes combine into one duration");
        check(ReminderTimers.TryDuration("30с", out var seconds) && seconds.TotalSeconds == 30 && ReminderTimers.TryDuration("24 год", out _), "Reminder durations support seconds and the full 24-hour boundary");
        check(new[] { "", "-5", "0", "25 год", "1:30", "5 хв сміття", "9999999999999999", "5.5 хв", "0 с" }.All(x => !ReminderTimers.TryDuration(x, out _)), "Invalid or ambiguous durations never silently create a timer");
        check(ReminderTimers.Format(TimeSpan.FromMilliseconds(1)) == "00:01" && ReminderTimers.Format(TimeSpan.FromSeconds(3601)) == "1:00:01", "Timer labels round up and show hours explicitly");
        var now = new DateTimeOffset(2026, 11, 22, 23, 59, 0, TimeSpan.FromHours(2));
        var timers = new ReminderTimers();
        var tea = timers.Add("Чай", TimeSpan.FromMinutes(5), now)!;
        var pause = timers.Add("Перерва", TimeSpan.FromHours(1), now)!;
        var otherTea = timers.Add("Чай", TimeSpan.FromMinutes(10), now)!;
        check(tea.Id != otherTea.Id && timers.Next == tea && timers.Items.Count == 3, "Independent timers can share names and the nearest deadline wins");
        timers.Toggle(tea.Id, now.AddMinutes(2));
        check(tea.Status == ReminderStatus.Paused && tea.Remaining(now.AddHours(3)).TotalMinutes == 3 && pause.Status == ReminderStatus.Running, "Pausing one timer freezes its remaining time without affecting another");
        check(timers.Advance(now.AddHours(2)) && pause.AlertPending && otherTea.AlertPending && tea.Status == ReminderStatus.Paused, "Sleep and midnight catch-up complete every overdue timer but leave paused timers alone");
        check(!timers.Advance(now.AddHours(3)), "A completed timer does not finish again on later ticks");
        var path = Path.Combine(directory, "reminder-settings.json");
        new Settings { Reminders = timers, Note = "Моя нотатка", FocusMinutes = 50 }.Save(path);
        var restored = Settings.Load(path);
        check(restored.Reminders.Items.Count == 3 && restored.Reminders.Items.Single(x => x.Id == tea.Id).Remaining(now).TotalMinutes == 3 && restored.Note == "Моя нотатка" && restored.FocusMinutes == 50, "Timer identities, pauses and unrelated settings survive JSON persistence");
        check(restored.Reminders.Items.Count(x => x.AlertPending) == 2, "Unseen completion alerts survive an application restart");
        check(timers.Acknowledge(pause.NoticeKey) && !timers.Acknowledge(pause.NoticeKey), "Acknowledging a displayed reminder is idempotent");
        var oldRun = pause.NoticeKey; timers.Restart(pause.Id, now.AddHours(2));
        check(pause.Status == ReminderStatus.Running && !pause.AlertPending && pause.NoticeKey != oldRun && pause.Remaining(now.AddHours(2)).TotalMinutes == 60, "Restart creates a fresh alert identity and the original duration");
        timers.Toggle(tea.Id, now.AddHours(2));
        check(tea.Remaining(now.AddHours(2)).TotalMinutes == 3, "Resuming continues the saved remaining duration");
        check(timers.Remove(otherTea.Id) && !timers.Remove(otherTea.Id) && timers.Items.Count == 2, "Deleting one timer preserves all other timers");
        var runningPath = Path.Combine(directory, "reminder-running.json"); new Settings { Reminders = timers }.Save(runningPath);
        var resumed = Settings.Load(runningPath).Reminders; resumed.Advance(now.AddDays(2));
        check(resumed.Items.All(x => x.Status == ReminderStatus.Completed && x.AlertPending), "Timers that expired while the app was closed complete when it reopens");
        var limit = new ReminderTimers(); for (var i = 0; i < ReminderTimers.Limit; i++) limit.Add("Таймер " + i, TimeSpan.FromSeconds(1), now);
        check(limit.Add("Зайвий", TimeSpan.FromMinutes(1), now) == null && limit.Items.Count == 12, "The timer limit is enforced without replacing an existing timer");
        var bad = new ReminderTimers { Items = new() { new() { Id = "broken", RunId = "", Title = "  Чай\n  з лимоном  ", DurationSeconds = -1, RemainingSeconds = double.NaN, Status = (ReminderStatus)99 }, new() { Status = ReminderStatus.Running, Deadline = null } } };
        bad.Normalize(now);
        check(bad.Items[0].Title == "Чай з лимоном" && bad.Items[0].DurationSeconds == 1 && bad.Items.All(x => x.Status == ReminderStatus.Paused && Guid.TryParse(x.Id, out _)), "Malformed saved timer values normalize safely without inventing running deadlines");
        var queue = new IslandNotifications(); limit.Advance(now.AddSeconds(2));
        foreach (var timer in limit.Items) queue.Post(timer.Title, "celebrate", timer.NoticeKey, 10, 0, reminder: ReminderNotice.From(timer));
        queue.Post("Гучність", "wink", "volume", 0, .1);
        check(queue.Current?.Key == limit.Items[0].NoticeKey && queue.PendingCount == 12, "Simultaneous timer alerts queue instead of overwriting each other or being capped at four");
        var delivered = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i < 12; i++) { if (queue.Current?.Reminder is { } notice) delivered.Add(notice.Key); queue.Advance((i + 1) * 5.1); }
        check(delivered.Count == 12, "All twelve completions are delivered even after the ordinary notification expiry window");
        var deferred = new IslandNotifications(); deferred.SetSuspended(true, 0);
        foreach (var timer in limit.Items.Take(2)) deferred.Post(timer.Title, "celebrate", timer.NoticeKey, 10, 0, reminder: ReminderNotice.From(timer));
        check(deferred.Current == null && deferred.PendingCount == 2, "Full-screen mode defers every timer without displaying a card");
        deferred.RemoveReminder(limit.Items[0].Id); deferred.SetSuspended(false, 7200);
        check(deferred.Current?.Key == limit.Items[1].NoticeKey, "Deleting a timer cancels its deferred alert while another survives hours in full screen");
        deferred.RemoveReminder(limit.Items[1].Id);
        check(deferred.Current == null && deferred.PendingCount == 0, "Restart or deletion also removes a currently visible reminder card");
    }
}

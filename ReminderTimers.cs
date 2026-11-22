using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HelperBar;

public enum ReminderStatus { Running, Paused, Completed }

public sealed class ReminderTimer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Таймер";
    public int DurationSeconds { get; set; } = 300;
    public double RemainingSeconds { get; set; } = 300;
    public DateTimeOffset? Deadline { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Paused;
    public bool AlertPending { get; set; }
    [JsonIgnore] public string NoticeKey => "reminder:" + Id + ":" + RunId;
    public TimeSpan Remaining(DateTimeOffset now) => TimeSpan.FromSeconds(Status == ReminderStatus.Completed ? 0 :
        Math.Clamp(Status == ReminderStatus.Running && Deadline is { } end ? (end - now).TotalSeconds : RemainingSeconds, 0, DurationSeconds));
}

public sealed class ReminderTimers
{
    public const int Limit = 12, MaximumSeconds = 24 * 60 * 60;
    public List<ReminderTimer> Items { get; set; } = new();
    public void Normalize(DateTimeOffset now)
    {
        Items ??= new(); var ids = new HashSet<string>(StringComparer.Ordinal);
        Items = Items.Where(x => x != null).Take(Limit).ToList();
        foreach (var timer in Items)
        {
            if (!Guid.TryParse(timer.Id, out _) || !ids.Add(timer.Id)) { timer.Id = Guid.NewGuid().ToString("N"); ids.Add(timer.Id); }
            if (!Guid.TryParse(timer.RunId, out _)) timer.RunId = Guid.NewGuid().ToString("N");
            timer.Title = CleanTitle(timer.Title); timer.DurationSeconds = Math.Clamp(timer.DurationSeconds, 1, MaximumSeconds);
            timer.RemainingSeconds = double.IsFinite(timer.RemainingSeconds) ? Math.Clamp(timer.RemainingSeconds, 0, timer.DurationSeconds) : timer.DurationSeconds;
            if (!Enum.IsDefined(timer.Status)) timer.Status = ReminderStatus.Paused;
            if (timer.Status == ReminderStatus.Running && timer.Deadline == null) timer.Status = ReminderStatus.Paused;
            if (timer.Status == ReminderStatus.Running && timer.Deadline > now.AddSeconds(MaximumSeconds)) timer.Deadline = now.AddSeconds(timer.DurationSeconds);
            if (timer.Status != ReminderStatus.Running) timer.Deadline = null;
            if (timer.Status == ReminderStatus.Completed) timer.RemainingSeconds = 0;
            else timer.AlertPending = false;
        }
    }
    public static string CleanTitle(string? title)
    {
        var value = string.Join(" ", (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Length == 0) return "Таймер";
        return value.Length <= 40 ? value : value[..(char.IsHighSurrogate(value[39]) ? 39 : 40)];
    }
    public ReminderTimer? Add(string title, TimeSpan duration, DateTimeOffset now)
    {
        if (Items.Count >= Limit || duration.TotalSeconds < 1 || duration.TotalSeconds > MaximumSeconds) return null;
        var seconds = (int)Math.Ceiling(duration.TotalSeconds);
        var timer = new ReminderTimer { Title = CleanTitle(title), DurationSeconds = seconds, RemainingSeconds = seconds, Deadline = now.AddSeconds(seconds), Status = ReminderStatus.Running };
        Items.Add(timer); return timer;
    }
    public bool Toggle(string id, DateTimeOffset now)
    {
        var timer = Items.FirstOrDefault(x => x.Id == id); if (timer == null) return false;
        if (timer.Status == ReminderStatus.Completed) return Restart(id, now);
        if (timer.Status == ReminderStatus.Running)
        {
            timer.RemainingSeconds = timer.Remaining(now).TotalSeconds; timer.Deadline = null;
            if (timer.RemainingSeconds == 0) Complete(timer); else timer.Status = ReminderStatus.Paused;
        }
        else { timer.Deadline = now.AddSeconds(Math.Max(1, timer.RemainingSeconds)); timer.Status = ReminderStatus.Running; }
        return true;
    }
    public bool Restart(string id, DateTimeOffset now)
    {
        var timer = Items.FirstOrDefault(x => x.Id == id); if (timer == null) return false;
        timer.RunId = Guid.NewGuid().ToString("N"); timer.RemainingSeconds = timer.DurationSeconds;
        timer.Deadline = now.AddSeconds(timer.DurationSeconds); timer.Status = ReminderStatus.Running; timer.AlertPending = false; return true;
    }
    public bool Remove(string id) => Items.RemoveAll(x => x.Id == id) > 0;
    public bool Advance(DateTimeOffset now)
    {
        var changed = false;
        foreach (var timer in Items)
            if (timer.Status == ReminderStatus.Running && timer.Deadline <= now) { Complete(timer); changed = true; }
        return changed;
    }
    private static void Complete(ReminderTimer timer)
    { timer.Status = ReminderStatus.Completed; timer.Deadline = null; timer.RemainingSeconds = 0; timer.AlertPending = true; }
    public bool Acknowledge(string key)
    {
        var timer = Items.FirstOrDefault(x => x.NoticeKey == key && x.AlertPending);
        if (timer == null) return false; timer.AlertPending = false; return true;
    }
    [JsonIgnore] public ReminderTimer? Next => Items.Where(x => x.Status == ReminderStatus.Running).OrderBy(x => x.Deadline).FirstOrDefault();
    public static string Format(TimeSpan duration)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds));
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    }
    public static string DurationLabel(int seconds) => seconds % 3600 == 0 ? $"{seconds / 3600} год" : seconds % 60 == 0 ? $"{seconds / 60} хв" : $"{seconds} с";
    public static bool TryDuration(string? input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var value = (input ?? "").Trim().ToLowerInvariant(); if (value.Length is 0 or > 40) return false;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        { if (minutes is < 1 or > 1440) return false; duration = TimeSpan.FromMinutes(minutes); return true; }
        var matches = Regex.Matches(value, @"\G\s*(\d{1,5})\s*(годин(?:а|и)?|год|хвилин(?:а|и)?|хв|секунд(?:а|и)?|сек|с|h|m|s)\s*", RegexOptions.CultureInvariant);
        var consumed = 0; long seconds = 0;
        foreach (Match match in matches)
        {
            if (match.Index != consumed) return false; consumed += match.Length;
            var unit = match.Groups[2].Value; var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            seconds += (long)number * (unit.StartsWith("год", StringComparison.Ordinal) || unit == "h" ? 3600 : unit.StartsWith("хв", StringComparison.Ordinal) || unit == "m" ? 60 : 1);
        }
        if (consumed != value.Length || seconds is < 1 or > MaximumSeconds) return false;
        duration = TimeSpan.FromSeconds(seconds); return true;
    }
}

internal sealed record ReminderNotice(string Id, string Key, string Title, int DurationSeconds)
{
    public static ReminderNotice From(ReminderTimer timer) => new(timer.Id, timer.NoticeKey, timer.Title, timer.DurationSeconds);
}

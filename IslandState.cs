using System.Collections.Generic;
using System.Linq;

namespace HelperBar;

public enum IslandMode { Clock, Music, Focus, Pet, Notification, Expanded }

public static class IslandState
{
    public static IslandMode Resolve(bool expanded, bool notification, bool pet, bool timer, bool music) =>
        expanded ? IslandMode.Expanded : notification ? IslandMode.Notification : pet ? IslandMode.Pet : timer ? IslandMode.Focus : music ? IslandMode.Music : IslandMode.Clock;
}

internal sealed record IslandNotice(string Message, string Mood, string Key, int Priority, double Created, DeviceNotice? Device = null, ReminderNotice? Reminder = null)
{
    public double Duration => Reminder != null ? 5 : Device == null ? 3 : 4.5;
    public bool IsTimer => Reminder != null || Key == "timer";
}

internal sealed class IslandNotifications
{
    private readonly List<IslandNotice> waiting = new();
    public IslandNotice? Current { get; private set; }
    public double Until { get; private set; }
    public int PendingCount => waiting.Count;
    public bool Suspended { get; private set; }
    public bool SetSuspended(bool value, double now)
    {
        if (Suspended == value) return false;
        if (value)
        {
            if (Current != null && now < Until) Enqueue(Current);
            Current = null; Until = 0; Suspended = true; return false;
        }
        Suspended = false;
        // Deferred notices get a fresh display window, even after hours in a game.
        for (var i = 0; i < waiting.Count; i++) waiting[i] = waiting[i] with { Created = now };
        return Advance(now);
    }
    private void Enqueue(IslandNotice notice)
    {
        waiting.RemoveAll(item => item.Key == notice.Key); waiting.Add(notice);
        var ordinary = waiting.Where(item => item.Reminder == null).ToArray();
        if (ordinary.Length > 4) waiting.Remove(ordinary.OrderBy(item => item.Priority).ThenBy(item => item.Created).First());
    }
    public bool Post(string message, string mood, string key, int priority, double now, DeviceNotice? device = null, ReminderNotice? reminder = null)
    {
        Advance(now);
        var notice = new IslandNotice(message, mood, key, priority, now, device, reminder);
        if (Suspended) { Enqueue(notice); return false; }
        waiting.RemoveAll(item => item.Key == key);
        if (Current == null || Current.Key == key || priority > Current.Priority || (device == null && reminder == null && Current.Reminder == null && priority == Current.Priority))
        {
            if ((Current?.Device != null || Current?.IsTimer == true) && Current.Key != key) Enqueue(Current);
            Current = notice; Until = now + notice.Duration; return true;
        }
        Enqueue(notice); return false;
    }
    public bool Advance(double now)
    {
        if (Suspended) return false;
        if (Current == null && waiting.Count == 0) return false;
        if (Current != null && now < Until) return false;
        waiting.RemoveAll(item => !item.IsTimer && now - item.Created > (item.Device == null ? 10 : 20));
        var next = waiting.OrderByDescending(item => item.Priority).FirstOrDefault();
        Current = next; Until = next == null ? 0 : now + next.Duration;
        if (next != null) waiting.Remove(next);
        return next != null;
    }
    public void RefreshDevices(IReadOnlyList<DeviceBattery> devices, Settings settings)
    {
        var currentDevices = BatteryValues.Merge(devices).ToDictionary(d => d.Id, System.StringComparer.OrdinalIgnoreCase);
        IslandNotice? Refresh(IslandNotice notice)
        {
            if (notice.Device == null) return notice;
            if (!currentDevices.TryGetValue(notice.Device.Device.Id, out var latest)) return null;
            var update = notice.Device with { Device = latest };
            return update.Allowed(settings) ? notice with { Device = update, Message = update.Message } : null;
        }
        for (var i = waiting.Count - 1; i >= 0; i--)
        {
            var updated = Refresh(waiting[i]);
            if (updated == null) waiting.RemoveAt(i); else waiting[i] = updated;
        }
        if (Current != null) { Current = Refresh(Current); if (Current == null) Until = 0; }
    }
    public void RemoveReminder(string id)
    {
        waiting.RemoveAll(item => item.Reminder?.Id == id);
        if (Current?.Reminder?.Id == id) { Current = null; Until = 0; }
    }
}

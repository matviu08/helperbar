using System;
using System.Collections.Generic;
using System.Linq;

namespace HelperBar;

internal enum DeviceNoticeKind { Connected, LowBattery }

internal sealed record DeviceNotice(DeviceBattery Device, DeviceNoticeKind Kind)
{
    public string Title => Kind == DeviceNoticeKind.LowBattery ? "ЧАС ЗАРЯДИТИ" : "ПІДКЛЮЧЕНО";
    public string Message => (Kind == DeviceNoticeKind.LowBattery ? "Низький заряд · " : "Підключено · ") + Device.Name + " · " + Device.LevelText;
    public string Key => "device:" + Device.Id;
    public string Mood => Kind == DeviceNoticeKind.LowBattery ? "surprised" : "happy";
    public int Priority => Kind == DeviceNoticeKind.LowBattery ? 5 : 2;
    public bool Allowed(Settings settings) => BatteryValues.Visible(Device, settings) &&
        (Kind == DeviceNoticeKind.LowBattery ? settings.NotifyLowBattery && Device.Percent is >= 0 and <= 20 : settings.NotifyDeviceConnections);
}

/// <summary>Tracks successful observations, not UI rebuilds. Low-charge latches survive sleep/reconnect and preference toggles.</summary>
internal sealed class DeviceNoticeTracker
{
    private sealed class State
    {
        public bool Connected, LowWarned;
        public int Missing;
        public DateTimeOffset LastSeen, MissingSince;
        public DateTimeOffset? LastConnectionNotice;
    }
    private readonly Dictionary<string, State> states = new(StringComparer.OrdinalIgnoreCase);
    private bool initialized;

    public void Pause()
    {
        initialized = false;
        foreach (var state in states.Values) { state.Connected = false; state.Missing = 0; }
    }
    public IReadOnlyList<DeviceNotice> Observe(DeviceBatterySnapshot snapshot, Settings settings, DateTimeOffset now)
    {
        if (snapshot.Scanning || !snapshot.Successful) return Array.Empty<DeviceNotice>();
        var output = new List<DeviceNotice>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in BatteryValues.Merge(snapshot.Devices))
        {
            seen.Add(device.Id);
            if (!states.TryGetValue(device.Id, out var state)) states[device.Id] = state = new();
            var connected = initialized && !state.Connected;
            state.Connected = true; state.Missing = 0; state.LastSeen = now;
            if (device.Percent is >= 25 and <= 100) state.LowWarned = false;
            var low = new DeviceNotice(device, DeviceNoticeKind.LowBattery);
            var connection = new DeviceNotice(device, DeviceNoticeKind.Connected);
            if (!state.LowWarned && low.Allowed(settings))
            {
                state.LowWarned = true; output.Add(low);
                if (connected) state.LastConnectionNotice = now; // One card for a newly connected, already-low device.
            }
            else if (connected && connection.Allowed(settings) &&
                     (state.LastConnectionNotice == null || now - state.LastConnectionNotice.Value >= TimeSpan.FromMinutes(1)))
            { state.LastConnectionNotice = now; output.Add(connection); }
        }
        foreach (var (id, state) in states)
        {
            if (seen.Contains(id)) continue;
            if (state.Missing++ == 0) state.MissingSince = now;
            // A single missing poll or repeated rapid Refresh clicks are not evidence of a reconnect.
            if (state.Missing >= 2 && now - state.MissingSince >= TimeSpan.FromSeconds(4)) state.Connected = false;
        }
        foreach (var id in states.Where(p => !p.Value.Connected && now - p.Value.LastSeen > TimeSpan.FromDays(1)).Select(p => p.Key).ToArray()) states.Remove(id);
        initialized = true;
        return output;
    }
}

public sealed partial class IslandWindow
{
    private readonly DeviceNoticeTracker deviceNoticeTracker = new();
    private bool DeviceMonitoringEnabled => BatteryEnabled || settings.NotifyDeviceConnections || settings.NotifyLowBattery;

    private void OnDeviceBatteriesChanged(DeviceBatterySnapshot snapshot)
    {
        if (closed) return;
        UpdateDeviceBatteryUi(snapshot);
        if (snapshot.Scanning) return;
        surface.RefreshDeviceNotices(snapshot.Successful ? snapshot.Devices : Array.Empty<DeviceBattery>());
        foreach (var notice in deviceNoticeTracker.Observe(snapshot, settings, DateTimeOffset.UtcNow)) surface.NotifyDevice(notice);
    }
    private void DeviceNoticePreferencesChanged()
    {
        Persist(); UpdateDeviceBatteryMonitoring();
        // Applying a preference never manufactures a connection; it can reveal an existing unreported low charge.
        if (deviceBatteries.Running) OnDeviceBatteriesChanged(deviceBatteries.Snapshot);
    }
}

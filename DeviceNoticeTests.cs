using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class DeviceNoticeTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var now = DateTimeOffset.UtcNow; var settings = new Settings();
        var mouse = new DeviceBattery("mouse", "Мишка", PeripheralKind.Mouse, 80, "Test", now);
        var headphones = new DeviceBattery("headphones", "Навушники", PeripheralKind.Headphones, 72, "Test", now);
        DeviceBatterySnapshot Snapshot(params DeviceBattery[] devices) => new(devices, false, "Test", now);
        var tracker = new DeviceNoticeTracker();
        check(tracker.Observe(Snapshot(mouse), settings, now).Count == 0, "Device notification baseline does not announce already-connected devices on startup");
        var connected = tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(5));
        check(connected.Count == 1 && connected[0].Kind == DeviceNoticeKind.Connected && connected[0].Device.Percent == 72, "New headphones produce a connection card with their actual battery percentage");
        check(tracker.Observe(Snapshot(mouse, headphones, headphones), settings, now.AddSeconds(10)).Count == 0, "Repeated polls and duplicate endpoints do not repeat connection notices");
        tracker.Observe(Snapshot(mouse), settings, now.AddSeconds(15));
        check(tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(20)).Count == 0, "A single missing poll does not produce a reconnect notification");
        tracker.Observe(Snapshot(mouse), settings, now.AddSeconds(25)); tracker.Observe(Snapshot(mouse), settings, now.AddSeconds(30));
        check(tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(35)).Count == 0, "A short reconnect is suppressed by the per-device connection cooldown");
        tracker.Observe(Snapshot(mouse), settings, now.AddSeconds(65)); tracker.Observe(Snapshot(mouse), settings, now.AddSeconds(70));
        check(tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(75)).Count == 1, "A confirmed reconnect after the cooldown is announced again");
        tracker.Observe(Snapshot() with { Successful = false }, settings, now.AddSeconds(80));
        tracker.Observe(Snapshot() with { Successful = false }, settings, now.AddSeconds(90));
        check(tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(150)).Count == 0, "Native discovery errors do not manufacture disconnect and reconnect events");
        tracker.Observe(Snapshot() with { Scanning = true }, settings, now.AddSeconds(160));
        check(tracker.Observe(Snapshot(mouse, headphones), settings, now.AddSeconds(170)).Count == 0, "In-progress snapshots do not affect connection history");

        var battery = new DeviceNoticeTracker(); battery.Observe(Snapshot(mouse with { Percent = 21 }), settings, now);
        var low = battery.Observe(Snapshot(mouse with { Percent = 20 }), settings, now.AddSeconds(5));
        check(low.Count == 1 && low[0].Kind == DeviceNoticeKind.LowBattery && low[0].Device.Percent == 20, "Reaching exactly 20 percent emits one low-battery notice");
        check(battery.Observe(Snapshot(mouse with { Percent = 0 }), settings, now.AddSeconds(10)).Count == 0, "Further discharge to real zero does not repeat a low-battery warning");
        battery.Observe(Snapshot(mouse with { Percent = 21 }), settings, now.AddSeconds(15));
        check(battery.Observe(Snapshot(mouse with { Percent = 20 }), settings, now.AddSeconds(20)).Count == 0, "Battery jitter around 20 percent does not rearm the warning");
        battery.Observe(Snapshot(mouse with { Percent = 25 }), settings, now.AddSeconds(25));
        check(battery.Observe(Snapshot(mouse with { Percent = 19 }), settings, now.AddSeconds(30)).Count == 1, "A recharge to at least 25 percent rearms the next low-battery warning");
        battery.Pause();
        check(battery.Observe(Snapshot(mouse with { Percent = 19 }), settings, now.AddSeconds(40)).Count == 0, "Turning monitoring off and on preserves low-charge deduplication and silently rebuilds the baseline");
        battery.Observe(Snapshot(), settings, now.AddSeconds(45)); battery.Observe(Snapshot(), settings, now.AddSeconds(50));
        check(battery.Observe(Snapshot(mouse with { Percent = 19 }), settings, now.AddSeconds(55)).All(n => n.Kind != DeviceNoticeKind.LowBattery), "Sleep and reconnect do not repeat an already warned low charge");
        var initialLow = new DeviceNoticeTracker().Observe(Snapshot(mouse with { Percent = 10 }), settings, now);
        check(initialLow.Count == 1 && initialLow[0].Kind == DeviceNoticeKind.LowBattery, "An already-low device still gets one useful warning on startup");
        var unknown = new DeviceNoticeTracker(); unknown.Observe(Snapshot(), settings, now);
        var noCharge = unknown.Observe(Snapshot(headphones with { Percent = null }), settings, now.AddSeconds(5));
        check(noCharge.Count == 1 && noCharge[0].Kind == DeviceNoticeKind.Connected && noCharge[0].Message.Contains("Заряд недоступний"), "Unknown battery remains unknown in a connection notification");
        var filters = new DeviceNoticeTracker(); filters.Observe(Snapshot(), settings, now); settings.BatteryHeadphones = false;
        check(filters.Observe(Snapshot(headphones), settings, now.AddSeconds(5)).Count == 0, "Device category preferences suppress unwanted alerts");
        settings.BatteryHeadphones = true;
        check(filters.Observe(Snapshot(headphones), settings, now.AddSeconds(10)).Count == 0, "Re-enabling a category does not masquerade as a new connection");
        var twins = new DeviceNoticeTracker(); twins.Observe(Snapshot(), settings, now);
        check(twins.Observe(Snapshot(mouse, mouse with { Id = "mouse2" }), settings, now.AddSeconds(5)).Count == 2, "Same-name devices have independent connection identities");

        void Post(IslandNotifications queue, DeviceNotice notice, double time) => queue.Post(notice.Message, notice.Mood, notice.Key, notice.Priority, time, notice);
        var queue = new IslandNotifications(); var connection = new DeviceNotice(headphones, DeviceNoticeKind.Connected);
        Post(queue, connection, 0); Post(queue, new(mouse, DeviceNoticeKind.Connected), .1);
        check(queue.Current?.Device?.Device.Id == headphones.Id && queue.PendingCount == 1, "Simultaneous equal-priority devices are shown sequentially instead of replacing each other");
        queue.Post("Timer", "celebrate", "timer", 10, .2);
        check(queue.Current?.Key == "timer" && queue.PendingCount == 2, "Timer completion preempts a device card while keeping it in the queue");
        queue.Advance(3.3); check(queue.Current?.Device != null && queue.Until == 7.8, "Queued device cards receive a full readable display interval");
        var deferred = new IslandNotifications(); deferred.SetSuspended(true, 0); Post(deferred, connection, 1);
        deferred.RefreshDevices(new[] { headphones with { Percent = 60 } }, settings); deferred.SetSuspended(false, 3600);
        check(deferred.Current?.Device?.Device.Percent == 60 && deferred.Current.Message.Contains("60%"), "Deferred connection notices refresh their battery before appearing after a game");
        deferred.SetSuspended(true, 3601); deferred.RefreshDevices(Array.Empty<DeviceBattery>(), settings); deferred.SetSuspended(false, 3700);
        check(deferred.Current == null && deferred.PendingCount == 0, "A device disconnected during full screen never produces a stale connected card on return");
        deferred.SetSuspended(true, 3800); Post(deferred, new(mouse with { Percent = 20 }, DeviceNoticeKind.LowBattery), 3801);
        deferred.RefreshDevices(new[] { mouse with { Percent = 80 } }, settings); deferred.SetSuspended(false, 3900);
        check(deferred.Current == null, "Charging during full screen cancels an obsolete low-battery warning");
        Post(deferred, connection, 4000); settings.NotifyDeviceConnections = false;
        deferred.RefreshDevices(new[] { headphones }, settings);
        check(deferred.Current == null, "Disabling connection alerts immediately removes a pending or visible connection card");
        var path = Path.Combine(directory, "device-notice-settings.json"); settings.Save(path); var restored = Settings.Load(path);
        check(!restored.NotifyDeviceConnections && restored.NotifyLowBattery && restored.BatteryHeadphones, "Connection and low-battery preferences persist independently");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunDeviceNoticeIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        try
        {
            SetExpanded(true); OpenSettings("devices"); await Task.Delay(500);
            var originalHome = settings.Home.Items.Select(i => i.Id).ToArray();
            Check(!BatteryEnabled && !deviceBatteries.Running, "Test starts with no battery tile or background device monitoring");
            deviceConnectionBox!.IsChecked = true; deviceLowBatteryBox!.IsChecked = true;
            Check(!BatteryEnabled && deviceBatteries.Running, "Device notification toggles start the shared monitor without adding a Home tile");
            for (var i = 0; i < 250 && deviceBatteries.Snapshot.Scanning; i++) await Task.Delay(100);
            Check(deviceBatteries.Snapshot.Successful && !deviceBatteries.Snapshot.Scanning, "Native device reading succeeds with notification-only monitoring");
            File.WriteAllText(Path.Combine(directory, "device-notice-live.json"), JsonSerializer.Serialize(deviceBatteries.Snapshot, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var device in deviceBatteries.Snapshot.Devices) report.Add($"INFO Live device: {device.Name}, {device.LevelText}");
            settingsScroll!.ScrollToTop();
            await Task.Delay(350); Capture(Path.Combine(directory, "device-notice-settings.png"));
            // Stop the real scanner before rendering clearly labelled simulated transitions. No hardware state is changed.
            deviceBatteries.Stop(); deviceNoticeTracker.Pause();
            var now = DateTimeOffset.UtcNow;
            var headphones = new DeviceBattery("fixture:headphones", "Навушники · тест", PeripheralKind.Headphones, 72, "Тестові дані", now);
            var mouse = new DeviceBattery("fixture:mouse", "Мишка · тест", PeripheralKind.Mouse, 80, "Тестові дані", now);
            DeviceBatterySnapshot Snapshot(params DeviceBattery[] devices) => new(devices, false, "Тестові переходи", now);
            SetExpanded(false); OnDeviceBatteriesChanged(Snapshot());
            var foreground = FullscreenDetector.GetForegroundWindow(); OnDeviceBatteriesChanged(Snapshot(headphones));
            Check(surface.DeviceToast?.Device.Id == headphones.Id && surface.Mood == "happy" && !surface.Expanded && FullscreenDetector.GetForegroundWindow() == foreground,
                "Connection creates a happy compact card without opening the full panel or changing foreground focus");
            await Task.Delay(750); Capture(Path.Combine(directory, "device-connected-fixture.png"));
            Check(Math.Abs(surface.IslandWidth.Value - 416) < 3 && Math.Abs(surface.IslandHeight.Value - 76) < 3,
                "Device notification animates to its own compact card size");
            var until = surface.ToastUntil; OnDeviceBatteriesChanged(Snapshot(headphones));
            Check(surface.ToastUntil == until, "Repeated hardware observations do not restart the toast animation or duration");
            OnDeviceBatteriesChanged(Snapshot(headphones, mouse)); OnDeviceBatteriesChanged(Snapshot(headphones, mouse with { Percent = 20 }));
            Check(surface.DeviceToast?.Kind == DeviceNoticeKind.LowBattery && surface.DeviceToast.Device.Percent == 20 && surface.Mood == "surprised",
                "Crossing 20 percent produces a low-battery card and a surprised-eye reaction");
            await Task.Delay(600); Capture(Path.Combine(directory, "device-low-fixture.png"));
            settings.Note = "Тест: залишити відкриту нотатку без змін."; SetExpanded(true); SwitchPage("Нотатки"); await Task.Delay(450);
            Capture(Path.Combine(directory, "device-expanded-fixture.png"));
            Check(surface.Expanded && currentPage == "Нотатки" && settings.Note.StartsWith("Тест:") && surface.DeviceToast != null,
                "A device notice remains visible in the expanded header without replacing the page or note");
            OnDeviceBatteriesChanged(Snapshot(headphones, mouse with { Percent = 30 }));
            Check(surface.DeviceToast?.Kind != DeviceNoticeKind.LowBattery, "A recovered charge dismisses a no-longer-relevant low-battery card");
            SetExpanded(false); OnDeviceBatteriesChanged(Snapshot());
            var unknown = headphones with { Id = "fixture:unknown", Name = "Навушники з дуже довгою назвою · тест обрізання", Percent = null };
            OnDeviceBatteriesChanged(Snapshot(unknown)); await Task.Delay(600); Capture(Path.Combine(directory, "device-unknown-fixture.png"));
            Check(surface.DeviceToast?.Device.Percent == null && surface.Toast.Contains("Заряд недоступний"), "An unknown battery produces an explicit unavailable label, never a fake percentage");
            surface.RefreshDeviceNotices(Array.Empty<DeviceBattery>()); surface.DeferNotifications(true);
            var keyboard = new DeviceBattery("fixture:keyboard", "Клавіатура · тест", PeripheralKind.Keyboard, 65, "Тестові дані", now);
            OnDeviceBatteriesChanged(Snapshot(unknown, keyboard));
            Check(surface.DeviceToast == null && surface.Toast.Length == 0, "Full-screen deferral suppresses both card and reaction until the user returns");
            OnDeviceBatteriesChanged(Snapshot(unknown, keyboard with { Percent = 60 })); surface.DeferNotifications(false);
            Check(surface.DeviceToast?.Device.Id == keyboard.Id && surface.DeviceToast.Device.Percent == 60,
                "Returning from full screen shows the device with its latest confirmed charge");
            surface.DeferNotifications(true); OnDeviceBatteriesChanged(Snapshot()); surface.DeferNotifications(false);
            Check(surface.DeviceToast == null, "A disconnected device's deferred notification is discarded");
            var failing = Snapshot() with { Successful = false, Status = "Тестова помилка читання" };
            surface.NotifyDevice(new DeviceNotice(headphones, DeviceNoticeKind.Connected)); OnDeviceBatteriesChanged(failing);
            Check(surface.DeviceToast == null, "A read error removes stale device cards without inventing connection events");
            SetExpanded(true); OpenSettings("devices");
            deviceConnectionBox!.IsChecked = false;
            Check(deviceBatteries.Running, "Low-battery alerts keep monitoring active when connection alerts are disabled");
            deviceLowBatteryBox!.IsChecked = false;
            Check(!deviceBatteries.Running && surface.DeviceToast == null, "Disabling both alert types stops monitoring when there is no battery tile");
            Check(settings.Home.Items.Select(i => i.Id).SequenceEqual(originalHome), "Notification settings preserve the user's Home layout");
            report.Add("INFO Visual transitions and low-charge cases use labelled fixtures; native reading uses Windows devices. No device was paired, disconnected or discharged by the test.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "device-notice-integration.txt"), report); Close(); }
    }
}

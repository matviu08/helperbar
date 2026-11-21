using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunBatteryIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); report.Add("PASS " + message); }
        try
        {
            SetExpanded(true); OpenSettings("devices"); await Task.Delay(600);
            var originalIds = settings.Home.Items.Select(i => i.Id).ToArray();
            Check(batteryEnabledBox?.IsChecked == false && !deviceBatteries.Running, "Battery monitoring starts disabled for existing layouts");
            batteryEnabledBox!.IsChecked = true;
            Check(BatteryEnabled && deviceBatteries.Running && settings.Home.Items[0].Kind == "devices", "Settings toggle starts monitoring and places the new widget in view");
            for (var i = 0; i < 250 && deviceBatteries.Snapshot.Scanning; i++) await Task.Delay(100);
            var real = deviceBatteries.Snapshot;
            Check(!real.Scanning && !real.Status.StartsWith("Не вдалося"), "Native Windows device discovery completes without an error");
            Check(real.Devices.Select(d => d.Id).Distinct().Count() == real.Devices.Count && real.Devices.All(d => d.Percent == null || d.Percent is >= 0 and <= 100),
                "Live results contain unique devices and valid percentages only");
            foreach (var device in real.Devices) report.Add($"INFO Live device: {device.Name}, {device.Kind}, {device.LevelText}, source {device.Source}");
            if (real.Devices.Count == 0) report.Add("INFO No active supported peripherals at scan time; live probe is a separate hardware check.");
            File.WriteAllText(Path.Combine(directory, "battery-live.json"), JsonSerializer.Serialize(real, new JsonSerializerOptions { WriteIndented = true }));
            Capture(Path.Combine(directory, "battery-settings-live.png"));
            SwitchPage("Головна"); await Task.Delay(550); Capture(Path.Combine(directory, "battery-home-live.png"));
            Check(homeBatteryRows != null && homeScroll!.ScrollableWidth == 0, "Device widget fits the Home width without horizontal clipping");
            deviceBatteries.Stop();
            var now = DateTimeOffset.Now;
            var fixture = new DeviceBatterySnapshot(new[] {
                new DeviceBattery("fixture:mouse", "Мишка · тест вигляду", PeripheralKind.Mouse, 100, "Тестові дані", now),
                new DeviceBattery("fixture:keyboard", "Клавіатура · тест вигляду", PeripheralKind.Keyboard, 15, "Тестові дані", now),
                new DeviceBattery("fixture:headset", "Навушники · тест вигляду", PeripheralKind.Headphones, null, "Тестові дані", now)
            }, false, "Тестові стани", now);
            UpdateDeviceBatteryUi(fixture); await Task.Delay(500); Capture(Path.Combine(directory, "battery-states-fixture.png"));
            Check(homeBatteryRows!.Children.Count == 3, "Full, low and unavailable battery states render together");
            var firstRow = homeBatteryRows.Children[0]; UpdateDeviceBatteryUi(fixture with { Scanning = true });
            Check(ReferenceEquals(firstRow, homeBatteryRows.Children[0]), "Polling does not rebuild or replay unchanged device rows");
            settings.BatteryMouse = false; UpdateDeviceBatteryUi(fixture);
            Check(homeBatteryRows.Children.Count == 2, "Hidden device category is removed from the Home widget");
            settings.BatteryMouse = true; UpdateDeviceBatteryUi(fixture with { Devices = Array.Empty<DeviceBattery>(), Status = "Немає підключених пристроїв" });
            Check(homeBatteryRows.Children.Count == 1 && homeBatteryRows.Children[0] is TextBlock, "Disconnect clears old percentages and shows the empty state");
            OpenSettings("devices"); batteryEnabledBox!.IsChecked = false;
            Check(!BatteryEnabled && !deviceBatteries.Running && settings.Home.Items.Select(i => i.Id).SequenceEqual(originalIds), "Disabling removes only the battery widget and stops its timer");
            settings.Home.Items.Clear(); batteryEnabledBox.IsChecked = true;
            Check(BatteryEnabled && deviceBatteries.Running && settings.Home.Items.Count == 1, "Battery toggle works when Home was intentionally empty");
            ChangeHome(layout => layout.Items.RemoveAll(i => i.Kind == "devices") > 0);
            Check(!deviceBatteries.Running, "Removing the tile in the Home editor stops battery monitoring");
            UndoHome(); Check(BatteryEnabled && deviceBatteries.Running, "Undo restores both the battery tile and monitoring");
            deviceBatteries.Stop();
            // A late native callback must never put stale connected devices back after the user turns the feature off.
            var pending = new TaskCompletionSource<DeviceBatterySnapshot>();
            using (var service = new DeviceBatteryService(_ => pending.Task))
            {
                service.Start(); service.Stop(); pending.SetResult(fixture); await Task.Delay(80);
                Check(service.Snapshot == DeviceBatterySnapshot.Disabled && !service.Running, "A late scan result is discarded after disabling monitoring");
            }
            var pendingFirst = new TaskCompletionSource<DeviceBatterySnapshot>(); var reads = 0;
            using (var service = new DeviceBatteryService(_ => ++reads == 1 ? pendingFirst.Task : Task.FromResult(real)))
            {
                service.Start(); service.Stop(); service.Start(); pendingFirst.SetResult(fixture); await Task.Delay(80);
                Check(reads == 2 && service.Snapshot == real && service.Running, "Rapid off/on starts a fresh scan and ignores the cancelled generation");
            }
            var failingReads = 0;
            using (var service = new DeviceBatteryService(_ => ++failingReads == 1 ? Task.FromResult(fixture) : Task.FromException<DeviceBatterySnapshot>(new IOException("Test failure"))))
            {
                service.Start(); await service.RefreshAsync();
                Check(service.Snapshot.Devices.Count == 0 && service.Snapshot.Status.StartsWith("Не вдалося"), "Scan failure clears stale percentages and leaves an explicit retry state");
            }
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "battery-integration.txt"), report); Close(); }
    }
}

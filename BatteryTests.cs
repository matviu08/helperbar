using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class BatteryTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        check(BatteryValues.Percent((byte)0) == 0 && BatteryValues.Percent((byte)100) == 100 && BatteryValues.Percent((ushort)42) == 42,
            "Battery percentages retain real zero and 100 percent readings");
        check(new object?[] { null, (byte)101, (byte)255, -1, 101, uint.MaxValue, "100", true }.All(v => BatteryValues.Percent(v) == null),
            "Unknown battery sentinels and invalid types never become a full battery");
        check(BatteryValues.Classify(15, 2, null, null, "", "AJ159 PRO") == PeripheralKind.Mouse &&
              BatteryValues.Classify(15, 1, null, null, "", "Wireless device") == PeripheralKind.Keyboard,
            "BLE appearance identifies a mouse and keyboard without guessing from brand names");
        check(BatteryValues.Classify(null, null, 5, 0x20, "", "Device") == PeripheralKind.Mouse &&
              BatteryValues.Classify(null, null, 5, 0x10, "", "Device") == PeripheralKind.Keyboard &&
              BatteryValues.Classify(null, null, 4, 6, "", "Device") == PeripheralKind.Headphones,
            "Classic Bluetooth device classes identify the supported peripheral types");
        check(BatteryValues.Classify(null, null, null, null, "Audio.Headphone", "Device") == PeripheralKind.Headphones &&
              BatteryValues.Classify(null, null, null, null, "Input.Mouse", "Device") == PeripheralKind.Mouse &&
              BatteryValues.Classify(null, null, 2, null, "Phone", "Phone") == null &&
              BatteryValues.Classify(15, 4, null, null, "Input.Gamepad", "Controller") == null,
            "Windows categories support fallback classification while phones and gamepads stay out");
        var now = DateTimeOffset.Now;
        var first = new DeviceBattery("bt:one", "Same model", PeripheralKind.Mouse, 0, "test", now);
        var second = first with { Id = "bt:two", Percent = 100 };
        var merged = BatteryValues.Merge(new[] { first, first with { Percent = null, ReadAt = now.AddSeconds(1) }, second });
        check(merged.Count == 2 && merged.Single(d => d.Id == first.Id).Percent == 0,
            "Same-name physical devices stay separate and duplicate endpoints prefer a known reading");
        check(first.Low && (first with { Percent = 20 }).Low && !(first with { Percent = 21 }).Low && !(first with { Percent = null }).Low &&
              (first with { Percent = null }).LevelText == "Заряд недоступний", "Low battery and unsupported battery are distinct states");
        var preferences = new Settings { BatteryMouse = false, BatteryKeyboard = true, BatteryHeadphones = false };
        check(!BatteryValues.Visible(first, preferences) && BatteryValues.Visible(first with { Kind = PeripheralKind.Keyboard }, preferences) &&
              !BatteryValues.Visible(first with { Kind = PeripheralKind.Headphones }, preferences), "Battery category filters are independent");
        preferences.Home.AddWidget("devices"); var path = Path.Combine(directory, "battery-settings.json"); preferences.Save(path);
        var restored = Settings.Load(path);
        check(restored.Home.Items.Count(i => i.Kind == "devices") == 1 && !restored.BatteryMouse && restored.BatteryKeyboard && !restored.BatteryHeadphones,
            "Battery widget and category choices survive settings normalization and reload");
        check(!HomeLayout.Default().Items.Any(i => i.Kind == "devices"), "Existing layouts do not gain a battery tile during migration");
    }
}

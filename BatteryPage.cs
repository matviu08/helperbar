using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private readonly DeviceBatteryService deviceBatteries = new();
    private StackPanel? homeBatteryRows, batteryCategories;
    private TextBlock? homeBatteryStatus, batteryPreferenceStatus;
    private CheckBox? batteryEnabledBox;
    private CheckBox? deviceConnectionBox, deviceLowBatteryBox;
    private Button? batteryRefresh;
    private string? batteryRowsSignature;
    private bool updatingBatteryToggle;
    private bool BatteryEnabled => settings.Home.Items.Any(i => i.Kind == "devices");

    private void ClearBatteryPreferences()
    {
        batteryEnabledBox = null; batteryCategories = null; batteryPreferenceStatus = null; batteryRefresh = null;
        deviceConnectionBox = deviceLowBatteryBox = null;
    }
    private void UpdateDeviceBatteryMonitoring()
    {
        if (closed) return;
        if (DeviceMonitoringEnabled) deviceBatteries.Start();
        else { deviceNoticeTracker.Pause(); deviceBatteries.Stop(); }
        surface.RefreshDeviceNotices(deviceBatteries.Snapshot.Devices);
        UpdateDeviceBatteryUi(deviceBatteries.Snapshot);
    }
    private void SetBatteryEnabled(bool enabled)
    {
        if (updatingBatteryToggle) return;
        if (enabled && !BatteryEnabled)
        {
            if (settings.Home.Items.Count >= HomeLayout.Limit)
            {
                updatingBatteryToggle = true; if (batteryEnabledBox != null) batteryEnabledBox.IsChecked = false; updatingBatteryToggle = false;
                surface.Notify("На головній уже 32 плитки. Прибери одну, щоб додати заряд.", "curious"); return;
            }
            ChangeHome(layout => { if (!layout.AddWidget("devices")) return false; layout.MoveTo(layout.Items[^1].Id, 0); return true; });
        }
        else if (!enabled && BatteryEnabled) ChangeHome(layout => layout.Items.RemoveAll(i => i.Kind == "devices") > 0);
    }
    private FrameworkElement BatteryPreferences()
    {
        batteryEnabledBox = Ui.Toggle("Показувати заряд пристроїв на головній", BatteryEnabled, SetBatteryEnabled);
        AutomationProperties.SetName(batteryEnabledBox, "Показувати заряд пристроїв на головній");
        batteryCategories = new StackPanel();
        void Filter(string title, bool value, Action<bool> set)
        {
            var toggle = Ui.Toggle(title, value, state => { set(state); DeviceNoticePreferencesChanged(); });
            toggle.FontSize = 11; batteryCategories.Children.Add(toggle);
        }
        Filter("Мишка", settings.BatteryMouse, value => settings.BatteryMouse = value);
        Filter("Клавіатура", settings.BatteryKeyboard, value => settings.BatteryKeyboard = value);
        Filter("Навушники", settings.BatteryHeadphones, value => settings.BatteryHeadphones = value);
        batteryPreferenceStatus = Ui.Text("", 10, "#A99AB9");
        batteryRefresh = HomeSmallButton("Оновити", async () => await deviceBatteries.RefreshAsync());
        var status = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(batteryRefresh, Dock.Right); batteryRefresh.Margin = new Thickness(10, 0, 0, 0);
        status.Children.Add(batteryRefresh); status.Children.Add(batteryPreferenceStatus);
        deviceConnectionBox = Ui.Toggle("Сповіщати про підключення", settings.NotifyDeviceConnections, value => { settings.NotifyDeviceConnections = value; DeviceNoticePreferencesChanged(); });
        deviceLowBatteryBox = Ui.Toggle("Попереджати про заряд 20% і нижче", settings.NotifyLowBattery, value => { settings.NotifyLowBattery = value; DeviceNoticePreferencesChanged(); });
        var panel = Ui.Column(
            SettingsGroup("Заряд і сповіщення", batteryEnabledBox, deviceConnectionBox, deviceLowBatteryBox),
            SettingsGroup("Типи пристроїв", batteryCategories, Ui.Gap(4), Ui.Text("Для віджета й сповіщень одночасно.", 10, "#8E7F9E")),
            SettingsGroup("Підключення", status, Ui.Gap(7),
                Ui.Text("Bluetooth · для USB-приймачів — якщо Windows отримує заряд. Перевірка кожні 5 с; сплячі й відключені пристрої зникають зі списку.", 10, "#8E7F9E")),
            Ui.Text("Сповіщення працюють і без віджета. У повному екрані повідомлення зачекають.", 10, "#8E7F9E"));
        UpdateDeviceBatteryUi(deviceBatteries.Snapshot); return panel;
    }
    private Border DeviceBatteryTile()
    {
        homeBatteryRows = new StackPanel(); batteryRowsSignature = null;
        homeBatteryStatus = HomeLine("", 9, "#9382A6");
        var options = HomeSmallButton("Налаштувати", () => OpenSettings("devices")); options.FontSize = 9; options.MinHeight = 23;
        AutomationProperties.SetAutomationId(options, "DeviceSettings");
        var tile = new Border
        {
            Width = 460, MinHeight = 94, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#343044"), Background = Ui.Brush("#1B1724"),
            Child = Ui.Column(HomeHeadingWithAction("ТВОЇ ПРИСТРОЇ", options), Ui.Gap(4), homeBatteryRows, Ui.Gap(4), homeBatteryStatus)
        };
        UpdateDeviceBatteryUi(deviceBatteries.Snapshot); return tile;
    }
    private void UpdateDeviceBatteryUi(DeviceBatterySnapshot snapshot)
    {
        if (closed) return;
        var visible = snapshot.Devices.Where(d => BatteryValues.Visible(d, settings)).ToArray();
        var noCategories = !settings.BatteryMouse && !settings.BatteryKeyboard && !settings.BatteryHeadphones;
        var status = !DeviceMonitoringEnabled ? "Заряд і сповіщення вимкнені" : snapshot.Scanning ? "Перевіряю підключення…" : noCategories ? "Обери типи пристроїв вище" : snapshot.Status;
        if (batteryCategories != null) batteryCategories.IsEnabled = DeviceMonitoringEnabled;
        if (batteryRefresh != null) batteryRefresh.IsEnabled = DeviceMonitoringEnabled && !snapshot.Scanning;
        if (batteryPreferenceStatus != null)
            batteryPreferenceStatus.Text = DeviceMonitoringEnabled && visible.Length > 0 && !snapshot.Scanning
                ? string.Join(" · ", visible.Select(d => d.Name + "  " + d.LevelText)) : status;
        if (homeBatteryStatus != null)
            homeBatteryStatus.Text = snapshot.Scanning ? "Оновлюю…" : visible.Length > 0 ? "Оновлено " + snapshot.UpdatedAt.ToString("HH:mm") + " · автоматично кожні 5 с" : noCategories ? "Увімкни потрібні типи в налаштуваннях" : status;
        if (homeBatteryRows == null) return;
        var signature = string.Join("|", visible.Select(d => d.Id + ":" + d.Name + ":" + d.Kind + ":" + d.Percent)) + ":" + noCategories;
        if (batteryRowsSignature == signature) return;
        batteryRowsSignature = signature; homeBatteryRows.Children.Clear();
        if (visible.Length == 0)
        {
            homeBatteryRows.Children.Add(Ui.Text(noCategories ? "Які пристрої показувати?" : "Підключи мишку, клавіатуру або навушники", 12, "#CBBBDD")); return;
        }
        foreach (var device in visible) homeBatteryRows.Children.Add(BatteryDeviceRow(device, settings.ReducedMotion));
    }
    private static FrameworkElement BatteryDeviceRow(DeviceBattery device, bool reducedMotion)
    {
        var color = device.Low ? "#F0B386" : "#A5DEC5";
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5), ToolTip = device.Name + " · " + device.LevelText + "\n" + device.Source };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = HomeGlyph(device.Kind switch { PeripheralKind.Mouse => "\uE962", PeripheralKind.Keyboard => "\uE765", PeripheralKind.Headphones => "\uE7F6", _ => "\uE83F" }, 20);
        glyph.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(glyph);
        var name = HomeLine(device.Name, 12, "#E9DEF5", true);
        var track = new Grid { Height = 3, Margin = new Thickness(0, 7, 14, 1) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Ui.Brush("#3A3047") });
        if (device.Percent is { } percent)
        {
            var scale = new System.Windows.Media.ScaleTransform(percent / 100d, 1);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Ui.Brush(color), RenderTransform = scale });
            if (!reducedMotion) scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0, percent / 100d, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        var detail = Ui.Column(name, track); Grid.SetColumn(detail, 1); row.Children.Add(detail);
        var level = Ui.Text(device.LevelText, device.Percent.HasValue ? 18 : 10, device.Percent.HasValue ? color : "#A99AB9", device.Percent.HasValue);
        level.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(level, 2); row.Children.Add(level);
        AutomationProperties.SetName(row, device.Name + ", " + device.LevelText); return row;
    }
}

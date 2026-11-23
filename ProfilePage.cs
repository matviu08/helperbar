using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private Button? homeProfileButton;
    private readonly Dictionary<string, Button> profileChoices = new();

    private void SwitchProfile(string id)
    {
        if (!settings.Profiles.Switch(settings, id)) return;
        saveTimer.Stop(); homeUndo = null; homeView = "dashboard"; homeGeneration++;
        ClearSettingsPage(); settingsScrollOffsets.Clear();
        Resources["AccentBrush"] = Ui.Brush(settings.Accent); Ui.ReducedMotion = settings.ReducedMotion;
        surface.RefreshProfilePresentation();
        if (!settings.AudioReactive) { audio?.Dispose(); audio = null; }
        else if (!smokeTest) audio ??= new AudioSpectrum();
        if (!smokeTest) DeviceNoticePreferencesChanged(); else surface.RefreshDeviceNotices(deviceBatteries.Snapshot.Devices);
        SwitchPage(currentPage); // Rebind every visible control to the newly active profile.
        if (!smokeTest) CheckFullscreen(null, EventArgs.Empty);
        SaveNow(); surface.React("wink", .8);
    }
    private Button CreateHomeProfileButton()
    {
        homeProfileButton = HomeSmallButton(settings.Profiles.Active.Title + "  ⌄", () => RebuildHome("profiles"));
        homeProfileButton.MinWidth = 83; homeProfileButton.MaxWidth = 130; homeProfileButton.HorizontalAlignment = HorizontalAlignment.Left;
        homeProfileButton.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        homeProfileButton.ToolTip = "Змінити профіль · окремі віджети, кольори й сповіщення";
        AutomationProperties.SetName(homeProfileButton, "Профіль: " + settings.Profiles.Active.Title);
        return homeProfileButton;
    }
    private FrameworkElement HomeProfiles()
    {
        profileChoices.Clear();
        var cards = new WrapPanel();
        foreach (var definition in ProfileCollection.Definitions)
        {
            var profile = settings.Profiles.Items.Find(x => x.Id == definition.Id)!;
            var options = profile.Id == settings.Profiles.ActiveId ? ProfileOptions.Capture(settings) : profile.Options;
            var active = profile.Id == settings.Profiles.ActiveId;
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.Brush(options.Accent), Margin = new Thickness(0, 0, 8, 0) };
            var heading = new DockPanel(); DockPanel.SetDock(dot, Dock.Left); heading.Children.Add(dot); heading.Children.Add(Ui.Text(profile.Title + (active ? "  ✓" : ""), 14, "#EEE5F8", true));
            var caption = Ui.Text(definition.Description, 10, "#AB9BBC"); caption.Height = 28;
            var button = Ui.Button("", () => { if (active) RebuildHome("dashboard"); else SwitchProfile(profile.Id); }, "Увімкнути профіль «" + profile.Title + "»", scaleOnHover: false);
            button.Width = 226; button.Height = 87; button.Padding = new Thickness(12, 9, 12, 8); button.Margin = new Thickness(0, 0, 8, 8); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Background = Ui.Brush(active ? "#2B2438" : "#19161F"); button.BorderBrush = Ui.Brush(active ? options.Accent : "#2B2734");
            button.Content = Ui.Column(heading, Ui.Gap(4), caption, Ui.Text(options.Home.Items.Count + " плиток · власні налаштування", 9, "#8F809E"));
            AutomationProperties.SetItemStatus(button, active ? "Активний профіль" : ""); profileChoices[profile.Id] = button; cards.Children.Add(button);
        }
        return HomeFrame("ТВІЙ РЕЖИМ", cards, HomeSmallButton("Налаштування", () => OpenSettings("profiles")), HomeSmallButton("← Головна", () => RebuildHome("dashboard")));
    }
    private FrameworkElement ProfilesPreferences()
    {
        profileChoices.Clear();
        var options = new Grid(); options.ColumnDefinitions.Add(new ColumnDefinition()); options.ColumnDefinitions.Add(new ColumnDefinition());
        options.RowDefinitions.Add(new RowDefinition()); options.RowDefinitions.Add(new RowDefinition());
        for (var i = 0; i < ProfileCollection.Definitions.Length; i++)
        {
            var definition = ProfileCollection.Definitions[i]; var active = settings.Profiles.ActiveId == definition.Id;
            var button = HomeSmallButton(definition.Title + (active ? "  ✓" : ""), () => SwitchProfile(definition.Id));
            button.Margin = new Thickness(0, 0, i % 2 == 0 ? 6 : 0, 6); button.MinHeight = 32;
            if (active) { button.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); button.Foreground = Ui.Brush("#15121E"); }
            Grid.SetColumn(button, i % 2); Grid.SetRow(button, i / 2); options.Children.Add(button); profileChoices[definition.Id] = button;
            AutomationProperties.SetName(button, "Увімкнути профіль «" + definition.Title + "»"); AutomationProperties.SetItemStatus(button, active ? "Активний профіль" : "");
        }
        var widgets = HomeSmallButton("Віджети", () => { SwitchPage("Головна"); RebuildHome("edit"); });
        var style = HomeSmallButton("Кольори й рух", () => SelectSettingsSection("appearance"));
        return Ui.Column(
            SettingsGroup("Активний: " + settings.Profiles.Active.Title, options,
                Ui.Text("Кожен режим запам’ятовує твої зміни автоматично.", 10, "#A99AB9"), Ui.Gap(8), Ui.Columns(widgets, style)),
            SettingsGroup("Сповіщення цього профілю",
                Ui.Toggle("Підключення пристроїв", settings.NotifyDeviceConnections, value => { settings.NotifyDeviceConnections = value; DeviceNoticePreferencesChanged(); }),
                Ui.Toggle("Заряд пристроїв 20% і нижче", settings.NotifyLowBattery, value => { settings.NotifyLowBattery = value; DeviceNoticePreferencesChanged(); }),
                Ui.Toggle("Картка виконаної справи", settings.NotifyTaskCompletion, value => { settings.NotifyTaskCompletion = value; surface.RefreshNotificationPreferences(); Persist(); }),
                Ui.Toggle("Звук завершення таймерів", settings.TimerSound, value => { settings.TimerSound = value; Persist(); }),
                Ui.Toggle("Банер Windows після фокусу", settings.NotifyFocusBanner, value => { settings.NotifyFocusBanner = value; Persist(); }), Ui.Gap(6),
                Ui.Text("Картки завершення таймерів залишаються в острові. У повному екрані вони зачекають.", 10, "#93829F")),
            SettingsGroup("Спільне для всіх режимів", Ui.Text("Нотатка, справи, поличка файлів, відліки таймерів, гучність Windows, позиція та гарячі клавіші.", 10, "#A99AB9")));
    }
}

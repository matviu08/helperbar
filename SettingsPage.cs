using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private static readonly (string Id, string Label, string Glyph, string Title, string Hint)[] settingsSections =
    {
        ("appearance", "Вигляд", "\uE790", "Твій острів", "Кольори, живі очі та характер руху"),
        ("music", "Музика", "\uE8D6", "У ритмі музики", "Мініплеєр і реакція на звук"),
        ("devices", "Пристрої", "\uE772", "Твої пристрої", "Заряд, підключення та сповіщення"),
        ("fullscreen", "Ігри", "\uE7FC", "Ігри й повний екран", "Тихий режим, коли потрібна увага"),
        ("position", "Позиція", "\uE7F4", "Місце на екрані", "Розмір, монітор і положення острова"),
        ("general", "Загальні", "\uE713", "Щодня під рукою", "Запуск, поведінка та гарячі клавіші")
    };
    private readonly Dictionary<string, Button> settingsNavigation = new();
    private readonly Dictionary<string, double> settingsScrollOffsets = new();
    private ScrollViewer? settingsScroll;
    private TextBlock? settingsTitle, settingsHint;
    private string? visibleSettingsSection;

    // Keep the existing page key so upgrades preserve the user's last tab.
    private void OpenSettings(string section)
    {
        settings.SettingsSection = section;
        SwitchPage("Вигляд");
    }

    private void ClearSettingsPage()
    {
        RememberSettingsScroll();
        settingsScroll = null; settingsTitle = settingsHint = null; visibleSettingsSection = null;
        settingsNavigation.Clear();
    }

    private void RememberSettingsScroll()
    {
        if (settingsScroll != null && visibleSettingsSection != null)
            settingsScrollOffsets[visibleSettingsSection] = settingsScroll.VerticalOffset;
    }

    private FrameworkElement SettingsPage()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        var menu = new StackPanel();
        var label = Ui.Text("РОЗДІЛИ", 8, "#81758F", true); label.Margin = new Thickness(9, 7, 0, 9);
        menu.Children.Add(label);
        foreach (var section in settingsSections)
        {
            var button = Ui.Button(section.Label, () => SelectSettingsSection(section.Id), section.Title);
            button.MinHeight = 30; button.Height = 30; button.Padding = new Thickness(8, 4, 7, 4);
            button.Margin = new Thickness(0, 0, 0, 3); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = section.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Width = 23, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = section.Label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            settingsNavigation[section.Id] = button; menu.Children.Add(button);
        }
        layout.Children.Add(new Border { Background = Ui.Brush("#131217"), BorderBrush = Ui.Brush("#28232F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(4), Child = menu });
        var detail = new Grid(); Grid.SetColumn(detail, 2);
        detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        detail.RowDefinitions.Add(new RowDefinition());
        settingsTitle = Ui.Text("", 16, "#F0E7FB", true);
        settingsHint = Ui.Text("", 10, "#94859F"); settingsHint.Margin = new Thickness(0, 3, 0, 0);
        detail.Children.Add(Ui.Column(settingsTitle, settingsHint));
        settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        Grid.SetRow(settingsScroll, 1); detail.Children.Add(settingsScroll); layout.Children.Add(detail);
        SelectSettingsSection(settings.SettingsSection, false);
        return layout;
    }

    private void SelectSettingsSection(string id, bool animate = true)
    {
        if (settingsScroll == null || (visibleSettingsSection == id && animate)) return;
        var section = Array.Find(settingsSections, s => s.Id == id);
        if (section.Id == null) section = settingsSections[0];
        RememberSettingsScroll();
        ClearBatteryPreferences(); ClearFullscreenPreferences();
        capturingShortcut = false;
        visibleSettingsSection = section.Id; settings.SettingsSection = section.Id; Persist();
        settingsTitle!.Text = section.Title; settingsHint!.Text = section.Hint;
        foreach (var (key, button) in settingsNavigation)
        {
            var selected = key == section.Id;
            button.Background = selected ? Ui.Brush("#30283F") : Brushes.Transparent;
            button.BorderBrush = selected ? Ui.Brush("#4B3B61") : Brushes.Transparent;
            if (selected) button.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            else button.Foreground = Ui.Brush("#A69AAF");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(button, selected ? "Вибрано" : "");
        }
        var content = section.Id switch
        {
            "music" => MusicPreferences(),
            "devices" => BatteryPreferences(),
            "fullscreen" => FullscreenPreferences(),
            "position" => PositionPreferences(),
            "general" => DailyPreferences(),
            _ => AppearancePreferences()
        };
        content.Margin = new Thickness(0, 0, 8, 3);
        settingsScroll.Content = content;
        settingsScroll.ScrollToVerticalOffset(settingsScrollOffsets.GetValueOrDefault(section.Id));
        if (animate) Ui.Enter(content, settings.ReducedMotion);
    }

    private static Border SettingsGroup(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title.ToUpperInvariant(), 8.5, "#A190B3", true)); stack.Children.Add(Ui.Gap(7));
        foreach (var child in children) stack.Children.Add(child);
        var card = Ui.Card(stack, new Thickness(0, 0, 0, 9)); card.Padding = new Thickness(11, 10, 11, 10); card.CornerRadius = new CornerRadius(12);
        return card;
    }

    private FrameworkElement MusicPreferences() => Ui.Column(
        SettingsGroup("Відображення",
            Ui.Toggle("Музика у згорнутому острові", settings.CompactMusic, value => { settings.CompactMusic = value; Persist(); }),
            Ui.Toggle("Анімація від системного звуку", settings.AudioReactive, value =>
            {
                settings.AudioReactive = value;
                if (value) { if (!smokeTest) audio ??= new AudioSpectrum(); } else { audio?.Dispose(); audio = null; }
                Persist();
            })),
        SettingsGroup("Реакція на звук",
            SliderRow("Чутливість", .6, 1.8, settings.AudioSensitivity, value => { settings.AudioSensitivity = value; Persist(); }, value => $"{value:0.0}×"),
            SliderRow("Музичні смуги", 0, 1.5, settings.SpectrumIntensity, value => { settings.SpectrumIntensity = value; Persist(); }, Percent),
            SliderRow("Рідкий контур", 0, 1.5, settings.EdgeIntensity, value => { settings.EdgeIntensity = value; Persist(); }, Percent),
            SliderRow("Підсвічування", 0, 1.5, settings.GlowIntensity, value => { settings.GlowIntensity = value; Persist(); }, Percent),
            SliderRow("Очі й баси", 0, 1.5, settings.EyeIntensity, value => { settings.EyeIntensity = value; Persist(); }, Percent),
            Ui.Text("0% вимикає окрему реакцію. Джерело музики й керування — у вкладці «Музика» зверху.", 10, "#93829F")));

    private static string Percent(double value) => $"{value * 100:0}%";

    private FrameworkElement PositionPreferences() => Ui.Column(
        SettingsGroup("Розмір і відступ",
            SliderRow("Розмір острова", .75, 1.4, settings.Scale, value => { settings.Scale = value; SetPosition(); Persist(); }, Percent),
            SliderRow("Відступ зверху", 0, 180, settings.TopOffset, value => { settings.TopOffset = value; settings.FreePosition = false; SetPosition(); Persist(); }, value => $"{value:0} px"),
            Ui.Text("Зміна відступу повертає острів у режим по центру.", 10, "#93829F")),
        SettingsGroup("Розташування",
            Ui.Button("Нагору по центру", ResetPosition), Ui.Gap(6), Ui.Button("Наступний екран", NextScreen), Ui.Gap(8),
            Ui.Text("Можна перетягнути острів за верхню частину. Позиція й обраний монітор збережуться автоматично.", 10, "#93829F")));
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private bool demoRunning;

    private void BuildExpanded()
    {
        expanded.Width = 480; expanded.Height = 376;
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(254) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        Canvas.SetLeft(expanded, 60); Canvas.SetTop(expanded, 26);
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var petButton = petHeaderButton = Ui.Button("◉ ◉", TogglePet, "Залишити лише очі · " + settings.PetShortcut); petButton.Padding = new Thickness(9, 4, 9, 4); petButton.MinHeight = 28;
        var collapse = Ui.Button("⌃", () => SetExpanded(false), "Згорнути · Esc"); collapse.Margin = new Thickness(6, 0, 0, 0); collapse.Padding = new Thickness(10, 3, 10, 3); collapse.MinHeight = 28;
        headerButtons.Children.Add(petButton); headerButtons.Children.Add(collapse); expanded.Children.Add(headerButtons);
        expanded.Children.Add(new Border { Height = 1, Background = Ui.Brush("#2C2734"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6), IsHitTestVisible = false });
        var navigation = new Border { Background = Ui.Brush("#16131D"), CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = tabs };
        Grid.SetRow(navigation, 1); expanded.Children.Add(navigation);
        pageHost.Margin = new Thickness(0, 13, 0, 0); Grid.SetRow(pageHost, 2); expanded.Children.Add(pageHost);
        var footer = new Grid(); Grid.SetRow(footer, 3);
        footer.Children.Add(Ui.Text("HELPERBAR  /  ОСОБИСТИЙ ПРОСТІР", 8.5, "#595763"));
        var keys = shortcutFooter = Ui.Text(settings.OpenShortcut, 9, "#797584"); keys.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(keys);
        expanded.Children.Add(footer);
        expanded.Opacity = 0; expanded.IsHitTestVisible = false; expanded.Visibility = Visibility.Hidden;
        stage.Children.Add(expanded);
        SwitchPage(settings.LastPage);
    }

    private void SwitchPage(string name)
    {
        if (currentPage == "Нотатки") { saveTimer.Stop(); SaveNow(); }
        currentPage = name;
        settings.LastPage = name; Persist();
        tabs.Children.Clear();
        tabs.ColumnDefinitions.Clear();
        foreach (var page in pageNames)
        {
            var button = Ui.Button(page == "Вигляд" ? "Налаштування" : page, () => { SwitchPage(page); surface.React("curious", .6); }, scaleOnHover: false);
            button.SetResourceReference(StyleProperty, "NavigationTab");
            button.MinHeight = 30;
            button.Margin = new Thickness(0, 0, page == "Вигляд" ? 0 : 3, 0);
            Grid.SetColumn(button, tabs.ColumnDefinitions.Count);
            tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(page == "Вигляд" ? 1.4 : 1, GridUnitType.Star) });
            button.Tag = page == name ? "active" : "inactive";
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, page == name ? "Вибрано" : "");
            tabs.Children.Add(button);
        }
        clockLabel = dateLabel = ramLabel = powerLabel = timerLabel = timerStateLabel = noteStateLabel = null;
        timerToggle = null; progressFill = null;
        ClearMusicPage();
        ClearFocusPage();
        ClearHomePage();
        ClearSettingsPage();
        ClearBatteryPreferences();
        ClearFullscreenPreferences();
        pageHost.Children.Clear();
        var pageContent = name switch
        {
            "Музика" => MusicPage(), "Фокус" => FocusPage(), "Нотатки" => NotesPage(), "Вигляд" => SettingsPage(), _ => HomePage()
        };
        pageHost.Children.Add(pageContent);
        Ui.Enter(pageContent, settings.ReducedMotion);
        OnSecond(null, EventArgs.Empty);
    }

    private FrameworkElement FocusSessionPage()
    {
        timerLabel = Ui.Text("25:00", 51, "#F2EBFF", true); timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        timerStateLabel = Ui.Text("Твій час для важливого", 11, "#9B8EAC"); timerStateLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var minutes in new[] { 5, 15, 25, 50 })
        {
            var button = Ui.Button(minutes + " хв", () => { focus.Set(minutes); settings.FocusMinutes = minutes; Persist(); surface.React("curious", 1); OnSecond(null, EventArgs.Empty); });
            button.Padding = new Thickness(14, 4, 14, 4); button.MinHeight = 27; button.Margin = new Thickness(4, 0, 4, 0); presets.Children.Add(button);
        }
        progressFill = new Border { Height = 3, Width = 0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left }; progressFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var progress = new Grid { Height = 3, Width = 420, HorizontalAlignment = HorizontalAlignment.Center };
        progress.Children.Add(new Border { Background = Ui.Brush("#2C2736"), CornerRadius = new CornerRadius(2) }); progress.Children.Add(progressFill);
        timerToggle = Ui.Button("▶   Почати", () => { focus.Toggle(DateTimeOffset.Now); surface.React(focus.Running ? "curious" : "sleepy", 1.5); OnSecond(null, EventArgs.Empty); }, primary: true);
        var reset = Ui.Button("↺   Скинути", () => { focus.Reset(); OnSecond(null, EventArgs.Empty); surface.React("wink", 1); });
        var controls = Ui.Columns(timerToggle, reset);
        return Ui.Column(presets, Ui.Gap(3), timerLabel, timerStateLabel, Ui.Gap(17), progress, Ui.Gap(17), controls);
    }

    private FrameworkElement NotesPage()
    {
        var heading = Ui.Text("Думка, яку хочеться зберегти", 16, "#E9E1F3", true);
        var note = new TextBox { Text = settings.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 160, Margin = new Thickness(0, 10, 0, 0), MaxLength = 100000 };
        System.Windows.Automation.AutomationProperties.SetName(note, "Твої нотатки");
        noteStateLabel = Ui.Text("Зберігається на цьому комп’ютері", 10, "#807588"); noteStateLabel.Margin = new Thickness(0, 9, 0, 0);
        note.TextChanged += (_, _) => { settings.Note = note.Text; if (noteStateLabel != null) noteStateLabel.Text = "Зберігаю…"; Persist(); };
        return Ui.Column(heading, note, noteStateLabel);
    }

    private FrameworkElement AppearancePreferences()
    {
        var stack = new StackPanel();
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        colors.Children.Add(Ui.Text("Акцент", 11, "#9F94AD"));
        var accentButtons = new List<(Button Button, string Color)>();
        var bodyButtons = new List<(Button Button, string Color)>();
        void UpdateColors()
        {
            foreach (var (button, color) in accentButtons)
            {
                var selected = color == settings.Accent;
                button.Content = selected ? "✓" : "";
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Вибрано" : "");
            }
            foreach (var (button, color) in bodyButtons)
            {
                var selected = color == settings.Body;
                button.BorderBrush = Ui.Brush(selected ? settings.Accent : "#303139");
                button.Foreground = Ui.Brush(selected ? settings.Accent : "#D8D0E0");
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Вибрано" : "");
            }
        }
        var swatches = new Dictionary<string, string> { ["Лаванда"] = "#BCACFF", ["М’ята"] = "#8AE4CB", ["Персик"] = "#FFC69E", ["Рожевий"] = "#F6A9CD", ["Блакитний"] = "#9FCCFF", ["Білий"] = "#E7E8EF" };
        foreach (var (label, value) in swatches)
        {
            var swatch = Ui.Button("", () => { settings.Accent = value; Resources["AccentBrush"] = Ui.Brush(value); UpdateColors(); surface.React("happy"); Persist(); }, label);
            swatch.Background = Ui.Brush(value); swatch.Foreground = Ui.Brush("#1B1725"); swatch.Width = 27; swatch.Height = 27; swatch.MinHeight = 27; swatch.Padding = new Thickness(0); swatch.Margin = new Thickness(10, 0, 0, 0); colors.Children.Add(swatch);
            accentButtons.Add((swatch, value));
        }
        var bodyColors = new WrapPanel();
        foreach (var (name, color) in new[] { ("Чорний", "#08090C"), ("Слива", "#17101F"), ("Океан", "#09161C"), ("Графіт", "#1D1E24") })
        {
            var choice = Ui.Button(name, () => { settings.Body = color; UpdateColors(); Persist(); }); choice.Margin = new Thickness(0, 0, 6, 4); choice.Padding = new Thickness(8, 5, 8, 5); choice.MinHeight = 27; choice.FontSize = 10; bodyColors.Children.Add(choice);
            bodyButtons.Add((choice, color));
        }
        UpdateColors();
        stack.Children.Add(SettingsGroup("Кольори", colors, Ui.Gap(10), bodyColors));
        stack.Children.Add(SettingsGroup("Рух і анімації",
            Ui.Toggle("Живі очі", settings.Eyes, value => { settings.Eyes = value; Persist(); }),
            Ui.Toggle("Рідкий контур і краплі", settings.Liquid, value => { settings.Liquid = value; surface.React("happy"); Persist(); }),
            Ui.Toggle("Зменшити рух", settings.ReducedMotion, value => { settings.ReducedMotion = value; Ui.ReducedMotion = value; Persist(); }), Ui.Gap(6),
            SliderRow("Швидкість руху", .6, 1.6, settings.Speed, value => { settings.Speed = value; Persist(); }, value => $"{value:0.0}×")));
        var moods = new WrapPanel();
        foreach (var (title, mood) in new[] { ("Радість", "happy"), ("Цікавість", "curious"), ("Подив", "surprised"), ("Сон", "sleepy"), ("Підморгнути", "wink"), ("Запаморочення", "dizzy"), ("Свято", "celebrate") })
        {
            var button = Ui.Button(title, () => surface.React(mood, 3)); button.FontSize = 10; button.Padding = new Thickness(9, 5, 9, 5); button.MinHeight = 28; button.Margin = new Thickness(0, 0, 6, 6); moods.Children.Add(button);
        }
        stack.Children.Add(SettingsGroup("Настрій очей", moods, Ui.Gap(6), Ui.Button("✧   Подивитися всі реакції", PlayDemo, primary: true)));
        stack.Children.Add(Ui.Button("Повернути стандартний вигляд", ResetAppearance, "Скинути лише кольори, очі та рух"));
        return stack;
    }

    private FrameworkElement SliderRow(string title, double min, double max, double value, Action<double> changed, Func<double, string> format)
    {
        var valueLabel = Ui.Text(format(value), 10, "#BAAECC"); valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var heading = new DockPanel(); DockPanel.SetDock(valueLabel, Dock.Right); valueLabel.Margin = new Thickness(10, 0, 0, 0);
        heading.Children.Add(valueLabel); heading.Children.Add(Ui.Text(title, 11, "#A99BB8"));
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = (max - min) / 30, LargeChange = (max - min) / 10 };
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, _) => { valueLabel.Text = format(slider.Value); changed(slider.Value); };
        return Ui.Column(heading, slider, Ui.Gap(6));
    }

    private async void PlayDemo()
    {
        if (demoRunning) return;
        demoRunning = true;
        SetExpanded(false); surface.PetOnly = true;
        try
        {
            foreach (var (label, mood) in new[] { ("Привіт!", "happy"), ("О, а це що?", "curious"), ("Ого!", "surprised"), ("Трохи дрімаю…", "sleepy"), ("Тобі підморгую", "wink"), ("Забагато обертів", "dizzy"), ("Маленька перемога!", "celebrate") })
            {
                if (closed) break;
                surface.Notify(label, mood);
                await System.Threading.Tasks.Task.Delay(2200);
            }
        }
        finally { demoRunning = false; }
    }

    private void ResetAppearance()
    {
        settings.Accent = "#BCACFF"; settings.Body = "#08090C"; settings.Eyes = true; settings.Liquid = true; settings.ReducedMotion = false;
        settings.Speed = 1;
        Ui.ReducedMotion = false; Resources["AccentBrush"] = Ui.Brush(settings.Accent); Persist(); OpenSettings("appearance"); surface.React("happy");
    }
}

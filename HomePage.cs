using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private string homeView = "dashboard";
    private HomeLayout? homeUndo;
    private TextBlock? homeMusicTitle, homeMusicArtist, homeFocusTime, homeFocusStatus, homeNote, homeVolumePercent;
    private Image? homeArtwork;
    private Button? homePlay, homeFocusToggle, homeMute;
    private Slider? homeVolume;
    private bool homeUpdatingVolume;
    private ScrollViewer? homeScroll;

    private void ClearHomePage()
    {
        homeFilesButton = homeProfileButton = null;
        CloseAudioOutputs(); homeOutput = null;
        homeTodoTitle = homeTodoStatus = null; homeTodoDone = null;
        homeGeneration++;
        homeMusicTitle = homeMusicArtist = homeFocusTime = homeFocusStatus = homeNote = homeVolumePercent = null;
        homeArtwork = null; homePlay = homeFocusToggle = homeMute = null; homeVolume = null; homeScroll = null;
        shortcutNameBox = shortcutPathBox = null; shortcutSaveButton = null; shortcutError = null;
        homeBatteryRows = null; homeBatteryStatus = null; batteryRowsSignature = null;
        if (currentPage != "Головна") homeView = "dashboard";
    }
    private void RebuildHome(string? view = null)
    {
        if (currentPage != "Головна" || closed) return;
        if (view != null) homeView = view;
        var offset = homeScroll?.VerticalOffset ?? 0;
        clockLabel = dateLabel = ramLabel = powerLabel = null;
        ClearHomePage(); pageHost.Children.Clear();
        var content = HomePage(); pageHost.Children.Add(content); Ui.Enter(content, settings.ReducedMotion);
        OnSecond(null, EventArgs.Empty); UpdateHomeMusic(); UpdateHomeVolume(volumeService?.State ?? VolumeState.Unavailable);
        if (view == null && homeScroll != null) homeScroll.ScrollToVerticalOffset(offset);
    }
    private FrameworkElement HomePage() => homeView switch { "profiles" => HomeProfiles(), "edit" => HomeEditor(), "add" => HomeAddPage(), "shortcut" => HomeShortcutForm(), _ => HomeDashboard() };
    private FrameworkElement HomeDashboard()
    {
        var tiles = new WrapPanel();
        foreach (var item in settings.Home.Items) tiles.Children.Add(item.Kind == "shortcut" ? ShortcutTile(item) : WidgetTile(item));
        if (settings.Home.Items.Count == 0)
        {
            var empty = Ui.Column(Ui.Text("Тут буде все твоє", 21, "#EEE6FA", true), Ui.Gap(8),
                Ui.Text("Додай улюблені програми, папки, сайти й віджети.", 12, "#A79AB9"), Ui.Gap(16),
                Ui.Button("＋  Додати першу плитку", () => RebuildHome("add"), primary: true));
            empty.Margin = new Thickness(16, 24, 16, 0); tiles.Children.Add(empty);
        }
        homeFilesButton = HomeSmallButton("Файли · " + settings.Files.Items.Count, () => OpenFilesPanel());
        var result = HomeFrame("ПІД РУКОЮ", tiles, homeFilesButton, HomeSmallButton("＋ Додати", () => RebuildHome("add")), HomeSmallButton("Налаштувати", () => RebuildHome("edit")));
        UpdateHomeMusic(); UpdateHomeWidgets(); UpdateHomeVolume(volumeService?.State ?? VolumeState.Unavailable);
        return result;
    }
    private FrameworkElement HomeFrame(string title, UIElement content, params Button[] actions)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); grid.RowDefinitions.Add(new RowDefinition());
        var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 9, 7) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
        foreach (var button in actions) { button.Margin = new Thickness(6, 0, 0, 0); buttons.Children.Add(button); }
        toolbar.Children.Add(buttons); toolbar.Children.Add(homeView == "dashboard" ? CreateHomeProfileButton() : Ui.Text(title, 9, "#9B8CAE", true)); grid.Children.Add(toolbar);
        homeScroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        Grid.SetRow(homeScroll, 1); grid.Children.Add(homeScroll); return grid;
    }
    private static Button HomeSmallButton(string name, Action action)
    {
        var button = Ui.Button(name, action); button.FontSize = 10; button.Padding = new Thickness(9, 4, 9, 4); button.MinHeight = 27; return button;
    }
    private Border WidgetTile(HomeItem item)
    {
        if (item.Kind == "devices") return DeviceBatteryTile();
        FrameworkElement content;
        Action? open = null;
        switch (item.Kind)
        {
            case "clock":
                clockLabel = Ui.Text(DateTime.Now.ToString("HH:mm"), 28, "#F4ECFF", true);
                dateLabel = HomeLine("", 10, "#A79ABB");
                content = Ui.Column(HomeHeading("СЬОГОДНІ"), clockLabel, dateLabel); break;
            case "music":
                homeMusicTitle = HomeLine("Увімкни музику", 12, "#F0E8FA", true);
                homeMusicArtist = HomeLine("Плеєр з’явиться тут", 10, "#A99BB9");
                homeArtwork = new Image { Width = 32, Height = 32, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0, 0, 32, 32), 7, 7) };
                var cover = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 9, 0) };
                cover.Children.Add(HomeGlyph("\uE8D6", 22)); cover.Children.Add(homeArtwork);
                var music = new DockPanel(); DockPanel.SetDock(cover, Dock.Left); music.Children.Add(cover); music.Children.Add(Ui.Column(homeMusicTitle, Ui.Gap(3), homeMusicArtist));
                homePlay = HomeSmallButton("▶", async () => await RunMediaCommand("toggle")); homePlay.Width = 28; homePlay.Padding = new Thickness(0); homePlay.MinHeight = 23;
                content = Ui.Column(HomeHeadingWithAction("МУЗИКА", homePlay), Ui.Gap(8), music); open = () => SwitchPage("Музика"); break;
            case "focus":
                homeFocusTime = Ui.Text("", 25, "#ECE1FE", true); homeFocusStatus = HomeLine("", 10, "#A69AB9");
                homeFocusToggle = HomeSmallButton("▶", () => { focus.Toggle(DateTimeOffset.Now); OnSecond(null, EventArgs.Empty); surface.React("curious", .7); });
                homeFocusToggle.Width = 28; homeFocusToggle.MinHeight = 23; homeFocusToggle.Padding = new Thickness(0);
                content = Ui.Column(HomeHeadingWithAction("ФОКУС", homeFocusToggle), homeFocusTime, homeFocusStatus); open = () => { settings.FocusView = "focus"; SwitchPage("Фокус"); }; break;
            case "system":
                ramLabel = HomeLine("", 13, "#E2D9ED", true); powerLabel = HomeLine("", 10, "#A99CB8");
                content = Ui.Column(HomeHeading("ТВІЙ КОМП’ЮТЕР"), Ui.Gap(9), ramLabel, Ui.Gap(5), powerLabel); break;
            case "note":
                homeNote = Ui.Text("", 12, "#DED3EC"); homeNote.MaxHeight = 43; homeNote.TextTrimming = TextTrimming.CharacterEllipsis;
                content = Ui.Column(HomeHeading("НОТАТКА  ↗"), Ui.Gap(8), homeNote); open = () => OpenNotesSection("note"); break;
            case "tasks":
                content = HomeTodoContent(); open = () => OpenNotesSection("tasks"); break;
            case "volume":
                homeVolumePercent = HomeLine("—", 12, "#E9DFF7", true);
                homeMute = HomeSmallButton("♪", () => volumeService?.ToggleMute()); homeMute.Width = 28; homeMute.MinHeight = 23; homeMute.Padding = new Thickness(0);
                homeVolume = new Slider { Minimum = 0, Maximum = 100, SmallChange = 2, LargeChange = 10, Height = 24 };
                AutomationProperties.SetName(homeVolume, "Гучність на головній");
                homeVolume.ValueChanged += (_, _) => { if (!homeUpdatingVolume) { homeVolumePercent.Text = $"{homeVolume.Value:0}%"; volumeService?.SetLevel((float)(homeVolume.Value / 100)); } };
                homeOutput = CreateOutputButton(true);
                homeVolumePercent.Width = 38; homeVolumePercent.TextAlignment = TextAlignment.Right;
                var volumeBottom = new Grid(); volumeBottom.ColumnDefinitions.Add(new ColumnDefinition()); volumeBottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
                volumeBottom.Children.Add(homeOutput); Grid.SetColumn(homeVolumePercent, 1); volumeBottom.Children.Add(homeVolumePercent);
                content = Ui.Column(HomeHeadingWithAction("ГУЧНІСТЬ WINDOWS", homeMute), Ui.Gap(1), homeVolume, volumeBottom); break;
            default: content = Ui.Text(item.Title); break;
        }
        var tile = new Border { Width = 226, Height = 92, CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("#2B2734"), Background = Ui.Brush("#19161F"), Child = content };
        if (item.Kind == "clock") tile.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#2A2136"), (Color)ColorConverter.ConvertFromString("#18151E"), 35);
        if (open != null)
        {
            tile.Focusable = true; tile.Cursor = Cursors.Hand; tile.ToolTip = "Відкрити: " + item.Title; AutomationProperties.SetName(tile, item.Title);
            tile.MouseLeftButtonUp += (_, e) => { if (!e.Handled) { open(); e.Handled = true; } };
            tile.KeyDown += (_, e) => { if (e.OriginalSource == tile && e.Key is Key.Enter or Key.Space) { open(); e.Handled = true; } };
        }
        return tile;
    }
    private FrameworkElement ShortcutTile(HomeItem item)
    {
        var button = Ui.Button("", () => OpenHomeShortcut(item), item.Name + "\n" + item.Target);
        button.Width = 109; button.Height = 70; button.Padding = new Thickness(6, 8, 6, 6); button.Margin = new Thickness(0, 0, 8, 8);
        button.Background = Ui.Brush("#19171F"); button.BorderBrush = Ui.Brush("#2B2734");
        var icon = new Grid { Height = 26 };
        icon.Children.Add(HomeGlyph(ShortcutGlyph(item), 22));
        var iconPath = item.TargetType == "app" ? item.Target : item.TargetType == "builtin" && item.Target == "explorer" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") : item.TargetType == "builtin" && item.Target == "calculator" ? Path.Combine(Environment.SystemDirectory, "calc.exe") : null;
        if (iconPath != null)
        {
            var image = new Image { Width = 25, Height = 25 }; icon.Children.Add(image); _ = LoadHomeIcon(iconPath, image);
        }
        var title = HomeLine(item.Name, 10, "#D8CDE6"); title.TextAlignment = TextAlignment.Center;
        button.Content = Ui.Column(icon, Ui.Gap(5), title);
        if (item.TargetType is "app" or "folder") _ = CheckHomeTarget(item, button, title);
        return button;
    }
    private static async Task CheckHomeTarget(HomeItem item, Button button, TextBlock title)
    {
        if (await Task.Run(() => ShortcutTarget.IsAvailable(item))) return;
        title.Foreground = Ui.Brush("#E8B79C"); button.BorderBrush = Ui.Brush("#77513D");
        button.ToolTip = "Не знайдено: " + item.Target + "\nНатисни, щоб змінити шлях.";
    }
    private static async Task LoadHomeIcon(string path, Image target)
    {
        var icon = await AppCatalog.IconAsync(path); if (icon != null) target.Source = icon;
    }
    private static TextBlock HomeHeading(string text) => HomeLine(text, 8, "#9A89AD", true);
    private static FrameworkElement HomeHeadingWithAction(string name, Button action)
    {
        var row = new DockPanel { LastChildFill = true, Height = 21 }; DockPanel.SetDock(action, Dock.Right); row.Children.Add(action); row.Children.Add(HomeHeading(name)); return row;
    }
    private static TextBlock HomeLine(string text, double size, string color, bool bold = false)
    {
        var label = Ui.Text(text, size, color, bold); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; return label;
    }
    private static TextBlock HomeGlyph(string glyph, double size = 20)
    {
        var label = Ui.Text(glyph, size, "#C1ACE7"); label.FontFamily = new FontFamily("Segoe MDL2 Assets"); label.HorizontalAlignment = HorizontalAlignment.Center; return label;
    }
    private static string ShortcutGlyph(HomeItem item) => item.TargetType switch { "folder" => "\uE8B7", "web" => "\uE774", "builtin" => item.Target switch { "explorer" => "\uE8B7", "calculator" => "\uE8EF", _ => "\uE722" }, _ => "\uE71D" };
    private async void OpenHomeShortcut(HomeItem item)
    {
        if (!await Task.Run(() => ShortcutTarget.IsAvailable(item)))
        {
            if (closed) return;
            surface.Notify("Ярлик недоступний — вибери новий шлях", "curious"); EditHomeShortcut(item); return;
        }
        if (!closed) Launch(ShortcutTarget.LaunchPath(item));
    }
    private void UpdateHomeMusic()
    {
        if (homeMusicTitle == null) return;
        var track = media.Track; homeMusicTitle.Text = track.Available ? track.Title : "Увімкни музику"; homeMusicTitle.ToolTip = track.Title;
        homeMusicArtist!.Text = track.Available ? (track.Playing ? "" : "На паузі · ") + track.Artist : "YouTube Music / Spotify";
        homeArtwork!.Source = track.Artwork; homePlay!.Content = track.Playing ? "Ⅱ" : "▶"; homePlay.IsEnabled = track.CanToggle;
        AutomationProperties.SetName(homePlay, track.Playing ? "Пауза на головній" : "Відтворити на головній");
    }
    private void UpdateHomeWidgets()
    {
        if (homeFocusTime != null)
        {
            homeFocusTime.Text = FocusTimer.Format(focus.Remaining(DateTimeOffset.Now)); homeFocusStatus!.Text = focus.Running ? "Зосередься на своєму" : focus.Finished ? "Готово · час відпочити" : "Одна справа за раз";
            homeFocusToggle!.Content = focus.Running ? "Ⅱ" : "▶"; AutomationProperties.SetName(homeFocusToggle, focus.Running ? "Призупинити фокус" : "Почати фокус");
        }
        if (homeNote != null) homeNote.Text = string.IsNullOrWhiteSpace(settings.Note) ? "Натисни, щоб зберегти думку…" : settings.Note;
        UpdateHomeTodos();
    }
    private void UpdateHomeVolume(VolumeState state)
    {
        if (homeVolume == null) return;
        homeUpdatingVolume = true;
        if (!homeVolume.IsMouseCaptureWithin) homeVolume.Value = state.Level * 100;
        homeVolume.IsEnabled = homeMute!.IsEnabled = CanAdjustVolume(state);
        homeMute.Content = state.Muted ? "✕" : "♪"; homeMute.ToolTip = state.Muted ? "Увімкнути звук" : "Вимкнути звук";
        AutomationProperties.SetName(homeMute, homeMute.ToolTip.ToString());
        homeVolumePercent!.Text = state.Available ? $"{state.Percent}%" : "—";
        homeVolumePercent.ToolTip = state.Available ? state.Muted ? "Звук вимкнений" : "Гучність Windows" : "Вихід недоступний";
        homeUpdatingVolume = false;
    }
}

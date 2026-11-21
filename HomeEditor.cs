using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private const string HomeDragFormat = "HelperBar.HomeTile";
    private string homeAddType = "widget", shortcutType = "web", shortcutName = "", shortcutPath = "";
    private string? editingHomeId;
    private Task<IReadOnlyList<InstalledShortcut>>? catalogTask;
    private int homeGeneration;
    private TextBox? shortcutNameBox, shortcutPathBox;
    private Button? shortcutSaveButton;
    private TextBlock? shortcutError;

    private void ChangeHome(Func<HomeLayout, bool> change, string? nextView = null)
    {
        var before = settings.Home.Copy();
        if (!change(settings.Home)) return;
        homeUndo = before; Persist(); UpdateDeviceBatteryMonitoring(); RebuildHome(nextView); surface.React("wink", .6);
    }
    private void UndoHome()
    {
        if (homeUndo == null) return;
        settings.Home = homeUndo; homeUndo = null; Persist(); UpdateDeviceBatteryMonitoring(); RebuildHome();
    }
    private FrameworkElement HomeEditor()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(Ui.Text("Потягни за ⋮⋮ або користуйся стрілками. × прибирає лише плитку.", 10, "#9988AB")); stack.Children.Add(Ui.Gap(9));
        for (var index = 0; index < settings.Home.Items.Count; index++)
        {
            var item = settings.Home.Items[index]; var position = index;
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(99) });
            var grip = Ui.Text("⋮⋮", 16, "#9F8CB7"); grip.Cursor = Cursors.SizeAll; grip.ToolTip = "Перетягнути плитку"; row.Children.Add(grip);
            var label = Ui.Column(HomeLine(item.Title, 12, "#DED3EC", true), HomeLine(item.Kind == "shortcut" ? ShortcutDescription(item) : "Віджет", 9, "#8E7E9D"));
            var title = Ui.Button("", () => { if (item.Kind == "shortcut") EditHomeShortcut(item); }, item.Kind == "shortcut" ? "Редагувати " + item.Title : item.Title);
            title.Content = label; title.Background = Brushes.Transparent; title.BorderThickness = new Thickness(0); title.Padding = new Thickness(3); title.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (item.Kind != "shortcut") title.Focusable = false;
            Grid.SetColumn(title, 1); row.Children.Add(title);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var up = HomeSmallButton("↑", () => ChangeHome(layout => layout.MoveTo(item.Id, position - 1)));
            var down = HomeSmallButton("↓", () => ChangeHome(layout => layout.MoveTo(item.Id, position + 1)));
            var remove = HomeSmallButton("×", () => ChangeHome(layout => layout.Items.RemoveAll(i => i.Id == item.Id) > 0));
            up.IsEnabled = index > 0; down.IsEnabled = index < settings.Home.Items.Count - 1;
            AutomationProperties.SetName(up, "Підняти " + item.Title); AutomationProperties.SetName(down, "Опустити " + item.Title); AutomationProperties.SetName(remove, "Прибрати " + item.Title);
            foreach (var button in new[] { up, down, remove }) { button.Width = 29; button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 0, 0); controls.Children.Add(button); }
            Grid.SetColumn(controls, 2); row.Children.Add(controls);
            var shell = new Border { Child = row, Padding = new Thickness(8, 4, 6, 4), Background = Ui.Brush("#1D1924"), BorderBrush = Ui.Brush("#302839"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 0, 6), AllowDrop = true };
            AttachHomeDrag(grip, shell, item.Id); stack.Children.Add(shell);
        }
        if (settings.Home.Items.Count == 0) stack.Children.Add(Ui.Text("Поки порожньо. Додай те, чим користуєшся.", 12, "#C7B8D9"));
        stack.Children.Add(Ui.Gap(6));
        stack.Children.Add(HomeSmallButton("Повернути стандартну головну", () => ChangeHome(layout => { layout.Items = HomeLayout.Default().Items; return true; })));
        var undo = HomeSmallButton("↶ Скасувати", UndoHome); undo.IsEnabled = homeUndo != null;
        return HomeFrame("ТВОЯ РОЗКЛАДКА", stack, undo, HomeSmallButton("＋ Додати", () => RebuildHome("add")), HomeSmallButton("Готово", () => RebuildHome("dashboard")));
    }
    private void AttachHomeDrag(FrameworkElement grip, Border target, string id)
    {
        Point? origin = null;
        grip.MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(grip); grip.CaptureMouse(); e.Handled = true; };
        grip.MouseLeftButtonUp += (_, _) => { origin = null; grip.ReleaseMouseCapture(); };
        grip.MouseMove += (_, e) =>
        {
            if (origin is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(grip) - start).Length < 5) return;
            origin = null; grip.ReleaseMouseCapture(); DragDrop.DoDragDrop(grip, new DataObject(HomeDragFormat, id), DragDropEffects.Move);
        };
        target.DragOver += (_, e) =>
        {
            var valid = e.Data.GetDataPresent(HomeDragFormat) && e.Data.GetData(HomeDragFormat) is string draggedId && settings.Home.Items.Any(i => i.Id == draggedId);
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
            target.BorderBrush = valid ? Ui.Brush(settings.Accent) : Ui.Brush("#302839"); e.Handled = true;
            if (homeScroll != null)
            {
                var y = e.GetPosition(homeScroll).Y;
                if (y < 24) homeScroll.ScrollToVerticalOffset(homeScroll.VerticalOffset - 9);
                else if (y > homeScroll.ActualHeight - 24) homeScroll.ScrollToVerticalOffset(homeScroll.VerticalOffset + 9);
            }
        };
        target.DragLeave += (_, _) => target.BorderBrush = Ui.Brush("#302839");
        target.Drop += (_, e) =>
        {
            target.BorderBrush = Ui.Brush("#302839");
            if (e.Data.GetData(HomeDragFormat) is string draggedId)
            {
                var after = e.GetPosition(target).Y > target.ActualHeight / 2;
                ChangeHome(layout => layout.MoveRelative(draggedId, id, after));
            }
            e.Handled = true;
        };
    }
    private FrameworkElement HomeAddPage()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var categories = new Grid(); for (var i = 0; i < 4; i++) categories.ColumnDefinitions.Add(new ColumnDefinition());
        var index = 0;
        foreach (var (type, label) in new[] { ("widget", "Віджети"), ("app", "Програми"), ("folder", "Папки"), ("web", "Сайти") })
        {
            var button = HomeSmallButton(label, () => { homeAddType = type; RebuildHome("add"); });
            button.Margin = new Thickness(0, 0, 6, 8); if (homeAddType == type) { button.Background = Ui.Brush("#3C2F50"); button.BorderBrush = Ui.Brush("#75628E"); }
            Grid.SetColumn(button, index++); categories.Children.Add(button);
        }
        stack.Children.Add(categories);
        if (settings.Home.Items.Count >= HomeLayout.Limit) stack.Children.Add(Ui.Text("На головній уже 32 плитки. Прибери зайву, щоб додати нову.", 11, "#E7BBA3"));
        else if (homeAddType == "widget")
        {
            foreach (var widget in HomeLayout.Widgets)
            {
                var exists = settings.Home.Items.Any(i => i.Kind == widget.Kind);
                stack.Children.Add(HomeChoice(widget.Title, widget.Description, widget.Glyph, exists, () => ChangeHome(layout => layout.AddWidget(widget.Kind))));
            }
            stack.Children.Add(Ui.Gap(8)); stack.Children.Add(HomeHeading("ШВИДКІ ДІЇ WINDOWS")); stack.Children.Add(Ui.Gap(6));
            foreach (var item in HomeLayout.Default().Items.Where(i => i.TargetType == "builtin"))
                stack.Children.Add(HomeChoice(item.Name, "Відкрити одним кліком", ShortcutGlyph(item), settings.Home.Items.Any(i => i.TargetType == "builtin" && i.Target == item.Target), () => ChangeHome(layout => layout.AddShortcut(item.Copy()))));
        }
        else if (homeAddType == "app")
        {
            stack.Children.Add(HomeHeading("ПОШУК У МЕНЮ «ПУСК»")); stack.Children.Add(Ui.Gap(5));
            var search = new TextBox { Height = 33, Padding = new Thickness(9, 6, 9, 6), ToolTip = "Пошук у меню Пуск" };
            AutomationProperties.SetName(search, "Знайти програму"); stack.Children.Add(search); stack.Children.Add(Ui.Gap(7));
            stack.Children.Add(HomeSmallButton("Обрати .exe або ярлик…", () => BrowseHomeApplication(null)));
            stack.Children.Add(Ui.Gap(8));
            var results = new StackPanel(); results.Children.Add(Ui.Text("Читаю програми з меню «Пуск»…", 11, "#A392B7")); stack.Children.Add(results);
            _ = FillAppCatalog(search, results, homeGeneration);
        }
        else if (homeAddType == "folder")
        {
            stack.Children.Add(Ui.Gap(12)); stack.Children.Add(Ui.Text("Твої папки на відстані кліку", 16, "#E7DCF3", true)); stack.Children.Add(Ui.Gap(8));
            stack.Children.Add(Ui.Text("Робота, навчання, завантаження або власний проєкт — вибери потрібну папку.", 12, "#A99AB8")); stack.Children.Add(Ui.Gap(13));
            stack.Children.Add(Ui.Button("Обрати папку…", () => BrowseHomeFolder(null), primary: true));
        }
        else
        {
            stack.Children.Add(Ui.Text("Відкриватимуться у твоєму стандартному браузері.", 11, "#A99AB8")); stack.Children.Add(Ui.Gap(9));
            stack.Children.Add(Ui.Button("＋  Власний сайт", () => NewHomeShortcut("web", "", "https://"), primary: true)); stack.Children.Add(Ui.Gap(10));
            foreach (var (name, address) in new[] { ("YouTube Music", "https://music.youtube.com/"), ("YouTube", "https://www.youtube.com/"), ("Spotify", "https://open.spotify.com/") })
                stack.Children.Add(HomeChoice(name, new Uri(address).Host, "\uE774", settings.Home.Items.Any(i => i.Target == address), () => NewHomeShortcut("web", name, address)));
        }
        return HomeFrame("ДОДАТИ НА ГОЛОВНУ", stack, HomeSmallButton("← Розкладка", () => RebuildHome("edit")), HomeSmallButton("Готово", () => RebuildHome("dashboard")));
    }
    private FrameworkElement HomeChoice(string title, string subtitle, string glyph, bool exists, Action add)
    {
        var row = new DockPanel();
        var symbol = HomeGlyph(glyph, 19); symbol.Width = 30; symbol.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(symbol, Dock.Left); row.Children.Add(symbol);
        var label = Ui.Column(HomeLine(title, 12, "#DDD0ED", true), HomeLine(subtitle, 9, "#92809F"));
        var status = Ui.Text(exists ? "✓" : "＋", 17, "#BCA6DB"); status.Width = 20; DockPanel.SetDock(status, Dock.Right); row.Children.Add(status); row.Children.Add(label);
        var button = Ui.Button("", add, exists ? title + " вже на головній" : "Додати " + title); button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(8, 7, 8, 7); button.Margin = new Thickness(0, 0, 0, 6); button.IsEnabled = !exists; return button;
    }
    private async Task FillAppCatalog(TextBox search, StackPanel results, int generation)
    {
        var apps = await (catalogTask ??= AppCatalog.ReadAsync());
        if (closed || homeView != "add" || generation != homeGeneration) return;
        void Render()
        {
            results.Children.Clear();
            var matches = apps.Where(app => app.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Take(40).ToArray();
            if (matches.Length == 0) results.Children.Add(Ui.Text("Не знайшлося. Можна вибрати .exe або ярлик вручну.", 11, "#A99AB8"));
            foreach (var app in matches)
                results.Children.Add(HomeChoice(app.Name, "З меню «Пуск»", "\uE71D", settings.Home.Items.Any(i => string.Equals(i.Target, app.Path, StringComparison.OrdinalIgnoreCase)), () => NewHomeShortcut("app", app.Name, app.Path)));
            if (matches.Length == 40) results.Children.Add(Ui.Text("Уточни пошук, щоб знайти решту програм.", 10, "#9D8BAE"));
        }
        search.TextChanged += (_, _) => Render(); Render();
    }
    private static string ShortcutDescription(HomeItem item) => item.TargetType switch { "app" => "Програма · натисни, щоб змінити", "folder" => "Папка · натисни, щоб змінити", "web" => "Сайт · натисни, щоб змінити", _ => "Дія Windows · перейменувати" };
    private void NewHomeShortcut(string type, string name, string target)
    { editingHomeId = null; shortcutType = type; shortcutName = name; shortcutPath = target; RebuildHome("shortcut"); }
    private void EditHomeShortcut(HomeItem item)
    { editingHomeId = item.Id; shortcutType = item.TargetType; shortcutName = item.Name; shortcutPath = item.Target; RebuildHome("shortcut"); }
    private FrameworkElement HomeShortcutForm()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(HomeHeading("НАЗВА ПЛИТКИ")); stack.Children.Add(Ui.Gap(5));
        shortcutNameBox = new TextBox { Text = shortcutName, MaxLength = 60, Height = 34, Padding = new Thickness(9, 7, 9, 7) };
        AutomationProperties.SetName(shortcutNameBox, "Назва плитки"); stack.Children.Add(shortcutNameBox); stack.Children.Add(Ui.Gap(10));
        stack.Children.Add(HomeHeading(shortcutType == "web" ? "АДРЕСА САЙТУ" : "РОЗТАШУВАННЯ")); stack.Children.Add(Ui.Gap(5));
        shortcutPathBox = new TextBox { Text = shortcutPath, Height = 34, Padding = new Thickness(9, 7, 9, 7), IsReadOnly = shortcutType == "builtin" };
        AutomationProperties.SetName(shortcutPathBox, "Адреса або шлях"); stack.Children.Add(shortcutPathBox); stack.Children.Add(Ui.Gap(6));
        shortcutNameBox.TextChanged += (_, _) => shortcutName = shortcutNameBox.Text;
        shortcutPathBox.TextChanged += (_, _) => shortcutPath = shortcutPathBox.Text;
        if (shortcutType is "app" or "folder") stack.Children.Add(HomeSmallButton("Обрати інший шлях…", () => { if (shortcutType == "app") BrowseHomeApplication(editingHomeId); else BrowseHomeFolder(editingHomeId); }));
        shortcutError = Ui.Text("", 10, "#F0B69E"); stack.Children.Add(shortcutError); stack.Children.Add(Ui.Gap(8));
        shortcutSaveButton = Ui.Button(editingHomeId == null ? "Додати на головну" : "Зберегти зміни", async () => await SaveHomeShortcut(), primary: true);
        stack.Children.Add(shortcutSaveButton);
        return HomeFrame(editingHomeId == null ? "НОВА ПЛИТКА" : "ЗМІНИТИ ПЛИТКУ", stack, HomeSmallButton("← Назад", () => RebuildHome(editingHomeId == null ? "add" : "edit")));
    }
    private async Task SaveHomeShortcut()
    {
        HomeItem item;
        if (shortcutType == "builtin" && editingHomeId != null)
        {
            var existing = settings.Home.Items.FirstOrDefault(i => i.Id == editingHomeId); if (existing == null) return;
            if (string.IsNullOrWhiteSpace(shortcutName)) { shortcutError!.Text = "Вкажи назву плитки."; return; }
            item = existing.Copy(); item.Name = shortcutName.Trim();
        }
        else if (!ShortcutTarget.TryCreate(shortcutType, shortcutName, shortcutPath, out item, out var error)) { shortcutError!.Text = error; return; }
        var generation = homeGeneration; shortcutSaveButton!.IsEnabled = false;
        var available = await Task.Run(() => ShortcutTarget.IsAvailable(item));
        if (closed || generation != homeGeneration) return;
        shortcutSaveButton.IsEnabled = true;
        if (!available) { shortcutError!.Text = "Цей шлях зараз недоступний. Перевір програму або папку."; return; }
        if (editingHomeId == null && (settings.Home.Items.Count >= HomeLayout.Limit || settings.Home.Items.Any(i => i.TargetType == item.TargetType && string.Equals(i.Target, item.Target, item.TargetType == "web" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))))
        { shortcutError!.Text = "Така плитка вже є або досягнуто ліміт 32 плиток."; return; }
        ChangeHome(layout =>
        {
            if (editingHomeId == null) return layout.AddShortcut(item);
            var index = layout.Items.FindIndex(i => i.Id == editingHomeId); if (index < 0) return false;
            item.Id = editingHomeId; layout.Items[index] = item; return true;
        }, "dashboard");
    }
    private void BrowseHomeApplication(string? editId)
    {
        var dialog = new OpenFileDialog { Title = "Обери програму або ярлик", Filter = "Програми та ярлики|*.exe;*.lnk;*.url", CheckFileExists = true, DereferenceLinks = false };
        if (dialog.ShowDialog(this) == true) SetBrowsedShortcut("app", dialog.FileName, editId);
    }
    private void BrowseHomeFolder(string? editId)
    {
        var dialog = new OpenFolderDialog { Title = "Обери папку для головної", Multiselect = false };
        if (dialog.ShowDialog(this) == true) SetBrowsedShortcut("folder", dialog.FolderName, editId);
    }
    private void SetBrowsedShortcut(string type, string path, string? editId)
    {
        var previousName = homeView == "shortcut" ? shortcutName : "";
        editingHomeId = editId; shortcutType = type; shortcutPath = path;
        shortcutName = string.IsNullOrWhiteSpace(previousName) ? Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar)) : previousName;
        RebuildHome("shortcut");
    }
}

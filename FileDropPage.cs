using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private Border? filesPanel;
    private bool fileDragActive, fileDragWasExpanded, fileDragPanelWasOpen, filesIncomingView;
    private int fileDragVersion, filesRenderVersion;
    private string[] incomingFiles = Array.Empty<string>();
    private string filesMessage = "";
    private TextBlock? filesFeedback;
    private Button? homeFilesButton, filesKeepAll, filesCopyAll;
    private ScrollViewer? filesScroll;
    private Action<string> writeFileClipboard = Clipboard.SetText;
    private Action<string> revealShelfFile = ShelfFileAccess.ShowInFolder;
    private readonly Dictionary<string, (Button Show, Button Copy, Button Keep)> fileButtons = new(StringComparer.OrdinalIgnoreCase);
    private bool FilesPanelOpen => filesPanel?.Visibility == Visibility.Visible;

    private void InitializeFileDrop()
    {
        filesPanel = new Border { Visibility = Visibility.Collapsed, Background = Ui.Brush(settings.Body), Margin = new Thickness(0, 13, 0, 0), CornerRadius = new CornerRadius(12) };
        Grid.SetRow(filesPanel, 2); expanded.Children.Add(filesPanel);
        stage.AllowDrop = true;
        stage.PreviewDragEnter += OnFileDragOver;
        stage.PreviewDragOver += OnFileDragOver;
        stage.PreviewDragLeave += async (_, e) =>
        {
            if (!fileDragActive) return;
            var version = ++fileDragVersion;
            await Task.Delay(120);
            if (!closed && version == fileDragVersion) CancelFileDrag();
        };
        stage.PreviewDrop += OnFileDrop;
    }
    private static bool IsFileData(IDataObject data)
    {
        try { return !data.GetDataPresent(HomeDragFormat, false) && data.GetDataPresent(DataFormats.FileDrop, false); }
        catch (ExternalException) { return false; }
    }
    private static DragDropEffects FileDropEffect(DragDropEffects allowed) =>
        (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!IsFileData(e.Data)) return; // Keep the home editor's own tile drag and normal text editing intact.
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (fullscreenClickThrough || fullscreenQuietApplied || !IsInside(e.GetPosition(stage)) || FileDropEffect(e.AllowedEffects) == DragDropEffects.None)
        { CancelFileDrag(); return; }
        e.Effects = FileDropEffect(e.AllowedEffects); fileDragVersion++;
        BeginFileDrag();
    }
    private void BeginFileDrag()
    {
        if (fileDragActive) return;
        fileDragWasExpanded = surface.Expanded; fileDragPanelWasOpen = FilesPanelOpen; fileDragActive = true;
        CloseAudioOutputs(); hoverTime = 0;
        SetExpanded(true, false); // OLE drag must keep focus in the source application.
        filesPanel!.Visibility = Visibility.Visible; pageHost.Visibility = Visibility.Hidden;
        RenderFilesPanel(); surface.React("curious", 1.5);
    }
    private void CancelFileDrag()
    {
        if (!fileDragActive) return;
        fileDragActive = false; fileDragVersion++;
        if (fileDragPanelWasOpen) RenderFilesPanel(); else CloseFilesPanel();
        if (!fileDragWasExpanded) SetExpanded(false, false);
    }
    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!IsFileData(e.Data)) return;
        e.Handled = true; e.Effects = DragDropEffects.None;
        if (fullscreenClickThrough || fullscreenQuietApplied || !IsInside(e.GetPosition(stage)) || FileDropEffect(e.AllowedEffects) == DragDropEffects.None)
        { CancelFileDrag(); return; }
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop, false) is not string[] paths || !ReceiveFiles(paths)) { CancelFileDrag(); return; }
            e.Effects = FileDropEffect(e.AllowedEffects);
        }
        catch (ExternalException) { CancelFileDrag(); surface.Notify("Не вдалося прочитати перетягнуті файли", "curious"); }
    }
    private bool ReceiveFiles(string[] paths)
    {
        var batch = FileShelf.ReadBatch(paths, out var skipped);
        if (batch.Length == 0) return false;
        fileDragActive = false; fileDragVersion++; incomingFiles = batch;
        filesMessage = skipped > 0 ? $"Отримано: {batch.Length}. Пропущено: {skipped} (повтори, зайві або непідтримувані)." : "Обери дію або залиш файли під рукою.";
        OpenFilesPanel(true, false); surface.React("happy", 1.5); return true;
    }
    private void OpenFilesPanel(bool incoming = false, bool activate = true)
    {
        if (filesPanel == null) return;
        fileDragActive = false; fileDragVersion++; filesIncomingView = incoming;
        CloseAudioOutputs(); SetExpanded(true, activate);
        filesPanel.Visibility = Visibility.Visible; pageHost.Visibility = Visibility.Hidden;
        RenderFilesPanel();
    }
    private void CloseFilesPanel()
    {
        fileDragActive = false; fileDragVersion++; filesRenderVersion++;
        if (filesPanel != null) { filesPanel.Visibility = Visibility.Collapsed; filesPanel.Child = null; }
        pageHost.Visibility = Visibility.Visible; filesFeedback = null; filesScroll = null; fileButtons.Clear();
    }
    private void RenderFilesPanel()
    {
        if (filesPanel == null || !FilesPanelOpen) return;
        filesRenderVersion++; fileButtons.Clear(); filesKeepAll = filesCopyAll = null;
        filesPanel.Background = Ui.Brush(settings.Body);
        if (fileDragActive)
        {
            var icon = HomeGlyph("\uE896", 32); icon.HorizontalAlignment = HorizontalAlignment.Center;
            var title = Ui.Text("Відпусти файли тут", 21, "#F0E8FF", true); title.TextAlignment = TextAlignment.Center;
            var hint = Ui.Text("Показати в папці · скопіювати шлях · залишити під рукою", 11, "#B5A8CC"); hint.TextAlignment = TextAlignment.Center;
            var content = Ui.Column(icon, Ui.Gap(13), title, Ui.Gap(9), hint, Ui.Gap(14), CenteredFileHint("Файли залишаться на своїх місцях"));
            content.VerticalAlignment = VerticalAlignment.Center;
            filesPanel.Child = new Border { Background = Ui.Brush("#221B30"), BorderBrush = Ui.Brush(settings.Accent), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(18), Child = content };
            return;
        }
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new DockPanel();
        var close = HomeSmallButton("Готово", CloseFilesPanel); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
        var sections = new StackPanel { Orientation = Orientation.Horizontal };
        void Section(string label, bool incoming)
        {
            var button = HomeSmallButton(label, () => { filesIncomingView = incoming; filesMessage = ""; RenderFilesPanel(); });
            button.SetResourceReference(StyleProperty, "NavigationTab"); button.Tag = filesIncomingView == incoming ? "active" : "inactive"; button.Margin = new Thickness(0, 0, 5, 0);
            sections.Children.Add(button);
        }
        if (incomingFiles.Length > 0) Section("Отримані · " + incomingFiles.Length, true);
        Section("Під рукою · " + settings.Files.Items.Count, false); top.Children.Add(sections); grid.Children.Add(top);
        var paths = filesIncomingView ? incomingFiles : settings.Files.Items.Select(x => x.Path).ToArray();
        filesFeedback = Ui.Text(filesMessage.Length > 0 ? filesMessage : "Посилання на файли · оригінали залишаються у своїх папках", 10, "#A99ABA");
        filesFeedback.Margin = new Thickness(2, 5, 2, 8); Grid.SetRow(filesFeedback, 1); grid.Children.Add(filesFeedback);
        var rows = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        if (paths.Length == 0)
        {
            var title = Ui.Text("Файли, які потрібні зараз", 18, "#E7DDF4", true); title.TextAlignment = TextAlignment.Center;
            var empty = Ui.Column(Ui.Gap(15), title, Ui.Gap(8), CenteredFileHint("Перетягни файл або папку на острів\nі натисни «Залишити під рукою».")); rows.Children.Add(empty);
        }
        foreach (var path in paths) rows.Children.Add(FileRow(path));
        filesScroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(filesScroll, 2); grid.Children.Add(filesScroll);
        if (paths.Length > 0)
        {
            var actions = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            filesCopyAll = HomeSmallButton(paths.Length == 1 ? "Скопіювати шлях" : "Скопіювати всі шляхи", () => CopyFilePaths(paths));
            if (filesIncomingView)
            {
                filesKeepAll = Ui.Button(paths.All(settings.Files.Contains) ? "✓ Уже під рукою" : paths.Length == 1 ? "＋ Залишити під рукою" : "＋ Залишити всі", () => KeepFiles(paths), primary: true, scaleOnHover: false);
                filesKeepAll.FontSize = 11; filesKeepAll.MinHeight = 30; filesKeepAll.Padding = new Thickness(8, 5, 8, 5); filesKeepAll.IsEnabled = paths.Any(x => !settings.Files.Contains(x));
                actions = Ui.Columns(filesCopyAll, filesKeepAll); actions.Margin = new Thickness(0, 7, 0, 0);
            }
            else actions.Children.Add(filesCopyAll);
            Grid.SetRow(actions, 3); grid.Children.Add(actions);
        }
        filesPanel.Child = grid;
    }
    private static TextBlock CenteredFileHint(string text)
    { var hint = Ui.Text(text, 11, "#998AAE"); hint.TextAlignment = TextAlignment.Center; return hint; }
    private Border FileRow(string path)
    {
        var file = new ShelfFile { Path = path }; var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(102) });
        var glyph = HomeGlyph("\uE8A5", 21); glyph.Foreground = Ui.Brush(settings.Accent); grid.Children.Add(glyph);
        var title = HomeLine(file.Name, 12, "#E8DFF4", true); var location = HomeLine(path, 9, "#9687A8");
        var details = Ui.Column(title, Ui.Gap(3), location); details.ToolTip = path; details.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(details, 1); grid.Children.Add(details);
        var show = FileAction("\uE8B7", "Показати в папці: " + file.Name, () => _ = ShowShelfFile(path));
        var copy = FileAction("\uE8C8", "Скопіювати шлях: " + file.Name, () => CopyFilePaths(new[] { path }));
        var saved = settings.Files.Contains(path);
        var keep = FileAction(filesIncomingView ? saved ? "\uE73E" : "\uE710" : "\uE711", filesIncomingView ? "Залишити під рукою: " + file.Name : "Прибрати з полички: " + file.Name,
            () => { if (filesIncomingView) KeepFiles(new[] { path }); else RemoveShelfFile(path); });
        keep.IsEnabled = !filesIncomingView || !saved;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; actions.Children.Add(show); actions.Children.Add(copy); actions.Children.Add(keep); Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        fileButtons[path] = (show, copy, keep);
        _ = UpdateFileAvailability(path, location, glyph, filesRenderVersion);
        return new Border { Child = grid, Padding = new Thickness(9, 9, 5, 9), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(12), Background = Ui.Brush("#1D1825") };
    }
    private static Button FileAction(string glyph, string label, Action action)
    {
        var button = Ui.Button(glyph, action, label, scaleOnHover: false); button.FontFamily = new FontFamily("Segoe MDL2 Assets"); button.FontSize = 12;
        button.Width = 30; button.MinHeight = 30; button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 0, 0); return button;
    }
    private async Task UpdateFileAvailability(string path, TextBlock label, TextBlock glyph, int version)
    {
        var available = false; var directory = false;
        try { (available, directory) = await Task.Run(() => { var folder = System.IO.Directory.Exists(path); return (folder || System.IO.File.Exists(path), folder); }).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        if (closed || version != filesRenderVersion) return;
        if (!available) { label.Text = "Недоступний · переміщений або диск від’єднано"; label.Foreground = Ui.Brush("#D5AB82"); }
        else if (directory) glyph.Text = "\uE8B7";
    }
    private void KeepFiles(string[] paths)
    {
        var added = 0; var full = 0;
        foreach (var path in paths) { if (settings.Files.Contains(path)) continue; if (settings.Files.Add(path)) added++; else full++; }
        filesMessage = full > 0 ? $"Додано: {added}. Поличка вміщує {FileShelf.Limit} файлів; прибери зайве й повтори." : added > 0 ? $"Залишено під рукою: {added}. Доступні також після перезапуску." : "Ці файли вже під рукою.";
        if (added > 0) { Persist(); surface.React("happy", 1); }
        UpdateHomeFiles(); RenderFilesPanel();
    }
    private void RemoveShelfFile(string path)
    {
        if (!settings.Files.Remove(path)) return;
        Persist(); filesMessage = "Прибрано з полички. Оригінал файлу залишився на місці.";
        UpdateHomeFiles(); RenderFilesPanel(); surface.React("wink", .7);
    }
    private void CopyFilePaths(string[] paths)
    {
        try { writeFileClipboard(string.Join(Environment.NewLine, paths)); FileFeedback(paths.Length == 1 ? "Шлях скопійовано" : "Шляхи скопійовано — кожен з нового рядка"); surface.React("wink", .7); }
        catch (ExternalException) { FileFeedback("Буфер обміну зараз зайнятий. Спробуй ще раз."); }
    }
    private void FileFeedback(string message) { filesMessage = message; if (filesFeedback != null) filesFeedback.Text = message; }
    private async Task ShowShelfFile(string path)
    {
        var version = filesRenderVersion;
        try
        {
            if (!await Task.Run(() => ShelfFileAccess.Exists(path)).WaitAsync(TimeSpan.FromSeconds(2)))
            { if (!closed && version == filesRenderVersion) FileFeedback("Файл недоступний: можливо, його перемістили або від’єднали диск."); return; }
            if (closed) return;
            revealShelfFile(path);
        }
        catch (Exception ex) when (ex is ExternalException or TimeoutException or UnauthorizedAccessException or System.IO.IOException)
        { if (!closed && version == filesRenderVersion) FileFeedback("Не вдалося відкрити папку. Перевір доступність файлу."); }
    }
    private void UpdateHomeFiles()
    {
        if (homeFilesButton != null) { homeFilesButton.Content = "Файли · " + settings.Files.Items.Count; AutomationProperties.SetName(homeFilesButton, "Файли під рукою: " + settings.Files.Items.Count); }
    }
}

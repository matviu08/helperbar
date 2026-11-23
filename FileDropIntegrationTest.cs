using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunFileDropIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>(); var inspect = Environment.GetCommandLineArgs().Contains("--file-drop-inspect");
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); report.Add("PASS " + label); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Picture(string name) { await Task.Delay(380); UpdateLayout(); Capture(Path.Combine(directory, name)); }
        try
        {
            var fixture = Path.Combine(directory, "File drop fixtures"); Directory.CreateDirectory(fixture);
            var first = Path.Combine(fixture, "Ідеї для HelperBar.txt"); var second = Path.Combine(fixture, "Макет, версія (2).txt");
            File.WriteAllText(first, "Synthetic drag and drop fixture"); File.WriteAllText(second, "Second synthetic fixture");
            var folder = Path.Combine(fixture, "Референси"); Directory.CreateDirectory(folder);
            var originalHome = settings.Home.Items.Select(x => x.Id).ToArray(); settings.Note = "Нотатка має зберегтися";
            settings.TimerSound = false; settings.Todos.Add("Збережена справа"); var taskId = settings.Todos.Items[0].Id;
            focus.Toggle(DateTimeOffset.Now); var reminder = settings.Reminders.Add("Тестовий таймер", TimeSpan.FromMinutes(5), DateTimeOffset.Now)!; var deadline = reminder.Deadline;
            ResetPosition(); SwitchPage("Нотатки"); SetExpanded(false); await Task.Delay(400);
            Check(IsFileData(new DataObject(DataFormats.FileDrop, new[] { first })) && !IsFileData(new DataObject(HomeDragFormat, "tile")) && !IsFileData(new DataObject(DataFormats.Text, first)), "Physical file drops are accepted while home tile drags and text keep their own handlers");
            Check(FileDropEffect(DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link) == DragDropEffects.Link && FileDropEffect(DragDropEffects.All) == DragDropEffects.Copy && FileDropEffect(DragDropEffects.Move) == DragDropEffects.None, "The target never tells the drag source to move or delete an original");
            BeginFileDrag(); await Picture("files-drag-hover.png");
            Check(surface.Expanded && FilesPanelOpen && fileDragActive && pageHost.Visibility == Visibility.Hidden && currentPage == "Нотатки", "File hover expands a dedicated drop panel without changing the selected page");
            CancelFileDrag(); await Task.Delay(300);
            Check(!surface.Expanded && !FilesPanelOpen && currentPage == "Нотатки" && pageHost.Visibility == Visibility.Visible, "Leaving a previously collapsed island restores its prior state");
            SetExpanded(true); BeginFileDrag(); CancelFileDrag();
            Check(surface.Expanded && !FilesPanelOpen, "Leaving an already expanded island keeps the original page open");
            BeginFileDrag(); Check(ReceiveFiles(new[] { first, second, folder, first.ToUpperInvariant() }), "A multi-file drop is accepted"); await Picture("files-received.png");
            Check(!fileDragActive && incomingFiles.Length == 3 && filesMessage.Contains("Пропущено: 1") && settings.Files.Items.Count == 0, "A drop previews distinct files and directories before any explicit shelf action");
            Check(fileButtons.Count == 3 && filesKeepAll!.IsEnabled && filesCopyAll!.ActualWidth > 150, "Each received file has folder, copy and keep actions plus batch controls");
            Check(filesScroll!.ActualHeight > 70 && filesKeepAll!.TransformToAncestor(expanded).Transform(new Point(0, filesKeepAll.ActualHeight)).Y <= 346, "Scrollable files and batch actions fit within the existing island height");
            string? clipboardText = null; var clipboardAction = writeFileClipboard; var revealAction = revealShelfFile;
            try
            {
                writeFileClipboard = value => clipboardText = value;
                Click(fileButtons[second].Copy); Check(clipboardText == second && filesMessage == "Шлях скопійовано", "The row copy action sends the complete Unicode path and shell punctuation to the clipboard boundary");
                Click(filesCopyAll!); Check(clipboardText == string.Join(Environment.NewLine, incomingFiles), "Batch copying preserves file order with one unquoted full path per line");
                writeFileClipboard = _ => throw new System.Runtime.InteropServices.COMException("Synthetic clipboard contention");
                Click(fileButtons[first].Copy); Check(filesMessage.Contains("зайнятий") && FilesPanelOpen, "Clipboard contention gives retry feedback without losing the pending files");
                string? revealed = null; revealShelfFile = value => revealed = value;
                await ShowShelfFile(second); Check(revealed == second, "Folder navigation receives the exact selected path instead of a shell command string");
                revealShelfFile = _ => throw new System.Runtime.InteropServices.COMException("Synthetic Explorer failure");
                await ShowShelfFile(second); Check(filesMessage.Contains("Не вдалося") && FilesPanelOpen, "Explorer failures give inline feedback and keep the files available");
            }
            finally { writeFileClipboard = clipboardAction; revealShelfFile = revealAction; }
            Click(fileButtons[first].Keep); Check(settings.Files.Items.Count == 1 && !fileButtons[first].Keep.IsEnabled && filesKeepAll!.IsEnabled, "Keeping one file updates only that row and leaves the remaining batch available");
            Click(filesKeepAll!); Check(settings.Files.Items.Count == 3 && !filesKeepAll!.IsEnabled, "Keeping all files adds the remaining distinct references exactly once");
            KeepFiles(new[] { first }); Check(settings.Files.Items.Count == 3, "Repeated keep actions do not duplicate shelf entries");
            var saved = Path.Combine(directory, "files-integration-settings.json"); settings.Save(saved); var loaded = Settings.Load(saved);
            Check(loaded.Files.Items.Count == 3 && loaded.Note == settings.Note && loaded.Todos.Items[0].Id == taskId && loaded.Reminders.Items[0].Deadline == deadline, "Shelf restart persistence preserves notes, task identities and reminder deadlines");
            CloseFilesPanel(); SwitchPage("Головна"); await Picture("files-home.png");
            Check(homeFilesButton!.Content.ToString() == "Файли · 3" && settings.Home.Items.Select(x => x.Id).SequenceEqual(originalHome), "Home exposes the shelf count without modifying the user's tile arrangement");
            Click(homeFilesButton!); await Picture("files-shelf.png");
            Check(!filesIncomingView && fileButtons.Count == 3, "Home opens the saved shelf directly");
            BeginFileDrag(); CancelFileDrag(); Check(FilesPanelOpen && !filesIncomingView && fileButtons.Count == 3, "Cancelling a second drag restores an already-open shelf");
            Click(fileButtons[first].Keep); Check(settings.Files.Items.Count == 2 && File.ReadAllText(first) == "Synthetic drag and drop fixture", "Removing from the shelf keeps the source file and its contents untouched");
            var missing = Path.Combine(fixture, "Зараз недоступний.txt"); ReceiveFiles(new[] { missing }); await ShowShelfFile(missing);
            Check(filesMessage.Contains("недоступний") && FilesPanelOpen, "A missing file gives inline feedback without opening a broken destination or closing the panel");
            KeepFiles(new[] { missing }); OpenFilesPanel(); await Picture("files-unavailable.png");
            Check(settings.Files.Contains(missing), "Unavailable entries stay removable and are not silently discarded");
            SetExpanded(false); Check(!FilesPanelOpen && !fileDragActive, "Collapsing the island closes the file overlay");
            OpenFilesPanel(); SwitchPage("Музика"); Check(!FilesPanelOpen && pageHost.Visibility == Visibility.Visible, "Main navigation leaves the file overlay and restores normal page input");
            Check(settings.Note == "Нотатка має зберегтися" && settings.Todos.Items[0].Id == taskId && focus.Running && reminder.Deadline == deadline, "File actions preserve notes, tasks, focus and running reminders");
            settings.Files.Items.Clear(); for (var i = 0; i < FileShelf.Limit; i++) settings.Files.Add(Path.Combine(fixture, "Довга назва файлу для перевірки меж панелі " + i + ".txt"));
            ReceiveFiles(new[] { first }); KeepFiles(new[] { first });
            Check(settings.Files.Items.Count == FileShelf.Limit && !settings.Files.Contains(first) && filesMessage.Contains("вміщує"), "A full shelf reports the limit and never replaces an existing reference");
            OpenFilesPanel(); await Task.Delay(150); filesScroll!.ScrollToBottom(); await Picture("files-long-list.png");
            Check(filesScroll.ExtentHeight > filesScroll.ViewportHeight && fileButtons.Values.All(x => x.Keep.ActualWidth == 30), "Long file names and many references remain bounded in the scrolling list");
            settings.ReducedMotion = true; BeginFileDrag(); await Task.Delay(150); Check(surface.Expanded && FilesPanelOpen, "Reduced motion still opens the drop target"); CancelFileDrag();
            report.Add("INFO Private Settings and generated fixture files were used; no user files, clipboard or settings were changed.");
            if (inspect)
            {
                settings.Files.Items.Clear(); incomingFiles = Array.Empty<string>(); settings.ReducedMotion = false; SwitchPage("Головна"); SetExpanded(false);
                Title = "HelperBar — перевірка перетягування";
                var source = new Border { Width = 390, Height = 64, Background = Ui.Brush("#392E51"), CornerRadius = new CornerRadius(12), Child = CenteredFileHint("ТЕСТОВІ ФАЙЛИ\nПотягни цю картку на острів угорі") };
                Canvas.SetLeft(source, 105); Canvas.SetTop(source, 432); stage.Children.Add(source);
                Point? origin = null;
                source.MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(source); source.CaptureMouse(); e.Handled = true; };
                source.MouseLeftButtonUp += (_, e) => { origin = null; source.ReleaseMouseCapture(); e.Handled = true; };
                source.MouseMove += (_, e) =>
                {
                    if (origin is not { } start || e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(source) - start).Length < 5) return;
                    origin = null; source.ReleaseMouseCapture();
                    var effect = DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, new[] { first, second, folder }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                    File.AppendAllText(Path.Combine(directory, "file-drop-ole.txt"), $"Effect={effect}; Received={incomingFiles.Length}; Active={fileDragActive}; Originals={File.Exists(first) && File.Exists(second) && Directory.Exists(folder)}\n");
                };
                // The fixture lies outside the normal island hit region and exists only in this inspection mode.
                fileDropFixture = true;
            }
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; inspect = false; }
        finally { File.WriteAllLines(Path.Combine(directory, "file-drop-integration.txt"), report); if (!inspect) Close(); }
    }
    private bool fileDropFixture;
}

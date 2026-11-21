using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunHomeIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        try
        {
            SetExpanded(true); SwitchPage("Головна"); await Task.Delay(650);
            Capture(Path.Combine(directory, "home-default.png"));
            Check(homeScroll!.ViewportWidth >= 468 && homeScroll.ScrollableHeight > 0, "Home grid fits two widgets and scrolls instead of clipping extra tiles");
            Check(homeFocusToggle != null && !focus.Running, "Focus widget uses the existing timer");
            homeFocusToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(focus.Running && (string)homeFocusToggle.Content == "Ⅱ", "Home timer button starts the shared timer and reflects its state");
            SwitchPage("Фокус"); Check(focus.Running, "Opening the full focus page does not reset the widget timer");
            SwitchPage("Головна"); homeFocusToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!focus.Running, "Returning home keeps the same timer and can pause it");
            NewHomeShortcut("web", "Моя музика", "example.com/music"); await Task.Delay(250);
            Check(shortcutNameBox!.GetRectFromCharacterIndex(0).Height >= 12, "Single-line name field reserves enough vertical space for readable text");
            Capture(Path.Combine(directory, "home-add-site.png")); await SaveHomeShortcut();
            Check(settings.Home.Items.Any(i => i.Name == "Моя музика" && i.Target == "https://example.com/music"), "Website form adds a normalized pinned tile");
            var folder = Path.Combine(directory, "Папка для тесту"); Directory.CreateDirectory(folder);
            NewHomeShortcut("folder", "Мої файли", folder); await SaveHomeShortcut();
            Check(settings.Home.Items.Any(i => i.Target == folder), "Folder form saves a Unicode local folder target");
            var pinned = settings.Home.Items.First(i => i.Target == folder); EditHomeShortcut(pinned);
            shortcutNameBox!.Text = "Проєкти"; await SaveHomeShortcut();
            Check(settings.Home.Items.Single(i => i.Id == pinned.Id).Name == "Проєкти", "Editing a pinned tile keeps its identity and updates its name");
            NewHomeShortcut("web", "Bad", "javascript:alert(1)"); await SaveHomeShortcut();
            Check(shortcutError!.Text.Length > 0 && !settings.Home.Items.Any(i => i.Name == "Bad"), "Invalid website stays in the form with a visible error");
            RebuildHome("edit"); await Task.Delay(250); Capture(Path.Combine(directory, "home-editor.png"));
            var last = settings.Home.Items[^1].Id;
            ChangeHome(layout => layout.MoveTo(last, 0));
            Check(settings.Home.Items[0].Id == last, "Layout edit moves the selected tile");
            UndoHome(); Check(settings.Home.Items[^1].Id == last, "Undo restores the previous layout");
            settings.Home.AddWidget("note"); settings.Home.AddWidget("volume"); settings.Note = "Маленька думка на сьогодні\nЗберегти те, що важливо.";
            volumeService = new AudioVolumeService(); volumeService.Changed += UpdateVolumeControls; volumeService.Start();
            for (var i = 0; i < 30 && !volumeService.State.Available; i++) await Task.Delay(100);
            RebuildHome("dashboard"); await Task.Delay(300); homeScroll!.ScrollToBottom(); await Task.Delay(150);
            Capture(Path.Combine(directory, "home-widgets.png"));
            Check(homeNote?.Text == settings.Note && homeVolume != null, "Optional note and volume widgets render from shared app data");
            Check(volumeService.State.Available && Math.Abs(homeVolume!.Value - volumeService.State.Level * 100) < .2, "Home volume widget reflects the real Windows endpoint without changing it");
            var path = Path.Combine(directory, "home-integration-settings.json"); settings.Save(path); var savedIds = settings.Home.Items.Select(i => i.Id).ToArray();
            settings.Home = Settings.Load(path).Home; RebuildHome("dashboard");
            Check(settings.Home.Items.Select(i => i.Id).SequenceEqual(savedIds), "Rebuilding from saved settings preserves all tile identities and order");
            var apps = await AppCatalog.ReadAsync(); Check(apps.All(a => Path.IsPathFullyQualified(a.Path)), "Start-menu catalog returns absolute shortcut paths");
            report.Add($"INFO Start-menu shortcuts available: {apps.Count}; no pinned targets were launched.");
            homeAddType = "app"; RebuildHome("add"); await Task.Delay(700); Capture(Path.Combine(directory, "home-app-picker.png"));
            settings.Home.Items.Clear(); RebuildHome("dashboard"); await Task.Delay(250); Capture(Path.Combine(directory, "home-empty.png"));
            Check(homeScroll!.Content is System.Windows.Controls.WrapPanel empty && empty.Children.Count == 1, "An empty layout offers an add action without restoring unwanted defaults");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "home-integration.txt"), report); Close(); }
    }
}

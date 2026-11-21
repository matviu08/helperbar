using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task<bool> CheckSettingsNavigation(string directory)
    {
        var report = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var item in Descendants<T>(child)) yield return item;
            }
        }
        try
        {
            var before = JsonSerializer.SerializeToElement(settings);
            OpenSettings("appearance"); await Task.Delay(350);
            Check(tabs.Children.OfType<Button>().Any(b => Equals(b.Content, "Налаштування")) && tabs.ActualWidth <= expanded.ActualWidth,
                "Settings is named clearly in the top navigation and all tabs fit the island");
            foreach (var tab in tabs.Children.OfType<Button>())
            {
                tab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                await Task.Delay(180);
                var bounds = tab.TransformToAncestor(tabs).TransformBounds(new Rect(tab.RenderSize));
                Check(bounds.Left >= -.1 && bounds.Right <= tabs.ActualWidth + .1 && bounds.Top >= -.1 && bounds.Bottom <= tabs.ActualHeight + .1,
                    tab.Content + ": hovering stays within the navigation row");
                Check(Descendants<TextBlock>(tab).All(t => t.ActualWidth + .5 >= t.DesiredSize.Width), tab.Content + ": tab label fits completely");
                tab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            }
            foreach (var section in settingsSections)
            {
                settingsNavigation[section.Id].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(320);
                Check(visibleSettingsSection == section.Id && settingsScroll!.ScrollableWidth == 0 && settingsTitle!.Text == section.Title,
                    section.Id + ": navigation opens the intended section without horizontal scrolling");
                var content = (FrameworkElement)settingsScroll!.Content;
                foreach (var text in Descendants<TextBlock>(content))
                    Check(text.ActualWidth + text.Margin.Left + text.Margin.Right + .5 >= text.DesiredSize.Width, section.Id + ": label fits: " + text.Text.Replace("\n", " "));
                Capture(Path.Combine(directory, "settings-" + section.Id + ".png"));
                var navTop = settingsNavigation["appearance"].TranslatePoint(new Point(), pageHost);
                settingsScroll.ScrollToBottom(); await Task.Delay(180);
                Check(settingsNavigation.Values.All(b => b.TranslatePoint(new Point(0, b.ActualHeight), pageHost).Y <= pageHost.ActualHeight + .5)
                    && settingsNavigation["appearance"].TranslatePoint(new Point(), pageHost) == navTop,
                    section.Id + ": every sidebar entry remains visible while the detail scrolls");
                Capture(Path.Combine(directory, "settings-" + section.Id + "-bottom.png"));
            }
            var after = JsonSerializer.SerializeToElement(settings);
            Check(before.EnumerateObject().Where(p => p.Name is not ("LastPage" or "SettingsSection"))
                    .All(p => p.Value.GetRawText() == after.GetProperty(p.Name).GetRawText()),
                "Browsing all sections preserves every existing preference, note and Home item");
            SelectSettingsSection("music"); await Task.Delay(200); settingsScroll!.ScrollToVerticalOffset(90); await Task.Delay(100);
            var offset = settingsScroll.VerticalOffset;
            SelectSettingsSection("general"); await Task.Delay(100); SelectSettingsSection("music"); await Task.Delay(200);
            Check(offset > 0 && Math.Abs(settingsScroll.VerticalOffset - offset) < 1, "Each section remembers its own scroll position");
            var compact = Descendants<CheckBox>((DependencyObject)settingsScroll.Content).Single(b => AutomationProperties.GetName(b) == "Музика у згорнутому острові");
            compact.IsChecked = false;
            SelectSettingsSection("appearance"); SelectSettingsSection("music"); await Task.Delay(100);
            Check(!settings.CompactMusic && Descendants<CheckBox>((DependencyObject)settingsScroll.Content)
                .Single(b => AutomationProperties.GetName(b) == "Музика у згорнутому острові").IsChecked == false,
                "A music toggle still updates its setting and retains the value after navigation");
            settings.Scale = 1.15; settings.TopOffset = 47; settings.AudioSensitivity = 1.3; settings.OpenOnHover = true;
            ResetAppearance();
            Check(settings.Scale == 1.15 && settings.TopOffset == 47 && settings.AudioSensitivity == 1.3 && !settings.CompactMusic && settings.OpenOnHover,
                "Reset appearance preserves music, placement and general preferences");
            settings.Scale = 1; settings.TopOffset = 12; SetPosition();
            OpenSettings("fullscreen"); fullscreenEyesButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(settings.FullscreenEyes && fullscreenChoices!.IsEnabled, "Full-screen mode selection works inside its own section");
            fullscreenEnabledBox!.IsChecked = false;
            Check(!settings.FullscreenEnabled && !fullscreenChoices!.IsEnabled, "Disabling full-screen automation disables its mode choices");
            settings.Home.AddWidget("devices"); SwitchPage("Головна"); await Task.Delay(200);
            Descendants<Button>(pageHost).Single(b => AutomationProperties.GetAutomationId(b) == "DeviceSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(visibleSettingsSection == "devices" && batteryEnabledBox?.IsChecked == true, "The Home device widget opens device settings directly");
            SwitchPage("Нотатки"); SwitchPage("Вигляд");
            Check(visibleSettingsSection == "devices", "Returning from another top-level tab restores the selected settings section");
            var path = Path.Combine(directory, "settings-navigation.json"); settings.Save(path);
            Check(Settings.Load(path).SettingsSection == "devices", "The selected settings section survives saving and reloading");
            File.WriteAllText(path, "{\"SettingsSection\":\"removed-section\",\"LastPage\":\"Вигляд\",\"Note\":\"Зберегти\"}");
            var fallback = Settings.Load(path);
            Check(fallback.SettingsSection == "appearance" && fallback.LastPage == "Вигляд" && fallback.Note == "Зберегти",
                "Unknown sections safely fall back while keeping the old page key and user data");
            return true;
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; return false; }
        finally { File.WriteAllLines(Path.Combine(directory, "settings-navigation.txt"), report); }
    }
}

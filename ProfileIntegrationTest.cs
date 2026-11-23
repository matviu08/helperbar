using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunProfileIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory); var report = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); report.Add("PASS " + message); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Picture(string name) { await Task.Delay(380); UpdateLayout(); Capture(Path.Combine(directory, name)); }
        IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T element) yield return element; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
        }
        try
        {
            settings.TimerSound = false; settings.Note = "Спільна нотатка"; settings.Todos.Add("Спільне завдання"); settings.Files.Add(Path.Combine(directory, "shared-reference.txt"));
            focus.Toggle(DateTimeOffset.UtcNow); var reminder = settings.Reminders.Add("Спільний відлік", TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow)!; var deadline = reminder.Deadline;
            var personalIds = settings.Home.Items.Select(x => x.Id).ToArray(); var personalAccent = settings.Accent;
            ResetPosition(); SetExpanded(true); SwitchPage("Головна"); await Picture("profiles-home-personal.png");
            Check(homeProfileButton != null && homeFilesButton != null && homeProfileButton.ActualWidth >= 83 && homeProfileButton.TransformToAncestor(pageHost).Transform(new Point(homeProfileButton.ActualWidth, 0)).X < homeFilesButton.TransformToAncestor(pageHost).Transform(new Point()).X, "The profile switcher and file shelf both fit in the existing home toolbar");
            Click(homeProfileButton!); await Picture("profiles-picker.png");
            Check(homeView == "profiles" && profileChoices.Count == 4 && homeScroll!.ScrollableHeight < 1, "All four profiles are visible together without enlarging the island");
            Click(profileChoices["work"]); await Picture("profiles-work.png");
            Check(settings.Profiles.ActiveId == "work" && homeView == "dashboard" && homeTodoTitle != null && homeFocusTime != null && homeNote?.Text == settings.Note, "Work selection opens its own task/focus dashboard backed by shared content");
            Check(((SolidColorBrush)Resources["AccentBrush"]).Color == (Color)ColorConverter.ConvertFromString(settings.Accent) && !Ui.ReducedMotion, "Switching updates WPF accent resources and motion preferences immediately");
            ChangeHome(layout => layout.AddWidget("music")); var workIds = settings.Home.Items.Select(x => x.Id).ToArray(); Check(homeUndo != null, "Editing a profile creates a local layout undo");
            settings.Accent = "#FFC69E"; settings.MotionIntensity = .27; settings.ReducedMotion = true; settings.NotifyLowBattery = false;
            Click(homeProfileButton!); Click(profileChoices["music"]); await Picture("profiles-music.png");
            Check(homeUndo == null && settings.Home.Items.Any(x => x.Kind == "volume") && settings.Accent != "#FFC69E" && settings.NotifyLowBattery && !settings.ReducedMotion && homeMusicTitle != null, "Music has independent presentation and clears undo from the previous layout");
            UndoHome(); Check(settings.Home.Items.Count == 4, "Undo in another profile cannot replace its widgets with the previous profile's layout");
            Click(homeProfileButton!); Click(profileChoices["work"]);
            Check(settings.Accent == "#FFC69E" && settings.MotionIntensity == .27 && Ui.ReducedMotion && !settings.NotifyLowBattery && settings.Home.Items.Select(x => x.Id).SequenceEqual(workIds), "Returning to work restores unsaved style edits, notification preferences and widget identities");
            OpenSettings("profiles"); await Picture("profiles-settings.png");
            Check(visibleSettingsSection == "profiles" && settingsNavigation.Count == 7 && settingsNavigation.Values.All(x => x.TransformToAncestor(pageHost).Transform(new Point(0, x.ActualHeight)).Y <= pageHost.ActualHeight + 1), "The new profiles section and all existing settings sections fit in the sidebar");
            Click(profileChoices["games"]); await Picture("profiles-settings-games.png");
            Check(currentPage == "Вигляд" && visibleSettingsSection == "profiles" && settings.Profiles.ActiveId == "games" && !settings.TimerSound && !settings.NotifyFocusBanner, "Changing a profile from settings keeps that section open and applies quiet game preferences");
            var taskToggle = Descendants<CheckBox>(settingsScroll!).Single(x => AutomationProperties.GetName(x) == "Картка виконаної справи"); taskToggle.IsChecked = true;
            var task = settings.Todos.Items[0]; ToggleTodo(task.Id); Check(task.Done && surface.Toast.Contains(task.Title), "The profile's task-card switch takes effect on real task completion");
            taskToggle.IsChecked = false; Check(!surface.Toast.Contains(task.Title), "Disabling task cards removes an obsolete visible card immediately");
            ToggleTodo(task.Id); ToggleTodo(task.Id); Check(task.Done && surface.Mood == "celebrate" && !surface.Toast.Contains(task.Title), "Muted task completion keeps the eye reaction and shared task state");
            var timingNotice = new ReminderNotice(reminder.Id, reminder.NoticeKey, reminder.Title, reminder.DurationSeconds);
            surface.NotifyReminder(timingNotice); SwitchProfile("personal");
            Check(surface.ReminderToast?.Id == reminder.Id, "Switching a profile does not discard a pending timer completion card");
            Check(settings.Home.Items.Select(x => x.Id).SequenceEqual(personalIds) && settings.Accent == personalAccent && settings.Note == "Спільна нотатка" && settings.Files.Items.Count == 1 && focus.Running && reminder.Deadline == deadline, "Personal layout, notes, file shelf, focus and reminder deadlines survive the complete profile round trip");
            OpenSettings("appearance"); await Task.Delay(100);
            var strength = Descendants<Slider>(settingsScroll!).Single(x => AutomationProperties.GetName(x) == "Сила декоративного руху"); strength.Value = 0;
            surface.React("celebrate"); Check(settings.MotionIntensity == 0 && surface.ParticleCount == 0, "The real decoration slider suppresses particles at zero without disabling the island");
            settings.Profiles.CaptureActive(settings); var otherBefore = System.Text.Json.JsonSerializer.Serialize(settings.Profiles.Items.Where(x => x.Id != "personal")); ResetAppearance(); settings.Profiles.CaptureActive(settings);
            Check(settings.MotionIntensity == 1 && System.Text.Json.JsonSerializer.Serialize(settings.Profiles.Items.Where(x => x.Id != "personal")) == otherBefore, "Reset appearance affects only the current profile");
            SwitchProfile("games"); SwitchPage("Головна"); await Picture("profiles-games.png");
            Check(!settings.AudioReactive && homeVolume != null && ramLabel != null && settings.FullscreenEyes, "The game dashboard includes system/volume widgets and quiet fullscreen eyes");
            ReceiveFiles(new[] { Path.Combine(directory, "same-shared-file.txt") }); Check(FilesPanelOpen, "File dropping remains available within a preset profile"); CloseFilesPanel();
            var savePath = Path.Combine(directory, "profiles-integration-settings.json"); settings.Save(savePath); var restored = Settings.Load(savePath);
            Check(restored.Profiles.ActiveId == "games" && !restored.TimerSound && restored.Files.Items.Count == 1 && restored.Todos.Items[0].Done && restored.Note == settings.Note, "Saving from a preset preserves the selected mode and shared content");
            restored.Profiles.Switch(restored, "work"); Check(restored.Accent == "#FFC69E" && restored.Home.Items.Select(x => x.Id).SequenceEqual(workIds), "Inactive customizations remain available after loading the settings file");
            report.Add("INFO Test settings, tasks and file references are synthetic. Audio output, real playback, Windows volume and user content were not changed.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "profile-integration.txt"), report); Close(); }
    }
}

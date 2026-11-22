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
    private async Task RunReminderIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); report.Add("PASS " + label); }
        async Task WaitFor(Func<bool> predicate)
        { for (var i = 0; i < 80 && !predicate(); i++) await Task.Delay(100); if (!predicate()) throw new TimeoutException("Reminder state did not converge."); }
        Button FindButton(DependencyObject parent, string name)
        {
            Button? Find(DependencyObject node)
            {
                if (node is Button button && AutomationProperties.GetName(button) == name) return button;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = Find(VisualTreeHelper.GetChild(node, i)); if (child != null) return child; }
                return null;
            }
            return Find(parent) ?? throw new InvalidOperationException("Button missing: " + name);
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Picture(string name)
        { await Task.Delay(350); surface.InvalidateVisual(); UpdateLayout(); Capture(Path.Combine(directory, name)); }
        try
        {
            settings.TimerSound = false; ResetPosition(); SetExpanded(true); SwitchPage("Фокус"); await Task.Delay(550);
            Click(timerToggle!);
            Check(focus.Running && timerToggle!.TransformToAncestor(pageHost).Transform(new Point(0, timerToggle.ActualHeight)).Y <= pageHost.ActualHeight + 1, "Original focus timer still starts and fits below the new section navigation");
            await Picture("timers-focus.png");
            Click(timersSectionButton!); await Task.Delay(200);
            Check(settings.FocusView == "timers" && focus.Running && settings.Reminders.Items.Count == 0, "Opening reminders keeps the independent focus timer running");
            await Picture("timers-empty.png");
            Click(FindButton(focusSection!, "＋ Свій таймер")); await Task.Delay(100);
            Click(reminderStartButton!);
            Check(settings.Reminders.Items.Count == 0 && reminderError!.Text.Contains("назву"), "An empty title is rejected inline without creating a timer");
            reminderTitleInput!.Text = "Чай · тест"; reminderDurationInput!.Text = "пізніше"; Click(reminderStartButton!);
            Check(settings.Reminders.Items.Count == 0 && reminderError!.Text.Contains("Вкажи час"), "Invalid duration is rejected with an actionable message");
            reminderDurationInput.Text = "5 хв"; await Picture("timers-form.png");
            Check(reminderError!.Text.Length == 0, "Editing a field clears obsolete validation feedback");
            Check(reminderStartButton!.TransformToAncestor(pageHost).Transform(new Point(0, reminderStartButton.ActualHeight)).Y <= pageHost.ActualHeight + 1, "Custom name, duration, feedback and submit controls fit the page");
            Click(reminderStartButton); await Task.Delay(200);
            var tea = settings.Reminders.Items.Single();
            Check(tea.Title == "Чай · тест" && tea.DurationSeconds == 300 && tea.Status == ReminderStatus.Running, "The form starts a real named five-minute countdown");
            Click(FindButton(focusSection!, "Перерва · 1 год")); await Task.Delay(150);
            Check(settings.Reminders.Items.Count == 2 && settings.Reminders.Items[1].DurationSeconds == 3600 && focus.Running, "Quick presets add an independent hour-long reminder without resetting focus");
            Click(reminderViews[tea.Id].Toggle); await Task.Delay(120);
            var paused = tea.Remaining(DateTimeOffset.UtcNow); await Task.Delay(300);
            Check(tea.Status == ReminderStatus.Paused && tea.Remaining(DateTimeOffset.UtcNow) == paused && settings.Reminders.Items[1].Status == ReminderStatus.Running, "The row pause button freezes only the selected timer");
            await Picture("timers-list.png");
            Click(reminderViews[tea.Id].Toggle); Check(tea.Status == ReminderStatus.Running, "The same row resumes the remaining countdown");
            var oldKey = tea.NoticeKey; Click(FindButton(reminderRows!, "Почати заново: " + tea.Title));
            Check(tea.NoticeKey != oldKey && tea.Remaining(DateTimeOffset.UtcNow).TotalSeconds > 298, "Restart restores the full duration and creates a new completion identity");
            var savePath = Path.Combine(directory, "timers-persistence.json"); settings.Save(savePath); var saved = Settings.Load(savePath);
            Check(saved.Reminders.Items.Count == 2 && saved.Reminders.Items[0].Deadline == tea.Deadline && saved.FocusView == "timers", "Running deadlines and the selected section survive a settings round-trip");
            Click(FindButton(reminderRows!, "Видалити: Перерва")); Check(settings.Reminders.Items.Count == 1 && settings.Reminders.Items[0].Id == tea.Id, "Delete removes only its own timer");
            SetExpanded(false); OnSecond(null, EventArgs.Empty); await Picture("timers-compact.png");
            Check(surface.TimerIsReminder && surface.TimerTitle == tea.Title && surface.TimerText != FocusTimer.Format(focus.Remaining(DateTimeOffset.Now)), "Compact island shows the nearest reminder without replacing the focus countdown");
            foreach (var timer in settings.Reminders.Items.ToArray()) ChangeReminder(timer.Id, "remove");
            var first = settings.Reminders.Add("Чай готовий · тест", TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow)!;
            var second = settings.Reminders.Add("Час на перерву · тест", TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow)!;
            var foreground = FullscreenDetector.GetForegroundWindow();
            await WaitFor(() => first.Status == ReminderStatus.Completed && second.Status == ReminderStatus.Completed && !first.AlertPending);
            Check(surface.ReminderToast?.Id == first.Id && second.AlertPending && !surface.Expanded && FullscreenDetector.GetForegroundWindow() == foreground, "Real dispatcher ticks show the first completion without stealing focus and keep the second queued");
            await Picture("timers-completed-card.png");
            Check(Math.Abs(surface.IslandWidth.Value - 396) < 3 && Math.Abs(surface.IslandHeight.Value - 76) < 3 && surface.Mood == "celebrate", "Completion expands a compact card and triggers the celebration eyes");
            settings.Save(savePath); saved = Settings.Load(savePath);
            Check(!saved.Reminders.Items[0].AlertPending && saved.Reminders.Items[1].AlertPending, "A shown alert is acknowledged while an unseen queued alert remains durable");
            await WaitFor(() => surface.ReminderToast?.Id == second.Id && !second.AlertPending);
            Check(true, "The second simultaneous completion is delivered automatically after the first");
            foreach (var timer in settings.Reminders.Items.ToArray()) ChangeReminder(timer.Id, "remove");
            surface.DeferNotifications(true);
            var deferred = settings.Reminders.Add("Після гри · тест", TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow.AddSeconds(-3))!;
            OnSecond(null, EventArgs.Empty); await Task.Delay(200);
            Check(deferred.Status == ReminderStatus.Completed && deferred.AlertPending && surface.ReminderToast == null, "Full-screen notification deferral preserves completion without acknowledging an unseen card");
            surface.DeferNotifications(false); await WaitFor(() => !deferred.AlertPending && surface.ReminderToast?.Id == deferred.Id);
            Check(true, "Returning from quiet mode displays and acknowledges the deferred reminder");
            settings.Note = "Нотатку не змінювати"; SetExpanded(true); SwitchPage("Нотатки"); await Picture("timers-expanded-card.png");
            Check(currentPage == "Нотатки" && settings.Note == "Нотатку не змінювати" && surface.ReminderToast != null, "The expanded header shows the reminder without replacing the current page or note");
            ChangeReminder(deferred.Id, "restart"); Check(surface.ReminderToast == null && deferred.Status == ReminderStatus.Running, "Restart dismisses the obsolete completion card");
            foreach (var timer in settings.Reminders.Items.ToArray()) ChangeReminder(timer.Id, "remove");
            for (var i = 0; i < ReminderTimers.Limit; i++) settings.Reminders.Add("Довга назва нагадування для тесту · " + i, TimeSpan.FromHours(2 + i), DateTimeOffset.UtcNow);
            SwitchPage("Фокус"); SelectFocusSection("timers"); await Task.Delay(250); reminderScroll!.ScrollToBottom(); await Picture("timers-many.png");
            Check(reminderScroll.ExtentHeight > reminderScroll.ViewportHeight && reminderRows!.Children.OfType<Border>().All(x => x.ActualWidth <= pageHost.ActualWidth), "Twelve long-named timers scroll within the existing island width");
            AddReminder("Зайвий", TimeSpan.FromMinutes(1)); Check(settings.Reminders.Items.Count == ReminderTimers.Limit, "A full timer list rejects extra timers without losing existing reminders");
            SwitchPage("Головна"); Check(focus.Running && settings.Reminders.Items.Count == ReminderTimers.Limit, "Navigation leaves focus and every reminder running");
            report.Add("INFO Reminder fixtures used a private test Settings object; user settings and timers were not modified.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "reminder-integration.txt"), report); Close(); }
    }
}

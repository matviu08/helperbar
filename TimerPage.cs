using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private Grid? focusSection;
    private Button? focusSectionButton, timersSectionButton;
    private StackPanel? reminderRows;
    private ScrollViewer? reminderScroll;
    private TextBlock? reminderSummary, reminderError;
    private TextBox? reminderTitleInput, reminderDurationInput;
    private Button? reminderStartButton;
    private bool reminderForm;
    private readonly Dictionary<string, (TextBlock Time, TextBlock Status, Button Toggle)> reminderViews = new();
    private readonly HashSet<string> queuedReminders = new(StringComparer.Ordinal);

    private void ClearFocusPage()
    {
        focusSection = null; focusSectionButton = timersSectionButton = null;
        ClearReminderView();
    }
    private void ClearReminderView()
    {
        reminderRows = null; reminderScroll = null; reminderSummary = reminderError = null;
        reminderTitleInput = reminderDurationInput = null; reminderStartButton = null; reminderViews.Clear();
    }
    private FrameworkElement FocusPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); root.RowDefinitions.Add(new RowDefinition());
        focusSectionButton = Ui.Button("Зосередитись", () => SelectFocusSection("focus"), scaleOnHover: false);
        timersSectionButton = Ui.Button("Таймери", () => SelectFocusSection("timers"), scaleOnHover: false);
        foreach (var button in new[] { focusSectionButton, timersSectionButton })
        { button.SetResourceReference(StyleProperty, "NavigationTab"); button.MinHeight = 28; button.Margin = new Thickness(0, 0, 0, 6); }
        root.Children.Add(Ui.Columns(focusSectionButton, timersSectionButton, 6));
        focusSection = new Grid { Margin = new Thickness(0, 3, 0, 0) }; Grid.SetRow(focusSection, 1); root.Children.Add(focusSection);
        RenderFocusSection(); return root;
    }
    private void SelectFocusSection(string section)
    {
        settings.FocusView = section; reminderForm = false; Persist(); RenderFocusSection();
    }
    private void RenderFocusSection()
    {
        if (focusSection == null) return;
        timerLabel = timerStateLabel = null; timerToggle = null; progressFill = null; ClearReminderView();
        focusSection.Children.Clear();
        focusSectionButton!.Tag = settings.FocusView == "focus" ? "active" : "inactive";
        timersSectionButton!.Tag = settings.FocusView == "timers" ? "active" : "inactive";
        AutomationProperties.SetItemStatus(focusSectionButton, settings.FocusView == "focus" ? "Вибрано" : "");
        AutomationProperties.SetItemStatus(timersSectionButton, settings.FocusView == "timers" ? "Вибрано" : "");
        focusSection.Children.Add(settings.FocusView == "focus" ? FocusSessionPage() : reminderForm ? ReminderForm() : RemindersPage());
        OnSecond(null, EventArgs.Empty);
    }
    private FrameworkElement RemindersPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); root.RowDefinitions.Add(new RowDefinition());
        reminderSummary = Ui.Text("Твої маленькі нагадування", 11, "#BCAFCF");
        var add = HomeSmallButton("＋ Свій таймер", () => { reminderForm = true; RenderFocusSection(); reminderTitleInput?.Focus(); }); add.MinHeight = 27;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(add, Dock.Right); header.Children.Add(add); header.Children.Add(reminderSummary); root.Children.Add(header);
        var quick = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, minutes, label) in new[] { ("Чай", 5, "Чай · 5 хв"), ("Перерва", 60, "Перерва · 1 год"), ("Розім’ятись", 30, "Рух · 30 хв") })
        {
            var button = HomeSmallButton(label, () => AddReminder(title, TimeSpan.FromMinutes(minutes)));
            button.MinHeight = 26; button.Margin = new Thickness(0, 0, 6, 7); button.Padding = new Thickness(10, 3, 10, 3); quick.Children.Add(button);
        }
        Grid.SetRow(quick, 1); root.Children.Add(quick);
        reminderRows = new StackPanel();
        reminderScroll = new ScrollViewer { Content = reminderRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(reminderScroll, 2); root.Children.Add(reminderScroll); RebuildReminderRows(); return root;
    }
    private void RebuildReminderRows()
    {
        if (reminderRows == null) return;
        var offset = reminderScroll?.VerticalOffset ?? 0;
        reminderViews.Clear(); reminderRows.Children.Clear();
        foreach (var timer in settings.Reminders.Items.OrderBy(x => x.Status == ReminderStatus.Completed ? 1 : 0))
        {
            var title = HomeLine(timer.Title, 12, "#EDE3F8", true); title.ToolTip = timer.Title;
            var status = HomeLine("", 9, "#A99AB9");
            var labels = Ui.Column(title, Ui.Gap(4), status); labels.VerticalAlignment = VerticalAlignment.Center;
            var time = Ui.Text("", 18, timer.Status == ReminderStatus.Completed ? "#A5DEC5" : "#D4C1F0", true); time.HorizontalAlignment = HorizontalAlignment.Right;
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Button ActionButton(string glyph, Action action, string tip)
            { var button = Ui.Button(glyph, action, tip, scaleOnHover: false); button.Width = 27; button.Height = button.MinHeight = 27; button.Padding = new Thickness(0); button.Margin = new Thickness(4, 0, 0, 0); button.BorderThickness = new Thickness(0); return button; }
            var toggle = ActionButton("Ⅱ", () => ChangeReminder(timer.Id, "toggle"), "Пауза");
            var restart = ActionButton("↺", () => ChangeReminder(timer.Id, "restart"), "Почати заново: " + timer.Title);
            var remove = ActionButton("×", () => ChangeReminder(timer.Id, "remove"), "Видалити: " + timer.Title);
            controls.Children.Add(toggle); controls.Children.Add(restart); controls.Children.Add(remove);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(103) });
            row.Children.Add(labels); Grid.SetColumn(time, 1); row.Children.Add(time); Grid.SetColumn(controls, 2); row.Children.Add(controls);
            reminderRows.Children.Add(new Border { Child = row, Height = 60, Padding = new Thickness(12, 8, 10, 8), CornerRadius = new CornerRadius(13), Background = Ui.Brush(timer.Status == ReminderStatus.Completed ? "#1C2827" : "#211B2B"), Margin = new Thickness(0, 0, 3, 6) });
            reminderViews[timer.Id] = (time, status, toggle);
        }
        if (settings.Reminders.Items.Count == 0)
        {
            var empty = Ui.Column(Ui.Text("Чай не забудеться ☺", 17, "#E4D6F4", true), Ui.Gap(7), Ui.Text("Запусти швидкий таймер вище або створи свій.\nКожен відлік працює незалежно.", 11, "#A99AB9"));
            empty.Margin = new Thickness(15, 17, 10, 0); reminderRows.Children.Add(empty);
        }
        UpdateReminderLabels(DateTimeOffset.UtcNow); reminderScroll?.ScrollToVerticalOffset(offset);
    }
    private FrameworkElement ReminderForm()
    {
        var heading = Ui.Text("Про що нагадати?", 16, "#EEE4F8", true);
        reminderTitleInput = new TextBox { MaxLength = 40, Height = 34, Padding = new Thickness(10, 7, 10, 7) };
        reminderDurationInput = new TextBox { Text = "5 хв", MaxLength = 40, Height = 34, Padding = new Thickness(10, 7, 10, 7) };
        AutomationProperties.SetName(reminderTitleInput, "Назва таймера"); AutomationProperties.SetName(reminderDurationInput, "Тривалість таймера");
        var fields = Ui.Columns(Ui.Column(Ui.Text("НАЗВА", 9, "#A99AB9"), Ui.Gap(4), reminderTitleInput), Ui.Column(Ui.Text("ЧЕРЕЗ СКІЛЬКИ", 9, "#A99AB9"), Ui.Gap(4), reminderDurationInput), 10, 2, 1);
        var hint = Ui.Text("Наприклад: 5 хв, 1 год 30 хв або 30 с. До 24 годин.", 10, "#9D8EAE");
        reminderError = Ui.Text("", 10, "#F1B49D"); reminderError.MinHeight = 28; reminderError.MaxHeight = 32;
        reminderTitleInput.TextChanged += (_, _) => { if (reminderError != null) reminderError.Text = ""; };
        reminderDurationInput.TextChanged += (_, _) => { if (reminderError != null) reminderError.Text = ""; };
        reminderStartButton = Ui.Button("▶  Запустити", SubmitReminder, primary: true);
        var cancel = Ui.Button("Назад", () => { reminderForm = false; RenderFocusSection(); });
        var form = Ui.Column(heading, Ui.Gap(9), fields, Ui.Gap(8), hint, Ui.Gap(2), reminderError, Ui.Columns(reminderStartButton, cancel));
        form.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { SubmitReminder(); e.Handled = true; } };
        return form;
    }
    private void SubmitReminder()
    {
        if (reminderTitleInput == null || reminderDurationInput == null) return;
        if (string.IsNullOrWhiteSpace(reminderTitleInput.Text)) { reminderError!.Text = "Додай коротку назву — наприклад, Чай."; reminderTitleInput.Focus(); return; }
        if (!ReminderTimers.TryDuration(reminderDurationInput.Text, out var duration)) { reminderError!.Text = "Вкажи час від 1 с до 24 год: 5 хв або 1 год 30 хв."; reminderDurationInput.Focus(); return; }
        AddReminder(reminderTitleInput.Text, duration);
    }
    private void AddReminder(string title, TimeSpan duration)
    {
        var timer = settings.Reminders.Add(title, duration, DateTimeOffset.UtcNow);
        if (timer == null)
        {
            const string error = "Уже є 12 таймерів. Видали непотрібний і спробуй ще раз.";
            if (reminderError != null) reminderError.Text = error; else surface.Notify(error, "curious");
            return;
        }
        reminderForm = false; SaveNow(); RenderFocusSection(); surface.React("happy", .8);
    }
    private void ChangeReminder(string id, string action)
    {
        var now = DateTimeOffset.UtcNow;
        var changed = action switch { "toggle" => settings.Reminders.Toggle(id, now), "restart" => settings.Reminders.Restart(id, now), "remove" => settings.Reminders.Remove(id), _ => false };
        if (!changed) return;
        surface.RemoveReminder(id); SaveNow(); RebuildReminderRows(); OnSecond(null, EventArgs.Empty);
    }
    private void ProcessReminders(DateTimeOffset now)
    {
        if (!loadedOnce) return;
        var changed = settings.Reminders.Advance(now);
        queuedReminders.RemoveWhere(key => !settings.Reminders.Items.Any(x => x.NoticeKey == key && x.AlertPending));
        foreach (var timer in settings.Reminders.Items.Where(x => x.AlertPending))
            if (queuedReminders.Add(timer.NoticeKey)) surface.NotifyReminder(ReminderNotice.From(timer));
        if (changed) { SaveNow(); RebuildReminderRows(); }
        UpdateReminderLabels(now);
    }
    private void OnReminderShown(ReminderNotice notice)
    {
        if (!settings.Reminders.Acknowledge(notice.Key)) return;
        SaveNow();
        if (settings.TimerSound && !smokeTest) System.Media.SystemSounds.Asterisk.Play();
    }
    private void UpdateReminderLabels(DateTimeOffset now)
    {
        var active = settings.Reminders.Items.Count(x => x.Status == ReminderStatus.Running);
        if (reminderSummary != null) reminderSummary.Text = settings.Reminders.Items.Count == 0 ? "Твої маленькі нагадування" : $"Активних: {active}  ·  усього: {settings.Reminders.Items.Count}";
        if (timersSectionButton != null) timersSectionButton.Content = "Таймери" + (active > 0 ? " · " + active : "");
        foreach (var timer in settings.Reminders.Items)
        {
            if (!reminderViews.TryGetValue(timer.Id, out var view)) continue;
            view.Time.Text = timer.Status == ReminderStatus.Completed ? "Готово ✓" : ReminderTimers.Format(timer.Remaining(now));
            view.Status.Text = timer.Status switch { ReminderStatus.Running => "Відлік · " + ReminderTimers.DurationLabel(timer.DurationSeconds), ReminderStatus.Paused => "На паузі", _ => "Час вийшов · можна повторити" };
            view.Toggle.Content = timer.Status == ReminderStatus.Running ? "Ⅱ" : "▶";
            var action = timer.Status == ReminderStatus.Running ? "Призупинити" : timer.Status == ReminderStatus.Paused ? "Продовжити" : "Повторити";
            view.Toggle.ToolTip = action + ": " + timer.Title; AutomationProperties.SetName(view.Toggle, (string)view.Toggle.ToolTip);
        }
    }
}

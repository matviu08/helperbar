using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private Grid? notesSection;
    private Button? noteSectionButton, todoSectionButton, todoSubmit, todoAuxiliary;
    private TextBox? notesEditor, todoInput;
    private StackPanel? todoRows;
    private ScrollViewer? todoScroll;
    private TextBlock? todoFeedback, homeTodoTitle, homeTodoStatus;
    private Button? homeTodoDone;
    private readonly Dictionary<string, (Button Done, Button Pin, Button Edit, Button Remove)> todoButtons = new();
    private TodoList? todoUndo;
    private string todoDraft = "", todoMessage = "☆ — головне на сьогодні";
    private string? todoEditingId;
    private bool showCompletedTodos, todoError;
    private static DateOnly TodoToday => DateOnly.FromDateTime(DateTime.Now);

    private void ClearNotesPage()
    {
        notesSection = null; noteSectionButton = todoSectionButton = null;
        ClearNotesSection();
    }
    private void ClearNotesSection()
    {
        notesEditor = todoInput = null; noteStateLabel = todoFeedback = null;
        todoRows = null; todoScroll = null; todoSubmit = todoAuxiliary = null; todoButtons.Clear();
    }
    private FrameworkElement NotesPage()
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); root.RowDefinitions.Add(new RowDefinition());
        noteSectionButton = Ui.Button("Нотатка", () => SelectNotesSection("note"), scaleOnHover: false);
        todoSectionButton = Ui.Button("Справи", () => SelectNotesSection("tasks"), scaleOnHover: false);
        foreach (var button in new[] { noteSectionButton, todoSectionButton })
        { button.SetResourceReference(StyleProperty, "NavigationTab"); button.MinHeight = 28; button.Margin = new Thickness(0, 0, 0, 6); }
        root.Children.Add(Ui.Columns(noteSectionButton, todoSectionButton, 6));
        notesSection = new Grid { Margin = new Thickness(0, 3, 0, 0) }; Grid.SetRow(notesSection, 1); root.Children.Add(notesSection);
        RenderNotesSection(); return root;
    }
    private void OpenNotesSection(string section)
    { settings.NotesView = section; SwitchPage("Нотатки"); }
    private void SelectNotesSection(string section)
    { saveTimer.Stop(); settings.NotesView = section; SaveNow(); RenderNotesSection(); }
    private void RenderNotesSection()
    {
        if (notesSection == null) return;
        ClearNotesSection(); notesSection.Children.Clear();
        noteSectionButton!.Tag = settings.NotesView == "note" ? "active" : "inactive";
        todoSectionButton!.Tag = settings.NotesView == "tasks" ? "active" : "inactive";
        AutomationProperties.SetItemStatus(noteSectionButton, settings.NotesView == "note" ? "Вибрано" : "");
        AutomationProperties.SetItemStatus(todoSectionButton, settings.NotesView == "tasks" ? "Вибрано" : "");
        notesSection.Children.Add(settings.NotesView == "note" ? NotesEditorPage() : TodosPage()); UpdateTodoBadge();
    }
    private FrameworkElement TodosPage()
    {
        if (todoEditingId != null && !settings.Todos.Items.Any(x => x.Id == todoEditingId)) { todoEditingId = null; todoDraft = ""; }
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); root.RowDefinitions.Add(new RowDefinition());
        todoInput = new TextBox { Text = todoDraft, MaxLength = TodoList.TitleLimit, Padding = new Thickness(11, 7, 11, 7), Height = 34, ToolTip = "Додай справу й натисни Enter" };
        var placeholder = Ui.Text("Що хочеш зробити?", 12, "#897A99"); placeholder.Margin = new Thickness(12, 0, 0, 0); placeholder.IsHitTestVisible = false; placeholder.Visibility = todoDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(todoInput, "Текст справи");
        todoInput.TextChanged += (_, _) => { todoDraft = todoInput.Text; placeholder.Visibility = todoDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (todoError) { todoError = false; todoMessage = "☆ — головне на сьогодні"; UpdateTodoFeedback(); } };
        todoInput.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { SubmitTodo(); e.Handled = true; } };
        todoSubmit = Ui.Button("＋", SubmitTodo, "Додати справу", true, false); todoSubmit.Width = 38; todoSubmit.Height = todoSubmit.MinHeight = 34; todoSubmit.Padding = new Thickness(0); todoSubmit.FontSize = 18;
        var inputEditor = new Grid(); inputEditor.Children.Add(todoInput); inputEditor.Children.Add(placeholder);
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new ColumnDefinition()); inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); inputRow.Children.Add(inputEditor); Grid.SetColumn(todoSubmit, 1); todoSubmit.HorizontalAlignment = HorizontalAlignment.Right; inputRow.Children.Add(todoSubmit); root.Children.Add(inputRow);
        todoFeedback = HomeLine("", 10, "#AA9CBA");
        todoAuxiliary = HomeSmallButton("Скасувати", () => { if (todoEditingId != null) CancelTodoEdit(); else UndoTodo(); }); todoAuxiliary.MinHeight = 23; todoAuxiliary.Padding = new Thickness(6, 2, 6, 2); todoAuxiliary.BorderThickness = new Thickness(0);
        var feedback = new DockPanel(); DockPanel.SetDock(todoAuxiliary, Dock.Right); feedback.Children.Add(todoAuxiliary); feedback.Children.Add(todoFeedback); Grid.SetRow(feedback, 1); root.Children.Add(feedback);
        todoRows = new StackPanel(); todoScroll = new ScrollViewer { Content = todoRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(todoScroll, 2); root.Children.Add(todoScroll); RebuildTodoRows(); UpdateTodoFeedback(); return root;
    }
    private void RebuildTodoRows()
    {
        UpdateTodoBadge();
        if (todoRows == null) return;
        var rows = todoRows; var offset = todoScroll?.VerticalOffset ?? 0;
        var keyboardInside = rows.IsKeyboardFocusWithin; var focusTag = (Keyboard.FocusedElement as Button)?.Tag as string;
        rows.Children.Clear(); todoButtons.Clear();
        var pinned = settings.Todos.Pinned(TodoToday);
        void AddRow(TodoItem item)
        {
            var isPinned = pinned?.Id == item.Id;
            Button Small(string content, Action action, string label, string suffix)
            {
                var button = Ui.Button(content, action, label, scaleOnHover: false); button.Tag = item.Id + ":" + suffix;
                button.Width = 27; button.Height = button.MinHeight = 27; button.Padding = new Thickness(0); button.Margin = new Thickness(3, 0, 0, 0); button.BorderThickness = new Thickness(0); return button;
            }
            var done = Small(item.Done ? "✓" : "○", () => ToggleTodo(item.Id), item.Done ? "Повернути справу: " + item.Title : "Виконати: " + item.Title, "done");
            done.FontSize = item.Done ? 14 : 19; done.Foreground = Ui.Brush(item.Done ? "#A5DEC5" : "#D1BCE9"); done.Margin = new Thickness(0, 0, 9, 0); done.Width = 28;
            AutomationProperties.SetItemStatus(done, item.Done ? "Виконано" : "Не виконано");
            var pin = Small(isPinned ? "★" : "☆", () => ChangeTodo(list => list.TogglePin(item.Id, TodoToday), isPinned ? "Закріплення знято" : "Головне на сьогодні"), isPinned ? "Зняти закріплення: " + item.Title : "Головне на сьогодні: " + item.Title, "pin");
            pin.Foreground = Ui.Brush(isPinned ? "#D0BAF5" : "#92849F"); pin.IsEnabled = !item.Done;
            AutomationProperties.SetItemStatus(pin, isPinned ? "Закріплено на сьогодні" : "");
            var edit = Small("✎", () => EditTodo(item.Id), "Редагувати: " + item.Title, "edit");
            var remove = Small("×", () => { if (ChangeTodo(list => list.Remove(item.Id), "Справу видалено" ) && todoEditingId == item.Id) CancelTodoEdit(); }, "Видалити: " + item.Title, "remove");
            var title = HomeLine(item.Title, 12, item.Done ? "#8F839E" : "#EDE4F8", !item.Done); title.ToolTip = item.Title;
            if (item.Done) title.TextDecorations = TextDecorations.Strikethrough;
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (isPinned) { labels.Children.Add(Ui.Text("ГОЛОВНЕ НА СЬОГОДНІ", 8, "#C0A5E3", true)); labels.Children.Add(Ui.Gap(4)); }
            labels.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; actions.Children.Add(pin); actions.Children.Add(edit); actions.Children.Add(remove);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.Children.Add(done); Grid.SetColumn(labels, 1); row.Children.Add(labels); Grid.SetColumn(actions, 2); row.Children.Add(actions);
            rows.Children.Add(new Border { Child = row, Height = isPinned ? 61 : 46, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 3, 6), Background = Ui.Brush(isPinned ? "#33263F" : item.Done ? "#19171E" : "#221C2C") });
            todoButtons[item.Id] = (done, pin, edit, remove);
        }
        foreach (var item in settings.Todos.Items.Where(x => !x.Done).OrderBy(x => x.Id == pinned?.Id ? 0 : 1)) AddRow(item);
        if (settings.Todos.ActiveCount == 0)
        {
            var empty = Ui.Column(Ui.Text(settings.Todos.Items.Count == 0 ? "Маленькі кроки теж рахуються" : "На зараз усе ✓", 16, "#DCCBEF", true), Ui.Gap(7),
                Ui.Text(settings.Todos.Items.Count == 0 ? "Додай першу справу вище. Зірочкою познач головне на сьогодні." : "Незавершених справ немає. Можна видихнути.", 11, "#A99AB9"));
            empty.Margin = new Thickness(13, 15, 13, 14); rows.Children.Add(empty);
        }
        var completed = settings.Todos.Items.Where(x => x.Done).ToArray();
        if (completed.Length > 0)
        {
            var toggle = HomeSmallButton($"Виконані · {completed.Length}  {(showCompletedTodos ? "⌃" : "⌄")}", () => { showCompletedTodos = !showCompletedTodos; RebuildTodoRows(); });
            toggle.Tag = "completed-toggle"; toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.Background = System.Windows.Media.Brushes.Transparent; toggle.BorderThickness = new Thickness(0); toggle.Margin = new Thickness(0, 2, 0, 5); rows.Children.Add(toggle);
            if (showCompletedTodos) foreach (var item in completed) AddRow(item);
        }
        todoScroll?.ScrollToVerticalOffset(offset);
        if (keyboardInside) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!ReferenceEquals(todoRows, rows)) return;
            var buttons = todoButtons.Values.SelectMany(x => new[] { x.Done, x.Pin, x.Edit, x.Remove });
            var target = buttons.FirstOrDefault(x => Equals(x.Tag, focusTag)) ?? todoButtons.Values.FirstOrDefault().Done;
            if (target != null) target.Focus(); else todoInput?.Focus();
        }));
    }
    private void UpdateTodoBadge()
    { if (todoSectionButton != null) todoSectionButton.Content = "Справи" + (settings.Todos.ActiveCount > 0 ? " · " + settings.Todos.ActiveCount : ""); }
    private void UpdateTodoFeedback()
    {
        if (todoFeedback == null) return;
        todoFeedback.Text = todoMessage; todoFeedback.ToolTip = todoMessage; todoFeedback.Foreground = Ui.Brush(todoError ? "#F1B49D" : "#AA9CBA");
        todoSubmit!.Content = todoEditingId == null ? "＋" : "✓";
        todoSubmit.ToolTip = todoEditingId == null ? "Додати справу" : "Зберегти зміни"; AutomationProperties.SetName(todoSubmit, (string)todoSubmit.ToolTip);
        todoAuxiliary!.Visibility = todoUndo != null || todoEditingId != null ? Visibility.Visible : Visibility.Collapsed;
        todoAuxiliary.Content = todoEditingId != null ? "Назад" : "Скасувати";
        todoAuxiliary.ToolTip = todoEditingId != null ? "Скасувати редагування · Esc" : "Скасувати останню зміну у списку"; AutomationProperties.SetName(todoAuxiliary, (string)todoAuxiliary.ToolTip);
    }
    private void SubmitTodo()
    {
        var text = TodoList.CleanTitle(todoInput?.Text ?? todoDraft);
        if (text.Length == 0) { TodoValidation("Напиши коротко, що хочеш зробити."); return; }
        if (todoEditingId == null && settings.Todos.Items.Count >= TodoList.Limit) { TodoValidation("У списку вже 100 справ. Видали непотрібну й спробуй ще раз."); return; }
        if (todoEditingId is { } id)
        {
            if (!settings.Todos.Items.Any(x => x.Id == id)) { CancelTodoEdit(); TodoValidation("Цю справу вже видалено."); return; }
            if (!ChangeTodo(list => list.Rename(id, text), "Назву оновлено")) { todoMessage = "Назва не змінилася"; todoError = false; }
        }
        else ChangeTodo(list => list.Add(text) != null, "Справу додано");
        todoEditingId = null; todoDraft = ""; if (todoInput != null) { todoInput.Text = ""; todoInput.Focus(); } UpdateTodoFeedback();
    }
    private void TodoValidation(string message) { todoMessage = message; todoError = true; UpdateTodoFeedback(); todoInput?.Focus(); }
    private void EditTodo(string id)
    {
        var item = settings.Todos.Items.FirstOrDefault(x => x.Id == id); if (item == null) return;
        todoEditingId = id; todoDraft = item.Title; todoMessage = "Редагування · Enter — зберегти, Esc — скасувати"; todoError = false;
        if (todoInput != null) { todoInput.Text = todoDraft; todoInput.Focus(); todoInput.SelectAll(); } UpdateTodoFeedback();
    }
    private void CancelTodoEdit()
    { todoEditingId = null; todoDraft = ""; if (todoInput != null) todoInput.Text = ""; todoMessage = "☆ — головне на сьогодні"; todoError = false; UpdateTodoFeedback(); }
    private void ToggleTodo(string id)
    {
        var item = settings.Todos.Items.FirstOrDefault(x => x.Id == id); if (item == null) return;
        var done = !item.Done; var title = item.Title;
        if (ChangeTodo(list => list.SetDone(id, done), done ? "Виконано: " + title : "Справу повернуто до списку") && done)
            surface.Notify("Готово · " + title, "celebrate", "todo-complete", 2);
    }
    private bool ChangeTodo(Func<TodoList, bool> action, string message)
    {
        var before = settings.Todos.Copy(); if (!action(settings.Todos)) return false;
        todoUndo = before; todoMessage = message; todoError = false;
        SaveNow(); RebuildTodoRows(); UpdateTodoFeedback(); UpdateHomeTodos(); UpdateFocusTodo(); return true;
    }
    private void UndoTodo()
    {
        if (todoUndo == null) return;
        settings.Todos = todoUndo; todoUndo = null; settings.Todos.Normalize(TodoToday);
        if (todoEditingId != null && !settings.Todos.Items.Any(x => x.Id == todoEditingId)) CancelTodoEdit();
        todoMessage = "Останню зміну скасовано"; todoError = false;
        SaveNow(); RebuildTodoRows(); UpdateTodoFeedback(); UpdateHomeTodos(); UpdateFocusTodo();
        surface.Notify("Останню зміну скасовано", "wink", "todo-complete", 2);
    }
    private void RefreshTodoDay(DateOnly today)
    {
        if (!settings.Todos.RefreshDay(today)) return;
        if (loadedOnce) SaveNow(); RebuildTodoRows(); UpdateHomeTodos(); UpdateFocusTodo();
    }
    private void UpdateFocusTodo()
    {
        var pinned = settings.Todos.Pinned(TodoToday);
        if (timerStateLabel != null)
        {
            timerStateLabel.Text = pinned != null ? "Головне: " + pinned.Title : focus.Running ? "Одна справа. Вся твоя увага." : focus.Finished ? "Готово. Зроби паузу ☺" : "Твій час для важливого";
            timerStateLabel.ToolTip = pinned?.Title;
        }
        if (homeFocusStatus != null)
        {
            homeFocusStatus.Text = pinned?.Title ?? (focus.Running ? "Зосередься на своєму" : focus.Finished ? "Готово · час відпочити" : "Одна справа за раз");
            homeFocusStatus.ToolTip = pinned?.Title;
        }
    }
    private FrameworkElement HomeTodoContent()
    {
        homeTodoTitle = Ui.Text("", 12, "#E6D9F5", true); homeTodoTitle.MaxHeight = 28; homeTodoTitle.LineHeight = 14; homeTodoTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; homeTodoTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        homeTodoStatus = HomeLine("", 9, "#A89AB8");
        homeTodoDone = HomeSmallButton("✓", () => { var pinned = settings.Todos.Pinned(TodoToday); if (pinned != null) ToggleTodo(pinned.Id); }); homeTodoDone.Width = 27; homeTodoDone.MinHeight = 23; homeTodoDone.Padding = new Thickness(0);
        return Ui.Column(HomeHeadingWithAction("ГОЛОВНЕ НА СЬОГОДНІ", homeTodoDone), Ui.Gap(3), homeTodoTitle, Ui.Gap(3), homeTodoStatus);
    }
    private void UpdateHomeTodos()
    {
        if (homeTodoTitle != null)
        {
            var pinned = settings.Todos.Pinned(TodoToday);
            homeTodoTitle.Text = pinned?.Title ?? "Закріпи одну важливу справу"; homeTodoTitle.ToolTip = homeTodoTitle.Text;
            homeTodoStatus!.Text = $"Залишилось: {settings.Todos.ActiveCount}  ·  виконано: {settings.Todos.Items.Count - settings.Todos.ActiveCount}";
            homeTodoDone!.IsEnabled = pinned != null; homeTodoDone.ToolTip = pinned == null ? "Спочатку закріпи справу" : "Виконати: " + pinned.Title; AutomationProperties.SetName(homeTodoDone, (string)homeTodoDone.ToolTip);
        }
        UpdateFocusTodo();
    }
}

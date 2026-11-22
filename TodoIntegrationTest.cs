using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunTodoIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory); var report = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); report.Add("PASS " + label); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Key(Key key) => todoInput!.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(todoInput), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        async Task Picture(string name) { await Task.Delay(350); surface.InvalidateVisual(); UpdateLayout(); Capture(Path.Combine(directory, name)); }
        TodoItem Item(string id) => settings.Todos.Items.Single(x => x.Id == id);
        try
        {
            settings.TimerSound = false; settings.Note = "Ця нотатка має залишитися.\nІ цей другий рядок теж."; var originalHome = settings.Home.Items.Select(x => x.Id).ToArray();
            ResetPosition(); SetExpanded(true); SwitchPage("Нотатки"); await Task.Delay(450);
            Check(notesEditor?.Text == settings.Note && notesEditor.ActualHeight > 100 && notesEditor.TransformToAncestor(pageHost).Transform(new Point(0, notesEditor.ActualHeight)).Y < pageHost.ActualHeight, "The existing multiline note remains readable and fits below the new navigation");
            notesEditor!.AppendText("\nТестова зміна."); var expectedNote = settings.Note;
            Click(todoSectionButton!); await Picture("todos-empty.png");
            Check(settings.NotesView == "tasks" && settings.Note == expectedNote && settings.Todos.Items.Count == 0, "Opening tasks preserves the edited note and starts with an empty task list");
            Click(todoSubmit!); Check(settings.Todos.Items.Count == 0 && todoError && todoFeedback!.Text.Contains("Напиши"), "An empty task is rejected inline");
            todoInput!.Text = "Доробити HelperBar"; Check(!todoError, "Typing clears stale task validation feedback"); Key(System.Windows.Input.Key.Enter);
            var first = settings.Todos.Items.Single().Id;
            Check(Item(first).Title == "Доробити HelperBar" && todoInput.Text.Length == 0, "Enter adds the task and clears the input for the next one");
            todoInput.Text = "Прочитати кілька сторінок"; Click(todoSubmit!); var second = settings.Todos.Items[1].Id;
            Click(todoButtons[first].Pin); Check(settings.Todos.Pinned(TodoToday)?.Id == first, "The row star pins today's main task");
            Click(todoButtons[second].Pin); Check(settings.Todos.Pinned(TodoToday)?.Id == second, "Pinning another row replaces the main task without removing either row");
            Click(todoButtons[second].Pin); Check(settings.Todos.Pinned(TodoToday) == null, "A second click on the same star removes the daily pin");
            Click(todoButtons[first].Pin); await Picture("todos-list.png");
            Check(settings.Home.Items.Select(x => x.Id).SequenceEqual(originalHome), "Creating and pinning tasks never inserts a widget into the user's existing home layout");
            settings.FocusView = "focus"; SwitchPage("Фокус"); Click(timerToggle!); await Picture("todos-focus.png");
            Check(focus.Running && timerStateLabel!.Text.Contains(Item(first).Title) && timerStateLabel.ActualWidth == 420, "Focus shows the main task and keeps its own timer controls and bounded text width");
            var reminder = settings.Reminders.Add("Незалежний тестовий таймер", TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow)!;
            var deadline = reminder.Deadline; OpenNotesSection("tasks"); await Task.Delay(150);
            Click(todoButtons[first].Edit); todoInput!.Text = "Назву ще не зберігати"; Key(System.Windows.Input.Key.Escape);
            Check(todoEditingId == null && Item(first).Title == "Доробити HelperBar" && surface.Expanded, "Escape cancels editing without changing the task or collapsing the island");
            Click(todoButtons[first].Edit); todoInput.Text = "Доробити маленьку корисну функцію"; Click(todoSubmit!);
            Check(Item(first).Title == "Доробити маленьку корисну функцію" && settings.Todos.Pinned(TodoToday)?.Id == first, "Saving an edit preserves the task identity and daily pin");
            var savePath = Path.Combine(directory, "todos-persistence.json"); settings.Save(savePath); var restored = Settings.Load(savePath);
            Check(restored.Todos.Pinned(TodoToday)?.Id == first && restored.Todos.Items.Count == 2 && restored.Note == expectedNote && restored.Reminders.Items[0].Deadline == deadline, "Task edits, daily pin, note and active reminder deadlines survive a settings round-trip");
            Click(todoButtons[first].Done);
            Check(Item(first).Done && settings.Todos.Pinned(TodoToday) == null && !todoButtons.ContainsKey(first) && surface.Mood == "celebrate", "Completing the main task clears the pin, folds the finished row and celebrates with the eyes");
            await Picture("todos-completed.png");
            Click(todoAuxiliary!); Check(!Item(first).Done && settings.Todos.Pinned(TodoToday)?.Id == first, "Undo restores both an accidentally completed task and its main-task pin");
            Click(todoButtons[first].Done);
            var completedToggle = todoRows!.Children.OfType<Button>().Single(); Click(completedToggle); await Picture("todos-finished-section.png");
            Check(todoButtons.ContainsKey(first) && !todoButtons[first].Pin.IsEnabled, "Completed tasks can be expanded and cannot be pinned while complete");
            Click(todoButtons[first].Done); Check(!Item(first).Done && settings.Todos.Pinned(TodoToday) == null, "The completed-row check reopens a task without silently pinning it");
            Click(todoButtons[second].Remove); Check(settings.Todos.Items.Count == 1 && settings.Todos.Items[0].Id == first, "Delete removes only the chosen row");
            Click(todoAuxiliary!); Check(settings.Todos.Items.Select(x => x.Id).SequenceEqual(new[] { first, second }), "Undo deletion restores the original identities and ordering");
            Click(todoButtons[first].Pin);
            settings.Home.Items.Clear(); settings.Home.AddWidget("tasks"); settings.Home.AddWidget("focus"); settings.Home.AddWidget("note"); SwitchPage("Головна"); await Picture("todos-home.png");
            Check(homeTodoTitle?.Text == Item(first).Title && homeTodoDone!.IsEnabled && homeFocusStatus!.Text == Item(first).Title && homeNote!.Text == expectedNote, "The optional home widget, focus widget and note display shared live data");
            Check(homeTodoStatus!.TransformToAncestor(pageHost).Transform(new Point(0, homeTodoStatus.ActualHeight)).Y < 126, "The task widget's title, action and counters fit within its tile");
            Click(homeTodoDone!); Check(Item(first).Done && currentPage == "Головна" && !homeTodoDone!.IsEnabled && focus.Running && reminder.Deadline == deadline, "Completing from home updates the task without navigating, stopping focus or changing a reminder");
            OpenNotesSection("tasks"); Click(todoAuxiliary!); Check(!Item(first).Done && settings.Todos.Pinned(TodoToday)?.Id == first, "The last home completion can be undone from the shared task list");
            todoInput!.Text = "Незбережена чернетка"; Click(noteSectionButton!); Check(notesEditor!.Text == expectedNote, "Returning to the note leaves all existing text intact");
            Click(todoSectionButton!); Check(todoInput!.Text == "Незбережена чернетка", "Switching sections retains an unfinished task draft within the session");
            RefreshTodoDay(TodoToday.AddDays(1)); Check(settings.Todos.PinnedId.Length == 0 && settings.Todos.Items.Count == 2 && focus.Running, "Day rollover removes only the daily pin, preserving tasks and running focus");
            todoInput.Text = "";
            for (var i = 0; i < 25; i++) settings.Todos.Add(new string('Д', 115) + " " + i);
            RebuildTodoRows(); await Task.Delay(200); todoScroll!.ScrollToBottom(); await Picture("todos-long-list.png");
            Check(todoScroll.ExtentHeight > todoScroll.ViewportHeight && todoRows!.Children.OfType<Border>().All(x => x.ActualWidth <= pageHost.ActualWidth), "Long task titles remain bounded and the list scrolls inside the island");
            settings.ReducedMotion = true; var target = settings.Todos.Items.Last(); ToggleTodo(target.Id);
            Check(target.Done && surface.Mood == "celebrate", "Reduced-motion mode retains completion feedback and data changes");
            report.Add("INFO All tasks, notes, timers and home edits belong to private test Settings; user data was not changed.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "todo-integration.txt"), report); Close(); }
    }
}

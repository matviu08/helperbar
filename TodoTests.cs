using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class TodoTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var today = DateOnly.FromDateTime(DateTime.Now); var list = new TodoList();
        check(list.Add(" \r\n\t ") == null && list.Items.Count == 0, "Blank task text never creates an empty row");
        var first = list.Add("  Завершити\n  маленьку справу  ")!; var second = list.Add("Прочитати кілька сторінок")!;
        check(first.Title == "Завершити маленьку справу" && list.ActiveCount == 2, "Task input normalizes pasted whitespace and preserves Ukrainian text");
        var duplicate = list.Add(first.Title)!;
        check(duplicate.Id != first.Id && list.Items.Count == 3, "Tasks with the same title remain independently addressable");
        check(list.TogglePin(first.Id, today) && list.Pinned(today)?.Id == first.Id, "An active task can become today's main task");
        list.TogglePin(second.Id, today);
        check(list.Pinned(today)?.Id == second.Id && list.Items.Count == 3, "Pinning another task replaces only the pin, preserving every task");
        list.TogglePin(second.Id, today); check(list.Pinned(today) == null, "Clicking the main-task star again unpins it");
        list.TogglePin(first.Id, today); var undo = list.Copy();
        check(list.SetDone(first.Id, true) && list.Pinned(today) == null && list.ActiveCount == 2, "Completing the main task clears its pin and updates the active count");
        check(!list.SetDone(first.Id, true) && !list.TogglePin(first.Id, today), "A completed task cannot be pinned or completed twice");
        check(undo.Pinned(today)?.Id == first.Id && !undo.Items[0].Done, "Undo snapshots restore completion and today's pin without aliasing live items");
        list = undo.Copy(); list.Rename(first.Id, "Нова назва");
        check(list.Pinned(today)?.Title == "Нова назва" && undo.Items[0].Title != "Нова назва", "Renaming keeps identity and the pin while leaving undo data unchanged");
        check(!list.Rename(first.Id, " ") && !list.Rename(first.Id, "Нова назва"), "Blank and unchanged edits do not overwrite a task");
        var path = Path.Combine(directory, "todo-settings.json");
        var settings = new Settings { Todos = list, NotesView = "tasks", Note = "Збережена нотатка\nДругий рядок", FocusMinutes = 50 };
        settings.Reminders.Add("Незалежний таймер", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
        var layout = settings.Home.Items.Select(x => x.Id).ToArray(); settings.Save(path); var loaded = Settings.Load(path);
        check(loaded.Todos.Items.Select(x => x.Id).SequenceEqual(list.Items.Select(x => x.Id)) && loaded.Todos.Pinned(today)?.Id == first.Id && loaded.NotesView == "tasks", "Task identities, order, main task and selected notes section survive restart");
        check(loaded.Note == settings.Note && loaded.FocusMinutes == 50 && loaded.Reminders.Items.Count == 1 && loaded.Home.Items.Select(x => x.Id).SequenceEqual(layout), "Saving tasks preserves notes, focus preferences, reminders and the customized home layout");
        list.SetDone(first.Id, true); settings.Save(path); var finished = Settings.Load(path).Todos;
        check(finished.Items.Single(x => x.Id == first.Id).Done && finished.Pinned(today) == null && finished.ActiveCount == 2, "Completed tasks remain completed after restart and cannot revive a cleared pin");
        list.SetDone(first.Id, false); list.TogglePin(first.Id, today);
        check(list.RefreshDay(today.AddDays(1)) && list.Pinned(today.AddDays(1)) == null && list.Items.Count == 3 && list.ActiveCount == 3, "A new local day clears only the daily pin, never the task list");
        check(!list.RefreshDay(today.AddDays(1)), "Day rollover is idempotent and does not cause repeated writes");
        var oldDayUndo = undo.Copy(); oldDayUndo.Normalize(today.AddDays(1));
        check(oldDayUndo.Pinned(today.AddDays(1)) == null && oldDayUndo.Items.Count == 3, "Undo after midnight cannot resurrect yesterday's main-task pin");
        list.TogglePin(second.Id, today); check(list.Remove(second.Id) && !list.Remove(second.Id) && list.Pinned(today) == null && list.Items.Count == 2, "Deleting a pinned task clears only that task and its pin");
        list.SetDone(first.Id, true); list.SetDone(first.Id, false);
        check(list.Items.First(x => x.Id == first.Id).Done == false && list.Pinned(today) == null, "Reopening a completed task does not silently repin it");
        var malformed = new TodoList { PinnedId = "missing", PinnedDay = "invalid", Items = new() { new() { Id = "broken", Title = "  збережено  " }, new() { Id = "broken", Title = "Інше" }, new() { Title = "\r\n" } } };
        malformed.Normalize(today);
        check(malformed.Items.Count == 2 && malformed.Items.All(x => Guid.TryParse(x.Id, out _)) && malformed.Items.Select(x => x.Id).Distinct().Count() == 2 && malformed.PinnedId.Length == 0, "Malformed saved tasks repair IDs, discard blank rows and clear invalid pins");
        check(TodoList.CleanTitle(new string('а', 119) + "🙂").Length == 119, "Title truncation never splits a surrogate-pair character");
        var bounded = new TodoList(); for (var i = 0; i < TodoList.Limit; i++) bounded.Add("Справа " + i);
        check(bounded.Add("Зайва") == null && bounded.Items.Count == TodoList.Limit, "The task limit rejects extras without replacing existing data");
        var catalog = HomeLayout.Default(); var original = catalog.Items.Select(x => x.Id).ToArray();
        check(!catalog.Items.Any(x => x.Kind == "tasks") && catalog.AddWidget("tasks") && !catalog.AddWidget("tasks") && catalog.Items.Take(original.Length).Select(x => x.Id).SequenceEqual(original), "The optional task widget is added once without changing the existing home layout");
        var queue = new IslandNotifications(); queue.Post("Таймер", "celebrate", "timer", 10, 0); queue.Post("Справу виконано", "celebrate", "todo-complete", 2, .1);
        check(queue.Current?.Key == "timer" && queue.PendingCount == 1, "Task celebrations do not replace a higher-priority timer completion");
    }
}

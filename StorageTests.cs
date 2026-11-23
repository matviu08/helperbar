using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class StorageTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var fixture = Path.Combine(directory, "storage-" + Guid.NewGuid().ToString("N"));
        var oldApp = Path.Combine(fixture, "portable"); var oldData = Path.Combine(oldApp, "data"); var shared = Path.Combine(fixture, "local", "HelperBar", "data");
        Directory.CreateDirectory(oldData);
        var oldPath = Path.Combine(oldData, "settings.json");
        var settings = new Settings { Note = "Зберегти мої думки", Accent = "#123456" }; settings.Todos.Add("Моя справа"); settings.Files.Add(Path.Combine(fixture, "example.txt"));
        settings.Profiles.Normalize(settings); settings.Profiles.Switch(settings, "work"); settings.Home.Items.Clear();
        settings.Reminders.Add("Відлік", TimeSpan.FromMinutes(30), DateTimeOffset.UtcNow); settings.Save(oldPath);
        var original = File.ReadAllText(oldPath); File.WriteAllText(Path.Combine(oldData, "unrelated.txt"), "Do not migrate this file");
        var resolved = AppStorage.Resolve(oldApp, shared); var newPath = Path.Combine(shared, "settings.json");
        check(resolved.Directory == shared && resolved.Warning == null && File.ReadAllText(newPath) == original, "Migration atomically copies the complete saved settings without reserializing private content");
        check(File.ReadAllText(oldPath) == original && !File.Exists(Path.Combine(shared, "unrelated.txt")), "The legacy file remains intact and unrelated files never enter the new data directory");
        var loaded = Settings.Load(newPath);
        check(loaded.Profiles.ActiveId == "work" && loaded.Home.Items.Count == 0 && loaded.Note == settings.Note && loaded.Todos.Items.Count == 1 && loaded.Files.Items.Count == 1 && loaded.Reminders.Items[0].Deadline == settings.Reminders.Items[0].Deadline, "Profiles, empty layouts, notes, tasks, files and running deadlines survive relocation");
        loaded.Note = "New data wins"; loaded.Save(newPath); AppStorage.Resolve(oldApp, shared);
        check(Settings.Load(newPath).Note == "New data wins" && File.ReadAllText(oldPath) == original, "Subsequent launches never overwrite AppData with older portable settings");
        var backupOnly = Path.Combine(fixture, "backup-only"); Directory.CreateDirectory(backupOnly);
        File.WriteAllText(Path.Combine(backupOnly, "settings.json.bak"), original);
        check(AppStorage.Resolve(oldApp, backupOnly).Directory == backupOnly && Settings.Load(Path.Combine(backupOnly, "settings.json")).Note == settings.Note, "An existing AppData backup is preferred over importing stale portable data");
        File.WriteAllText(oldPath, "broken JSON"); File.WriteAllText(oldPath + ".bak", original);
        var recovered = AppStorage.Resolve(oldApp, Path.Combine(fixture, "recovered"));
        check(recovered.Warning != null && Settings.Load(Path.Combine(recovered.Directory, "settings.json")).Note == settings.Note && File.ReadAllText(oldPath) == "broken JSON", "A valid legacy backup recovers a damaged primary without destroying either source");
        File.WriteAllText(oldPath + ".bak", "also broken"); var corrupt = AppStorage.Resolve(oldApp, Path.Combine(fixture, "invalid"));
        check(corrupt.Directory == oldData && corrupt.Warning != null && !Directory.Exists(Path.Combine(fixture, "invalid")), "Unrecoverable old data stays in its original location with an explicit warning");
        File.WriteAllText(oldPath, original); var blocked = Path.Combine(fixture, "blocked"); File.WriteAllText(blocked, "A file blocks the destination directory");
        check(AppStorage.Resolve(oldApp, blocked).Directory == oldData && File.ReadAllText(oldPath) == original, "A failed destination write falls back to the original settings instead of starting empty");
        var fresh = AppStorage.Resolve(Path.Combine(fixture, "fresh-app"), Path.Combine(fixture, "fresh-data"));
        check(!Directory.Exists(fresh.Directory) && fresh.Warning == null, "A new installation creates no personal data until it actually saves settings");
        check(!Directory.EnumerateFiles(shared, "*.tmp").Any(), "A successful migration leaves no temporary copies behind");
    }
}

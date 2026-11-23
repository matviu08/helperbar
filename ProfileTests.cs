using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HelperBar;

internal static class ProfileTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var path = Path.Combine(directory, "profile-settings.json");
        File.WriteAllText(path, "{\"Accent\":\"#123456\",\"Body\":\"#112233\",\"Home\":{\"Items\":[]},\"Note\":\"Моя нотатка\",\"NotifyDeviceConnections\":false,\"Scale\":1.2}");
        var s = Settings.Load(path);
        check(s.Profiles.Items.Count == 4 && s.Profiles.ActiveId == "personal" && s.Profiles.Active.Title == "Моє", "Legacy settings migrate into a personal profile plus three separate presets");
        check(s.Home.Items.Count == 0 && s.Profiles.Active.Options.Home.Items.Count == 0 && s.Accent == "#123456" && s.Body == "#112233" && !s.NotifyDeviceConnections && s.Note == "Моя нотатка" && s.Scale == 1.2, "Migration preserves an intentionally empty layout, custom colors, notification preferences and personal data");
        s.Home.AddWidget("note"); var personalId = s.Home.Items[0].Id; s.MotionIntensity = .37; s.NotifyTaskCompletion = false;
        var todo = s.Todos.Add("Спільна справа")!; var reminder = s.Reminders.Add("Спільний таймер", TimeSpan.FromMinutes(12), DateTimeOffset.UtcNow)!;
        s.Files.Add(Path.Combine(directory, "reference.txt")); var sharedNote = s.Note; var sharedTasks = s.Todos; var sharedFiles = s.Files; var sharedReminders = s.Reminders;
        s.PetOnly = true; s.OpenOnHover = true; s.PositionX = .3; s.OpenShortcut = "Ctrl+Alt+F";
        check(s.Profiles.Switch(s, "work") && s.Home.Items.Select(x => x.Kind).SequenceEqual(new[] { "tasks", "focus", "note", "clock" }) && s.MotionIntensity == .5, "Work begins with its own task/focus layout and quieter decoration");
        check(s.Profiles.Items[0].Options.Home.Items.Single().Id == personalId && s.Profiles.Items[0].Options.MotionIntensity == .37, "Switching captures unsaved changes before applying another profile");
        s.Home.Items.Clear(); s.Accent = "#ABCDEF"; s.TimerSound = false; s.ReducedMotion = true; s.Speed = .7;
        s.Profiles.Switch(s, "music"); check(s.AudioReactive && s.SpectrumIntensity > 1 && s.Home.Items.Any(x => x.Kind == "music") && !s.ReducedMotion, "Music uses independent audio, motion and widget settings");
        s.Profiles.Switch(s, "games"); check(!s.AudioReactive && !s.TimerSound && !s.NotifyTaskCompletion && !s.NotifyFocusBanner && s.NotifyLowBattery && s.FullscreenEyes, "Games suppresses optional interruptions while preserving low battery warnings and fullscreen quiet mode");
        s.Profiles.Switch(s, "work"); check(s.Home.Items.Count == 0 && s.Accent == "#ABCDEF" && !s.TimerSound && s.ReducedMotion && s.Speed == .7, "Returning restores that profile's edited settings, including an empty layout");
        check(ReferenceEquals(s.Todos, sharedTasks) && ReferenceEquals(s.Files, sharedFiles) && ReferenceEquals(s.Reminders, sharedReminders) && s.Note == sharedNote && s.Todos.Items[0].Id == todo.Id && s.Reminders.Items[0].Deadline == reminder.Deadline, "Switching never clones, clears or restarts shared content and reminders");
        check(s.PetOnly && s.OpenOnHover && s.PositionX == .3 && s.Scale == 1.2 && s.OpenShortcut == "Ctrl+Alt+F", "Window placement, pet mode, hover behavior and shortcuts stay global");
        s.Save(path); var loaded = Settings.Load(path);
        check(loaded.Profiles.ActiveId == "work" && loaded.Accent == "#ABCDEF" && loaded.Home.Items.Count == 0 && loaded.ReducedMotion, "The selected profile and its latest edits survive restart");
        loaded.Profiles.Switch(loaded, "personal"); check(loaded.Accent == "#123456" && loaded.Home.Items[0].Id == personalId && loaded.MotionIntensity == .37 && !loaded.NotifyTaskCompletion, "Personal settings remain available after saving and restarting in another profile");
        loaded.Home.Items[0].Kind = "focus";
        check(loaded.Profiles.Active.Options.Home.Items[0].Kind == "note", "Live widgets never alias their stored profile snapshot");
        var before = JsonSerializer.Serialize(loaded); check(!loaded.Profiles.Switch(loaded, "personal") && !loaded.Profiles.Switch(loaded, "unknown") && JsonSerializer.Serialize(loaded) == before, "Repeated and unknown selections are no-ops");
        loaded.Profiles.Items.Single(x => x.Id == "music").Options = new ProfileOptions { Accent = "invalid", Body = null!, MotionIntensity = double.NaN, Speed = 40, AudioSensitivity = -3, GlowIntensity = 9, Home = null! };
        loaded.Profiles.Switch(loaded, "music"); check(loaded.Accent == "#BCACFF" && loaded.Body == "#08090C" && loaded.MotionIntensity == 1 && loaded.Speed == 1.6 && loaded.AudioSensitivity == .6 && loaded.GlowIntensity == 1.5 && loaded.Home != null, "Invalid inactive profile values are normalized before use");
        loaded.Profiles.Items.Add(new IslandProfile { Id = "music" }); loaded.Profiles.Items.Add(null!); loaded.Profiles.ActiveId = "removed"; loaded.Profiles.Normalize(loaded);
        check(loaded.Profiles.Items.Select(x => x.Id).Distinct().Count() == 4 && loaded.Profiles.ActiveId == "personal" && loaded.Profiles.Active.Options.Accent == loaded.Accent, "Malformed collections recover four unique profiles and retain live settings");
        File.WriteAllText(path, "{\"Profiles\":null,\"Home\":{\"Items\":[]},\"Note\":\"keep\"}");
        check(Settings.Load(path).Profiles.Items.Count == 4 && Settings.Load(path).Home.Items.Count == 0, "Explicit null profile data has the same lossless migration as older settings");
        var notices = new IslandNotifications(); notices.SetSuspended(true, 0); notices.Post("Task", "celebrate", "todo-complete", 2, 1); notices.Post("Timer", "celebrate", "timer", 10, 1);
        notices.RemoveKey("todo-complete"); notices.SetSuspended(false, 2);
        check(notices.Current?.Key == "timer" && notices.PendingCount == 0, "Disabling task cards cancels a deferred card without cancelling timer completion");
        var presentation = new Settings { MotionIntensity = 0 }; var surface = new IslandSurface(presentation); surface.React("celebrate");
        check(surface.ParticleCount == 0 && surface.Mood == "celebrate", "Zero decoration keeps the eye expression without celebration particles");
        presentation.MotionIntensity = 1.5; surface.React("celebrate"); check(surface.ParticleCount == 33, "Decoration strength scales particle density within the configured limit");
        presentation.ReducedMotion = true; surface.RefreshProfilePresentation(); surface.React("celebrate");
        check(surface.ParticleCount == 0, "Reduced motion overrides a high profile intensity and clears old particles");
        presentation.NotifyTaskCompletion = false; surface.Notify("Done", "celebrate", "todo-complete");
        check(surface.Toast.Length == 0 && surface.Mood == "celebrate", "A muted task card still gives the completion expression");
        presentation.NotifyTaskCompletion = true; surface.Notify("Done", "celebrate", "todo-complete"); presentation.NotifyTaskCompletion = false; surface.RefreshNotificationPreferences();
        check(surface.Toast.Length == 0, "Turning off task cards also removes an already displayed task notice");
    }
}

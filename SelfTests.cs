using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class SelfTests
{
    public static int Run()
    {
        var results = new List<string>();
        var root = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(root);
        try
        {
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); results.Add("PASS " + label); }
            var spring = new Spring(226) { Target = 524 };
            var max = 0d;
            for (var i = 0; i < 240; i++) { spring.Step(1d / 60); max = Math.Max(max, spring.Value); }
            Check(Math.Abs(spring.Value - 524) < .01 && max > 524 && max < 550, "Expansion settles with bounded spring overshoot");
            spring.Target = 136;
            for (var i = 0; i < 120; i++) spring.Step(1d / 30);
            Check(Math.Abs(spring.Value - 136) < .01, "Interrupted expansion converges to pet size");
            spring.Target = 500; spring.Step(1000);
            Check(double.IsFinite(spring.Value) && spring.Value < 550, "Long frame cannot destabilize spring");
            spring.Step(.01, 1, true); Check(spring.Value == 500 && spring.Velocity == 0, "Reduced motion snaps without overshoot");
            var now = new DateTimeOffset(2026, 11, 21, 23, 59, 0, TimeSpan.FromHours(2));
            var timer = new FocusTimer(); timer.Set(5); timer.Toggle(now);
            Check(timer.Remaining(now.AddMinutes(2)).TotalSeconds == 180, "Timer crosses midnight correctly");
            timer.Toggle(now.AddMinutes(2)); Check(!timer.Running && timer.Remaining(now.AddHours(4)).TotalSeconds == 180, "Pause preserves remaining time");
            timer.Toggle(now.AddHours(4)); Check(!timer.Tick(now.AddHours(4).AddMinutes(2)), "Timer does not finish early");
            Check(timer.Tick(now.AddHours(9)) && timer.Finished && !timer.Running, "Timer finishes after system suspension");
            Check(!timer.Tick(now.AddHours(10)), "Completion notification only fires once");
            timer.Toggle(now); Check(timer.Remaining(now).TotalMinutes == 5 && !timer.Finished, "Completed timer can restart");
            timer.Reset(); Check(!timer.Running && timer.Remaining(now).TotalMinutes == 5, "Reset restores chosen duration");
            Check(FocusTimer.Format(TimeSpan.FromMilliseconds(1)) == "00:01", "Remaining time rounds up");
            var settingsPath = Path.Combine(root, "test-settings.json");
            var settings = new Settings { Note = "Привіт\nНотатка з українською 🫶", Scale = 9, Accent = "invalid" };
            settings.Save(settingsPath); var loaded = Settings.Load(settingsPath);
            Check(loaded.Note == settings.Note, "Unicode notes survive atomic save and reload");
            Check(loaded.Scale == 1.4 && loaded.Accent == "#BCACFF", "Settings validate scale and colors");
            File.WriteAllText(settingsPath, "{broken"); loaded = Settings.Load(settingsPath);
            Check(loaded.Scale == 1 && loaded.LoadWarning != null, "Corrupt settings recover with a visible warning");
            var pcm16 = new PcmFormat(2, 48000, 16, 4, false);
            Check(Math.Abs(pcm16.Sample(new byte[] { 0, 128 }) + 1) < .00001, "16-bit PCM decodes full negative scale");
            var pcm24 = new PcmFormat(2, 48000, 24, 6, false);
            Check(Math.Abs(pcm24.Sample(new byte[] { 0, 0, 128 }) + 1) < .00001, "24-bit PCM sign extension is correct");
            var floatPcm = new PcmFormat(2, 48000, 32, 8, true);
            Check(floatPcm.Sample(BitConverter.GetBytes(float.NaN)) == 0, "Non-finite audio is treated as silence");
            var analyzer = new SpectrumAnalyzer(48000);
            for (var i = 0; i < 8192; i++) analyzer.Push((float)(.3 * Math.Sin(2 * Math.PI * 110 * i / 48000)));
            var lowPeak = Array.IndexOf(analyzer.Latest.Bands, System.Linq.Enumerable.Max(analyzer.Latest.Bands));
            Check(lowPeak < 7 && analyzer.Latest.Bass > .4 && analyzer.Latest.Level > .4, "Real 110 Hz signal energizes bass bands");
            for (var i = 0; i < 8192; i++) analyzer.Push((float)(.3 * Math.Sin(2 * Math.PI * 6000 * i / 48000)));
            var highPeak = Array.IndexOf(analyzer.Latest.Bands, System.Linq.Enumerable.Max(analyzer.Latest.Bands));
            Check(highPeak > 17 && highPeak > lowPeak + 10, "6 kHz signal moves energy into treble bands");
            for (var i = 0; i < 4096; i++) analyzer.Push(0);
            Check(analyzer.Latest.Level == 0 && System.Linq.Enumerable.Max(analyzer.Latest.Bands) == 0, "Digital silence produces no invented visualizer activity");
            var motion = new AudioMotion();
            motion.Step(new SpectrumFrame(System.Linq.Enumerable.Repeat(1f, 24).ToArray(), 1, 1, 1), .05);
            Check(motion.Level > .5, "Audio envelope attacks promptly");
            for (var i = 0; i < 80; i++) motion.Step(SpectrumFrame.Silent, .05);
            Check(motion.Level < .001 && System.Linq.Enumerable.Max(motion.Bands) < .001, "Audio envelope decays to silence");
            var track = new MediaTrack { Available = true, Playing = true, Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(110), Position = TimeSpan.FromSeconds(30), UpdatedAt = now, MinSeek = TimeSpan.FromSeconds(15), MaxSeek = TimeSpan.FromSeconds(90) };
            Check(track.PositionAt(now.AddSeconds(5)).TotalSeconds == 35, "Playing timeline extrapolates from the session timestamp");
            Check((track with { Playing = false }).PositionAt(now.AddSeconds(5)).TotalSeconds == 30, "Paused timeline does not drift");
            Check(track.PositionAt(now.AddHours(1)).TotalSeconds == 110, "Timeline never exceeds track end");
            Check(NowPlayingService.SeekTarget(track, 0).TotalSeconds == 15 && NowPlayingService.SeekTarget(track, 1).TotalSeconds == 90, "Seeking respects player seek bounds");
            var legacyPath = Path.Combine(root, "legacy-settings.json");
            File.WriteAllText(legacyPath, "{\"Note\":\"Моя нотатка\",\"Scale\":1.2,\"ScreenIndex\":2}");
            var legacy = Settings.Load(legacyPath);
            Check(legacy.SchemaVersion == Settings.CurrentSchemaVersion && legacy.Note == "Моя нотатка" && legacy.Scale == 1.2 && legacy.ScreenIndex == 2 && !legacy.FreePosition, "Legacy settings migrate without losing notes, scale or monitor index");
            legacy.Note = "Наступна версія"; legacy.Save(legacyPath);
            File.WriteAllText(legacyPath, "{broken");
            var recovered = Settings.Load(legacyPath);
            Check(recovered.Note == "Моя нотатка" && recovered.LoadWarning != null, "Invalid primary settings recover the previous valid backup");
            recovered.Save(legacyPath);
            Check(File.ReadAllText(legacyPath + ".corrupt") == "{broken" && Settings.Load(legacyPath + ".bak").Note == "Моя нотатка", "Saving after recovery preserves the corrupt file and healthy backup");
            legacy.FreePosition = true; legacy.PositionX = .23; legacy.PositionY = .71; legacy.MonitorId = "monitor-abc"; legacy.PetOnly = true; legacy.LastPage = "Музика"; legacy.GlowIntensity = 0; legacy.EyeIntensity = .3; legacy.Save(legacyPath);
            var restored = Settings.Load(legacyPath);
            Check(restored.FreePosition && restored.PositionX == .23 && restored.PositionY == .71 && restored.MonitorId == "monitor-abc" && restored.PetOnly && restored.LastPage == "Музика" && restored.GlowIntensity == 0 && restored.EyeIntensity == .3, "Placement, compact mode, last page and independent audio effects survive restart");
            var leftMonitor = new System.Windows.Rect(-2560, -240, 2560, 1400);
            var range = PlacementGeometry.Range(leftMonitor, 1.2, 1.5);
            var point = PlacementGeometry.Resolve(range, true, .23, .71, 12, 1.5);
            var normal = PlacementGeometry.Normalize(range, point);
            Check(Math.Abs(normal.X - .23) < .00001 && Math.Abs(normal.Y - .71) < .00001 && point.X < 0, "Placement round-trips on a negative-coordinate monitor at 150 percent DPI");
            var outside = PlacementGeometry.Normalize(range, new System.Windows.Point(-99999, 99999));
            Check(outside.X == 0 && outside.Y == 1, "Off-screen drag coordinates clamp to the reachable area");
            var small = PlacementGeometry.Range(new System.Windows.Rect(0, 0, 320, 200), 1.4, 2);
            Check(small.Width == 0 && small.Height == 0, "Small work areas never create invalid placement bounds");
            Check(Shortcut.TryParse("Ctrl+Alt+Space", out var openShortcut) && !Shortcut.TryParse("A", out _) && !Shortcut.TryParse("Ctrl+F12", out _), "Shortcut parser requires modifiers and excludes the reserved F12 key");
            var registered = new HashSet<int>(); var deny = false;
            using (var bindings = new ShortcutRegistration((id, key) => !deny && registered.Add(id), id => registered.Remove(id)))
            {
                Check(bindings.Set("open", openShortcut), "Initial shortcut registers");
                Shortcut.TryParse("Ctrl+Shift+H", out var replacement);
                deny = true;
                Check(!bindings.Set("open", replacement) && registered.Count == 1 && bindings.ActionFor(registered.Single()) == "open", "Shortcut conflict keeps the previous binding active");
                deny = false;
                Check(bindings.Set("open", replacement) && registered.Count == 1 && !bindings.Set("pet", replacement), "Successful replacement releases the old binding and rejects duplicate actions");
            }
            Check(registered.Count == 0, "Closing releases every registered shortcut");
            Check(StartupService.CommandFor(@"C:\My Apps\HelperBar.exe") == "\"C:\\My Apps\\HelperBar.exe\" --startup", "Startup command quotes executable paths containing spaces");
            var notices = new IslandNotifications();
            notices.Post("Фокус завершено", "celebrate", "timer", 10, 0);
            for (var i = 0; i < 20; i++) notices.Post($"Гучність {i}", "wink", "volume", 0, .1 + i * .01);
            Check(notices.Current!.Key == "timer" && notices.PendingCount == 1, "Rapid volume changes coalesce without replacing timer completion");
            notices.Advance(3.1);
            Check(notices.Current!.Message == "Гучність 19" && notices.PendingCount == 0, "Queue shows only the latest volume value after the important notification");
            notices.Advance(6.2);
            Check(notices.Current == null && IslandState.Resolve(false, false, false, false, true) == IslandMode.Music, "After notifications expire, the music state resumes");
            Check(IslandState.Resolve(true, true, true, true, true) == IslandMode.Expanded && IslandState.Resolve(false, false, true, true, true) == IslandMode.Pet, "Manual expanded and pet modes preserve their priority");
            var home = HomeLayout.Default(); var firstId = home.Items[0].Id; var lastId = home.Items[^1].Id;
            Check(home.MoveTo(lastId, 0) && home.Items[0].Id == lastId && home.Items[1].Id == firstId, "Moving a home tile preserves identities and the order of all other tiles");
            Check(home.MoveRelative(firstId, lastId, false) && home.Items[0].Id == firstId && !home.MoveRelative(firstId, lastId, false), "Drag-before handles adjacent tiles without off-by-one reordering");
            Check(home.MoveRelative(firstId, home.Items[^1].Id, true) && home.Items[^1].Id == firstId, "Dragging after the final tile inserts at the end without losing an item");
            Check(home.AddWidget("note") && !home.AddWidget("note") && !home.AddWidget("unknown"), "Widget catalog allows one instance and rejects unknown widgets");
            Check(ShortcutTarget.TryCreate("web", "Моя музика", "example.com/music", out var website, out _) && website.Target == "https://example.com/music", "Website input normalizes a hostname to an HTTPS address");
            Check(!ShortcutTarget.TryCreate("web", "", "javascript:alert(1)", out _, out _) && !ShortcutTarget.TryCreate("web", "", "file:///C:/Windows", out _, out _) && !ShortcutTarget.TryCreate("web", "", "https://user:pass@example.com", out _, out _), "Website shortcuts reject executable schemes and embedded credentials");
            var testFolder = Path.Combine(root, "Мої файли з пробілами"); Directory.CreateDirectory(testFolder);
            Check(ShortcutTarget.TryCreate("folder", "Мої файли", testFolder, out var folder, out _) && ShortcutTarget.IsAvailable(folder) && ShortcutTarget.LaunchPath(folder) == testFolder, "Folder shortcuts preserve Unicode and spaces as one shell target");
            Check(!ShortcutTarget.TryCreate("app", "", "calc.exe --argument", out _, out _) && !ShortcutTarget.TryCreate("app", "", @"C:\scripts\run.cmd", out _, out _), "App shortcuts require a selected absolute executable or shortcut, not a command string");
            Check(home.AddShortcut(website) && !home.AddShortcut(website.Copy()) && home.AddShortcut(folder), "Pinned shortcuts reject duplicate targets");
            var homePath = Path.Combine(root, "home-settings.json"); var homeSettings = new Settings { Home = home, Note = "Залишити мою нотатку" }; homeSettings.Save(homePath);
            var homeReloaded = Settings.Load(homePath);
            Check(homeReloaded.SchemaVersion == 2 && homeReloaded.Note == homeSettings.Note && homeReloaded.Home.Items.Select(i => i.Id).SequenceEqual(home.Items.Select(i => i.Id)) && homeReloaded.Home.Items[^1].Target == testFolder, "Customized layout order and Unicode shortcut paths survive a settings reload");
            var snapshot = home.Copy(); home.Items.Clear();
            Check(snapshot.Items.Count > 0 && home.Items.Count == 0, "Undo layout snapshot does not alias the edited collection");
            homeSettings.Home = home; homeSettings.Save(homePath);
            Check(Settings.Load(homePath).Home.Items.Count == 0, "An intentionally empty home remains empty after restarting");
            var brokenHome = new HomeLayout { Items = new() { new() { Kind = "music", Id = "a" }, new() { Kind = "music", Id = "b" }, new() { Kind = "clock", Id = "a" }, new() { Kind = "unknown" }, new() { Name = "Missing app", TargetType = "app", Target = @"C:\Missing app\program.exe" } } };
            brokenHome.Normalize();
            Check(brokenHome.Items.Count == 3 && brokenHome.Items.Select(i => i.Id).Distinct().Count() == 3 && brokenHome.Items.Any(i => i.Name == "Missing app"), "Layout recovery removes duplicate widgets, repairs IDs and retains unavailable shortcuts for editing");
            for (var i = 0; i < 40; i++) home.AddShortcut(new HomeItem { Name = i.ToString(), TargetType = "web", Target = $"https://example.com/{i}" });
            Check(home.Items.Count == HomeLayout.Limit, "Pinned layout respects its size limit");
            BatteryTests.Run(Check, root);
            FullscreenTests.Run(Check, root);
            DeviceNoticeTests.Run(Check, root);
            File.WriteAllLines(Path.Combine(root, "self-test.txt"), results);
            return 0;
        }
        catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(root, "self-test.txt"), results); return 1; }
    }
}

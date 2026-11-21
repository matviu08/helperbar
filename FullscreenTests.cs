using System;
using System.IO;
using System.Windows;

namespace HelperBar;

internal static class FullscreenTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var monitor = new Rect(0, 0, 1920, 1080);
        check(FullscreenGeometry.Covers(monitor, monitor) && FullscreenGeometry.Covers(new Rect(-1, 0, 1922, 1081), monitor), "Full-screen client bounds allow only small edge rounding differences");
        check(!FullscreenGeometry.Covers(new Rect(0, 0, 1920, 1040), monitor) && !FullscreenGeometry.Covers(monitor, monitor, true), "Maximized work-area windows and captioned windows do not trigger full-screen mode");
        check(FullscreenGeometry.Covers(new Rect(-2560, -300, 2560, 1440), new Rect(-2560, -300, 2560, 1440)) &&
              !FullscreenGeometry.Covers(new Rect(-2560, 0, 2560, 1440), monitor), "Detection uses physical pixels on the island monitor, including negative coordinates");
        check(FullscreenGeometry.Covers(new Rect(-1920, 0, 3840, 1080), monitor) && !FullscreenGeometry.Covers(Rect.Empty, monitor), "Spanning full-screen windows cover the island display while invalid bounds are ignored");
        var debounce = new FullscreenDebounce();
        check(!debounce.Update(true, 0) && !debounce.Update(false, .1) && !debounce.Update(true, .2) && !debounce.Update(true, .4) && debounce.Update(true, .6), "Brief full-screen transitions do not flicker the island");
        check(debounce.Update(false, .7) && debounce.Update(true, .8) && debounce.Update(false, .9) && !debounce.Update(false, 1.6), "Exit waits for a stable foreground change");
        var visibility = new FullscreenVisibility();
        visibility.Observe(true, true, false, false);
        check(visibility.Presentation == FullscreenPresentation.Hidden, "Full-screen hide mode suppresses the island");
        visibility.ShowManually(); visibility.Observe(true, true, false, true);
        check(visibility.Revealed && visibility.Presentation == FullscreenPresentation.Normal, "Explicit reveal remains visible while the island has foreground focus");
        visibility.Observe(true, true, true, false);
        check(!visibility.Revealed && visibility.Presentation == FullscreenPresentation.Eyes, "Returning to the full-screen app ends the temporary reveal");
        visibility.HideManually(); visibility.Observe(false, true, true, false);
        check(visibility.UserHidden && visibility.Presentation == FullscreenPresentation.Hidden, "Leaving full screen never undoes an explicit hide-to-tray");
        visibility.ShowManually(); visibility.Observe(true, false, true, false);
        check(visibility.Presentation == FullscreenPresentation.Normal && !visibility.Active, "Disabling automatic mode restores normal visibility");
        var notices = new IslandNotifications(); notices.Post("Before game", "wink", "previous", 0, 0); notices.SetSuspended(true, 1);
        for (var i = 0; i < 20; i++) notices.Post("Volume " + i, "wink", "volume", 0, i + 2);
        notices.Post("Timer finished", "celebrate", "timer", 10, 25);
        check(notices.Current == null && notices.PendingCount == 3 && !notices.Advance(3600), "Full-screen notices stay deferred, coalesce repeated events and survive a long session");
        for (var i = 0; i < 12; i++) notices.Post("Routine " + i, "wink", "routine" + i, 0, i + 30);
        check(notices.PendingCount == 4, "Deferred queue remains bounded even during a long game");
        notices.SetSuspended(false, 4000);
        check(notices.Current?.Key == "timer" && notices.Until == 4003, "Returning from a game prioritizes timer completion and restarts its display time");
        notices.Advance(4003.1); check(notices.Current?.Message == "Routine 9", "Deferred ordinary events resume after the important alert");
        var path = Path.Combine(directory, "fullscreen-settings.json"); new Settings { FullscreenEnabled = false, FullscreenEyes = true }.Save(path);
        var restored = Settings.Load(path);
        check(!restored.FullscreenEnabled && restored.FullscreenEyes, "Full-screen preferences survive restart without resetting the selected appearance");
    }
}

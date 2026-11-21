using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunFullscreenIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>(); Window? fixture = null;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); report.Add("PASS " + message); }
        async Task WaitFor(Func<bool> predicate, string state, int attempts = 50)
        {
            for (var i = 0; i < attempts && !predicate(); i++) await Task.Delay(100);
            if (!predicate()) throw new TimeoutException(state);
        }
        try
        {
            settings.TimerSound = false; SetExpanded(false, false);
            var screen = Forms.Screen.FromHandle(handle); var bounds = screen.Bounds;
            var savedPet = settings.PetOnly; var savedPage = settings.LastPage;
            fixture = new Window { Title = "HelperBar — перевірка повного екрана", Width = 700, Height = 440,
                Background = Ui.Brush("#171925"), ShowInTaskbar = true,
                Content = new TextBlock { Text = "Перевірка HelperBar\nЦе тестове вікно закриється автоматично.", Foreground = Brushes.White, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } };
            fixture.Show(); var fixtureHandle = new WindowInteropHelper(fixture).Handle;
            // Desktop automation may need to activate the test window once when Windows rejects background focus requests.
            await WaitFor(() => FullscreenDetector.GetForegroundWindow() == fixtureHandle, "Activate the HelperBar test window to run the native focus checks", 600);
            void FillScreen()
            {
                fixture.WindowState = WindowState.Normal; fixture.WindowStyle = WindowStyle.None; fixture.ResizeMode = ResizeMode.NoResize;
                SetWindowPos(fixtureHandle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0044); fixture.Activate();
            }
            void RestoreFixture()
            {
                fixture.WindowState = WindowState.Normal;
                SetWindowPos(fixtureHandle, IntPtr.Zero, bounds.Left + 100, bounds.Top + 100, 700, 440, 0x0044); fixture.Activate();
            }
            StartFullscreenMonitoring(); await Task.Delay(400);
            Check(!FullscreenDetector.IsFullscreen(fixtureHandle, handle) && IsVisible, "A normal foreground window leaves the island visible");
            fixture.WindowState = WindowState.Maximized; await Task.Delay(350);
            Check(!FullscreenDetector.IsFullscreen(fixtureHandle, handle) && IsVisible, "A real maximized window is not mistaken for full screen");
            FillScreen(); await WaitFor(() => FullscreenDetector.GetForegroundWindow() == fixtureHandle, "Fixture did not get foreground");
            Check(FullscreenDetector.IsFullscreen(fixtureHandle, handle), "Native client bounds identify the full-screen fixture on the island monitor");
            await WaitFor(() => fullscreenVisibility.Active && !IsVisible, "Island did not hide in full screen");
            Check(!IsVisible && FullscreenDetector.GetForegroundWindow() == fixtureHandle, "Automatic hiding leaves the full-screen application's focus intact");
            for (var i = 0; i < 12; i++) surface.Notify("Гучність " + i, "wink", "volume");
            focus.Set(1); focus.Toggle(DateTimeOffset.Now.AddMinutes(-2)); OnSecond(null, EventArgs.Empty);
            Check(focus.Finished && fullscreenTimerFinished && surface.Toast.Length == 0 && !IsVisible, "Timer finishes while hidden and both its alert and ordinary notices stay deferred");
            RestoreFixture(); await WaitFor(() => !fullscreenVisibility.Active && IsVisible, "Island did not return after full screen");
            Check(FullscreenDetector.GetForegroundWindow() == fixtureHandle && !surface.Expanded, "Automatic return preserves compact state without stealing focus");
            Check(!fullscreenTimerFinished && surface.Toast.Contains("Час вийшов"), "Timer completion is delivered on return instead of disappearing during the game");
            await Task.Delay(350); Capture(Path.Combine(directory, "fullscreen-return-alert.png"));

            settings.FullscreenEyes = true; FillScreen();
            await WaitFor(() => fullscreenClickThrough, "Eyes mode did not become click-through"); await Task.Delay(650);
            Check(IsVisible && surface.PetOnly && !surface.Expanded && !stage.IsHitTestVisible && (Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x20L) != 0,
                "Eyes mode uses a visible non-interactive layered window without changing the saved pet setting");
            Check(SendFullscreenTestMessage(handle, 0x0084, IntPtr.Zero, IntPtr.Zero).ToInt64() == -1 && settings.PetOnly == savedPet,
                "Eyes mode returns transparent native hit tests and preserves the user's normal appearance");
            Capture(Path.Combine(directory, "fullscreen-eyes.png"));
            ToggleIslandManually(); await WaitFor(() => FullscreenDetector.GetForegroundWindow() == handle, "Manual reveal did not get foreground"); await Task.Delay(500);
            Check(IsVisible && surface.Expanded && fullscreenVisibility.Revealed && !fullscreenClickThrough && stage.IsHitTestVisible,
                "The hotkey action temporarily opens an interactive island during full screen");
            fixture.Activate(); await WaitFor(() => fullscreenClickThrough && !surface.Expanded, "Returning to the fixture did not restore eyes mode");
            Check(fullscreenVisibility.Active && !fullscreenVisibility.Revealed, "Returning focus to the game ends the temporary reveal");
            RestoreFixture(); await WaitFor(() => !fullscreenVisibility.Active && !fullscreenClickThrough, "Eyes mode did not clear");
            Check(IsVisible && surface.Expanded && surface.PetOnly == savedPet && FullscreenDetector.GetForegroundWindow() == fixtureHandle,
                "Leaving eyes mode restores the expanded panel and saved pet state without activation");

            SetExpanded(false, false); FillScreen(); await WaitFor(() => fullscreenClickThrough, "Second eyes entry failed");
            ToggleIslandManually(); SetExpanded(false);
            Check(fullscreenClickThrough && !fullscreenVisibility.Revealed, "Closing a manually revealed panel immediately returns to quiet eyes mode");
            HideIslandManually(); RestoreFixture(); await WaitFor(() => !fullscreenVisibility.Active, "Exit while hidden failed");
            Check(!IsVisible && fullscreenVisibility.UserHidden, "A manual hide-to-tray remains hidden after the full-screen application exits");
            ShowIslandManually(); SetExpanded(true); OpenSettings("fullscreen"); await Task.Delay(400);
            Check(fullscreenEnabledBox?.IsChecked == true && fullscreenChoices?.IsEnabled == true, "Appearance exposes the automatic-mode toggle and its two choices");
            fullscreenHideButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!settings.FullscreenEyes, "Hide choice updates the full-screen appearance setting");
            Capture(Path.Combine(directory, "fullscreen-settings.png"));
            FillScreen(); await WaitFor(() => !IsVisible, "Hide choice did not hide the island");
            ToggleIslandManually(); OpenSettings("fullscreen"); fullscreenEnabledBox!.IsChecked = false;
            fixture.Activate(); await Task.Delay(1200);
            Check(IsVisible && !fullscreenVisibility.Active && !fullscreenClickThrough, "Disabling via the real settings toggle keeps the island visible in full screen");
            var path = Path.Combine(directory, "fullscreen-integration-settings.json"); settings.Save(path); var saved = Settings.Load(path);
            Check(!saved.FullscreenEnabled && !saved.FullscreenEyes && saved.PetOnly == savedPet, "Full-screen settings persist independently of the user's pet appearance");
            report.Add($"INFO Native monitor: {bounds.Width}x{bounds.Height}, DPI {WindowDpi:0.##}; other monitor coordinates covered by model tests.");
            report.Add("INFO Test uses its own normal/maximized/borderless Windows window; no game or browser was controlled.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally
        {
            fullscreenTimer.Stop(); fixture?.Close();
            File.WriteAllLines(Path.Combine(directory, "fullscreen-integration.txt"), report); Close();
        }
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendFullscreenTestMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

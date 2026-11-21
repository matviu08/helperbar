using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunDailyIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        VolumeState? original = null;
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        async Task WaitFor(Func<bool> predicate)
        {
            for (var i = 0; i < 40 && !predicate(); i++) await Task.Delay(100);
            if (!predicate()) throw new TimeoutException("Desktop state did not converge.");
        }
        try
        {
            settings.FreePosition = true; settings.PositionX = .24; settings.PositionY = .36; SetPosition();
            await Task.Delay(300); Check(GetWindowRect(handle, out var before), "Native window coordinates can be read");
            SaveDraggedPosition();
            var path = Path.Combine(directory, "daily-settings.json"); settings.Save(path);
            var saved = Settings.Load(path);
            Check(saved.FreePosition && saved.MonitorId.Length > 0 && Math.Abs(saved.PositionX - .24) < .003 && Math.Abs(saved.PositionY - .36) < .003, "Native position and monitor identity survive a settings round-trip");
            SetPosition(); await Task.Delay(200); GetWindowRect(handle, out var after);
            Check(Math.Abs(before.Left - after.Left) <= 1 && Math.Abs(before.Top - after.Top) <= 1, "Reapplying saved native placement does not drift");
            Check(shortcuts?.IsRegistered("open") == true && shortcuts.IsRegistered("pet"), "Both shortcuts register with Windows");
            ResetPosition(); SetExpanded(true); OpenSettings("general"); await Task.Delay(500);
            Capture(Path.Combine(directory, "daily-preferences.png"));
            SelectSettingsSection("music"); await Task.Delay(300); Capture(Path.Combine(directory, "daily-effects.png"));
            volumeService = new AudioVolumeService(); volumeService.Changed += UpdateVolumeControls; volumeService.Start();
            await WaitFor(() => volumeService.State.Available);
            original = volumeService.State;
            Check(original.Level is >= 0 and <= 1, "Core Audio reads real endpoint volume and mute state");
            SwitchPage("Музика"); await Task.Delay(300);
            var lower = Math.Max(0, original.Level - .02f);
            volumeService.SetLevel(lower);
            await WaitFor(() => Math.Abs(volumeService.State.Level - lower) < .001 && Math.Abs(volumeSlider!.Value - lower * 100) < .2);
            Check(volumePercent!.Text == $"{volumeService.State.Percent}%", "Volume slider writes to Windows and receives the real percentage");
            volumeService.ToggleMute();
            await WaitFor(() => volumeService.State.Muted != original.Muted && (string)volumeMute!.Content == (volumeService.State.Muted ? "✕" : "♪"));
            Check(volumeService.State.Available, "Mute writes to the endpoint and updates the visible button");
            // A second independent service simulates a change from another Windows volume control.
            using (var external = new AudioVolumeService())
            {
                external.Start(); await WaitFor(() => external.State.Available);
                external.SetLevel(original.Level);
                await WaitFor(() => Math.Abs(volumeService.State.Level - original.Level) < .001 && Math.Abs(volumeSlider!.Value - original.Level * 100) < .2);
                Check(true, "Independent endpoint changes synchronize back into the slider");
            }
            if (volumeService.State.Muted != original.Muted) volumeService.ToggleMute();
            await WaitFor(() => volumeService.State.Muted == original.Muted);
            await media.StartAsync(); await Task.Delay(600);
            Capture(Path.Combine(directory, "daily-volume.png"));
            Check(musicCard!.ActualHeight <= pageHost.ActualHeight + .1 && volumeSlider!.ActualWidth > 100, "Music card and volume controls fit the expanded layout");
            report.Add($"INFO Native DPI: {VisualTreeHelper.GetDpi(this).DpiScaleX:0.##}; endpoint restored to {original.Percent}%, muted={original.Muted}.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally
        {
            if (original != null && volumeService?.State.Available == true)
            {
                volumeService.SetLevel(original.Level);
                if (volumeService.State.Muted != original.Muted) volumeService.ToggleMute();
                for (var i = 0; i < 20 && (Math.Abs(volumeService.State.Level - original.Level) > .001 || volumeService.State.Muted != original.Muted); i++) await Task.Delay(100);
            }
            File.WriteAllLines(Path.Combine(directory, "daily-integration.txt"), report); Close();
        }
    }
}

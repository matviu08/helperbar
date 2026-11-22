using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelperBar;

public sealed partial class IslandWindow
{
    private async Task RunAudioOutputIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        using var native = new WindowsAudioDevices();
        var originals = Enumerable.Range(0, 3).Select(native.Default).ToArray();
        VolumeState? originalVolume = null;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); report.Add("PASS " + label); }
        async Task WaitFor(Func<bool> predicate)
        {
            for (var i = 0; i < 70 && !predicate(); i++) await Task.Delay(100);
            if (!predicate()) throw new TimeoutException("Audio output state did not converge.");
        }
        void CapturePicker(string name)
        {
            var view = (FrameworkElement)audioOutputsPopup!.Child; view.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
        try
        {
            settings.ReducedMotion = true; Ui.ReducedMotion = true; ResetPosition(); SetExpanded(true);
            StartAudioControls(); audio = new AudioSpectrum();
            await WaitFor(() => !outputService!.State.Loading);
            Check(outputService!.State.Error == null, "Core Audio enumerates active playback devices successfully");
            Check(outputService.State.DefaultId == native.Default(1), "Picker and Windows agree on the default output");
            Check(outputService.State.Devices.All(x => x.Id.Length > 0 && x.Name.Length > 0), "Connected devices expose stable identities and friendly names");
            SwitchPage("Музика"); await Task.Delay(400);
            Check(musicOutput != null && musicCard!.ActualHeight <= pageHost.ActualHeight + .1 && volumeSlider!.ActualWidth > 100, "Music output button and full-width volume slider fit the existing card");
            Capture(Path.Combine(directory, "audio-music.png"));
            musicOutput!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
            Check(audioOutputsPopup?.IsOpen == true && audioOutputRows!.Children.OfType<Button>().Count() == outputService.State.Devices.Count, "Click opens one real choice per connected device");
            CapturePicker("audio-picker-live.png");
            var invalid = await outputService.SelectAsync("helperbar-deliberately-disconnected-endpoint");
            Check(!invalid.Success && native.Default(1) == originals[1], "Stale selection fails without changing the system output");
            if (outputService.State.Devices.Count > 0 && originals[1] != null)
            {
                await WaitFor(() => volumeService!.State.Available && CanAdjustVolume(volumeService.State));
                originalVolume = volumeService!.State;
                // Exercise the real PolicyConfig ABI even on a machine with only one output.
                native.SetDefault(originals[1]!, 1);
                Check(native.Default(1) == originals[1], "Native default-output COM call succeeds on this Windows build");
                var alternative = outputService.State.Devices.FirstOrDefault(x => x.Id != originals[1]);
                if (alternative != null)
                {
                    var switching = SelectAudioOutput(alternative.Id);
                    Check(outputService.State.Busy && !volumeSlider!.IsEnabled && !musicOutput.IsEnabled, "Volume and duplicate selection are disabled during a switch");
                    await switching;
                    await WaitFor(() => native.Default(0) == alternative.Id && native.Default(1) == alternative.Id && volumeService.State.EndpointId == alternative.Id && audio.EndpointId == alternative.Id && CanAdjustVolume(volumeService.State));
                    Check(outputService.State.DefaultId == alternative.Id && audioOutputsPopup == null, "Selecting a real alternate output updates Windows, volume, visualization and closes the picker");
                    Check(native.Default(2) == originals[2], "The real communication endpoint is preserved");
                    // Switch back externally: both services must notice without reopening the page.
                    native.SetDefault(originals[1]!, 1); if (originals[0] != null) native.SetDefault(originals[0]!, 0);
                    await WaitFor(() => outputService.State.DefaultId == originals[1] && volumeService.State.EndpointId == originals[1] && audio.EndpointId == originals[1]);
                    Check(Math.Abs(volumeService.State.Level - originalVolume.Level) < .001 && volumeService.State.Muted == originalVolume.Muted, "External Windows changes synchronize back and original volume/mute are preserved");
                }
                else report.Add("SKIP Alternate physical output: only one active playback endpoint is connected.");
            }
            CloseAudioOutputs();
            settings.Home.Items.Clear(); settings.Home.AddWidget("volume"); SwitchPage("Головна"); SetExpanded(true); await Task.Delay(300);
            Check(homeOutput != null && homeVolume!.ActualHeight == 24 && homeOutput.ActualHeight == 22, "Home volume tile includes a compact output selector");
            Check(surface.Expanded && expanded.IsVisible && expanded.Opacity > .99 && homeOutput!.TransformToAncestor(pageHost).Transform(new Point(0, homeOutput.ActualHeight)).Y <= pageHost.ActualHeight, "The home output selector is visible and fits inside its tile");
            Capture(Path.Combine(directory, "audio-home.png"));
            homeOutput!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
            Check(audioOutputsPopup?.IsOpen == true, "Home output selector opens the same live picker");
            SwitchPage("Музика"); Check(audioOutputsPopup == null, "Navigating away closes the detached popup");
            ShowAudioOutputs(musicOutput!); await Task.Delay(100);
            // UI fixtures cover long names, many devices and empty/error states without fake hardware writes.
            outputService.Changed -= UpdateAudioOutputs;
            var fixture = new AudioOutputs(Enumerable.Range(0, 9).Select(i => new AudioOutput("fixture-" + i, "Навушники з дуже довгою назвою пристрою — тест " + i, i % 2 == 0)).ToArray(), "fixture-1");
            RenderAudioOutputRows(fixture); await Task.Delay(100); CapturePicker("audio-picker-many.png");
            var picker = (FrameworkElement)audioOutputsPopup!.Child;
            Check(picker.ActualWidth == 310 && picker.ActualHeight < 380 && audioOutputRows!.Children.OfType<Button>().All(x => x.ActualWidth <= 284), "Long names and many outputs remain inside a bounded scrolling picker");
            RenderAudioOutputRows(new(Array.Empty<AudioOutput>(), null)); await Task.Delay(100); CapturePicker("audio-picker-empty.png");
            Check(audioOutputRows!.Children.OfType<Button>().Count() == 0 && audioOutputRows.Children.Count == 1, "Empty picker explains how to connect an output without invented choices");
            audioOutputError = "Пристрій уже від’єднано. Вибери інший вихід."; RenderAudioOutputRows(fixture); await Task.Delay(100); CapturePicker("audio-picker-error.png");
            Check(audioOutputHint!.Text == audioOutputError, "Switch errors remain visible alongside available alternatives");
            SetExpanded(false); Check(audioOutputsPopup == null, "Collapsing the island closes the output picker");
            report.Add("INFO Active outputs: " + string.Join("; ", native.Read().Devices.Select(x => x.Name)));
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally
        {
            for (var role = 0; role < 3; role++)
            {
                try { if (originals[role] is { } id && native.Default(role) != id) native.SetDefault(id, role); }
                catch (Exception ex) { report.Add("FAIL Restoring Windows role " + role + ": " + ex.Message); Environment.ExitCode = 1; }
            }
            try { Check(Enumerable.Range(0, 3).All(role => native.Default(role) == originals[role]), "All original Windows audio defaults are restored after the test"); }
            catch (Exception ex) { report.Add("FAIL " + ex.Message); Environment.ExitCode = 1; }
            File.WriteAllLines(Path.Combine(directory, "audio-output-integration.txt"), report); Close();
        }
    }
}

internal static class AudioProbe
{
    public static int Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        try
        {
            using var devices = new WindowsAudioDevices();
            File.WriteAllText(Path.Combine(directory, "audio-probe.json"), JsonSerializer.Serialize(new { Outputs = devices.Read(), Defaults = Enumerable.Range(0, 3).Select(devices.Default).ToArray(), Windows = Environment.OSVersion.VersionString }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "audio-probe.json"), ex.ToString()); return 1; }
    }
}

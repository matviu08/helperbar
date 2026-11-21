using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HelperBar;

internal sealed class MediaFixture : IDisposable
{
    private readonly Windows.Media.Playback.MediaPlayer player = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private SystemMediaTransportControls controls => player.SystemMediaTransportControls;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool alternate;
    private RandomAccessStreamReference? firstCover, secondCover;
    public MediaPlaybackState PlaybackState => player.PlaybackSession.PlaybackState;

    public async Task StartAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var wave = Path.Combine(folder, "test-signal.wav");
        using (var stream = File.Create(wave))
        using (var writer = new BinaryWriter(stream))
        {
            const int rate = 48000, seconds = 30, length = rate * seconds * 4;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(length + 36); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(length);
            for (var i = 0; i < rate * seconds; i++)
            {
                var t = i / (double)rate;
                var envelope = .25 + .75 * Math.Pow((Math.Sin(t * Math.PI * 4) + 1) * .5, 3);
                var signal = (Math.Sin(2 * Math.PI * 110 * t) * .22 + Math.Sin(2 * Math.PI * 880 * t) * .10 + Math.Sin(2 * Math.PI * 6000 * t) * .05) * envelope;
                var sample = (short)(signal * short.MaxValue); writer.Write(sample); writer.Write(sample);
            }
        }
        var cover1 = Path.Combine(folder, "test-cover-1.png"); var cover2 = Path.Combine(folder, "test-cover-2.png");
        CreateCover(cover1, false); CreateCover(cover2, true);
        firstCover = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(cover1));
        secondCover = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(cover2));
        player.CommandManager.IsEnabled = false;
        controls.IsEnabled = true; controls.IsPlayEnabled = true; controls.IsPauseEnabled = true; controls.IsNextEnabled = true; controls.IsPreviousEnabled = true;
        controls.ButtonPressed += (_, e) => dispatcher.BeginInvoke(() =>
        {
            if (e.Button == SystemMediaTransportControlsButton.Play) player.Play();
            if (e.Button == SystemMediaTransportControlsButton.Pause) player.Pause();
            if (e.Button is SystemMediaTransportControlsButton.Next or SystemMediaTransportControlsButton.Previous) { alternate = e.Button == SystemMediaTransportControlsButton.Next; UpdateMetadata(); }
            UpdateTimeline();
        });
        controls.PlaybackPositionChangeRequested += (_, e) => dispatcher.BeginInvoke(() => { player.PlaybackSession.Position = e.RequestedPlaybackPosition; UpdateTimeline(); });
        player.Source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(wave));
        player.Volume = .035; // Quiet local signal; the user's endpoint volume is not changed.
        UpdateMetadata();
        timer.Tick += (_, _) => UpdateTimeline(); timer.Start(); player.Play();
    }
    private void UpdateMetadata()
    {
        var display = controls.DisplayUpdater; display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = alternate ? "Second light" : "Local waves";
        display.MusicProperties.Artist = "HelperBar · тестовий сигнал";
        display.MusicProperties.AlbumTitle = "Перевірка інтеграції";
        display.Thumbnail = alternate ? secondCover : firstCover; display.Update();
    }
    private void UpdateTimeline()
    {
        controls.PlaybackStatus = player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(30), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(30), Position = player.PlaybackSession.Position
        });
    }
    private static void CreateCover(string path, bool second)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var accent = second ? Color.FromRgb(244, 148, 111) : Color.FromRgb(166, 139, 255);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(18, 22, 42), accent, 45), null, new Rect(0, 0, 300, 300));
            dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(255, 224, 167), accent, 90), null, new Point(170, 122), 74, 74);
            for (var i = 0; i < 13; i++) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(110, 19, 21, 39)), 10), new Point(-30, 150 + i * 16), new Point(330, 100 + i * 16));
            var label = new FormattedText(second ? "SECOND\nLIGHT" : "LOCAL\nWAVES", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 30, Brushes.White, 1);
            dc.DrawText(label, new Point(22, 210));
        }
        var bitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
    }
    public void Dispose() { timer.Stop(); player.Pause(); controls.IsEnabled = false; player.Dispose(); }
}

public sealed partial class IslandWindow
{
    private async Task RunMusicIntegrationTest()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        var report = new List<string>();
        using var fixture = new MediaFixture();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        try
        {
            audio = new AudioSpectrum(); await media.StartAsync(); await fixture.StartAsync(directory);
            for (var attempt = 0; attempt < 30 && (!media.Track.Artist.Contains("тестовий сигнал") || media.Track.Artwork == null); attempt++)
            {
                var source = media.Sources.FirstOrDefault(s => s.Id.Contains("helperbar", StringComparison.OrdinalIgnoreCase));
                if (source != null) media.SelectSource(source.Id);
                await Task.Delay(300); await media.RefreshAsync();
            }
            Check(media.Track.Title == "Local waves" && media.Track.Artist.Contains("тестовий сигнал"), "Windows media session provides real title and artist");
            Check(media.Track.Artwork is { PixelWidth: > 0 }, "Cover image loads through the Windows thumbnail stream");
            for (var attempt = 0; attempt < 25 && (!media.Track.Playing || media.Track.Duration.TotalSeconds != 30); attempt++) { await Task.Delay(200); await media.RefreshAsync(); }
            Check(media.Track.Playing && media.Track.Duration.TotalSeconds == 30, $"Playback state and duration come from the media session (playing={media.Track.Playing}, duration={media.Track.Duration.TotalSeconds}, player={fixture.PlaybackState})");
            Check(audio.Available, "WASAPI loopback opens the default render endpoint");
            var maximum = 0f; var changes = 0; var last = -1f;
            for (var i = 0; i < 35; i++) { await Task.Delay(50); var frame = audio.Snapshot; maximum = Math.Max(maximum, frame.Level); if (Math.Abs(frame.Level - last) > .01) changes++; last = frame.Level; }
            Check(maximum > .01 && changes > 4, "Live loopback audio drives changing spectrum values");
            SwitchPage("Музика"); SetExpanded(true); await Task.Delay(700); Capture(Path.Combine(directory, "music-playing.png"));
            SetExpanded(false); await Task.Delay(700); Capture(Path.Combine(directory, "music-compact.png"));
            Check(await media.CommandAsync("toggle"), "Targeted media-session pause command is accepted");
            await Task.Delay(700); await media.RefreshAsync(); Check(!media.Track.Playing && fixture.PlaybackState == MediaPlaybackState.Paused, "Pause updates both the actual player and the island");
            SetExpanded(true); await Task.Delay(400); Capture(Path.Combine(directory, "music-paused.png"));
            if (media.Track.CanSeek)
            {
                Check(await media.CommandAsync("seek", .3), "Player accepts seek command"); await Task.Delay(500); await media.RefreshAsync();
                Check(Math.Abs(media.Track.Position.TotalSeconds - 9) < 1.5, "Seek changes the actual media timeline");
            }
            else report.Add("INFO Test player does not advertise seeking; disabled control verified.");
            var oldArtwork = media.Track.Artwork;
            Check(await media.CommandAsync("next"), "Targeted next-track command is accepted");
            for (var i = 0; i < 20 && media.Track.Title != "Second light"; i++) { await Task.Delay(250); await media.RefreshAsync(); }
            Check(media.Track.Title == "Second light" && media.Track.Artwork != oldArtwork, "Track change replaces title and artwork without stale results");
            Check(await media.CommandAsync("toggle"), "Resume command is accepted"); await Task.Delay(700); await media.RefreshAsync(); Check(media.Track.Playing, "Resuming updates the playing state");
            Capture(Path.Combine(directory, "music-next-track.png"));
            SwitchPage("Головна"); await Task.Delay(400);
            Check(homeMusicTitle?.Text == media.Track.Title && homeArtwork?.Source == media.Track.Artwork, "Home music widget shares the real title and artwork with the full player");
            homePlay!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            for (var i = 0; i < 30 && (fixture.PlaybackState != MediaPlaybackState.Paused || media.Track.Playing); i++) { await Task.Delay(100); await media.RefreshAsync(); }
            Check(fixture.PlaybackState == MediaPlaybackState.Paused && !media.Track.Playing, $"Home play button pauses only the selected test player (player={fixture.PlaybackState}, playing={media.Track.Playing}, enabled={homePlay.IsEnabled})");
            Capture(Path.Combine(directory, "home-live-music.png"));
            SwitchPage("Музика"); await Task.Delay(200);
            Check(!media.Track.Playing && musicPlay!.Content.ToString() == "▶", "Full music page retains the pause state set from home");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "music-integration.txt"), report); Close(); }
    }
}

internal static class MediaProbe
{
    public static async Task<int> RunAsync()
    {
        using var service = new NowPlayingService(); using var spectrum = new AudioSpectrum();
        await service.RefreshAsync(); await Task.Delay(1200); await service.RefreshAsync();
        var peak = 0f; for (var i = 0; i < 20; i++) { peak = Math.Max(peak, spectrum.Snapshot.Level); await Task.Delay(50); }
        var track = service.Track;
        var folder = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "media-probe.json"), JsonSerializer.Serialize(new { track.Available, track.Title, track.Artist, track.Source, track.Playing, Artwork = track.Artwork != null, Duration = track.Duration.TotalSeconds, track.CanSeek, track.CanToggle, AudioAvailable = spectrum.Available, AudioStatus = spectrum.Status, Peak = peak, service.Sources, service.Error }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}

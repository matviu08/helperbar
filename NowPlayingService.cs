using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace HelperBar;

public sealed record MediaTrack
{
    public bool Available { get; init; }
    public string Title { get; init; } = "Тут житиме твоя музика";
    public string Artist { get; init; } = "Увімкни трек у YouTube Music або Spotify";
    public string Album { get; init; } = "";
    public string Source { get; init; } = "Очікуємо плеєр";
    public string SourceId { get; init; } = "";
    public BitmapSource? Artwork { get; init; }
    public Color? ArtworkColor { get; init; }
    public bool Playing { get; init; }
    public bool CanToggle { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanSeek { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan MinSeek { get; init; }
    public TimeSpan MaxSeek { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public double Rate { get; init; } = 1;
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var elapsed = Playing ? Math.Max(0, (now - UpdatedAt).TotalSeconds) * Rate : 0;
        var seconds = Position.TotalSeconds + elapsed;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, Start.TotalSeconds, Math.Max(Start.TotalSeconds, End.TotalSeconds)));
    }
    public static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{Math.Max(0, (int)time.TotalMinutes)}:{Math.Max(0, time.Seconds):00}";
}

public sealed record MediaSourceChoice(string Id, string Name);

/// <summary>Only reads the media information that players publish to Windows. No browser history or network access.</summary>
public sealed class NowPlayingService : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool refreshing, disposed;
    private int metadataRevision = 1, loadedRevision;
    private DateTimeOffset lastMetadataRead;
    private string? preferredSource;
    private readonly SemaphoreSlim commandGate = new(1, 1);
    public MediaTrack Track { get; private set; } = new();
    public IReadOnlyList<MediaSourceChoice> Sources { get; private set; } = Array.Empty<MediaSourceChoice>();
    public string? Error { get; private set; }
    public event Action<MediaTrack>? Changed;

    public NowPlayingService() { timer.Tick += async (_, _) => await RefreshAsync(); }
    public async Task StartAsync() { timer.Start(); await RefreshAsync(); }
    public void SelectSource(string? source) { preferredSource = source; Interlocked.Increment(ref metadataRevision); _ = RefreshAsync(); }

    public async Task RefreshAsync()
    {
        if (refreshing || disposed) return;
        refreshing = true;
        try
        {
            manager ??= await WithTimeout(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            if (disposed) return;
            var sessions = manager.GetSessions().ToArray();
            Sources = sessions.Select(s => new MediaSourceChoice(s.SourceAppUserModelId, SourceName(s.SourceAppUserModelId))).DistinctBy(s => s.Id).ToArray();
            var current = manager.GetCurrentSession();
            var selected = preferredSource == null ? null : sessions.FirstOrDefault(s => s.SourceAppUserModelId == preferredSource);
            if (selected == null)
            {
                preferredSource = null;
                selected = current != null && IsPlaying(current) ? current : sessions.FirstOrDefault(IsPlaying) ?? current ?? sessions.FirstOrDefault();
            }
            if (session == null || selected == null || !session.Equals(selected))
            {
                Detach(); session = selected;
                if (session != null) { session.MediaPropertiesChanged += MetadataChanged; session.PlaybackInfoChanged += PlaybackChanged; }
                Interlocked.Increment(ref metadataRevision);
                Publish(new MediaTrack { Source = selected == null ? "Очікуємо плеєр" : SourceName(selected.SourceAppUserModelId) });
            }
            if (selected == null) { Error = null; return; }
            var revision = Volatile.Read(ref metadataRevision);
            var track = Track;
            // Some browser sessions omit change events, so also refresh metadata at a low rate.
            if (loadedRevision != revision || DateTimeOffset.Now - lastMetadataRead > TimeSpan.FromSeconds(3))
            {
                var properties = await WithTimeout(selected.TryGetMediaPropertiesAsync());
                if (disposed || !Equals(session, selected) || revision != Volatile.Read(ref metadataRevision)) return;
                var identityChanged = !track.Available || track.SourceId != selected.SourceAppUserModelId || track.Title != properties.Title || track.Artist != properties.Artist || track.Album != properties.AlbumTitle;
                BitmapSource? artwork = identityChanged ? null : track.Artwork;
                Color? color = identityChanged ? null : track.ArtworkColor;
                if (properties.Thumbnail != null && (identityChanged || loadedRevision != revision || artwork == null))
                {
                    try { artwork = await ReadArtwork(properties.Thumbnail); color = artwork == null ? null : CoverColor(artwork); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { artwork = null; color = null; }
                }
                else if (properties.Thumbnail == null) { artwork = null; color = null; }
                if (disposed || !Equals(session, selected) || revision != Volatile.Read(ref metadataRevision)) return;
                track = track with
                {
                    Available = true, Title = string.IsNullOrWhiteSpace(properties.Title) ? "Без назви" : properties.Title,
                    Artist = string.IsNullOrWhiteSpace(properties.Artist) ? SourceName(selected.SourceAppUserModelId) : properties.Artist,
                    Album = properties.AlbumTitle ?? "", Source = SourceName(selected.SourceAppUserModelId), SourceId = selected.SourceAppUserModelId,
                    Artwork = artwork, ArtworkColor = color
                };
                loadedRevision = revision; lastMetadataRead = DateTimeOffset.Now;
            }
            var playback = selected.GetPlaybackInfo();
            var timeline = selected.GetTimelineProperties();
            var controls = playback.Controls;
            var now = DateTimeOffset.Now;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var rate = playback.PlaybackRate ?? 1;
            var updated = timeline.LastUpdatedTime;
            // Invalid timestamps must not extrapolate from the WinRT epoch.
            if (updated == default || updated > now || now - updated > TimeSpan.FromDays(1)) updated = now;
            Error = null;
            Publish(track with
            {
                Playing = playing, CanToggle = controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
                CanPrevious = controls.IsPreviousEnabled, CanNext = controls.IsNextEnabled, CanSeek = controls.IsPlaybackPositionEnabled && timeline.EndTime > timeline.StartTime,
                Start = timeline.StartTime, End = timeline.EndTime, Position = timeline.Position, UpdatedAt = updated,
                MinSeek = timeline.MinSeekTime, MaxSeek = timeline.MaxSeekTime, Rate = double.IsFinite(rate) && rate > 0 ? rate : 1
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = "Не вдалося прочитати плеєр. Спробуємо ще раз.";
            Detach(); session = null; manager = null;
            if (!disposed) Publish(new MediaTrack { Artist = Error });
        }
        finally { refreshing = false; }
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession value)
    {
        try { return value.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }
    private void MetadataChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Interlocked.Increment(ref metadataRevision);
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) { /* timeline and transport state are polled independently of artwork */ }
    private void Publish(MediaTrack track) { Track = track; Changed?.Invoke(track); }
    private void Detach()
    {
        if (session == null) return;
        try { session.MediaPropertiesChanged -= MetadataChanged; session.PlaybackInfoChanged -= PlaybackChanged; } catch { }
    }

    public async Task<bool> CommandAsync(string command, double fraction = 0)
    {
        if (disposed || session == null || !await commandGate.WaitAsync(0)) return false;
        try
        {
            var selected = session;
            var playback = selected.GetPlaybackInfo();
            var controls = playback.Controls;
            IAsyncOperation<bool>? operation = command switch
            {
                "toggle" when controls.IsPlayPauseToggleEnabled => selected.TryTogglePlayPauseAsync(),
                "toggle" when playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled => selected.TryPauseAsync(),
                "toggle" when playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPlayEnabled => selected.TryPlayAsync(),
                "previous" when controls.IsPreviousEnabled => selected.TrySkipPreviousAsync(),
                "next" when controls.IsNextEnabled => selected.TrySkipNextAsync(),
                "seek" when Track.CanSeek && double.IsFinite(fraction) => selected.TryChangePlaybackPositionAsync(SeekTarget(Track, fraction).Ticks),
                _ => null
            };
            var result = operation != null && await WithTimeout(operation);
            await RefreshAsync();
            return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
        finally { commandGate.Release(); }
    }

    private static async Task<T> WithTimeout<T>(IAsyncOperation<T> operation)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        return await operation.AsTask(cancel.Token).WaitAsync(cancel.Token);
    }

    public static TimeSpan SeekTarget(MediaTrack track, double fraction)
    {
        var min = Math.Max(track.Start.Ticks, track.MinSeek.Ticks);
        var max = track.MaxSeek > track.MinSeek ? Math.Min(track.End.Ticks, track.MaxSeek.Ticks) : track.End.Ticks;
        return TimeSpan.FromTicks((long)Math.Clamp(track.Start.Ticks + track.Duration.Ticks * Math.Clamp(fraction, 0, 1), min, Math.Max(min, max)));
    }
    private static async Task<BitmapSource?> ReadArtwork(IRandomAccessStreamReference thumbnail)
    {
        using var stream = await WithTimeout(thumbnail.OpenReadAsync());
        if (stream.Size == 0 || stream.Size > 16 * 1024 * 1024) return null;
        using var reader = new DataReader(stream);
        var length = await WithTimeout(reader.LoadAsync((uint)stream.Size));
        var bytes = new byte[length]; reader.ReadBytes(bytes);
        using var memory = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 300; image.StreamSource = memory; image.EndInit(); image.Freeze(); return image;
    }
    private static Color CoverColor(BitmapSource source)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform(16d / source.PixelWidth, 16d / source.PixelHeight));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        double r = 0, g = 0, b = 0, total = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var max = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2])); var min = Math.Min(bytes[i], Math.Min(bytes[i + 1], bytes[i + 2]));
            if (max < 35 || min > 235 || bytes[i + 3] < 128) continue;
            var weight = 1 + (max - min) / 40d;
            r += bytes[i + 2] * weight; g += bytes[i + 1] * weight; b += bytes[i] * weight; total += weight;
        }
        if (total == 0) return Color.FromRgb(188, 172, 255);
        var boost = 1.4; return Color.FromRgb((byte)Math.Clamp(r / total * boost, 55, 245), (byte)Math.Clamp(g / total * boost, 55, 245), (byte)Math.Clamp(b / total * boost, 55, 245));
    }
    private static string SourceName(string id)
    {
        var lower = id.ToLowerInvariant();
        foreach (var (key, name) in new[] { ("spotify", "Spotify"), ("msedge", "Microsoft Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("brave", "Brave"), ("opera", "Opera"), ("vlc", "VLC"), ("aimp", "AIMP"), ("helperbar", "HelperBar · тест") }) if (lower.Contains(key)) return name;
        return id.Contains('!') ? id.Split('!')[0].Split('_')[0] : Path.GetFileNameWithoutExtension(id);
    }
    public void Dispose() { disposed = true; timer.Stop(); Detach(); session = null; Changed = null; }
}

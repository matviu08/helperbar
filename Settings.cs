using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelperBar;

public sealed class Settings
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; }
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");
    public string Accent { get; set; } = "#BCACFF";
    public string Body { get; set; } = "#08090C";
    public bool Eyes { get; set; } = true;
    public bool Liquid { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool ShowSeconds { get; set; }
    public bool OpenOnHover { get; set; }
    public bool TimerSound { get; set; } = true;
    public bool AudioReactive { get; set; } = true;
    public bool CompactMusic { get; set; } = true;
    public double AudioSensitivity { get; set; } = 1;
    public double SpectrumIntensity { get; set; } = 1;
    public double EdgeIntensity { get; set; } = 1;
    public double GlowIntensity { get; set; } = 1;
    public double EyeIntensity { get; set; } = 1;
    public double Scale { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public double TopOffset { get; set; } = 12;
    public int ScreenIndex { get; set; }
    public string MonitorId { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public bool FreePosition { get; set; }
    public double PositionX { get; set; } = .5;
    public double PositionY { get; set; }
    public string LastPage { get; set; } = "Головна";
    public string SettingsSection { get; set; } = "appearance";
    public bool PetOnly { get; set; }
    public string OpenShortcut { get; set; } = "Ctrl+Alt+Space";
    public string PetShortcut { get; set; } = "Ctrl+Alt+E";
    public string Note { get; set; } = "";
    public int FocusMinutes { get; set; } = 25;
    public HomeLayout Home { get; set; } = HomeLayout.Default();
    public bool BatteryMouse { get; set; } = true;
    public bool BatteryKeyboard { get; set; } = true;
    public bool BatteryHeadphones { get; set; } = true;
    public bool NotifyDeviceConnections { get; set; } = true;
    public bool NotifyLowBattery { get; set; } = true;
    public bool FullscreenEnabled { get; set; } = true;
    public bool FullscreenEyes { get; set; }
    [JsonIgnore] public string? LoadWarning { get; private set; }

    public static Settings Load(string? path = null)
    {
        path ??= Path.Combine(DataDirectory, "settings.json");
        try
        {
            if (!File.Exists(path)) return new() { SchemaVersion = CurrentSchemaVersion };
            var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
            result.Scale = double.IsFinite(result.Scale) ? Math.Clamp(result.Scale, .75, 1.4) : 1;
            result.Speed = double.IsFinite(result.Speed) ? Math.Clamp(result.Speed, .6, 1.6) : 1;
            result.AudioSensitivity = double.IsFinite(result.AudioSensitivity) ? Math.Clamp(result.AudioSensitivity, .6, 1.8) : 1;
            result.SpectrumIntensity = Intensity(result.SpectrumIntensity);
            result.EdgeIntensity = Intensity(result.EdgeIntensity);
            result.GlowIntensity = Intensity(result.GlowIntensity);
            result.EyeIntensity = Intensity(result.EyeIntensity);
            result.PositionX = double.IsFinite(result.PositionX) ? Math.Clamp(result.PositionX, 0, 1) : .5;
            result.PositionY = double.IsFinite(result.PositionY) ? Math.Clamp(result.PositionY, 0, 1) : 0;
            result.MonitorId ??= ""; result.MonitorName ??= "";
            if (result.LastPage is not ("Головна" or "Музика" or "Фокус" or "Нотатки" or "Вигляд")) result.LastPage = "Головна";
            if (result.SettingsSection is not ("appearance" or "music" or "devices" or "fullscreen" or "position" or "general")) result.SettingsSection = "appearance";
            if (!Shortcut.TryParse(result.OpenShortcut, out _)) result.OpenShortcut = "Ctrl+Alt+Space";
            if (!Shortcut.TryParse(result.PetShortcut, out _)) result.PetShortcut = "Ctrl+Alt+E";
            result.SchemaVersion = CurrentSchemaVersion;
            result.TopOffset = double.IsFinite(result.TopOffset) ? Math.Clamp(result.TopOffset, 0, 180) : 12;
            result.FocusMinutes = Math.Clamp(result.FocusMinutes, 1, 180);
            result.Note ??= "";
            result.Home ??= HomeLayout.Default(); result.Home.Normalize();
            if (!ValidColor(result.Accent)) result.Accent = "#BCACFF";
            if (!ValidColor(result.Body)) result.Body = "#08090C";
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            if (!path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".bak"))
            {
                var recovered = Load(path + ".bak");
                if (recovered.LoadWarning == null) { recovered.LoadWarning = "Налаштування відновлено з резервної копії."; return recovered; }
            }
            return new() { LoadWarning = "Не вдалося прочитати налаштування. Використано стандартний вигляд." };
        }
    }

    private static bool ValidColor(string? color) => color is { Length: 7 } && color[0] == '#' && uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _);
    private static double Intensity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1.5) : 1;

    public void Save(string? path = null)
    {
        path ??= Path.Combine(DataDirectory, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SchemaVersion = CurrentSchemaVersion;
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path))
        {
            try
            {
                var previous = File.ReadAllText(path);
                using var valid = JsonDocument.Parse(previous);
                if (valid.RootElement.ValueKind != JsonValueKind.Object || JsonSerializer.Deserialize<Settings>(previous) == null) throw new JsonException();
                File.Copy(path, path + ".bak", true);
            }
            catch (JsonException) { File.Copy(path, path + ".corrupt", true); }
        }
        File.Move(temporary, path, true);
    }
}

public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public Spring(double value) { Value = Target = value; }
    public void Step(double elapsed, double speed = 1, bool reduced = false)
    {
        if (reduced) { Value = Target; Velocity = 0; return; }
        var remaining = Math.Min(elapsed, .05) * speed;
        while (remaining > 0)
        {
            var dt = Math.Min(remaining, 1.0 / 240);
            Velocity += ((Target - Value) * 230 - Velocity * 23) * dt;
            Value += Velocity * dt;
            remaining -= dt;
        }
        if (Math.Abs(Target - Value) < .002 && Math.Abs(Velocity) < .002) { Value = Target; Velocity = 0; }
    }
}

public sealed class FocusTimer
{
    private DateTimeOffset? deadline;
    private TimeSpan remaining = TimeSpan.FromMinutes(25);
    public bool Running => deadline.HasValue;
    public bool Finished { get; private set; }
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining(DateTimeOffset now) => deadline is { } end ? TimeSpan.FromTicks(Math.Max(0, (end - now).Ticks)) : remaining;
    public void Set(int minutes) { Duration = remaining = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180)); deadline = null; Finished = false; }
    public void Toggle(DateTimeOffset now)
    {
        if (Running) { remaining = Remaining(now); deadline = null; }
        else { if (remaining <= TimeSpan.Zero) remaining = Duration; deadline = now + remaining; Finished = false; }
    }
    public void Reset() { deadline = null; remaining = Duration; Finished = false; }
    public bool Tick(DateTimeOffset now)
    {
        if (deadline.HasValue && now >= deadline.Value) { deadline = null; remaining = TimeSpan.Zero; Finished = true; return true; }
        return false;
    }
    public static string Format(TimeSpan time) => $"{(int)Math.Ceiling(time.TotalSeconds) / 60:00}:{(int)Math.Ceiling(time.TotalSeconds) % 60:00}";
}

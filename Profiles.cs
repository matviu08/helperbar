using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HelperBar;

public sealed record ProfileDefinition(string Id, string Title, string Description);

// Only presentation and notification preferences belong here. Personal content and live timers stay in Settings.
public sealed class ProfileOptions
{
    public HomeLayout Home { get; set; } = HomeLayout.Default();
    public string Accent { get; set; } = "#BCACFF";
    public string Body { get; set; } = "#08090C";
    public bool Eyes { get; set; } = true;
    public bool Liquid { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public double MotionIntensity { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public bool AudioReactive { get; set; } = true;
    public bool CompactMusic { get; set; } = true;
    public double AudioSensitivity { get; set; } = 1;
    public double SpectrumIntensity { get; set; } = 1;
    public double EdgeIntensity { get; set; } = 1;
    public double GlowIntensity { get; set; } = 1;
    public double EyeIntensity { get; set; } = 1;
    public bool NotifyDeviceConnections { get; set; } = true;
    public bool NotifyLowBattery { get; set; } = true;
    public bool NotifyTaskCompletion { get; set; } = true;
    public bool NotifyFocusBanner { get; set; } = true;
    public bool TimerSound { get; set; } = true;
    public bool BatteryMouse { get; set; } = true;
    public bool BatteryKeyboard { get; set; } = true;
    public bool BatteryHeadphones { get; set; } = true;
    public bool FullscreenEnabled { get; set; } = true;
    public bool FullscreenEyes { get; set; }

    public static ProfileOptions Capture(Settings s) => new()
    {
        Home = s.Home.Copy(), Accent = s.Accent, Body = s.Body, Eyes = s.Eyes, Liquid = s.Liquid,
        ReducedMotion = s.ReducedMotion, MotionIntensity = s.MotionIntensity, Speed = s.Speed,
        AudioReactive = s.AudioReactive, CompactMusic = s.CompactMusic, AudioSensitivity = s.AudioSensitivity,
        SpectrumIntensity = s.SpectrumIntensity, EdgeIntensity = s.EdgeIntensity, GlowIntensity = s.GlowIntensity, EyeIntensity = s.EyeIntensity,
        NotifyDeviceConnections = s.NotifyDeviceConnections, NotifyLowBattery = s.NotifyLowBattery,
        NotifyTaskCompletion = s.NotifyTaskCompletion, NotifyFocusBanner = s.NotifyFocusBanner, TimerSound = s.TimerSound,
        BatteryMouse = s.BatteryMouse, BatteryKeyboard = s.BatteryKeyboard, BatteryHeadphones = s.BatteryHeadphones,
        FullscreenEnabled = s.FullscreenEnabled, FullscreenEyes = s.FullscreenEyes
    };
    public void Apply(Settings s)
    {
        s.Home = Home.Copy(); s.Accent = Accent; s.Body = Body; s.Eyes = Eyes; s.Liquid = Liquid;
        s.ReducedMotion = ReducedMotion; s.MotionIntensity = MotionIntensity; s.Speed = Speed;
        s.AudioReactive = AudioReactive; s.CompactMusic = CompactMusic; s.AudioSensitivity = AudioSensitivity;
        s.SpectrumIntensity = SpectrumIntensity; s.EdgeIntensity = EdgeIntensity; s.GlowIntensity = GlowIntensity; s.EyeIntensity = EyeIntensity;
        s.NotifyDeviceConnections = NotifyDeviceConnections; s.NotifyLowBattery = NotifyLowBattery;
        s.NotifyTaskCompletion = NotifyTaskCompletion; s.NotifyFocusBanner = NotifyFocusBanner; s.TimerSound = TimerSound;
        s.BatteryMouse = BatteryMouse; s.BatteryKeyboard = BatteryKeyboard; s.BatteryHeadphones = BatteryHeadphones;
        s.FullscreenEnabled = FullscreenEnabled; s.FullscreenEyes = FullscreenEyes;
    }
    public void Normalize()
    {
        Home ??= HomeLayout.Default(); Home.Normalize();
        if (!Settings.ValidColor(Accent)) Accent = "#BCACFF";
        if (!Settings.ValidColor(Body)) Body = "#08090C";
        MotionIntensity = Settings.Intensity(MotionIntensity);
        SpectrumIntensity = Settings.Intensity(SpectrumIntensity); EdgeIntensity = Settings.Intensity(EdgeIntensity);
        GlowIntensity = Settings.Intensity(GlowIntensity); EyeIntensity = Settings.Intensity(EyeIntensity);
        Speed = double.IsFinite(Speed) ? Math.Clamp(Speed, .6, 1.6) : 1;
        AudioSensitivity = double.IsFinite(AudioSensitivity) ? Math.Clamp(AudioSensitivity, .6, 1.8) : 1;
    }
}

public sealed class IslandProfile
{
    public string Id { get; set; } = "personal";
    public ProfileOptions Options { get; set; } = new();
    [JsonIgnore] public string Title => ProfileCollection.Definitions.FirstOrDefault(x => x.Id == Id)?.Title ?? "Моє";
}

public sealed class ProfileCollection
{
    public static readonly ProfileDefinition[] Definitions =
    {
        new("personal", "Моє", "Твій звичний острів"), new("work", "Робота", "Справи, фокус і спокійні ефекти"),
        new("music", "Музика", "Плеєр, звук і виразні реакції"), new("games", "Ігри", "Стан ПК і менше відволікань")
    };
    public string ActiveId { get; set; } = "personal";
    public List<IslandProfile> Items { get; set; } = new();
    [JsonIgnore] public IslandProfile Active => Items.First(x => x.Id == ActiveId);

    public void Normalize(Settings live)
    {
        Items ??= new();
        var valid = new List<IslandProfile>();
        foreach (var definition in Definitions)
        {
            var existing = Items.FirstOrDefault(x => x != null && x.Id == definition.Id && x.Options != null);
            existing ??= new IslandProfile { Id = definition.Id, Options = definition.Id == "personal" ? ProfileOptions.Capture(live) : Preset(definition.Id) };
            existing.Options.Normalize(); valid.Add(existing);
        }
        Items = valid;
        // The root settings mirror the current live profile. Keep them when recovering a missing active ID.
        if (!Definitions.Any(x => x.Id == ActiveId)) ActiveId = "personal";
        Active.Options = ProfileOptions.Capture(live);
    }
    public void CaptureActive(Settings live)
    {
        if (Items.Count != Definitions.Length || !Items.Any(x => x.Id == ActiveId)) Normalize(live);
        Active.Options = ProfileOptions.Capture(live);
    }
    public bool Switch(Settings live, string id)
    {
        if (id == ActiveId || !Definitions.Any(x => x.Id == id)) return false;
        CaptureActive(live);
        var next = Items.Single(x => x.Id == id); next.Options.Normalize();
        next.Options.Apply(live); ActiveId = id; return true;
    }
    private static ProfileOptions Preset(string id)
    {
        var result = new ProfileOptions { Home = new HomeLayout() };
        string[] widgets;
        switch (id)
        {
            case "work":
                result.Accent = "#8AE4CB"; result.Body = "#101B1C"; result.MotionIntensity = .5; result.Speed = .85;
                result.SpectrumIntensity = .5; result.EdgeIntensity = .25; result.GlowIntensity = .3; result.EyeIntensity = .4;
                result.NotifyDeviceConnections = false; result.NotifyFocusBanner = false;
                widgets = new[] { "tasks", "focus", "note", "clock" }; break;
            case "music":
                result.Accent = "#F6A9CD"; result.Body = "#1B101D"; result.MotionIntensity = 1.15; result.Speed = 1.1;
                result.SpectrumIntensity = 1.25; result.EdgeIntensity = 1.1; result.GlowIntensity = 1.2; result.EyeIntensity = 1.1;
                widgets = new[] { "music", "volume", "devices", "clock" }; break;
            default:
                result.Accent = "#9FCCFF"; result.Body = "#09161C"; result.MotionIntensity = .2; result.Speed = .9;
                result.AudioReactive = false; result.CompactMusic = false; result.Liquid = false;
                result.NotifyDeviceConnections = false; result.NotifyTaskCompletion = false; result.NotifyFocusBanner = false; result.TimerSound = false;
                result.FullscreenEyes = true;
                widgets = new[] { "system", "volume", "devices", "clock" }; break;
        }
        foreach (var kind in widgets) result.Home.AddWidget(kind);
        return result;
    }
}

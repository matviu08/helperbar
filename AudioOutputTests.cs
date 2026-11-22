using System;
using System.Collections.Generic;
using System.Linq;

namespace HelperBar;

internal static class AudioOutputTests
{
    public static void Run(Action<bool, string> check)
    {
        using var fake = new Fake();
        var first = fake.Read();
        check(first.Current?.Name == "Колонки" && first.Devices.Count == 2, "Output model resolves the active endpoint by ID");
        check(new AudioOutput("x", "Speakers (Realtek(R) Audio)", false).ShortName == "Realtek(R) Audio" && new AudioOutput("y", "My (Studio) Output", false).ShortName == "My (Studio) Output", "Compact names preserve adapter details and custom device names");
        check(AudioOutputSwitch.Select(fake, "phones").Success && fake.Defaults.SequenceEqual(new[] { "phones", "phones", "calls" }), "Selecting headphones changes console and multimedia, preserving calls");
        fake.Calls.Clear();
        check(AudioOutputSwitch.Select(fake, "phones").Success && fake.Calls.Count == 0, "Selecting the current output does not rewrite Windows defaults");
        fake.Calls.Clear();
        check(!AudioOutputSwitch.Select(fake, "unplugged").Success && fake.Calls.Count == 0, "A disconnected output is rejected before any default is changed");
        fake.Defaults = new[] { "speakers", "speakers", "calls" }; fake.FailRole = 1;
        check(!AudioOutputSwitch.Select(fake, "phones").Success && fake.Defaults.SequenceEqual(new[] { "speakers", "speakers", "calls" }), "A partial switch rolls the console endpoint back after multimedia fails");
        fake.FailRole = null; fake.IgnoreRole = 1;
        check(!AudioOutputSwitch.Select(fake, "phones").Success && fake.Default(0) == "speakers", "An unconfirmed Windows write is a failure and rolls back");
        fake.IgnoreRole = null; fake.FailRole = 0; fake.Calls.Clear();
        check(!AudioOutputSwitch.Select(fake, "phones").Success && fake.Calls.All(x => x.Role == 0), "Failure on the first role never changes multimedia or calls");
        fake.FailRole = null; fake.Devices.Clear(); fake.Defaults = new string?[] { null, null, "calls" };
        check(fake.Read().Current == null && !AudioOutputSwitch.Select(fake, "phones").Success, "No connected outputs is a valid recoverable state");
        fake.Devices.Add(new("phones", "Нові навушники", true));
        check(AudioOutputSwitch.Select(fake, "phones").Success && fake.Read().Current?.Name == "Нові навушники", "A newly attached endpoint can become the first default");
        check(!first.SameAs(fake.Read()) && first.SameAs(first with { Devices = first.Devices.ToArray() }), "Refresh compares device content, catching hotplug and rename without spurious updates");
        check(!(first with { SwitchingId = "phones" }).SameAs(first) && (first with { SwitchingId = "phones" }).Busy, "Switch progress is visible even before Windows changes the default");
        fake.Defaults = new[] { "speakers", "speakers", "calls" }; fake.FailRole = 1; fake.FailRollback = true;
        var failedRollback = AudioOutputSwitch.Select(fake, "phones");
        check(!failedRollback.Success && failedRollback.Message.Contains("частково"), "A failed rollback explicitly reports a partial Windows change");
    }
    private sealed class Fake : IAudioDevices
    {
        public readonly List<AudioOutput> Devices = new() { new("speakers", "Колонки", false), new("phones", "Навушники", true) };
        public string?[] Defaults = { "speakers", "speakers", "calls" };
        public readonly List<(string Id, int Role)> Calls = new();
        public int? FailRole, IgnoreRole;
        public bool FailRollback;
        public AudioOutputs Read() => new(Devices.ToArray(), Defaults[1]);
        public string? Default(int role) => Defaults[role];
        public void SetDefault(string id, int role)
        {
            Calls.Add((id, role));
            if (role == FailRole || FailRollback && id == "speakers") throw new InvalidOperationException("Simulated device failure");
            if (role != IgnoreRole) Defaults[role] = id;
        }
        public void Dispose() { }
    }
}

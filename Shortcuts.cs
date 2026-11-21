using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace HelperBar;

public readonly record struct Shortcut(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var pieces = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        foreach (var part in pieces.Take(pieces.Length - 1))
        {
            var flag = part.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        if (modifiers == 0 || !Enum.TryParse<Key>(pieces[^1], true, out var key) || !AllowedKey(key)) return false;
        shortcut = new(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        return true;
    }
    public static bool AllowedKey(Key key) => key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F11 or Key.Space or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
        return string.Join('+', parts);
    }
}

/// <summary>Registers a replacement before releasing the existing shortcut.</summary>
internal sealed class ShortcutRegistration : IDisposable
{
    private readonly Func<int, Shortcut, bool> register;
    private readonly Action<int> unregister;
    private readonly Dictionary<string, (int Id, Shortcut Key)> bindings = new();
    private int nextId = 10;
    public ShortcutRegistration(Func<int, Shortcut, bool> register, Action<int> unregister) { this.register = register; this.unregister = unregister; }
    public bool Set(string action, Shortcut key)
    {
        var exists = bindings.TryGetValue(action, out var old);
        if (exists && old.Key == key) return true;
        if (bindings.Any(pair => pair.Key != action && pair.Value.Key == key)) return false;
        var id = nextId++;
        if (!register(id, key)) return false;
        bindings[action] = (id, key);
        if (exists) unregister(old.Id);
        return true;
    }
    public string? ActionFor(int id) => bindings.FirstOrDefault(pair => pair.Value.Id == id).Key;
    public bool IsRegistered(string action) => bindings.ContainsKey(action);
    public void Dispose() { foreach (var binding in bindings.Values) unregister(binding.Id); bindings.Clear(); }
}

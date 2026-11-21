using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace HelperBar;

public sealed record WidgetDefinition(string Kind, string Title, string Description, string Glyph);

public sealed class HomeItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "shortcut";
    public string Name { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string Target { get; set; } = "";
    public HomeItem Copy() => new() { Id = Id, Kind = Kind, Name = Name, TargetType = TargetType, Target = Target };
    [JsonIgnore] public string Title => Kind == "shortcut" ? Name : HomeLayout.Widgets.FirstOrDefault(w => w.Kind == Kind)?.Title ?? Name;
}

public sealed class HomeLayout
{
    public const int Limit = 32;
    public static readonly WidgetDefinition[] Widgets =
    {
        new("clock", "Годинник і дата", "Час і сьогоднішня дата", "\uE787"),
        new("music", "Музика", "Трек, обкладинка й пауза одним кліком", "\uE8D6"),
        new("focus", "Фокус", "Твій таймер: почати або призупинити", "\uE916"),
        new("system", "Стан ПК", "Оперативна пам’ять і живлення", "\uE7F4"),
        new("note", "Нотатка", "Перші рядки твоєї збереженої думки", "\uE70B"),
        new("volume", "Гучність", "Рівень Windows і вимкнення звуку", "\uE767"),
        new("devices", "Заряд пристроїв", "Мишка, клавіатура й навушники поруч", "\uE83F")
    };
    public List<HomeItem> Items { get; set; } = new();
    public static HomeLayout Default() => new()
    {
        Items = new()
        {
            new() { Kind = "clock" }, new() { Kind = "music" },
            new() { Name = "Провідник", TargetType = "builtin", Target = "explorer" },
            new() { Name = "Калькулятор", TargetType = "builtin", Target = "calculator" },
            new() { Name = "Знімок", TargetType = "builtin", Target = "screenshot" },
            new() { Name = "YouTube Music", TargetType = "web", Target = "https://music.youtube.com/" },
            new() { Kind = "focus" }, new() { Kind = "system" }
        }
    };
    public HomeLayout Copy() => new() { Items = Items.Select(item => item.Copy()).ToList() };
    public void Normalize()
    {
        Items ??= new();
        var ids = new HashSet<string>(); var widgets = new HashSet<string>(); var normalized = new List<HomeItem>();
        foreach (var item in Items)
        {
            if (item == null || normalized.Count == Limit) continue;
            if (item.Kind != "shortcut" && (!Widgets.Any(w => w.Kind == item.Kind) || !widgets.Add(item.Kind))) continue;
            item.Name = (item.Name ?? "").Trim(); item.Target ??= ""; item.TargetType ??= "";
            if (item.Kind == "shortcut" && (item.Name.Length == 0 || item.Target.Length == 0 || item.TargetType is not ("app" or "folder" or "web" or "builtin"))) continue;
            if (item.Name.Length > 60) item.Name = item.Name[..60];
            if (string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); ids.Add(item.Id); }
            normalized.Add(item);
        }
        Items = normalized;
    }
    public bool AddWidget(string kind)
    {
        if (Items.Count >= Limit || !Widgets.Any(w => w.Kind == kind) || Items.Any(i => i.Kind == kind)) return false;
        Items.Add(new HomeItem { Kind = kind }); return true;
    }
    public bool AddShortcut(HomeItem item)
    {
        if (Items.Count >= Limit || Items.Any(i => i.Kind == "shortcut" && i.TargetType == item.TargetType && string.Equals(i.Target, item.Target, item.TargetType == "web" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))) return false;
        Items.Add(item); return true;
    }
    public bool MoveTo(string id, int index)
    {
        var from = Items.FindIndex(i => i.Id == id);
        if (from < 0) return false;
        var to = Math.Clamp(index, 0, Items.Count - 1);
        if (from == to) return false;
        var item = Items[from]; Items.RemoveAt(from); Items.Insert(to, item); return true;
    }
    public bool MoveRelative(string id, string targetId, bool after)
    {
        var from = Items.FindIndex(i => i.Id == id); var to = Items.FindIndex(i => i.Id == targetId);
        if (from < 0 || to < 0 || from == to) return false;
        var insertion = to + (after ? 1 : 0); if (from < insertion) insertion--;
        return MoveTo(id, insertion);
    }
}

internal static class ShortcutTarget
{
    public static bool TryCreate(string type, string? name, string? value, out HomeItem item, out string error)
    {
        item = new(); error = "";
        var target = (value ?? "").Trim().Trim('"');
        if (type == "web")
        {
            if (!target.Contains("://", StringComparison.Ordinal) && !target.Contains(':')) target = "https://" + target;
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            { error = "Вкажи адресу сайту, наприклад https://music.youtube.com"; return false; }
            target = uri.AbsoluteUri;
            if (string.IsNullOrWhiteSpace(name)) name = uri.Host;
        }
        else if (type is "app" or "folder")
        {
            if (!Path.IsPathFullyQualified(target)) { error = "Вибери програму або папку кнопкою огляду."; return false; }
            try { target = Path.GetFullPath(target); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { error = "Цей шлях не вдалося прочитати."; return false; }
            if (type == "app" && Path.GetExtension(target).ToLowerInvariant() is not (".exe" or ".lnk" or ".url"))
            { error = "Вибери програму .exe або ярлик .lnk / .url."; return false; }
            if (string.IsNullOrWhiteSpace(name)) name = type == "folder" ? Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(target);
            if (string.IsNullOrWhiteSpace(name)) name = target;
        }
        else { error = "Невідомий тип ярлика."; return false; }
        name = name!.Trim();
        if (name.Length > 60) { error = "Скороти назву до 60 символів."; return false; }
        item = new HomeItem { Name = name, TargetType = type, Target = target }; return true;
    }
    public static bool IsAvailable(HomeItem item) => item.TargetType switch
    {
        "app" => File.Exists(item.Target), "folder" => Directory.Exists(item.Target),
        "web" => TryCreate("web", item.Name, item.Target, out _, out _),
        "builtin" => item.Target is "explorer" or "calculator" or "screenshot", _ => false
    };
    public static string LaunchPath(HomeItem item) => item.TargetType == "builtin" ? item.Target switch
    { "explorer" => "explorer.exe", "calculator" => "calc.exe", "screenshot" => "ms-screenclip:", _ => throw new InvalidOperationException() } : item.Target;
}

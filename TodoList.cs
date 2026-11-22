using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace HelperBar;

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public TodoItem Copy() => new() { Id = Id, Title = Title, Done = Done };
}

public sealed class TodoList
{
    public const int Limit = 100, TitleLimit = 120;
    public List<TodoItem> Items { get; set; } = new();
    public string PinnedId { get; set; } = "";
    public string PinnedDay { get; set; } = "";
    [JsonIgnore] public int ActiveCount => Items.Count(item => !item.Done);
    public TodoItem? Pinned(DateOnly today) => PinnedDay == Day(today) ? Items.FirstOrDefault(item => item.Id == PinnedId && !item.Done) : null;
    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string CleanTitle(string? text)
    {
        var printable = new string((text ?? "").Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray());
        var title = string.Join(" ", printable.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= TitleLimit ? title : title[..(char.IsHighSurrogate(title[TitleLimit - 1]) ? TitleLimit - 1 : TitleLimit)];
    }
    public void Normalize(DateOnly today)
    {
        Items ??= new(); var ids = new HashSet<string>(StringComparer.Ordinal); var valid = new List<TodoItem>();
        foreach (var item in Items)
        {
            if (item == null || valid.Count >= Limit) continue;
            item.Title = CleanTitle(item.Title); if (item.Title.Length == 0) continue;
            if (!Guid.TryParse(item.Id, out _) || !ids.Add(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); ids.Add(item.Id); }
            valid.Add(item);
        }
        Items = valid; PinnedId ??= ""; PinnedDay ??= ""; RefreshDay(today);
    }
    public bool RefreshDay(DateOnly today)
    {
        if (Pinned(today) != null || PinnedId.Length == 0 && PinnedDay.Length == 0) return false;
        PinnedId = PinnedDay = ""; return true;
    }
    public TodoItem? Add(string text)
    {
        var title = CleanTitle(text); if (title.Length == 0 || Items.Count >= Limit) return null;
        var item = new TodoItem { Title = title }; Items.Add(item); return item;
    }
    public bool Rename(string id, string text)
    {
        var item = Items.FirstOrDefault(x => x.Id == id); var title = CleanTitle(text);
        if (item == null || title.Length == 0 || item.Title == title) return false;
        item.Title = title; return true;
    }
    public bool SetDone(string id, bool done)
    {
        var item = Items.FirstOrDefault(x => x.Id == id); if (item == null || item.Done == done) return false;
        item.Done = done; if (done && PinnedId == id) PinnedId = PinnedDay = ""; return true;
    }
    public bool TogglePin(string id, DateOnly today)
    {
        var item = Items.FirstOrDefault(x => x.Id == id); if (item == null || item.Done) return false;
        if (Pinned(today)?.Id == id) PinnedId = PinnedDay = "";
        else { PinnedId = id; PinnedDay = Day(today); }
        return true;
    }
    public bool Remove(string id)
    {
        if (Items.RemoveAll(x => x.Id == id) == 0) return false;
        if (PinnedId == id) PinnedId = PinnedDay = ""; return true;
    }
    public TodoList Copy() => new() { Items = Items.Select(x => x.Copy()).ToList(), PinnedId = PinnedId, PinnedDay = PinnedDay };
}

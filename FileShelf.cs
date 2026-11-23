using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace HelperBar;

public sealed class ShelfFile
{
    public string Path { get; set; } = "";
    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
}

public sealed class FileShelf
{
    public const int Limit = 20;
    public List<ShelfFile> Items { get; set; } = new();
    public bool Contains(string path) => Items.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
    public bool Add(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == null || Items.Count >= Limit || Contains(normalized)) return false;
        Items.Add(new ShelfFile { Path = normalized }); return true;
    }
    public bool Remove(string path) => Items.RemoveAll(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;
    public void Normalize()
    {
        var original = Items; Items = new();
        if (original == null) return;
        foreach (var item in original) { if (item != null) Add(item.Path); if (Items.Count == Limit) break; }
    }
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767 || path.Any(c => c < 32 || c is '"' or '<' or '>' or '|' or '*' or '?')) return null;
        try
        {
            if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return null;
            var full = System.IO.Path.GetFullPath(path);
            var root = System.IO.Path.GetPathRoot(full)!;
            if (full[root.Length..].Contains(':')) return null;
            return System.IO.Path.TrimEndingDirectorySeparator(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
    public static string[] ReadBatch(IEnumerable<string> paths, out int skipped)
    {
        var accepted = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); skipped = 0;
        foreach (var path in paths)
        {
            var normalized = NormalizePath(path);
            if (normalized == null || !seen.Add(normalized) || accepted.Count == Limit) { skipped++; continue; }
            accepted.Add(normalized);
        }
        return accepted.ToArray();
    }
}

internal static class ShelfFileAccess
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    // Use the shell's item ID rather than interpolating a dropped path into command-line arguments.
    public static void ShowInFolder(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var item, 0, out _));
        try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0)); }
        finally { if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr item, uint attributes, out uint actualAttributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);
}

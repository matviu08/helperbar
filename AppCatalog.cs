using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelperBar;

internal sealed record InstalledShortcut(string Name, string Path);

internal static class AppCatalog
{
    public static Task<IReadOnlyList<InstalledShortcut>> ReadAsync() => Task.Run<IReadOnlyList<InstalledShortcut>>(() =>
    {
        var result = new List<InstalledShortcut>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
                foreach (var path in Directory.EnumerateFiles(root, "*", options))
                    if (Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url") result.Add(new(Path.GetFileNameWithoutExtension(path), path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return result.DistinctBy(s => s.Path, StringComparer.OrdinalIgnoreCase).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    });

    private static readonly Dictionary<string, Task<ImageSource?>> icons = new(StringComparer.OrdinalIgnoreCase);
    public static Task<ImageSource?> IconAsync(string path)
    {
        if (icons.TryGetValue(path, out var cached)) return cached;
        if (icons.Count > 96) icons.Clear();
        return icons[path] = Task.Run<ImageSource?>(() =>
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32)); bitmap.Freeze(); return bitmap;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        });
    }
}

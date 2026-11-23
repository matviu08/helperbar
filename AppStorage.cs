using System;
using System.IO;
using System.Text.Json;

namespace HelperBar;

internal sealed record StorageLocation(string Directory, string? Warning = null);

internal static class AppStorage
{
    private static readonly Lazy<StorageLocation> current = new(() => Resolve(AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelperBar", "data")));
    public static StorageLocation Current => current.Value;

    // Only the two known settings files are considered; the old directory and its contents are never deleted.
    internal static StorageLocation Resolve(string applicationDirectory, string sharedDirectory)
    {
        var target = Path.Combine(sharedDirectory, "settings.json");
        if (File.Exists(target) || File.Exists(target + ".bak")) return new(sharedDirectory);
        var legacyDirectory = Path.Combine(applicationDirectory, "data");
        var legacy = Path.Combine(legacyDirectory, "settings.json");
        if (!File.Exists(legacy) && !File.Exists(legacy + ".bak")) return new(sharedDirectory);
        string? temporary = null;
        try
        {
            var source = ValidSettings(legacy) ? legacy : ValidSettings(legacy + ".bak") ? legacy + ".bak" : null;
            if (source == null) return new(legacyDirectory, "Перевір налаштування: старі файли залишено у попередній папці.");
            Directory.CreateDirectory(sharedDirectory);
            temporary = Path.Combine(sharedDirectory, "settings-migration-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(source, temporary);
            File.Move(temporary, target, false); // A complete settings file becomes visible atomically.
            temporary = null;
            return new(sharedDirectory, source.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? "Налаштування відновлено з резервної копії та перенесено в AppData." : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(target)) return new(sharedDirectory); // Another writer won the migration race.
            return new(legacyDirectory, "Не вдалося перенести налаштування в AppData. Працюємо з попередньою папкою.");
        }
        finally
        {
            if (temporary != null) try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private static bool ValidSettings(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path); using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object && JsonSerializer.Deserialize<Settings>(text) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return false; }
    }
}

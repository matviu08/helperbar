using System;
using System.IO;
using System.Linq;

namespace HelperBar;

internal static class FileShelfTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var shelf = new FileShelf(); var path = Path.Combine(directory, "Файл, із пробілами & дужками (1).txt");
        File.WriteAllText(path, "Synthetic file shelf fixture"); var content = File.ReadAllText(path);
        check(shelf.Add(path) && shelf.Items.Single().Name == Path.GetFileName(path), "A Unicode file with shell punctuation is represented by its full path and display name");
        check(!shelf.Add(path.ToUpperInvariant()) && shelf.Items.Count == 1, "Repeated drops deduplicate paths case-insensitively");
        var folder = Path.Combine(directory, "Поличка"); Directory.CreateDirectory(folder);
        check(shelf.Add(folder + Path.DirectorySeparatorChar) && !shelf.Add(folder) && shelf.Items.Count == 2, "Directory trailing separators normalize without duplicate shelf entries");
        check(FileShelf.NormalizePath(@"C:\") == @"C:\" && FileShelf.NormalizePath(@"C:\folder\..\item.txt") == @"C:\item.txt", "Drive roots remain valid and dot segments canonicalize");
        check(FileShelf.NormalizePath(@"\\server\share\папка\file.txt") != null, "UNC paths can be kept without contacting the remote server during normalization");
        check(new[] { "", "relative.txt", @"C:relative.txt", "https://example.com", @"\\.\PhysicalDrive0", @"C:\file.txt:stream", "C:\\bad\nname", "C:\\bad\"name", @"C:\*.txt" }.All(x => FileShelf.NormalizePath(x) == null), "Relative paths, URLs, device paths, streams and malformed file names are rejected");
        var batch = FileShelf.ReadBatch(new[] { path, path.ToUpperInvariant(), folder, "not a file path" }, out var skipped);
        check(batch.SequenceEqual(new[] { path, folder }) && skipped == 2, "Mixed drops retain valid distinct files and account for skipped entries");
        var many = Enumerable.Range(0, 30).Select(i => Path.Combine(directory, i + ".txt")).ToArray();
        check(FileShelf.ReadBatch(many, out skipped).Length == FileShelf.Limit && skipped == 10, "An oversized batch is bounded with an explicit omitted count");
        var settings = new Settings { Files = shelf, Note = "Зберегти нотатку", NotesView = "tasks" }; settings.Todos.Add("Зберегти справу");
        var savePath = Path.Combine(directory, "files-persistence.json"); settings.Save(savePath); var restored = Settings.Load(savePath);
        check(restored.Files.Items.Select(x => x.Path).SequenceEqual(shelf.Items.Select(x => x.Path)) && restored.Note == settings.Note && restored.Todos.Items.Count == 1, "Shelf persistence preserves ordering, notes and tasks across restart");
        check(shelf.Remove(path.ToUpperInvariant()) && !shelf.Remove(path) && File.ReadAllText(path) == content, "Removing a reference neither deletes nor changes the original file");
        var missing = Path.Combine(directory, "This file does not exist.txt"); shelf.Add(missing);
        shelf.Normalize(); check(shelf.Contains(missing), "Unavailable references survive normalization for disconnected drives and moved files");
        shelf = new FileShelf(); foreach (var item in many) shelf.Add(item);
        check(shelf.Items.Count == FileShelf.Limit && !shelf.Add(path) && shelf.Items[0].Path == many[0], "A full shelf never evicts an existing file to make room");
        shelf.Items.Insert(0, null!); shelf.Items.Add(new ShelfFile { Path = "relative" }); shelf.Normalize();
        check(shelf.Items.Count == FileShelf.Limit && shelf.Items.All(x => Path.IsPathFullyQualified(x.Path)), "Malformed persisted entries are repaired without filesystem access");
        File.WriteAllText(savePath, "{\"Files\":null,\"Note\":\"old note\"}");
        check(Settings.Load(savePath).Files.Items.Count == 0 && Settings.Load(savePath).Note == "old note", "Older settings acquire an empty shelf while preserving existing data");
        check(File.ReadAllText(path) == content && Directory.Exists(folder), "All shelf operations leave original files and folders intact");
    }
}

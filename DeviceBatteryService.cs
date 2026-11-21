using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace HelperBar;

public enum PeripheralKind { Mouse, Keyboard, Headphones, Other }
public sealed record DeviceBattery(string Id, string Name, PeripheralKind Kind, int? Percent, string Source, DateTimeOffset ReadAt)
{
    public string LevelText => Percent.HasValue ? $"{Percent}%" : "Заряд недоступний";
    public bool Low => Percent is <= 20;
}
public sealed record DeviceBatterySnapshot(IReadOnlyList<DeviceBattery> Devices, bool Scanning, string Status, DateTimeOffset UpdatedAt, bool Successful = true)
{
    public static readonly DeviceBatterySnapshot Disabled = new(Array.Empty<DeviceBattery>(), false, "Заряд і сповіщення вимкнені", default, false);
}

internal static class BatteryValues
{
    // 101, 255 and other out-of-range/sentinel values mean unknown, never 100%.
    public static int? Percent(object? value) => value switch
    {
        byte n when n <= 100 => n, ushort n when n <= 100 => n, uint n when n <= 100 => (int)n,
        short n when n is >= 0 and <= 100 => n, int n when n is >= 0 and <= 100 => n,
        _ => null
    };
    public static string Address(string? text) => (text ?? "").Replace(":", "").Replace("-", "").ToUpperInvariant();
    public static PeripheralKind? Classify(int? appearanceCategory, int? appearanceSubcategory, int? major, int? minor, string categories, string name)
    {
        if (appearanceCategory == 15)
        { if (appearanceSubcategory == 1) return PeripheralKind.Keyboard; if (appearanceSubcategory == 2) return PeripheralKind.Mouse; }
        if (major == 5)
        { if ((minor.GetValueOrDefault() & 0x20) != 0) return PeripheralKind.Mouse; if ((minor.GetValueOrDefault() & 0x10) != 0) return PeripheralKind.Keyboard; }
        if (major == 4 && minor is 1 or 2 or 6 or 7) return PeripheralKind.Headphones;
        var text = (categories + " " + name).ToLowerInvariant();
        if (text.Contains("mouse") || text.Contains("миша") || text.Contains("мишка")) return PeripheralKind.Mouse;
        if (text.Contains("keyboard") || text.Contains("клавіат")) return PeripheralKind.Keyboard;
        if (text.Contains("headphone") || text.Contains("headset") || text.Contains("earbud") || text.Contains("airpods") || text.Contains("навуш")) return PeripheralKind.Headphones;
        return null;
    }
    public static IReadOnlyList<DeviceBattery> Merge(IEnumerable<DeviceBattery> values) => values
        .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(d => d.Percent.HasValue).ThenByDescending(d => d.ReadAt).First())
        .OrderBy(d => d.Kind).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public static bool Visible(DeviceBattery device, Settings settings) => device.Kind switch
    { PeripheralKind.Mouse => settings.BatteryMouse, PeripheralKind.Keyboard => settings.BatteryKeyboard, PeripheralKind.Headphones => settings.BatteryHeadphones, _ => false };
}

/// <summary>Reads connected Bluetooth endpoints and Windows-provided device batteries. Never pairs devices or sends HID configuration commands.</summary>
internal sealed class DeviceBatteryReader
{
    // Windows Bluetooth drivers expose this per-device property; optional fallback to the documented BAS exists.
    internal const string DriverBattery = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly string[] EndpointProperties =
    {
        "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.ContainerId",
        "System.Devices.Aep.Category", "System.Devices.Aep.Bluetooth.Le.Appearance.Category", "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory",
        "System.Devices.Aep.Bluetooth.Cod.Major", "System.Devices.Aep.Bluetooth.Cod.Minor", "System.Devices.BatteryLife", DriverBattery
    };
    private static readonly string[] NodeProperties = { DriverBattery, "System.Devices.BatteryLife", "System.Devices.ContainerId", "System.Devices.ClassGuid", "System.Devices.Present" };
    private static readonly string[] ContainerProperties = { "System.Devices.BatteryLife", "System.Devices.Connected", "System.Devices.CategoryIds", "System.Devices.Category", "System.Devices.ContainerId" };
    private readonly Dictionary<string, (int? Value, DateTimeOffset Time)> gattCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<DeviceBatterySnapshot> ReadAsync(CancellationToken token)
    {
        var leTask = Find(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), EndpointProperties, DeviceInformationKind.AssociationEndpoint, token);
        var classicTask = Find(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), EndpointProperties, DeviceInformationKind.AssociationEndpoint, token);
        var nodesTask = Find("", NodeProperties, DeviceInformationKind.Device, token);
        var containersTask = Find("", ContainerProperties, DeviceInformationKind.DeviceContainer, token);
        await Task.WhenAll(leTask, classicTask, nodesTask, containersTask);
        var nodes = (await nodesTask).Where(n => NodePercent(n).HasValue).ToArray();
        var output = new List<DeviceBattery>(); var bluetoothContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (devices, isLe) in new[] { (await leTask, true), (await classicTask, false) })
        {
            foreach (var info in devices)
            {
                token.ThrowIfCancellationRequested();
                if (!Connected(info)) continue; // Paired/present entries can remain long after disconnect.
                var address = BatteryValues.Address(Property(info, "System.Devices.Aep.DeviceAddress")?.ToString());
                var container = Property(info, "System.Devices.Aep.ContainerId")?.ToString() ?? "";
                if (container.Length > 0) bluetoothContainers.Add(container.Trim('{', '}'));
                var kind = BatteryValues.Classify(Number(info, "System.Devices.Aep.Bluetooth.Le.Appearance.Category"), Number(info, "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory"),
                    Number(info, "System.Devices.Aep.Bluetooth.Cod.Major"), Number(info, "System.Devices.Aep.Bluetooth.Cod.Minor"), Text(Property(info, "System.Devices.Aep.Category")), info.Name);
                if (kind == null) continue; // Phones, computers, adapters and unrelated services are not peripherals here.
                var id = address.Length == 12 ? "bt:" + address : info.Id;
                var matchingNode = nodes.FirstOrDefault(n => address.Length == 12 && n.Id.Contains(address, StringComparison.OrdinalIgnoreCase))
                    ?? nodes.FirstOrDefault(n => container.Length > 0 && string.Equals(Property(n, "System.Devices.ContainerId")?.ToString()?.Trim('{', '}'), container.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
                var percent = NodePercent(info) ?? (matchingNode == null ? null : NodePercent(matchingNode));
                var readAt = DateTimeOffset.Now; var source = percent.HasValue ? "Windows" : "Пристрій не надає дані";
                if (percent == null && isLe)
                {
                    var reading = await ReadGatt(info.Id, id, token); percent = reading.Value; readAt = reading.Time;
                    if (percent.HasValue) source = "Bluetooth Battery Service";
                }
                output.Add(new(id, string.IsNullOrWhiteSpace(info.Name) ? "Bluetooth-пристрій" : info.Name, kind.Value, percent, source, readAt));
            }
        }
        // USB/dongle devices are included only when their driver publishes a real battery percentage.
        foreach (var info in await containersTask)
        {
            if (Property(info, "System.Devices.Connected") is not true || bluetoothContainers.Contains(info.Id.Trim('{', '}'))) continue;
            var percent = NodePercent(info); if (!percent.HasValue) continue;
            var kind = BatteryValues.Classify(null, null, null, null, Text(Property(info, "System.Devices.CategoryIds")) + " " + Text(Property(info, "System.Devices.Category")), info.Name);
            if (kind != null) output.Add(new("container:" + info.Id, info.Name, kind.Value, percent, "Windows", DateTimeOffset.Now));
        }
        var now = DateTimeOffset.Now;
        var connectedIds = output.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in gattCache.Keys.Where(key => !connectedIds.Contains(key)).ToArray()) gattCache.Remove(key);
        var merged = BatteryValues.Merge(output);
        return new(merged, false, merged.Count == 0 ? "Підключені бездротові пристрої не знайдені" : "Заряд підключених пристроїв", now);
    }
    internal static bool Connected(DeviceInformation info) => Property(info, "System.Devices.Aep.IsConnected") is true;
    private static object? Property(DeviceInformation info, string key) => info.Properties.TryGetValue(key, out var value) ? value : null;
    private static int? Number(DeviceInformation info, string key)
    { var value = Property(info, key); return value switch { byte n => n, ushort n => n, uint n when n <= int.MaxValue => (int)n, int n => n, _ => null }; }
    private static string Text(object? value) => value is string[] array ? string.Join(' ', array) : value?.ToString() ?? "";
    private static int? NodePercent(DeviceInformation info) => BatteryValues.Percent(Property(info, "System.Devices.BatteryLife")) ?? BatteryValues.Percent(Property(info, DriverBattery));
    private static async Task<DeviceInformation[]> Find(string query, string[] properties, DeviceInformationKind kind, CancellationToken token) =>
        (await Timed(DeviceInformation.FindAllAsync(query, properties, kind), token)).ToArray();
    private async Task<(int? Value, DateTimeOffset Time)> ReadGatt(string deviceId, string id, CancellationToken token)
    {
        if (gattCache.TryGetValue(id, out var previous) && DateTimeOffset.Now - previous.Time < TimeSpan.FromSeconds(45)) return previous;
        int? level = null;
        try
        {
            using var device = await Timed(BluetoothLEDevice.FromIdAsync(deviceId), token);
            if (device == null || device.ConnectionStatus != BluetoothConnectionStatus.Connected) return (null, DateTimeOffset.Now);
            var services = await Timed(device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached), token);
            try
            {
                if (services.Status == GattCommunicationStatus.Success)
                    foreach (var service in services.Services)
                    {
                        var characteristics = await Timed(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached), token);
                        if (characteristics.Status != GattCommunicationStatus.Success) continue;
                        foreach (var characteristic in characteristics.Characteristics)
                        {
                            if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read)) continue;
                            var result = await Timed(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached), token);
                            if (result.Status != GattCommunicationStatus.Success || result.Value.Length != 1) continue;
                            using var reader = DataReader.FromBuffer(result.Value); level = BatteryValues.Percent(reader.ReadByte());
                            if (level.HasValue) break;
                        }
                        if (level.HasValue) break;
                    }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
            if (device.ConnectionStatus != BluetoothConnectionStatus.Connected) level = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !token.IsCancellationRequested) { }
        var reading = (level, DateTimeOffset.Now); gattCache[id] = reading; return reading;
    }
    internal static async Task<T> Timed<T>(IAsyncOperation<T> operation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(6));
        return await operation.AsTask(timeout.Token).WaitAsync(timeout.Token);
    }
}

public sealed class DeviceBatteryService : IDisposable
{
    private readonly Func<CancellationToken, Task<DeviceBatterySnapshot>> read;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource? lifetime;
    private int generation;
    private bool busy;
    public DeviceBatterySnapshot Snapshot { get; private set; } = DeviceBatterySnapshot.Disabled;
    public bool Running => lifetime != null;
    public event Action<DeviceBatterySnapshot>? Changed;
    public DeviceBatteryService() : this(new DeviceBatteryReader().ReadAsync) { }
    internal DeviceBatteryService(Func<CancellationToken, Task<DeviceBatterySnapshot>> read)
    { this.read = read; timer.Tick += async (_, _) => await RefreshAsync(); }
    public void Start()
    {
        if (lifetime != null) return;
        lifetime = new(); generation++; timer.Start(); _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        if (lifetime == null || busy) return;
        busy = true; var revision = generation; var token = lifetime.Token;
        Publish(Snapshot with { Scanning = true, Status = "Перевіряю підключені пристрої…" });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(22));
            var result = await read(timeout.Token);
            if (revision == generation && !token.IsCancellationRequested) Publish(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (revision == generation && !token.IsCancellationRequested) Publish(new(Array.Empty<DeviceBattery>(), false, "Не вдалося оновити пристрої. Спробуємо ще раз.", DateTimeOffset.Now, false));
        }
        finally
        {
            busy = false;
            // A stop/start while a cancelled scan drains must not delay the new scan until the next timer tick.
            if (revision != generation && lifetime != null) _ = RefreshAsync();
        }
    }
    private void Publish(DeviceBatterySnapshot snapshot) { Snapshot = snapshot; Changed?.Invoke(snapshot); }
    public void Stop()
    {
        generation++; timer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; Publish(DeviceBatterySnapshot.Disabled);
    }
    public void Dispose() { Changed = null; Stop(); }
}

internal static class BatteryProbe
{
    public static async Task<int> RunAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "qa"); Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var snapshot = await new DeviceBatteryReader().ReadAsync(timeout.Token);
            File.WriteAllText(Path.Combine(directory, "battery-probe.json"), JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "battery-probe-error.txt"), ex.ToString()); return 1; }
    }
}

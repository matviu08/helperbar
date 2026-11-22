using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace HelperBar;

public sealed record AudioOutput(string Id, string Name, bool Headphones)
{
    public string ShortName
    {
        get
        {
            var parenthesis = Name.IndexOf(" (", StringComparison.Ordinal);
            if (parenthesis > 0 && Name.EndsWith(')') && Name[..parenthesis] is "Speakers" or "Headphones" or "Headset" or "Динаміки" or "Динамики" or "Навушники")
                return Name[(parenthesis + 2)..^1];
            return Name;
        }
    }
}
public sealed record AudioOutputs(IReadOnlyList<AudioOutput> Devices, string? DefaultId, bool Loading = false, string? SwitchingId = null, string? Error = null)
{
    public static readonly AudioOutputs Initial = new(Array.Empty<AudioOutput>(), null, true);
    public AudioOutput? Current => Devices.FirstOrDefault(x => x.Id == DefaultId);
    public bool Busy => SwitchingId != null;
    public bool SameAs(AudioOutputs other) => DefaultId == other.DefaultId && Loading == other.Loading && SwitchingId == other.SwitchingId && Error == other.Error && Devices.SequenceEqual(other.Devices);
}
internal interface IAudioDevices : IDisposable
{
    AudioOutputs Read();
    string? Default(int role);
    void SetDefault(string id, int role);
}
public sealed record AudioSwitchResult(bool Success, string Message);

internal static class AudioOutputSwitch
{
    // Console + multimedia follow the picker. Communications keeps its separate Windows preference.
    public static AudioSwitchResult Select(IAudioDevices devices, string id)
    {
        string? console = null, multimedia = null;
        var changed = new List<int>();
        try
        {
            var target = devices.Read().Devices.FirstOrDefault(x => x.Id == id);
            if (target == null) return new(false, "Пристрій уже від’єднано. Вибери інший вихід.");
            console = devices.Default(0); multimedia = devices.Default(1);
            foreach (var role in new[] { 0, 1 })
            {
                if ((role == 0 ? console : multimedia) == id) continue;
                // Record before calling: a failing driver can still partially apply a change.
                changed.Add(role); devices.SetDefault(id, role);
            }
            if (devices.Default(0) != id || devices.Default(1) != id)
                throw new InvalidOperationException("Windows did not confirm the selected output.");
            return new(true, target.Name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var restored = true;
            foreach (var role in changed.AsEnumerable().Reverse())
            {
                try
                {
                    var previous = role == 0 ? console : multimedia;
                    if (devices.Default(role) != id) continue; // Do not undo an unrelated external change.
                    if (previous == null) { restored = false; continue; }
                    devices.SetDefault(previous, role);
                    restored &= devices.Default(role) == previous;
                }
                catch { restored = false; }
            }
            return new(false, restored ? "Не вдалося перемкнути звук. Спробуй ще раз або відкрий налаштування Windows." : "Windows змінила вихід частково. Перевір пристрій у налаштуваннях звуку.");
        }
    }
}

/// <summary>One worker owns every COM object. Only immutable device information crosses to WPF.</summary>
internal sealed class WindowsAudioDevices : IAudioDevices
{
    private readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
    private IPolicyConfig? policy;
    public AudioOutputs Read()
    {
        IMMDeviceCollection? collection = null;
        var devices = new List<AudioOutput>();
        try
        {
            Check(enumerator.EnumAudioEndpoints(0, 1, out collection)); // Render, DEVICE_STATE_ACTIVE only.
            Check(collection.GetCount(out var count));
            if (count > 512) throw new InvalidOperationException("Unexpected audio endpoint count.");
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null; IPropertyStore? properties = null;
                try
                {
                    Check(collection.Item(i, out device)); Check(device.GetId(out var id));
                    Check(device.GetState(out var state)); if (state != 1) continue;
                    Check(device.OpenPropertyStore(0, out properties));
                    var name = ReadProperty(properties, new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14)) as string;
                    var form = ReadProperty(properties, new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0));
                    devices.Add(new(id, string.IsNullOrWhiteSpace(name) ? "Аудіопристрій" : name, form is uint value && value is 3 or 5));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* Hot unplug during enumeration: retry on the next refresh. */ }
                finally { Release(properties); Release(device); }
            }
        }
        finally { Release(collection); }
        return new(devices.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(), Default(1));
    }
    public string? Default(int role)
    {
        IMMDevice? device = null;
        try
        {
            var hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            if (hr == unchecked((int)0x80070490)) return null; // No default endpoint.
            Check(hr); Check(device.GetId(out var id)); return id;
        }
        finally { Release(device); }
    }
    public void SetDefault(string id, int role)
    {
        policy ??= (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!)!;
        Check(policy.SetDefaultEndpoint(id, role));
    }
    private static object? ReadProperty(IPropertyStore properties, PropertyKey key)
    {
        var value = new PropVariant();
        try
        {
            if (properties.GetValue(ref key, out value) < 0) return null;
            return value.Type switch { 31 => Marshal.PtrToStringUni(value.Pointer), 19 => value.Number, _ => null };
        }
        finally { PropVariantClear(ref value); }
    }
    public void Dispose() { Release(policy); Release(enumerator); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    // PROPVARIANT's union occupies 16 bytes on x64 (including counted-array variants).
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; [FieldOffset(8)] public uint Number; }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    // Windows' undocumented desktop policy interface. Keep isolated, validate by rereading
    // both defaults, and surface failures instead of claiming a successful switch.
    // ABI reference: https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void Slot1(); void Slot2(); void Slot3(); void Slot4(); void Slot5();
        void Slot6(); void Slot7(); void Slot8(); void Slot9(); void Slot10();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}

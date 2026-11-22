using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace HelperBar;

public sealed record VolumeState(bool Available, float Level, bool Muted)
{
    public string? EndpointId { get; init; }
    public int Percent => (int)Math.Round(Level * 100);
    public static readonly VolumeState Unavailable = new(false, 0, false);
}

/// <summary>Core Audio access stays on one MTA thread. UI changes are delivered by the dispatcher.</summary>
public sealed class AudioVolumeService : IDisposable
{
    private readonly AutoResetEvent wake = new(false);
    private readonly object gate = new();
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Thread? worker;
    private volatile bool stopping;
    private float? requestedLevel;
    private string? commandEndpointId;
    private int muteToggles;
    private VolumeState state = VolumeState.Unavailable, delivered = VolumeState.Unavailable;
    public VolumeState State => Volatile.Read(ref state);
    public event Action<VolumeState>? Changed;
    public void Start()
    {
        if (worker != null || stopping) return;
        poll.Tick += (_, _) => { var next = State; if (next != delivered) { delivered = next; Changed?.Invoke(next); } };
        worker = new Thread(Run) { IsBackground = true, Name = "HelperBar system volume" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start(); poll.Start();
    }
    public void SetLevel(float level)
    {
        if (stopping || !float.IsFinite(level) || !State.Available) return;
        lock (gate) { BindCommands(); requestedLevel = Math.Clamp(level, 0, 1); }
        wake.Set();
    }
    public void Change(float delta)
    {
        if (stopping || !State.Available || !float.IsFinite(delta)) return;
        lock (gate) { BindCommands(); requestedLevel = Math.Clamp((requestedLevel ?? State.Level) + delta, 0, 1); }
        wake.Set();
    }
    public void ToggleMute() { if (stopping || !State.Available) return; lock (gate) { BindCommands(); muteToggles++; } wake.Set(); }
    private void BindCommands()
    {
        if (commandEndpointId == State.EndpointId) return;
        requestedLevel = null; muteToggles = 0; commandEndpointId = State.EndpointId;
    }
    public void CancelPending() => ClearCommands();
    private void Run()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        IMMDeviceEnumerator? enumerator = null;
        IAudioEndpointVolume? endpoint = null;
        string? endpointId = null;
        try
        {
            while (!stopping)
            {
                try
                {
                    enumerator ??= (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
                    IMMDevice? device = null;
                    try
                    {
                        Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device)); Check(device.GetId(out var id));
                        if (endpoint == null || endpointId != id)
                        {
                            Release(endpoint); endpoint = null;
                            var iid = typeof(IAudioEndpointVolume).GUID;
                            Check(device.Activate(ref iid, 23, IntPtr.Zero, out var activated)); endpoint = (IAudioEndpointVolume)activated;
                            // Commands intended for a previous device must not change a newly connected device.
                            if (endpointId != null && endpointId != id) ClearCommands();
                            endpointId = id;
                        }
                    }
                    finally { Release(device); }
                    float? level; int toggles; string? commandId;
                    lock (gate) { level = requestedLevel; toggles = muteToggles; commandId = commandEndpointId; requestedLevel = null; muteToggles = 0; }
                    if (commandId == endpointId && level.HasValue) Check(endpoint.SetMasterVolumeLevelScalar(level.Value, IntPtr.Zero));
                    if (commandId == endpointId && toggles % 2 != 0)
                    { Check(endpoint.GetMute(out var muted)); Check(endpoint.SetMute(!muted, IntPtr.Zero)); }
                    Check(endpoint.GetMasterVolumeLevelScalar(out var actual)); Check(endpoint.GetMute(out var mute));
                    Volatile.Write(ref state, new VolumeState(true, Math.Clamp(actual, 0, 1), mute) { EndpointId = endpointId });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Volatile.Write(ref state, VolumeState.Unavailable); ClearCommands();
                    Release(endpoint); endpoint = null; Release(enumerator); enumerator = null;
                }
                wake.WaitOne(State.Available ? 200 : 1000);
            }
        }
        finally { Release(endpoint); Release(enumerator); if (initialized) CoUninitialize(); }
    }
    private void ClearCommands() { lock (gate) { requestedLevel = null; muteToggles = 0; commandEndpointId = null; } }
    public void Dispose()
    {
        if (stopping) return;
        stopping = true; poll.Stop(); Changed = null; wake.Set();
        if (worker == null || worker.Join(700)) wake.Dispose();
    }
    private static void Check(int code) { if (code < 0) Marshal.ThrowExceptionForHR(code); }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}

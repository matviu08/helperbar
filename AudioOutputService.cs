using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HelperBar;

public sealed class AudioOutputService : IDisposable
{
    private readonly AutoResetEvent wake = new(false);
    private readonly object gate = new();
    private readonly DispatcherTimer delivery = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Thread? worker;
    private volatile bool stopping;
    private AudioOutputs state = AudioOutputs.Initial, delivered = AudioOutputs.Initial;
    private (string Id, TaskCompletionSource<AudioSwitchResult> Completion)? request;
    private string? switchingId;
    public AudioOutputs State => Volatile.Read(ref state);
    public event Action<AudioOutputs>? Changed;
    public void Start()
    {
        if (worker != null || stopping) return;
        delivery.Tick += (_, _) => { var next = State; if (!next.SameAs(delivered)) { delivered = next; Changed?.Invoke(next); } };
        worker = new Thread(Run) { IsBackground = true, Name = "HelperBar audio outputs" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start(); delivery.Start();
    }
    public void Refresh() { lock (gate) { if (!stopping) wake.Set(); } }
    public Task<AudioSwitchResult> SelectAsync(string id)
    {
        lock (gate)
        {
            if (stopping || worker == null || switchingId != null) return Task.FromResult(new AudioSwitchResult(false, "Дочекайся завершення перемикання."));
            var completion = new TaskCompletionSource<AudioSwitchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            switchingId = id; request = (id, completion);
            Volatile.Write(ref state, State with { SwitchingId = id, Error = null });
            wake.Set(); return completion.Task;
        }
    }
    private void Publish(AudioOutputs snapshot)
    {
        lock (gate) Volatile.Write(ref state, snapshot with { SwitchingId = switchingId });
    }
    private void Run()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        IAudioDevices? devices = null;
        try
        {
            while (!stopping)
            {
                (string Id, TaskCompletionSource<AudioSwitchResult> Completion)? selected;
                lock (gate) { selected = request; request = null; }
                var result = new AudioSwitchResult(false, "Служба звуку недоступна. Відкрий налаштування Windows.");
                try
                {
                    devices ??= new WindowsAudioDevices();
                    if (selected is { } choice) result = AudioOutputSwitch.Select(devices, choice.Id);
                    var snapshot = devices.Read();
                    if (selected != null && !result.Success) snapshot = snapshot with { Error = result.Message };
                    Publish(snapshot);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Publish(new(Array.Empty<AudioOutput>(), null, Error: "Не вдалося отримати аудіовиходи. Перевір підключення."));
                    devices?.Dispose(); devices = null;
                }
                finally
                {
                    if (selected is { } choice)
                    {
                        lock (gate) { switchingId = null; Volatile.Write(ref state, State with { SwitchingId = null }); }
                        choice.Completion.TrySetResult(result);
                    }
                }
                wake.WaitOne(1000);
            }
        }
        finally
        {
            devices?.Dispose();
            lock (gate) { request?.Completion.TrySetResult(new(false, "HelperBar закривається.")); request = null; }
            if (initialized) CoUninitialize();
        }
    }
    public void Dispose()
    {
        lock (gate) { if (stopping) return; stopping = true; wake.Set(); }
        delivery.Stop(); Changed = null;
        if (worker == null || worker.Join(700)) wake.Dispose();
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

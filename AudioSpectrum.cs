using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HelperBar;

public sealed record SpectrumFrame(float[] Bands, float Level, float Bass, long Timestamp)
{
    public static readonly SpectrumFrame Silent = new(new float[SpectrumAnalyzer.BandCount], 0, 0, 0);
}

/// <summary>Hann-windowed FFT of real PCM samples. No oscillator or random values drive the visualizer.</summary>
public sealed class SpectrumAnalyzer
{
    public const int Size = 2048, BandCount = 24;
    private readonly float[] ring = new float[Size];
    private readonly double[] real = new double[Size], imaginary = new double[Size], window = new double[Size];
    private readonly int sampleRate;
    private int cursor, samples, sinceTransform;
    public SpectrumFrame Latest { get; private set; } = SpectrumFrame.Silent;
    public SpectrumAnalyzer(int sampleRate)
    {
        this.sampleRate = sampleRate;
        for (var i = 0; i < Size; i++) window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1));
    }
    public void Push(float value)
    {
        ring[cursor] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
        cursor = (cursor + 1) & (Size - 1); samples++; sinceTransform++;
        if (samples >= Size && sinceTransform >= 1024) { sinceTransform = 0; Transform(); }
    }
    private void Transform()
    {
        var energy = 0d;
        for (var i = 0; i < Size; i++) { var value = ring[(cursor + i) & (Size - 1)]; energy += value * value; real[i] = value * window[i]; imaginary[i] = 0; }
        for (int i = 1, j = 0; i < Size; i++)
        {
            var bit = Size >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (real[i], real[j]) = (real[j], real[i]);
        }
        for (var length = 2; length <= Size; length <<= 1)
        {
            var angle = -2 * Math.PI / length; var stepR = Math.Cos(angle); var stepI = Math.Sin(angle);
            for (var start = 0; start < Size; start += length)
            {
                double wr = 1, wi = 0;
                for (var j = 0; j < length / 2; j++)
                {
                    var a = start + j; var b = a + length / 2;
                    var tr = real[b] * wr - imaginary[b] * wi; var ti = real[b] * wi + imaginary[b] * wr;
                    real[b] = real[a] - tr; imaginary[b] = imaginary[a] - ti; real[a] += tr; imaginary[a] += ti;
                    (wr, wi) = (wr * stepR - wi * stepI, wr * stepI + wi * stepR);
                }
            }
        }
        var bands = new float[BandCount];
        var maxFrequency = Math.Min(16000, sampleRate / 2d);
        for (var band = 0; band < BandCount; band++)
        {
            var low = 40 * Math.Pow(maxFrequency / 40, band / (double)BandCount);
            var high = 40 * Math.Pow(maxFrequency / 40, (band + 1d) / BandCount);
            var from = Math.Clamp((int)Math.Floor(low * Size / sampleRate), 1, Size / 2 - 1);
            var to = Math.Clamp((int)Math.Ceiling(high * Size / sampleRate), from + 1, Size / 2);
            var magnitude = 0d;
            for (var bin = from; bin < to; bin++) magnitude = Math.Max(magnitude, Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) * 4 / Size);
            bands[band] = Normalize(magnitude);
        }
        var bass = 0f; for (var i = 0; i < 7; i++) bass = Math.Max(bass, bands[i]);
        Latest = new SpectrumFrame(bands, Normalize(Math.Sqrt(energy / Size)), bass, Stopwatch.GetTimestamp());
    }
    public static float Normalize(double magnitude) => magnitude < .00015 ? 0 : (float)Math.Clamp((20 * Math.Log10(magnitude) + 65) / 65, 0, 1);
}

internal readonly record struct PcmFormat(int Channels, int SampleRate, int Bits, int BlockAlign, bool FloatingPoint)
{
    public static PcmFormat Read(IntPtr pointer)
    {
        var tag = (ushort)Marshal.ReadInt16(pointer); var channels = (ushort)Marshal.ReadInt16(pointer, 2);
        var sampleRate = Marshal.ReadInt32(pointer, 4); var block = (ushort)Marshal.ReadInt16(pointer, 12); var bits = (ushort)Marshal.ReadInt16(pointer, 14);
        if (tag == 0xfffe)
        {
            if ((ushort)Marshal.ReadInt16(pointer, 16) < 22) throw new NotSupportedException("Invalid extensible audio format.");
            var sub = Marshal.PtrToStructure<Guid>(pointer + 24);
            tag = sub == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : sub == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1 : (ushort)0;
        }
        if (channels < 1 || channels > 32 || sampleRate < 8000 || sampleRate > 384000 || block < channels * bits / 8 || (tag != 1 && tag != 3) || (tag == 3 ? bits != 32 && bits != 64 : bits != 8 && bits != 16 && bits != 24 && bits != 32)) throw new NotSupportedException("Unsupported endpoint audio format.");
        return new(channels, sampleRate, bits, block, tag == 3);
    }
    public float Sample(ReadOnlySpan<byte> bytes)
    {
        double value;
        if (FloatingPoint) value = Bits == 32 ? BitConverter.ToSingle(bytes) : BitConverter.ToDouble(bytes);
        else value = Bits switch { 8 => (bytes[0] - 128) / 128d, 16 => BitConverter.ToInt16(bytes) / 32768d, 24 => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608d, 32 => BitConverter.ToInt32(bytes) / 2147483648d, _ => 0 };
        return double.IsFinite(value) ? (float)Math.Clamp(value, -1, 1) : 0;
    }
}

/// <summary>Reads the default render endpoint into an in-memory FFT; never records a file or opens a microphone.</summary>
public sealed class AudioSpectrum : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Thread worker;
    private SpectrumFrame latest = SpectrumFrame.Silent;
    private int disposed, available;
    private string status = "Під’єднуємо звук…";
    public bool Available => Volatile.Read(ref available) == 1;
    public string Status => Volatile.Read(ref status);
    public AudioSpectrum()
    {
        worker = new Thread(CaptureLoop) { IsBackground = true, Name = "HelperBar audio spectrum" }; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    public SpectrumFrame Snapshot
    {
        get { var frame = Volatile.Read(ref latest); return frame.Timestamp == 0 || Stopwatch.GetElapsedTime(frame.Timestamp).TotalSeconds > .18 ? SpectrumFrame.Silent : frame; }
    }
    private void CaptureLoop()
    {
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try { CaptureEndpoint(); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Volatile.Write(ref available, 0); Volatile.Write(ref latest, SpectrumFrame.Silent);
                    Volatile.Write(ref status, "Звук недоступний · перепід’єднуємо");
                    if (stop.Token.WaitHandle.WaitOne(1500)) break;
                }
            }
        }
        finally { Volatile.Write(ref available, 0); if (initialized) CoUninitialize(); }
    }
    private void CaptureEndpoint()
    {
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioClient? client = null; IAudioCaptureClient? capture = null; IntPtr formatPointer = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
            Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device)); Check(device.GetId(out var deviceId));
            var clientId = typeof(IAudioClient).GUID; Check(device.Activate(ref clientId, 23, IntPtr.Zero, out var activated)); client = (IAudioClient)activated;
            Check(client.GetMixFormat(out formatPointer)); var format = PcmFormat.Read(formatPointer);
            Check(client.Initialize(0, 0x00020000, 1_000_000, 0, formatPointer, IntPtr.Zero)); // shared-mode loopback, 100 ms buffer
            Marshal.FreeCoTaskMem(formatPointer); formatPointer = IntPtr.Zero;
            var captureId = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref captureId, out var service)); capture = (IAudioCaptureClient)service;
            var analyzer = new SpectrumAnalyzer(format.SampleRate); var bytes = new byte[format.BlockAlign * 8192];
            Check(client.Start()); Volatile.Write(ref available, 1); Volatile.Write(ref status, "Системний звук");
            var deviceCheck = Stopwatch.StartNew();
            while (!stop.IsCancellationRequested)
            {
                Check(capture.GetNextPacketSize(out var packetFrames));
                while (packetFrames > 0 && !stop.IsCancellationRequested)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try
                    {
                        var byteCount = checked((int)frames * format.BlockAlign);
                        if (byteCount > 8 * 1024 * 1024) throw new InvalidOperationException("Unexpected audio packet size.");
                        if (bytes.Length < byteCount) Array.Resize(ref bytes, byteCount);
                        var silent = (flags & 2) != 0;
                        if (!silent) Marshal.Copy(data, bytes, 0, byteCount);
                        for (var frame = 0; frame < frames; frame++)
                        {
                            float sum = 0, strongest = 0;
                            if (!silent)
                            {
                                for (var channel = 0; channel < format.Channels; channel++)
                                {
                                    var offset = frame * format.BlockAlign + channel * (format.Bits / 8);
                                    var sample = format.Sample(bytes.AsSpan(offset, format.Bits / 8));
                                    sum += sample; if (Math.Abs(sample) > Math.Abs(strongest)) strongest = sample;
                                }
                            }
                            var mono = sum / format.Channels;
                            // Preserve activity for out-of-phase stereo instead of cancelling both channels.
                            analyzer.Push(Math.Abs(mono) < Math.Abs(strongest) * .1 ? strongest : mono);
                        }
                        Volatile.Write(ref latest, analyzer.Latest);
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out packetFrames));
                }
                if (deviceCheck.ElapsedMilliseconds >= 1500)
                {
                    IMMDevice? current = null;
                    try { Check(enumerator.GetDefaultAudioEndpoint(0, 1, out current)); Check(current.GetId(out var currentId)); if (currentId != deviceId) return; }
                    finally { Release(current); }
                    deviceCheck.Restart();
                }
                if (stop.Token.WaitHandle.WaitOne(12)) return;
            }
        }
        finally
        {
            Volatile.Write(ref latest, SpectrumFrame.Silent); Volatile.Write(ref available, 0);
            if (client != null) { try { client.Stop(); } catch { } }
            if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
            Release(capture); Release(client); Release(device); Release(enumerator);
        }
    }
    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel(); if (worker.Join(1500)) stop.Dispose();
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint apartment);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint size);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}

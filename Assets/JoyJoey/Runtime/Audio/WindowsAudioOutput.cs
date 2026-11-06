using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace JoyJoey.Game.Audio
{
    /// <summary>An actual Windows render endpoint. The empty ID means Unity's normal system output.</summary>
    public sealed class AudioOutputDevice
    {
        public string Id { get; }
        public string Name { get; }
        public bool IsDefault { get; }
        public AudioOutputDevice(string id, string name, bool isDefault)
        { Id = id ?? ""; Name = name ?? ""; IsDefault = isDefault; }
    }

    /// <summary>
    /// Per-process WASAPI output for the final Unity mix. Never changes the Windows default device.
    /// Submit is called by the final listener filter; it allocates nothing and never waits for the OS.
    /// The caller must silence Unity's buffer only when Submit returns true, preventing doubled output.
    /// </summary>
    public sealed class WindowsAudioOutput : IDisposable
    {
        public const string SystemDefaultId = "";
        Route _route;
        string _selectedId = "";
        public string SelectedDeviceId => _selectedId;
        public string LastError { get; private set; } = "";
        public string Diagnostic { get; private set; } = "";
        public bool IsRouting
        {
            get { var route = Volatile.Read(ref _route); return route != null && Volatile.Read(ref route.Active) != 0; }
        }
        public long SubmittedFrames => Interlocked.Read(ref _submittedFrames);
        public long RenderedFrames => Interlocked.Read(ref _renderedFrames);
        public long NonSilentFrames => Interlocked.Read(ref _nonSilentFrames);
        public long UnderrunFrames => Interlocked.Read(ref _underrunFrames);
        public long OverflowFrames => Interlocked.Read(ref _overflowFrames);
        long _submittedFrames, _renderedFrames, _nonSilentFrames, _underrunFrames, _overflowFrames;

        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return IntPtr.Size == 8;
#else
                return false;
#endif
            }
        }

        /// <summary>Enumeration is isolated on an STA thread. Returns only active render endpoints.</summary>
        public static AudioOutputDevice[] GetDevices(out string error)
        {
            error = "";
            var systemDefault = new AudioOutputDevice("", "Predeterminado del sistema", true);
            if (!IsSupported) return new[] { systemDefault };
            AudioOutputDevice[] devices = Array.Empty<AudioOutputDevice>();
            string failure = null;
            var thread = NewStaThread(() =>
            {
                try { devices = EnumerateDevices(); }
                catch (Exception exception) { failure = exception.Message; }
            }, "Joy Joey · dispositivos de audio");
            thread.Start();
            if (!thread.Join(2000))
            { error = "La lista de dispositivos tardó demasiado. Podés usar la salida predeterminada."; return new[] { systemDefault }; }
            if (failure != null) error = "No se pudo consultar la lista de dispositivos de reproducción.";
            var choices = new AudioOutputDevice[devices.Length + 1];
            choices[0] = systemDefault;
            Array.Copy(devices, 0, choices, 1, devices.Length);
            return choices;
        }

        /// <summary>Called on the main thread. Empty ID returns immediately to Unity's normal output.</summary>
        public bool TrySelect(string deviceId, int sourceSampleRate, out string error)
        {
            error = "";
            StopRoute();
            _selectedId = "";
            LastError = "";
            Diagnostic = "";
            if (string.IsNullOrEmpty(deviceId)) return true;
            if (!IsSupported)
            { error = LastError = "La selección de salida está disponible en Windows de 64 bits."; return false; }
            if (sourceSampleRate < 8000 || sourceSampleRate > 192000)
            { error = LastError = "El formato de audio actual no permite cambiar de dispositivo."; return false; }

            var route = new Route(deviceId, sourceSampleRate);
            route.Thread = NewStaThread(() => Render(route), "Joy Joey · salida de audio");
            Volatile.Write(ref _route, route);
            route.Thread.Start();
            if (!route.Ready.Wait(2000) || Volatile.Read(ref route.Active) == 0)
            {
                Diagnostic = route.Failure ?? "WASAPI initialization timed out.";
                StopRoute();
                error = LastError = "No se pudo abrir ese dispositivo. Se mantiene la salida predeterminada.";
                return false;
            }
            _selectedId = deviceId;
            return true;
        }

        /// <summary>
        /// Main-thread health check. True means the chosen endpoint failed and output fell back to Unity.
        /// The audio callback already returns false immediately on failure, even before this is called.
        /// </summary>
        public bool Tick()
        {
            var route = Volatile.Read(ref _route);
            if (route == null || route.Failure == null) return false;
            Diagnostic = route.Failure;
            LastError = "El dispositivo dejó de estar disponible. Se usa la salida predeterminada.";
            _selectedId = "";
            StopRoute();
            return true;
        }

        /// <summary>
        /// Unity audio-thread entry. Input is interleaved, mono or stereo. Returns true when the caller
        /// must clear its Unity buffer. A failed/inactive route returns false, leaving normal output intact.
        /// </summary>
        public bool Submit(float[] data, int channels)
        {
            var route = Volatile.Read(ref _route);
            if (route == null || Volatile.Read(ref route.Active) == 0 || data == null || channels <= 0) return false;
            int frames = data.Length / channels;
            long write = Volatile.Read(ref route.WriteFrame);
            long read = Volatile.Read(ref route.ReadFrame);
            int available = (int)Math.Max(0, Route.Capacity - (write - read));
            int accepted = Math.Min(frames, available);
            for (int i = 0; i < accepted; i++)
            {
                int input = i * channels;
                int output = (int)((write + i) & Route.Mask) * 2;
                route.Samples[output] = data[input];
                route.Samples[output + 1] = data[input + (channels > 1 ? 1 : 0)];
            }
            Volatile.Write(ref route.WriteFrame, write + accepted);
            Interlocked.Add(ref _submittedFrames, accepted);
            if (accepted < frames) Interlocked.Add(ref _overflowFrames, frames - accepted);
            return Volatile.Read(ref route.Active) != 0;
        }

        void StopRoute()
        {
            var route = Interlocked.Exchange(ref _route, null);
            if (route == null) return;
            Interlocked.Exchange(ref route.Active, 0);
            Volatile.Write(ref route.Stop, 1);
            // All COM release/Stop calls stay on their owning worker thread.
            if (route.Thread == null || route.Thread != Thread.CurrentThread && route.Thread.Join(1000))
                route.Ready.Dispose();
        }
        public void Dispose() { StopRoute(); _selectedId = ""; }

        static Thread NewStaThread(ThreadStart start, string name)
        {
            var thread = new Thread(start) { IsBackground = true, Name = name };
            thread.SetApartmentState(ApartmentState.STA);
            return thread;
        }

        sealed class Route
        {
            public const int Capacity = 8192, Mask = Capacity - 1;
            public readonly float[] Samples = new float[Capacity * 2];
            public readonly string DeviceId;
            public readonly int SampleRate;
            public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
            public Thread Thread;
            public long ReadFrame, WriteFrame;
            public int Stop, Active;
            public volatile string Failure;
            public Route(string id, int rate) { DeviceId = id; SampleRate = rate; }
        }

        void Render(Route route)
        {
            IntPtr enumerator = IntPtr.Zero, device = IntPtr.Zero, client = IntPtr.Zero, render = IntPtr.Zero;
            bool comInitialized = false, started = false;
            try
            {
                int init = Native.CoInitializeEx(IntPtr.Zero, 2); // COINIT_APARTMENTTHREADED
                Check(init); comInitialized = true;
                enumerator = CreateEnumerator();
                Check(Method<Native.GetDevice>(enumerator, 5)(enumerator, route.DeviceId, out device));
                Guid audioClient = Native.AudioClient;
                Check(Method<Native.Activate>(device, 3)(device, ref audioClient, 23, IntPtr.Zero, out client));
                var format = new Native.WaveFormat
                {
                    FormatTag = 3, Channels = 2, SamplesPerSec = (uint)route.SampleRate,
                    AverageBytesPerSec = (uint)route.SampleRate * 8, BlockAlign = 8, BitsPerSample = 32, ExtraSize = 0
                };
                // Windows performs any channel/rate conversion to the endpoint mix format.
                Check(Method<Native.Initialize>(client, 3)(client, 0, 0x88080000u, 400000, 0, ref format, IntPtr.Zero));
                var getSize = Method<Native.GetUInt>(client, 4);
                Check(getSize(client, out uint capacity));
                if (capacity == 0 || capacity > route.SampleRate * 2) throw new InvalidOperationException("Invalid endpoint buffer size.");
                Guid renderClient = Native.RenderClient;
                Check(Method<Native.GetService>(client, 14)(client, ref renderClient, out render));
                var getPadding = Method<Native.GetUInt>(client, 6);
                var getBuffer = Method<Native.GetBuffer>(render, 3);
                var releaseBuffer = Method<Native.ReleaseBuffer>(render, 4);
                var start = Method<Native.Simple>(client, 10);
                var samples = new float[capacity * 2];
                Check(getBuffer(render, capacity, out _));
                Check(releaseBuffer(render, capacity, 2)); // AUDCLNT_BUFFERFLAGS_SILENT
                Check(start(client)); started = true;
                Volatile.Write(ref route.Active, 1);
                route.Ready.Set();
                while (Volatile.Read(ref route.Stop) == 0)
                {
                    Check(getPadding(client, out uint padding));
                    uint available = capacity - Math.Min(capacity, padding);
                    if (available > 0)
                    {
                        Fill(route, samples, (int)available);
                        Check(getBuffer(render, available, out IntPtr buffer));
                        try { Marshal.Copy(samples, 0, buffer, (int)available * 2); }
                        finally { Check(releaseBuffer(render, available, 0)); }
                        Interlocked.Add(ref _renderedFrames, available);
                    }
                    Thread.Sleep(3);
                }
            }
            catch (Exception exception)
            {
                route.Failure = exception.GetType().Name + ": " + exception.Message;
            }
            finally
            {
                Volatile.Write(ref route.Active, 0);
                route.Ready.Set();
                if (started && client != IntPtr.Zero)
                {
                    try { Method<Native.Simple>(client, 11)(client); }
                    catch (Exception) { }
                }
                Release(render); Release(client); Release(device); Release(enumerator);
                if (comInitialized) Native.CoUninitialize();
            }
        }

        void Fill(Route route, float[] output, int frames)
        {
            long read = Volatile.Read(ref route.ReadFrame);
            long write = Volatile.Read(ref route.WriteFrame);
            int maxQueue = Math.Max(512, route.SampleRate / 25);
            // Drop a stale backlog after scheduling stalls instead of increasing latency indefinitely.
            if (write - read > maxQueue) read = write - maxQueue;
            int taken = (int)Math.Min(frames, Math.Max(0, write - read));
            long nonSilent = 0;
            for (int i = 0; i < taken; i++)
            {
                int source = (int)((read + i) & Route.Mask) * 2;
                float left = route.Samples[source], right = route.Samples[source + 1];
                output[i * 2] = left;
                output[i * 2 + 1] = right;
                if (Math.Abs(left) > .00001f || Math.Abs(right) > .00001f) nonSilent++;
            }
            Array.Clear(output, taken * 2, (frames - taken) * 2);
            Volatile.Write(ref route.ReadFrame, read + taken);
            if (nonSilent > 0) Interlocked.Add(ref _nonSilentFrames, nonSilent);
            if (taken < frames) Interlocked.Add(ref _underrunFrames, frames - taken);
        }

        static AudioOutputDevice[] EnumerateDevices()
        {
            IntPtr enumerator = IntPtr.Zero, collection = IntPtr.Zero, defaultDevice = IntPtr.Zero;
            bool initialized = false;
            var result = new List<AudioOutputDevice>();
            try
            {
                Check(Native.CoInitializeEx(IntPtr.Zero, 2)); initialized = true;
                enumerator = CreateEnumerator();
                string defaultId = "";
                if (Method<Native.GetDefault>(enumerator, 4)(enumerator, 0, 1, out defaultDevice) >= 0)
                    defaultId = DeviceId(defaultDevice);
                Check(Method<Native.EnumEndpoints>(enumerator, 3)(enumerator, 0, 1, out collection));
                Check(Method<Native.GetUInt>(collection, 3)(collection, out uint count));
                var item = Method<Native.CollectionItem>(collection, 4);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr device = IntPtr.Zero, store = IntPtr.Zero;
                    try
                    {
                        Check(item(collection, i, out device));
                        string id = DeviceId(device), name = "Dispositivo de reproducción";
                        if (Method<Native.OpenStore>(device, 4)(device, 0, out store) >= 0)
                        {
                            var key = new Native.PropertyKey { FormatId = Native.FriendlyNameKey, PropertyId = 14 };
                            Native.PropVariant value;
                            if (Method<Native.GetProperty>(store, 5)(store, ref key, out value) >= 0)
                            {
                                try { if (value.Type == 31 && value.Pointer != IntPtr.Zero) name = Marshal.PtrToStringUni(value.Pointer) ?? name; }
                                finally { Native.PropVariantClear(ref value); }
                            }
                        }
                        result.Add(new AudioOutputDevice(id, name, id == defaultId));
                    }
                    finally { Release(store); Release(device); }
                }
            }
            finally
            {
                Release(defaultDevice); Release(collection); Release(enumerator);
                if (initialized) Native.CoUninitialize();
            }
            return result.ToArray();
        }

        static string DeviceId(IntPtr device)
        {
            if (device == IntPtr.Zero) return "";
            Check(Method<Native.GetId>(device, 5)(device, out IntPtr pointer));
            try { return Marshal.PtrToStringUni(pointer) ?? ""; }
            finally { Marshal.FreeCoTaskMem(pointer); }
        }
        static IntPtr CreateEnumerator()
        {
            Guid clsid = Native.DeviceEnumeratorClass, iid = Native.DeviceEnumerator;
            Check(Native.CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out IntPtr enumerator));
            return enumerator;
        }
        static T Method<T>(IntPtr pointer, int slot) where T : class
        {
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Missing COM interface.");
            IntPtr table = Marshal.ReadIntPtr(pointer);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), typeof(T));
        }
        static void Release(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return;
            try { Method<Native.Release>(pointer, 2)(pointer); }
            catch (Exception) { }
        }
        static void Check(int result)
        {
            if (result < 0) throw new COMException("WASAPI HRESULT 0x" + unchecked((uint)result).ToString("X8"), result);
        }

        static class Native
        {
            public static readonly Guid DeviceEnumeratorClass = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            public static readonly Guid DeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            public static readonly Guid AudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
            public static readonly Guid RenderClient = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
            public static readonly Guid FriendlyNameKey = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
            [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr reserved, uint flags);
            [DllImport("ole32.dll")] public static extern void CoUninitialize();
            [DllImport("ole32.dll")] public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr result);
            [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant value);
            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            public struct WaveFormat
            {
                public ushort FormatTag, Channels;
                public uint SamplesPerSec, AverageBytesPerSec;
                public ushort BlockAlign, BitsPerSample, ExtraSize;
            }
            [StructLayout(LayoutKind.Sequential)]
            public struct PropertyKey { public Guid FormatId; public uint PropertyId; }
            [StructLayout(LayoutKind.Explicit, Size = 24)]
            public struct PropVariant
            {
                [FieldOffset(0)] public ushort Type;
                [FieldOffset(8)] public IntPtr Pointer;
            }
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint Release(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Simple(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetUInt(IntPtr self, out uint value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int EnumEndpoints(IntPtr self, int flow, uint states, out IntPtr collection);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetDefault(IntPtr self, int flow, int role, out IntPtr device);
            [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] public delegate int GetDevice(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CollectionItem(IntPtr self, uint index, out IntPtr device);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetId(IntPtr self, out IntPtr id);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int OpenStore(IntPtr self, uint access, out IntPtr store);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetProperty(IntPtr self, ref PropertyKey key, out PropVariant value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Activate(IntPtr self, ref Guid iid, uint context, IntPtr activation, out IntPtr result);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int Initialize(IntPtr self, int share, uint flags, long duration, long period, ref WaveFormat format, IntPtr sessionGuid);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetService(IntPtr self, ref Guid iid, out IntPtr service);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetBuffer(IntPtr self, uint frames, out IntPtr buffer);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ReleaseBuffer(IntPtr self, uint frames, uint flags);
        }
    }
}

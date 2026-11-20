using System.Runtime.InteropServices;
using System.Text;

namespace XRPlayer;

/// <summary>Thin libmpv wrapper (client API).</summary>
public sealed class Mpv : IDisposable
{
    const string Lib = "libmpv-2.dll";

    [DllImport(Lib)] static extern IntPtr mpv_create();
    [DllImport(Lib)] static extern int mpv_initialize(IntPtr h);
    [DllImport(Lib)] static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport(Lib)] static extern int mpv_set_option_string(IntPtr h, byte[] name, byte[] data);
    [DllImport(Lib)] static extern int mpv_set_property_string(IntPtr h, byte[] name, byte[] data);
    [DllImport(Lib)] static extern IntPtr mpv_get_property_string(IntPtr h, byte[] name);
    [DllImport(Lib)] static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] static extern int mpv_command(IntPtr h, IntPtr[] args);
    [DllImport(Lib)] static extern int mpv_observe_property(IntPtr h, ulong reply, byte[] name, int format);
    [DllImport(Lib)] static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Lib)] static extern void mpv_wakeup(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    struct Event { public int id; public int error; public ulong reply; public IntPtr data; }
    [StructLayout(LayoutKind.Sequential)]
    struct EventProperty { public IntPtr name; public int format; public IntPtr data; }

    const int EV_SHUTDOWN = 1, EV_END_FILE = 7, EV_FILE_LOADED = 8, EV_PROPERTY_CHANGE = 22;
    const int FMT_STRING = 1;

    IntPtr h;
    Thread loop;
    volatile bool stop;

    public event Action<string, string> PropertyChanged; // name, value (string, may be null)
    public event Action<string[]> Message;
    public event Action FileLoaded;
    public event Action<string> EndFile;

    static byte[] U(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public Mpv(IntPtr wid)
    {
        h = mpv_create();
        if (h == IntPtr.Zero) throw new Exception("mpv_create failed");
        Option("wid", wid.ToInt64().ToString());
        Option("input-default-bindings", "no");
        Option("input-vo-keyboard", "yes");
        Option("input-cursor", "yes");
        Option("osc", "no");
        Option("keep-open", "yes");
        Option("hwdec", "auto-safe");
        Option("vo", "gpu-next");
        Option("sub-visibility", "no");           // we draw subtitles ourselves
        Option("secondary-sub-visibility", "no");
        Option("sub-auto", "fuzzy");
        Option("audio-display", "embedded-first");
        Option("screenshot-directory", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Option("cache", "yes");
        Option("demuxer-max-bytes", "150MiB");
        Option("force-window", "yes");
        Option("idle", "yes");
        Option("osd-level", "0");
        int r = mpv_initialize(h);
        if (r < 0) throw new Exception("mpv_initialize failed: " + r);
        loop = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        loop.Start();
    }

    public void Option(string n, string v) => mpv_set_option_string(h, U(n), U(v));
    public void Set(string n, string v) { if (h != IntPtr.Zero) mpv_set_property_string(h, U(n), U(v)); }
    public void Set(string n, bool v) => Set(n, v ? "yes" : "no");
    public void Set(string n, double v) => Set(n, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void Set(string n, int v) => Set(n, v.ToString());

    public string Get(string n)
    {
        if (h == IntPtr.Zero) return null;
        var p = mpv_get_property_string(h, U(n));
        if (p == IntPtr.Zero) return null;
        var s = Marshal.PtrToStringUTF8(p);
        mpv_free(p);
        return s;
    }
    public double GetD(string n, double def = 0) =>
        double.TryParse(Get(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : def;

    public void Observe(string n) => mpv_observe_property(h, 0, U(n), FMT_STRING);

    public int Command(params string[] args)
    {
        if (h == IntPtr.Zero) return -1;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                var b = U(args[i]);
                ptrs[i] = Marshal.AllocHGlobal(b.Length);
                Marshal.Copy(b, 0, ptrs[i], b.Length);
            }
            return mpv_command(h, ptrs);
        }
        finally { foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }
    }

    void EventLoop()
    {
        while (!stop)
        {
            var ev = Marshal.PtrToStructure<Event>(mpv_wait_event(h, 0.5));
            switch (ev.id)
            {
                case EV_SHUTDOWN: return;
                case 16: // client message
                    int n = Marshal.ReadInt32(ev.data);
                    var argv = Marshal.ReadIntPtr(ev.data, IntPtr.Size);
                    var list = new string[n];
                    for (int i = 0; i < n; i++) list[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                    Message?.Invoke(list);
                    break;
                case EV_FILE_LOADED: FileLoaded?.Invoke(); break;
                case EV_END_FILE: EndFile?.Invoke(null); break;
                case EV_PROPERTY_CHANGE:
                    var pr = Marshal.PtrToStructure<EventProperty>(ev.data);
                    string name = Marshal.PtrToStringUTF8(pr.name);
                    string val = null;
                    if (pr.format == FMT_STRING && pr.data != IntPtr.Zero)
                    {
                        var sp = Marshal.ReadIntPtr(pr.data);
                        if (sp != IntPtr.Zero) val = Marshal.PtrToStringUTF8(sp);
                    }
                    PropertyChanged?.Invoke(name, val);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (h == IntPtr.Zero) return;
        stop = true;
        var hh = h; h = IntPtr.Zero;
        mpv_wakeup(hh);
        loop?.Join(1000);
        mpv_terminate_destroy(hh);
    }
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace XRPlayer;

/// <summary>Native child window that mpv renders into.</summary>
public class MpvHost : HwndHost
{
    IntPtr hwnd;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);

    public IntPtr Handle => hwnd;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN
        hwnd = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000, 0, 0, 100, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);
}

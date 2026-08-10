using System.Runtime.InteropServices;

namespace ScrollFix;

/// <summary>
/// Global WH_MOUSE_LL hook that can swallow vertical wheel events.
/// </summary>
public sealed class MouseWheelHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int HcAction = 0;

    private readonly Func<int, bool> _shouldBlock;
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    public MouseWheelHook(Func<int, bool> shouldBlock)
    {
        _shouldBlock = shouldBlock;
        // Keep a managed reference so the delegate is not GC'd while hooked.
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        // For WH_MOUSE_LL, the hook runs in the installing process; null module handle is fine.
        _hook = SetWindowsHookEx(
            WhMouseLl,
            _proc,
            GetModuleHandle(null),
            0);

        if (_hook == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HcAction && wParam == (IntPtr)WmMouseWheel)
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var delta = unchecked((short)((info.MouseData >> 16) & 0xFFFF));
            try
            {
                if (_shouldBlock(delta))
                {
                    return (IntPtr)1; // swallow
                }
            }
            catch
            {
                // Never break the input chain because of filter bugs.
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

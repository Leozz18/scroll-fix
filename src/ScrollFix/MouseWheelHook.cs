using System.Runtime.InteropServices;

namespace ScrollFix;

/// <summary>
/// Global WH_MOUSE_LL hook that can swallow vertical wheel events and
/// re-inject previously held notches once a reversal is confirmed.
/// </summary>
public sealed class MouseWheelHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int HcAction = 0;
    private const uint LlmhfInjected = 0x01;
    private const uint InputMouse = 0;
    private const uint MouseEventFWheel = 0x0800;

    /// <summary>Tag placed in dwExtraInfo so we recognise our own replayed events.</summary>
    private static readonly IntPtr ReplayMarker = new(0x5CF1);

    private readonly Func<int, FilterDecision> _decide;
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    public MouseWheelHook(Func<int, FilterDecision> decide)
    {
        _decide = decide;
        // Keep a managed reference so the delegate is not GC'd while hooked.
        _proc = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

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

            // Our own replayed notches must not be filtered again.
            var isOurs = (info.Flags & LlmhfInjected) != 0 && info.DwExtraInfo == ReplayMarker;
            if (!isOurs)
            {
                var delta = unchecked((short)((info.MouseData >> 16) & 0xFFFF));
                try
                {
                    var decision = _decide(delta);
                    if (decision.ReplayDelta != 0)
                    {
                        Replay(decision.ReplayDelta);
                    }

                    if (!decision.Allow)
                    {
                        return (IntPtr)1; // swallow
                    }
                }
                catch
                {
                    // Never break the input chain because of filter bugs.
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void Replay(int delta)
    {
        var input = new Input
        {
            Type = InputMouse,
            Data = new MouseInput
            {
                MouseData = unchecked((uint)delta),
                Flags = MouseEventFWheel,
                ExtraInfo = ReplayMarker,
            },
        };

        SendInput(1, [input], Marshal.SizeOf<Input>());
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Data;
        // MOUSEINPUT is the largest member of the INPUT union on both x86 and x64,
        // so no explicit padding is required here.
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

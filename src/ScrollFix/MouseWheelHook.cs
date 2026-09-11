using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScrollFix;

/// <summary>
/// Global WH_MOUSE_LL hook that can swallow vertical wheel events and
/// re-inject previously held notches once a reversal is confirmed.
///
/// Threading model (important, see v1.1.0 post-mortem in CHANGELOG):
///  - The hook lives on its own high-priority thread with a private message
///    loop, so UI work (menus, dialogs, file writes) can never delay it.
///  - The callback only runs the filter and enqueues replays. It never calls
///    SendInput: doing so from inside a low-level hook deadlocks win32k,
///    because the raw input thread is blocked waiting for the callback while
///    SendInput waits for the raw input thread.
///  - Replays are sent from a dedicated worker thread, in order.
/// </summary>
public sealed class MouseWheelHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int WmQuit = 0x0012;
    private const int HcAction = 0;
    private const uint LlmhfInjected = 0x01;
    private const uint InputMouse = 0;
    private const uint MouseEventFWheel = 0x0800;

    /// <summary>Tag placed in dwExtraInfo so we recognise our own replayed events.</summary>
    private static readonly IntPtr ReplayMarker = new(0x5CF1);

    private readonly Func<int, FilterDecision> _decide;
    private readonly LowLevelMouseProc _proc;
    private readonly BlockingCollection<int> _replayQueue = new();

    private Thread? _hookThread;
    private Thread? _replayThread;
    private uint _hookThreadId;
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookThread is not null)
        {
            return;
        }

        _replayThread = new Thread(ReplayLoop)
        {
            IsBackground = true,
            Name = "ScrollFix.Replay",
        };
        _replayThread.Start();

        var startError = new StartResult();
        using var ready = new ManualResetEventSlim(false);

        _hookThread = new Thread(() => HookThreadMain(ready, startError))
        {
            IsBackground = true,
            Name = "ScrollFix.Hook",
            Priority = ThreadPriority.Highest,
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        ready.Wait();
        if (startError.Error is not null)
        {
            _hookThread = null;
            throw startError.Error;
        }
    }

    private sealed class StartResult
    {
        public Exception? Error;
    }

    public void Stop()
    {
        var thread = _hookThread;
        if (thread is null)
        {
            return;
        }

        PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _replayQueue.CompleteAdding();
        _replayThread?.Join(TimeSpan.FromSeconds(1));
        _replayQueue.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------ hook thread

    private void HookThreadMain(ManualResetEventSlim ready, StartResult result)
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            // For WH_MOUSE_LL, the hook runs in the installing process; null module handle is fine.
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            result.Error = ex;
            ready.Set();
            return;
        }

        ready.Set();

        // Low-level hooks are delivered through this thread's message queue.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
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
                    if (decision.ReplayDelta != 0 && !_replayQueue.IsAddingCompleted)
                    {
                        // Never SendInput from here; hand it to the replay thread.
                        _replayQueue.TryAdd(decision.ReplayDelta);
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

    // ---------------------------------------------------------- replay thread

    private void ReplayLoop()
    {
        try
        {
            foreach (var delta in _replayQueue.GetConsumingEnumerable())
            {
                Replay(delta);
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
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

    // ---------------------------------------------------------------- interop

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
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

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

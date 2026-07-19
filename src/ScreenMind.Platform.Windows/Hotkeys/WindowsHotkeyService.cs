using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenMind.Core.Hotkeys;

namespace ScreenMind.Platform.Windows.Hotkeys;

public sealed partial class WindowsHotkeyService : IHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const int WmAppExecute = 0x8001;
    private const int WmQuit = 0x0012;
    private const uint PmNoRemove = 0;

    private const int WH_MOUSE_LL = 14;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_NCXBUTTONDOWN = 0x00AB;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc? mouseHookProc;
    private IntPtr mouseHookHandle = IntPtr.Zero;

    private readonly ConcurrentQueue<Action> messageThreadActions = new();
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread messageThread;
    private readonly Dictionary<string, RegisteredHotkey> registrations = new(StringComparer.Ordinal);
    private readonly object gate = new();

    private int nextNativeId = 1;
    private bool isPaused;
    private bool isDisposed;
    private uint messageThreadId;

    public WindowsHotkeyService()
    {
        messageThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "ScreenMind hotkey message loop",
        };
        messageThread.Start();
        ready.Wait();
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public async Task<HotkeyRegistrationResult> RegisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ValidateHotkey(registration.Hotkey);

        return await InvokeOnMessageThreadAsync(
            () => RegisterCore(registration),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<HotkeyRegistrationResult> ReassignAsync(
        string id,
        Hotkey hotkey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ValidateHotkey(hotkey);

        return await InvokeOnMessageThreadAsync(
            () =>
            {
                if (!registrations.TryGetValue(id, out RegisteredHotkey? existing))
                {
                    return new HotkeyRegistrationResult(false, "Hotkey registration does not exist.");
                }

                if (!isPaused)
                {
                    UnregisterNative(existing);
                }
                RegisteredHotkey replacement = existing with { Hotkey = hotkey };
                HotkeyRegistrationResult result = RegisterNative(replacement);
                if (result.IsSuccess)
                {
                    registrations[id] = replacement;
                    return result;
                }

                HotkeyRegistrationResult rollback = RegisterNative(existing);
                if (rollback.IsSuccess)
                {
                    registrations[id] = existing;
                }
                else
                {
                    registrations.Remove(id);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UnregisterAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await InvokeOnMessageThreadAsync(
            () =>
            {
                if (registrations.Remove(id, out RegisteredHotkey? registration))
                {
                    if (!isPaused)
                    {
                        UnregisterNative(registration);
                    }
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        await InvokeOnMessageThreadAsync(
            () =>
            {
                if (!isPaused)
                {
                    foreach (RegisteredHotkey registration in registrations.Values)
                    {
                        UnregisterNative(registration);
                    }

                    isPaused = true;
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await InvokeOnMessageThreadAsync(
            () =>
            {
                if (!isPaused)
                {
                    return true;
                }

                foreach (RegisteredHotkey registration in registrations.Values)
                {
                    HotkeyRegistrationResult result = RegisterNative(registration);
                    if (!result.IsSuccess)
                    {
                        throw new InvalidOperationException(result.ConflictReason);
                    }
                }

                isPaused = false;
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        await InvokeOnMessageThreadAsync(
            () =>
            {
                isDisposed = true;
                if (!isPaused)
                {
                    foreach (RegisteredHotkey registration in registrations.Values)
                    {
                        UnregisterNative(registration);
                    }
                }

                registrations.Clear();
                NativeMethods.PostThreadMessage(messageThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
                return true;
            },
            CancellationToken.None).ConfigureAwait(false);

        messageThread.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }

    private HotkeyRegistrationResult RegisterCore(HotkeyRegistration registration)
    {
        if (registrations.ContainsKey(registration.Id))
        {
            return new HotkeyRegistrationResult(false, "Hotkey registration already exists.");
        }

        RegisteredHotkey nativeRegistration = new(
            registration.Id,
            registration.Description,
            registration.Hotkey,
            nextNativeId++);

        HotkeyRegistrationResult result = RegisterNative(nativeRegistration);
        if (!result.IsSuccess)
        {
            return result;
        }

        registrations.Add(registration.Id, nativeRegistration);
        return result;
    }

    private HotkeyRegistrationResult RegisterNative(RegisteredHotkey registration)
    {
        if (isPaused)
        {
            return HotkeyRegistrationResult.Success;
        }

        int vk = registration.Hotkey.VirtualKey;
        if (vk is >= 1 and <= 6)
        {
            return HotkeyRegistrationResult.Success;
        }

        uint modifiers = ToNativeModifiers(registration.Hotkey.Modifiers);
        if (NativeMethods.RegisterHotKey(
            IntPtr.Zero,
            registration.NativeId,
            modifiers,
            (uint)registration.Hotkey.VirtualKey))
        {
            return HotkeyRegistrationResult.Success;
        }

        int error = Marshal.GetLastWin32Error();
        return new HotkeyRegistrationResult(
            false,
            $"RegisterHotKey failed with Win32 error {error}: {new Win32Exception(error).Message}");
    }

    private static void UnregisterNative(RegisteredHotkey registration)
    {
        int vk = registration.Hotkey.VirtualKey;
        if (vk is >= 1 and <= 6)
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(IntPtr.Zero, registration.NativeId))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 1409)
            {
                throw new Win32Exception(error, "UnregisterHotKey failed.");
            }
        }
    }

    private Task<T> InvokeOnMessageThreadAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        messageThreadActions.Enqueue(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        if (!NativeMethods.PostThreadMessage(messageThreadId, WmAppExecute, IntPtr.Zero, IntPtr.Zero))
        {
            completion.SetException(new Win32Exception(Marshal.GetLastWin32Error(), "PostThreadMessage failed."));
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowsHookExW(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static int GetXButtonFromMouseData(uint mouseData)
    {
        return (int)((mouseData >> 16) & 0xFFFF);
    }

    private static HotkeyModifiers GetCurrentModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) modifiers |= HotkeyModifiers.Control;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) modifiers |= HotkeyModifiers.Shift;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) modifiers |= HotkeyModifiers.Alt;
        if (((GetAsyncKeyState(0x5B) & 0x8000) != 0) || ((GetAsyncKeyState(0x5C) & 0x8000) != 0)) modifiers |= HotkeyModifiers.Windows;
        return modifiers;
    }

    private IntPtr LowLevelMouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !isPaused && (wParam == (IntPtr)WM_XBUTTONDOWN || wParam == (IntPtr)WM_NCXBUTTONDOWN || wParam == (IntPtr)0x0207 || wParam == (IntPtr)0x00A7))
        {
            int virtualKey = 0;
            if (wParam == (IntPtr)0x0207 || wParam == (IntPtr)0x00A7)
            {
                virtualKey = 0x04; // VK_MBUTTON (Mouse 3)
            }
            else
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int xbutton = GetXButtonFromMouseData(hookStruct.mouseData);
                if (xbutton == 1) virtualKey = 0x05; // VK_XBUTTON1 (Mouse 4)
                else if (xbutton == 2) virtualKey = 0x06; // VK_XBUTTON2 (Mouse 5)
            }

            if (virtualKey > 0)
            {
                HotkeyModifiers currentModifiers = GetCurrentModifiers();
                RegisteredHotkey? matched = null;
                lock (gate)
                {
                    matched = registrations.Values.FirstOrDefault(r => 
                        r.Hotkey.VirtualKey == virtualKey && 
                        (r.Hotkey.Modifiers & ~HotkeyModifiers.NoRepeat) == (currentModifiers & ~HotkeyModifiers.NoRepeat));
                }

                if (matched is not null)
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(matched.Id));
                    return (IntPtr)1; // Consume message
                }
            }
        }

        return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    private void MessageLoop()
    {
        messageThreadId = NativeMethods.GetCurrentThreadId();
        
        mouseHookProc = LowLevelMouseCallback;
        IntPtr hMod = GetModuleHandle(IntPtr.Zero);
        mouseHookHandle = SetWindowsHookExW(WH_MOUSE_LL, mouseHookProc, hMod, 0);

        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        ready.Set();

        try
        {
            while (NativeMethods.GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmAppExecute)
                {
                    DrainActions();
                    continue;
                }

                if (message.Message == WmHotkey)
                {
                    RaiseHotkeyPressed(message.WParam.ToInt32());
                }
            }
        }
        finally
        {
            if (mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(mouseHookHandle);
                mouseHookHandle = IntPtr.Zero;
            }
        }
    }

    private void DrainActions()
    {
        while (messageThreadActions.TryDequeue(out Action? action))
        {
            action();
        }
    }

    private void RaiseHotkeyPressed(int nativeId)
    {
        RegisteredHotkey? registration;
        lock (gate)
        {
            registration = registrations.Values.FirstOrDefault(value => value.NativeId == nativeId);
        }

        if (registration is not null && !isPaused)
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(registration.Id));
        }
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint native = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            native |= 0x0001;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            native |= 0x0002;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            native |= 0x0004;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            native |= 0x0008;
        }

        if (modifiers.HasFlag(HotkeyModifiers.NoRepeat))
        {
            native |= 0x4000;
        }

        return native;
    }

    private static void ValidateHotkey(Hotkey hotkey)
    {
        if (hotkey.VirtualKey <= 0 || hotkey.VirtualKey > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(hotkey), "Virtual key must be in 1..255.");
        }
    }

    private sealed record RegisteredHotkey(
        string Id,
        string Description,
        Hotkey Hotkey,
        int NativeId);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        public readonly IntPtr Hwnd;

        public readonly int Message;

        public readonly IntPtr WParam;

        public readonly IntPtr LParam;

        public readonly uint Time;

        public readonly int PointX;

        public readonly int PointY;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll")]
        public static partial uint GetCurrentThreadId();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostThreadMessage(
            uint idThread,
            int msg,
            IntPtr wParam,
            IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        public static partial int GetMessage(
            out NativeMessage lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax);

        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PeekMessage(
            out NativeMessage lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax,
            uint wRemoveMsg);
    }
}

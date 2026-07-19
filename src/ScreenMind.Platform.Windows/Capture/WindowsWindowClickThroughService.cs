using System;
using System.Runtime.InteropServices;
using ScreenMind.Core.Capture;

namespace ScreenMind.Platform.Windows.Capture;

public sealed partial class WindowsWindowClickThroughService : IWindowClickThroughService, IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly System.Threading.Timer _keepOnTopTimer;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, byte> _activeClickThroughWindows = new();

    public WindowsWindowClickThroughService()
    {
        _keepOnTopTimer = new System.Threading.Timer(KeepWindowsOnTop, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public void Dispose()
    {
        _keepOnTopTimer.Dispose();
    }

    private void KeepWindowsOnTop(object? state)
    {
        foreach (var hwnd in _activeClickThroughWindows.Keys)
        {
            if (IsWindow(hwnd))
            {
                SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                _activeClickThroughWindows.TryRemove(hwnd, out _);
            }
        }
    }

    public void SetClickThrough(IntPtr windowHandle, bool clickThrough)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr currentStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE);
        IntPtr newStyle;

        if (clickThrough)
        {
            newStyle = new IntPtr(currentStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            _activeClickThroughWindows[windowHandle] = 0;
        }
        else
        {
            newStyle = new IntPtr(currentStyle.ToInt64() & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED));
            _activeClickThroughWindows.TryRemove(windowHandle, out _);
        }

        SetWindowLongPtr(windowHandle, GWL_EXSTYLE, newStyle);
        
        // Force the window frame to update and place it topmost
        SetWindowPos(windowHandle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_NOACTIVATE);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        else
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);
}

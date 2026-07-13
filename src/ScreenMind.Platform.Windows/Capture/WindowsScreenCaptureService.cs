using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenMind.Core.Capture;
using ScreenMind.Core.Imaging;
using ScreenMind.Core.Settings;

namespace ScreenMind.Platform.Windows.Capture;

public sealed partial class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int MonitorDefaultToNearest = 2;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint GwHwndNext = 2;

    private readonly ISettingsStore settingsStore;

    public WindowsScreenCaptureService(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task<ScreenImage> CaptureAsync(
        CaptureTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenMindSettings settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        CheckExclusionsAndForbidden(settings);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScreenRectangle bounds = ResolveBounds(target);
            if (bounds.IsEmpty)
            {
                throw new ScreenCaptureException(
                    ScreenCaptureErrorKind.InvalidRegion,
                    "Capture bounds are empty.");
            }

            ScreenImage image = CaptureBounds(bounds);
            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ScreenRectangle ResolveBounds(CaptureTarget target)
    {
        return target switch
        {
            CaptureTarget.ActiveWindow => ResolveActiveWindowBounds(),
            CaptureTarget.MonitorWithCursor => ResolveCursorMonitorBounds(),
            CaptureTarget.Monitor monitor => ResolveMonitorBounds(monitor.Handle),
            CaptureTarget.Region region => region.Bounds,
            _ => throw new ScreenCaptureException(
                ScreenCaptureErrorKind.UnsupportedTarget,
                "Capture target is not supported on Windows."),
        };
    }

    private static ScreenRectangle ResolveActiveWindowBounds()
    {
        IntPtr windowHandle = FindCapturableForegroundWindow();
        if (windowHandle == IntPtr.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.ActiveWindowUnavailable,
                "Active window is unavailable.");
        }

        if (NativeMethods.IsIconic(windowHandle))
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.MinimizedWindow,
                "Active window is minimized.");
        }

        if (TryGetDwmBounds(windowHandle, out ScreenRectangle dwmBounds) && !dwmBounds.IsEmpty)
        {
            return dwmBounds;
        }

        if (!NativeMethods.GetWindowRect(windowHandle, out NativeRectangle rectangle))
        {
            throw CreateCaptureException(
                ScreenCaptureErrorKind.InaccessibleWindow,
                "Active window bounds are inaccessible.");
        }

        return rectangle.ToScreenRectangle();
    }

    private static IntPtr FindCapturableForegroundWindow()
    {
        IntPtr windowHandle = NativeMethods.GetForegroundWindow();
        while (windowHandle != IntPtr.Zero)
        {
            _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
            if (processId != (uint)Environment.ProcessId && NativeMethods.IsWindowVisible(windowHandle))
            {
                return windowHandle;
            }

            windowHandle = NativeMethods.GetWindow(windowHandle, GwHwndNext);
        }

        throw new ScreenCaptureException(
            ScreenCaptureErrorKind.ActiveWindowUnavailable,
            "No capturable foreground window is available.");
    }

    private static ScreenRectangle ResolveCursorMonitorBounds()
    {
        if (!NativeMethods.GetCursorPos(out NativePoint point))
        {
            throw CreateCaptureException(
                ScreenCaptureErrorKind.InaccessibleWindow,
                "Cursor position is inaccessible.");
        }

        IntPtr monitor = NativeMethods.MonitorFromPoint(point, MonitorDefaultToNearest);
        return ResolveMonitorBounds(monitor);
    }

    private static ScreenRectangle ResolveMonitorBounds(IntPtr monitorHandle)
    {
        if (monitorHandle == IntPtr.Zero)
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.ActiveWindowUnavailable,
                "Monitor is unavailable.");
        }

        NativeMonitorInfo monitorInfo = NativeMonitorInfo.Create();
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            throw CreateCaptureException(
                ScreenCaptureErrorKind.InaccessibleWindow,
                "Monitor bounds are inaccessible.");
        }

        return monitorInfo.Monitor.ToScreenRectangle();
    }

    private static bool TryGetDwmBounds(IntPtr windowHandle, out ScreenRectangle bounds)
    {
        int result = NativeMethods.DwmGetWindowAttribute(
            windowHandle,
            DwmwaExtendedFrameBounds,
            out NativeRectangle rectangle,
            Marshal.SizeOf<NativeRectangle>());

        bounds = result == 0 ? rectangle.ToScreenRectangle() : default;
        return result == 0;
    }

    private static ScreenImage CaptureBounds(ScreenRectangle bounds)
    {
        try
        {
            using Bitmap bitmap = new(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                bounds.X,
                bounds.Y,
                0,
                0,
                new Size(bounds.Width, bounds.Height),
                CopyPixelOperation.SourceCopy);

            using MemoryStream stream = new();
            bitmap.Save(stream, ImageFormat.Png);
            return new ScreenImage(
                stream.ToArray(),
                "image/png",
                ScreenImageFormat.Png,
                bounds.Width,
                bounds.Height,
                DateTimeOffset.UtcNow);
        }
        catch (Win32Exception exception)
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.InaccessibleWindow,
                "Screen pixels are inaccessible.",
                exception);
        }
        catch (ExternalException exception)
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.InaccessibleWindow,
                "Screen pixels are inaccessible.",
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new ScreenCaptureException(
                ScreenCaptureErrorKind.InvalidRegion,
                "Capture bounds are invalid.",
                exception);
        }
    }

    private static ScreenCaptureException CreateCaptureException(
        ScreenCaptureErrorKind kind,
        string message)
    {
        return new ScreenCaptureException(
            kind,
            message,
            new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;

        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRectangle
    {
        public readonly int Left;

        public readonly int Top;

        public readonly int Right;

        public readonly int Bottom;

        public ScreenRectangle ToScreenRectangle() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;

        public NativeRectangle Monitor;

        public NativeRectangle WorkArea;

        public uint Flags;

        public static NativeMonitorInfo Create()
        {
            return new NativeMonitorInfo
            {
                Size = Marshal.SizeOf<NativeMonitorInfo>(),
            };
        }
    }

    private static void CheckExclusionsAndForbidden(ScreenMindSettings settings)
    {
        IntPtr activeHwnd = NativeMethods.GetForegroundWindow();
        if (activeHwnd == IntPtr.Zero || !NativeMethods.IsWindow(activeHwnd))
        {
            return;
        }

        // Check process name
        _ = NativeMethods.GetWindowThreadProcessId(activeHwnd, out uint processId);
        if (processId != 0)
        {
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName;
                if (settings.Privacy.BlockedProcessNames.Any(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ScreenCaptureException(
                        ScreenCaptureErrorKind.ProtectedWindow,
                        $"Capture blocked: process '{processName}' is in the forbidden list.");
                }
            }
            catch (ArgumentException)
            {
                // Process not found / exited
            }
        }

        // Check window title fragments
        string title = GetActiveWindowTitle(activeHwnd);
        if (!string.IsNullOrEmpty(title))
        {
            if (settings.Privacy.BlockedWindowTitleFragments.Any(fragment => title.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ScreenCaptureException(
                    ScreenCaptureErrorKind.ProtectedWindow,
                    $"Capture blocked: window title contains forbidden fragment.");
            }
        }
    }

    private static string GetActiveWindowTitle(IntPtr hWnd)
    {
        char[] buffer = new char[512];
        int length = NativeMethods.GetWindowText(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        public static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(IntPtr hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr GetWindow(IntPtr hWnd, uint command);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsIconic(IntPtr hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(IntPtr hWnd, out NativeRectangle lpRect);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetCursorPos(out NativePoint lpPoint);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr MonitorFromPoint(NativePoint pt, int flags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetMonitorInfo(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        [LibraryImport("dwmapi.dll", SetLastError = true)]
        public static partial int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out NativeRectangle pvAttribute,
            int cbAttribute);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenMind.Core.Capture;

namespace ScreenMind.Platform.Windows.Capture;

public sealed partial class WindowsWindowCaptureExclusionService : IWindowCaptureExclusionService
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaMonitor = 0x00000001;
    private const uint WdaExcludeFromCapture = 0x00000011;

    public Task<WindowCaptureExclusionStatus> ApplyAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (windowHandle == IntPtr.Zero)
        {
            return Task.FromResult(new WindowCaptureExclusionStatus(
                IsSupported: false,
                IsApplied: false,
                Reason: "Invalid window handle."));
        }

        if (!NativeMethods.SetWindowDisplayAffinity(windowHandle, WdaExcludeFromCapture))
        {
            int error = Marshal.GetLastWin32Error();
            string message = new Win32Exception(error).Message;
            return Task.FromResult(new WindowCaptureExclusionStatus(
                IsSupported: true,
                IsApplied: false,
                Reason: $"SetWindowDisplayAffinity failed (error {error}): {message}"));
        }

        return Task.FromResult(new WindowCaptureExclusionStatus(
            IsSupported: true,
            IsApplied: true));
    }

    public Task<WindowCaptureExclusionStatus> GetStatusAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (windowHandle == IntPtr.Zero)
        {
            return Task.FromResult(new WindowCaptureExclusionStatus(
                IsSupported: false,
                IsApplied: false,
                Reason: "Invalid window handle."));
        }

        if (!NativeMethods.GetWindowDisplayAffinity(windowHandle, out uint affinity))
        {
            int error = Marshal.GetLastWin32Error();
            string message = new Win32Exception(error).Message;
            return Task.FromResult(new WindowCaptureExclusionStatus(
                IsSupported: true,
                IsApplied: false,
                Reason: $"GetWindowDisplayAffinity failed (error {error}): {message}"));
        }

        bool isApplied = affinity == WdaExcludeFromCapture || affinity == WdaMonitor;
        return Task.FromResult(new WindowCaptureExclusionStatus(
            IsSupported: true,
            IsApplied: isApplied));
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);
    }
}

using System;

namespace ScreenMind.Core.Capture;

public interface IWindowClickThroughService
{
    void SetClickThrough(IntPtr windowHandle, bool clickThrough);
}

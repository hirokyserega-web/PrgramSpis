using ScreenMind.Core.Capture;

namespace ScreenMind.Core.Imaging;

public sealed record ImageMask(
    ScreenRectangle Bounds,
    ImageMaskMode Mode,
    string FillColor = "#000000");


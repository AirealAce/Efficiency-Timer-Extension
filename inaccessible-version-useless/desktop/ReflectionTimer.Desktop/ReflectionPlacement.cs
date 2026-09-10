using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public static class ReflectionPlacement
{
    public static Point Calculate(Rectangle workArea, Size windowSize, ReflectionPopupPosition position, int margin)
    {
        // WorkArea excludes the taskbar and can have negative coordinates on secondary monitors.
        var spareX = Math.Max(0, workArea.Width - windowSize.Width);
        var spareY = Math.Max(0, workArea.Height - windowSize.Height);
        var insetX = Math.Clamp(margin, 0, spareX / 2);
        var insetY = Math.Clamp(margin, 0, spareY / 2);
        var offset = position switch {
            ReflectionPopupPosition.TopLeft => new Point(insetX, insetY),
            ReflectionPopupPosition.TopRight => new Point(spareX - insetX, insetY),
            ReflectionPopupPosition.BottomLeft => new Point(insetX, spareY - insetY),
            ReflectionPopupPosition.BottomRight => new Point(spareX - insetX, spareY - insetY),
            _ => new Point(spareX / 2, spareY / 2)
        };
        // If the window cannot fit, keep its title bar reachable instead of placing it offscreen.
        return new(workArea.Left + offset.X, workArea.Top + offset.Y);
    }
}

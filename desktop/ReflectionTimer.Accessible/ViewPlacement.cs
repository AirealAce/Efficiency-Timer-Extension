namespace ReflectionTimer.Accessible;

internal static class ViewPlacement
{
    // 1 center; 2/3 top corners; 4/5 bottom corners; 6/7 top/bottom center.
    internal static Point Calculate(Rectangle area,Size size,int position)
    {
        var x=position is 2 or 4?area.Left+16:position is 3 or 5?area.Right-size.Width-16:area.Left+(area.Width-size.Width)/2;
        var y=position is 2 or 3 or 6?area.Top+16:position is 4 or 5 or 7?area.Bottom-size.Height-16:area.Top+(area.Height-size.Height)/2;
        return new(Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-size.Width)),Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-size.Height)));
    }
}

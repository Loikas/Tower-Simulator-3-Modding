using System;

namespace TS3Modding.PilotReadiness
{
    // Matches UILineRenderer.JHALOEAGCFL: relative scale, then negative pivot offset.
    internal static class LineRendererCoordinates
    {
        public static RoutePoint ToMesh(RoutePoint point, double xScale, double yScale, double pivotX, double pivotY) =>
            new RoutePoint((point.X-pivotX)*xScale,(point.Y-pivotY)*yScale);
        public static RoutePoint FromMesh(RoutePoint point, double xScale, double yScale, double pivotX, double pivotY) =>
            new RoutePoint(point.X/xScale+pivotX,point.Y/yScale+pivotY);
        public static void Pattern(double pixelStroke, bool caps, out double dash, out double gap)
        {
            dash=8; gap=6;
            if(double.IsNaN(pixelStroke)||double.IsInfinity(pixelStroke)||pixelStroke<=3) return;
            dash=Math.Max(dash,pixelStroke*3);
            gap=Math.Max(gap,pixelStroke*(caps?2:1.5)+2);
        }
    }
}
